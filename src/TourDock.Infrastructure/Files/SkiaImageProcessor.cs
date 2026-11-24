using SkiaSharp;
using TourDock.Application.Abstractions;
using TourDock.Application.Photos;
using TourDock.Domain.Common;

namespace TourDock.Infrastructure.Files;

/// <summary>
/// Image pipeline for untrusted uploads, built on SkiaSharp (MIT):
/// 1. The format is detected from the bytes (magic numbers) by the decoder; name and declared type are ignored.
/// 2. Dimensions are read from the header before decoding, so a "decompression bomb" is rejected cheaply.
/// 3. The image is fully decoded and re-encoded: EXIF (including GPS location), ICC/XMP and any trailing payload are dropped.
/// 4. Camera orientation is applied before metadata is dropped, so photos aren't sideways.
/// </summary>
internal sealed class SkiaImageProcessor : IImageProcessor
{
    private const int WebpQuality = 80;
    private static readonly SKEncodedImageFormat[] Allowed = [SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp];

    public Result<ProcessedImage> Process(Stream content)
    {
        using var data = SKData.Create(content);
        if (data is null || data.Size == 0)
        {
            return ImageErrors.Empty;
        }

        using var codec = SKCodec.Create(data);
        if (codec is null || !Allowed.Contains(codec.EncodedFormat))
        {
            return ImageErrors.UnsupportedFormat;
        }

        var (width, height) = (codec.Info.Width, codec.Info.Height);
        if (width > ImageErrors.MaxSide || height > ImageErrors.MaxSide)
        {
            return ImageErrors.TooLarge;
        }

        if (Math.Min(width, height) < ImageErrors.MinShortSide)
        {
            return ImageErrors.TooSmall;
        }

        using var decoded = SKBitmap.Decode(codec, codec.Info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Premul));
        if (decoded is null)
        {
            return ImageErrors.UnsupportedFormat;
        }

        using var oriented = ApplyOrientation(decoded, codec.EncodedOrigin);

        var variants = new List<ImageVariant>();
        foreach (var (name, maxSide) in PhotoVariants.All)
        {
            var scale = Math.Min(1.0, maxSide / (double)Math.Max(oriented.Width, oriented.Height)); // never upscale
            var (w, h) = ((int)Math.Round(oriented.Width * scale), (int)Math.Round(oriented.Height * scale));

            using var resized = scale < 1.0
                ? oriented.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKCubicResampler.Mitchell))
                : oriented.Copy();
            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, WebpQuality);
            variants.Add(new ImageVariant(name, encoded.ToArray(), w, h));
        }

        return new ProcessedImage(variants);
    }

    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
        {
            return source.Copy();
        }

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(result);
        var (w, h) = (source.Width, source.Height);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1, w / 2f, 0); break;
            case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, w / 2f, h / 2f); break;
            case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1, 0, h / 2f); break;
            case SKEncodedOrigin.LeftTop: canvas.Translate(h, 0); canvas.RotateDegrees(90); canvas.Scale(1, -1, 0, h / 2f); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(h, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(h, w); canvas.RotateDegrees(90); canvas.Scale(-1, -1, w / 2f, h / 2f); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, w); canvas.RotateDegrees(270); break;
        }

        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
        return result;
    }
}
