using Ocb.Contracts;

namespace Ocb.PluginApi.Storage;

/// <summary>
/// Request to initiate a multipart upload for an asset.
/// </summary>
public sealed record AssetUploadRequest(
    string TenantId,
    string BotId,
    string SessionId,
    string ResourceId,
    string FileName,
    long SizeBytes,
    string ExpectedSha256);

/// <summary>
/// A presigned URL for one part of a multipart upload.
/// </summary>
public sealed record MultipartPartPresignedUrl(
    int PartNumber,
    Uri UploadUrl);

/// <summary>
/// Result of initiating a multipart upload.
/// </summary>
public sealed record MultipartInitResult(
    string UploadId,
    string ObjectKey,
    IReadOnlyList<MultipartPartPresignedUrl> Parts);

/// <summary>
/// Request to download an asset with integrity verification.
/// </summary>
public sealed record AssetDownloadRequest(
    string TenantId,
    string ObjectKey,
    string ExpectedSha256,
    long? RangeStart = null,
    long? RangeLength = null);

/// <summary>
/// Result of an asset download with integrity verification.
/// </summary>
public sealed record DownloadResult(
    Stream Content,
    string ContentType,
    long ContentLength,
    string ActualSha256);

/// <summary>
/// Object storage plugin for asset upload/download with integrity verification.
/// Implemented by the MinIO infrastructure layer.
/// </summary>
public interface IAssetObjectStoragePlugin : IPluginContract
{
    /// <summary>
    /// Begin a multipart upload for the specified asset.
    /// </summary>
    Task<MultipartInitResult> BeginMultipartUploadAsync(
        AssetUploadRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Download an asset and verify its SHA-256 checksum.
    /// </summary>
    Task<DownloadResult> DownloadWithIntegrityAsync(
        AssetDownloadRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Create a signed (pre-signed) URL for temporary download access.
    /// </summary>
    Task<Uri> CreateSignedDownloadUrlAsync(
        string tenantId, string objectKey, TimeSpan lifetime, CancellationToken cancellationToken);
}
