using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TourDock.Application.Abstractions;

namespace TourDock.Infrastructure.Files;

/// <summary>S3-compatible object storage (RustFS locally; AWS S3 or similar in production, by configuration only).</summary>
internal sealed class S3FileStorage(IAmazonS3 s3, IOptions<StorageOptions> options) : IFileStorage
{
    // Keys are unique per photo and never overwritten, so browsers and CDNs may cache forever.
    private const string ImmutableCache = "public, max-age=31536000, immutable";

    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);
        var request = new PutObjectRequest
        {
            BucketName = options.Value.Bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
        };
        request.Headers.CacheControl = ImmutableCache;
        await s3.PutObjectAsync(request, cancellationToken);
    }

    public async Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return;
        }

        await s3.DeleteObjectsAsync(new DeleteObjectsRequest
        {
            BucketName = options.Value.Bucket,
            Objects = keys.Select(k => new KeyVersion { Key = k }).ToList(),
        }, cancellationToken);
    }

    public static IAmazonS3 CreateClient(StorageOptions options) => new AmazonS3Client(
        new BasicAWSCredentials(options.AccessKey, options.SecretKey),
        new AmazonS3Config
        {
            ServiceURL = options.ServiceUrl,
            ForcePathStyle = true, // bucket in the path, as S3-compatible servers expect
            AuthenticationRegion = "us-east-1",
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });
}

internal sealed class PhotoUrls(IOptions<StorageOptions> options) : IPhotoUrls
{
    public Application.Abstractions.PhotoUrls For(string storageKey)
    {
        var baseUrl = options.Value.PublicBaseUrl.TrimEnd('/');
        return new(
            $"{baseUrl}/{storageKey}/thumbnail.webp",
            $"{baseUrl}/{storageKey}/medium.webp",
            $"{baseUrl}/{storageKey}/large.webp");
    }
}

/// <summary>
/// Makes sure the bucket exists and that single objects are publicly readable (listing the bucket is not),
/// so photos can be served straight from storage or a CDN without going through the API.
/// </summary>
internal sealed partial class StorageInitializer(IAmazonS3 s3, IOptions<StorageOptions> options, ILogger<StorageInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var bucket = options.Value.Bucket;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var buckets = await s3.ListBucketsAsync(cancellationToken);
                if (buckets.Buckets?.Any(b => b.BucketName == bucket) != true)
                {
                    await s3.PutBucketAsync(bucket, cancellationToken);
                }

                await s3.PutBucketPolicyAsync(bucket, $$"""
                    {"Version":"2012-10-17","Statement":[{"Effect":"Allow","Principal":{"AWS":["*"]},
                     "Action":["s3:GetObject"],"Resource":["arn:aws:s3:::{{bucket}}/*"]}]}
                    """, cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < 10 && ex is AmazonServiceException or HttpRequestException)
            {
                LogWaiting(logger, attempt);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Object storage not ready yet (attempt {Attempt}); retrying")]
    private static partial void LogWaiting(ILogger logger, int attempt);
}
