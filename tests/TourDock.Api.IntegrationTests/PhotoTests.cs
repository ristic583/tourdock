using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using TourDock.Infrastructure.Outbox;

namespace TourDock.Api.IntegrationTests;

public sealed class PhotoTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private TestApi Api() => new(factory.CreateClient(), Ct);

    [Fact]
    public async Task Upload_stores_resized_webp_variants_served_straight_from_storage()
    {
        var (token, agencyId, tourId) = await TourAsync();

        var (status, photo) = await UploadAsync(token, agencyId, tourId, Jpeg(1600, 1000), "image/jpeg");

        status.Should().Be(HttpStatusCode.Created);
        photo.GetProperty("isCover").GetBoolean().Should().BeTrue();
        var (largeType, large) = await DownloadAsync(photo.GetProperty("urls").GetProperty("large").GetString()!);
        largeType.Should().Be("image/webp");
        Size(large).Should().Be((1600, 1000), "images are never upscaled");
        var (_, thumbnail) = await DownloadAsync(photo.GetProperty("urls").GetProperty("thumbnail").GetString()!);
        Size(thumbnail).Should().Be((480, 300));
    }

    [Fact]
    public async Task Camera_orientation_is_applied_and_exif_metadata_is_removed()
    {
        var (token, agencyId, tourId) = await TourAsync();

        // A landscape JPEG whose EXIF says "rotate 90°" (how phones store portrait photos).
        var (_, photo) = await UploadAsync(token, agencyId, tourId, WithExifOrientation(Jpeg(1200, 800), orientation: 6), "image/jpeg");

        var (_, large) = await DownloadAsync(photo.GetProperty("urls").GetProperty("large").GetString()!);
        Size(large).Should().Be((800, 1200));
        Encoding.ASCII.GetString(large).Should().NotContain("Exif");
    }

    [Theory]
    [InlineData("%PDF-1.7 fake document pretending to be a photo", "image/jpeg")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>", "image/png")]
    public async Task Files_that_are_not_real_images_are_rejected_whatever_they_claim(string content, string declaredType)
    {
        var (token, agencyId, tourId) = await TourAsync();

        var (status, body) = await UploadAsync(token, agencyId, tourId, Encoding.UTF8.GetBytes(content), declaredType);

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("code").GetString().Should().Be("Photo.UnsupportedFormat");
    }

    [Fact]
    public async Task Tiny_images_are_rejected()
    {
        var (token, agencyId, tourId) = await TourAsync();

        var (status, body) = await UploadAsync(token, agencyId, tourId, Jpeg(500, 400), "image/jpeg");

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("code").GetString().Should().Be("Photo.TooSmall");
    }

    [Fact]
    public async Task Uploads_over_ten_megabytes_are_rejected()
    {
        var (token, agencyId, tourId) = await TourAsync();

        var (status, _) = await UploadAsync(token, agencyId, tourId, new byte[11 * 1024 * 1024], "image/jpeg");

        status.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Only_the_agency_can_upload()
    {
        var api = Api();
        var (_, agencyId, tourId) = await TourAsync();

        var (status, _) = await UploadAsync(await api.RegisterUserAsync(), agencyId, tourId, Jpeg(1200, 800), "image/jpeg");

        status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Published_tour_shows_photos_and_deleting_a_photo_removes_its_files()
    {
        var api = Api();
        var (token, agencyId, tourId) = await TourAsync();
        var (_, first) = await UploadAsync(token, agencyId, tourId, Jpeg(1200, 800), "image/jpeg");
        var (_, second) = await UploadAsync(token, agencyId, tourId, Jpeg(1200, 800), "image/jpeg");
        var secondId = second.GetProperty("id").GetString();
        (await api.SendAsync(HttpMethod.Put, $"/api/v1/agencies/{agencyId}/tours/{tourId}/photos/{secondId}/cover", null, token))
            .Status.Should().Be(HttpStatusCode.OK);
        await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours/{tourId}/publish", null, token);

        var (_, publicTour) = await api.SendAsync(HttpMethod.Get, $"/api/v1/tours/{tourId}");
        var photos = publicTour.GetProperty("photos").EnumerateArray().ToList();
        photos.Should().HaveCount(2);
        photos.Single(p => p.GetProperty("isCover").GetBoolean()).GetProperty("id").GetString().Should().Be(secondId);

        var firstUrl = first.GetProperty("urls").GetProperty("medium").GetString()!;
        (await api.SendAsync(HttpMethod.Delete, $"/api/v1/agencies/{agencyId}/tours/{tourId}/photos/{first.GetProperty("id").GetString()}", null, token))
            .Status.Should().Be(HttpStatusCode.NoContent);
        await factory.Services.DrainOutboxAsync(Ct);

        using var http = new HttpClient();
        (await http.GetAsync(firstUrl, Ct)).StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    private async Task<(string Token, string AgencyId, string TourId)> TourAsync()
    {
        var api = Api();
        var (token, agencyId) = await api.CreateAgencyAsync();
        return (token, agencyId, await api.CreateTourAsync(token, agencyId));
    }

    private async Task<(HttpStatusCode Status, System.Text.Json.JsonElement Body)> UploadAsync(
        string token, string agencyId, string tourId, byte[] bytes, string declaredType)
    {
        using var client = factory.CreateClient();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(declaredType);
        form.Add(file, "file", "photo.jpg");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours/{tourId}/photos") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, string.IsNullOrEmpty(text) ? default : System.Text.Json.JsonDocument.Parse(text).RootElement.Clone());
    }

    private async Task<(string? ContentType, byte[] Bytes)> DownloadAsync(string url)
    {
        using var http = new HttpClient();
        using var response = await http.GetAsync(url, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "photos are publicly readable from storage");
        return (response.Content.Headers.ContentType?.MediaType, await response.Content.ReadAsByteArrayAsync(Ct));
    }

    private static (int Width, int Height) Size(byte[] image)
    {
        using var codec = SKCodec.Create(new MemoryStream(image));
        return (codec.Info.Width, codec.Info.Height);
    }

    private static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(10, 110, 106));
            using var paint = new SKPaint { Color = SKColors.Orange };
            canvas.DrawRect(0, 0, width / 3f, height / 3f, paint); // marks the top-left corner
        }

        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray();
    }

    /// <summary>Inserts a minimal EXIF (APP1) segment with the given orientation right after the JPEG start marker.</summary>
    private static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
    {
        byte[] tiff =
        [
            (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08, // big-endian TIFF header, first IFD at offset 8
            0x00, 0x01,                                               // one entry
            0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01,           // tag 0x0112 Orientation, SHORT, count 1
            (byte)(orientation >> 8), (byte)orientation, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,                                   // no next IFD
        ];
        var payload = Encoding.ASCII.GetBytes("Exif\0\0").Concat(tiff).ToArray();
        var length = payload.Length + 2;
        byte[] app1 = [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. payload];
        return [.. jpeg[..2], .. app1, .. jpeg[2..]];
    }
}
