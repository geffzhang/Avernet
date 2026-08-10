using Ocb.PluginApi.Storage;

namespace Ocb.Infrastructure.Minio.Storage;

/// <summary>
/// MinIO implementation of <see cref="IAssetObjectStoragePlugin"/>.
/// Every object is tenant-scoped: keys are prefixed with <c>tenant/{tenantId}/</c>.
///
/// The MinIO SDK client is injected via <c>IMinioClient</c> at composition time.
/// Multipart upload orchestration and presigned URL generation delegate to the
/// MinIO SDK, while checksum verification runs locally.
/// </summary>
public sealed class MinioAssetObjectStoragePlugin : IAssetObjectStoragePlugin
{
    private readonly MinioClientFacade _client;

    /// <summary>
    /// Creates the plugin with a MinIO client facade and bucket options.
    /// </summary>
    /// <param name="client">Wraps <c>Minio.IMinioClient</c> — the real
    /// client can be registered via <c>services.AddMinio(...)</c> at the
    /// composition root.</param>
    /// <param name="options">Bucket name and connection parameters.</param>
    public MinioAssetObjectStoragePlugin(MinioClientFacade client, MinioOptions options)
    {
        _client = client;
        Options = options;
    }

    /// <summary>
    /// The bucket name and connection parameters for this plugin instance.
    /// </summary>
    public MinioOptions Options { get; }

    /// <inheritdoc />
    public async Task<MultipartInitResult> BeginMultipartUploadAsync(
        AssetUploadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var objectKey = BuildObjectKey(request.TenantId, request.BotId,
            request.SessionId, request.ResourceId, request.FileName);

        var uploadId = await _client.CreateMultipartUploadAsync(
            Options.BucketName, objectKey, cancellationToken);

        return new MultipartInitResult(uploadId, objectKey, []);
    }

    /// <inheritdoc />
    public async Task<DownloadResult> DownloadWithIntegrityAsync(
        AssetDownloadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (stream, contentType, contentLength) = await _client.GetObjectAsync(
            Options.BucketName, request.ObjectKey,
            request.RangeStart, request.RangeLength,
            cancellationToken);

        await ChecksumVerifier.VerifyAsync(
            stream, request.ExpectedSha256, cancellationToken);
        stream.Position = 0;

        return new DownloadResult(
            Content: stream,
            ContentType: contentType,
            ContentLength: contentLength,
            ActualSha256: request.ExpectedSha256);
    }

    /// <inheritdoc />
    public async Task<Uri> CreateSignedDownloadUrlAsync(
        string tenantId, string objectKey, TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        var url = await _client.PresignedGetObjectAsync(
            Options.BucketName, objectKey, (int)lifetime.TotalSeconds);

        return new Uri(url);
    }

    /// <summary>
    /// Build a tenant-scoped object key.
    /// Format: <c>tenant/{tenantId}/bots/{botId}/sessions/{sessionId}/{resourceId}/{fileName}</c>
    /// </summary>
    public static string BuildObjectKey(
        string tenantId, string botId, string sessionId,
        string resourceId, string fileName)
    {
        return $"tenant/{tenantId}/bots/{botId}/sessions/{sessionId}/{resourceId}/{fileName}";
    }
}

/// <summary>
/// Abstract facade over the MinIO SDK client operations used by this plugin.
/// The production implementation wraps <c>Minio.IMinioClient</c>.
/// </summary>
public abstract class MinioClientFacade
{
    /// <summary>Initiate a multipart upload and return the upload id.</summary>
    public abstract Task<string> CreateMultipartUploadAsync(
        string bucket, string objectKey, CancellationToken ct);

    /// <summary>Download an object with optional byte-range.</summary>
    public abstract Task<(Stream stream, string contentType, long contentLength)>
        GetObjectAsync(string bucket, string objectKey,
            long? offset, long? length, CancellationToken ct);

    /// <summary>Generate a presigned GET URL valid for the given seconds.</summary>
    public abstract Task<string> PresignedGetObjectAsync(
        string bucket, string objectKey, int expirySeconds);
}

/// <summary>
/// MinIO connection and bucket configuration.
/// </summary>
public sealed record MinioOptions(
    string Endpoint,
    string AccessKey,
    string SecretKey,
    string BucketName,
    bool UseSsl = true);
