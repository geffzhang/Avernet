using Ocb.Contracts;
using Ocb.Contracts.Resources;
using Ocb.PluginApi.Storage;

namespace Ocb.Backend.Sessions;

/// <summary>
/// Manages session asset lifecycle — upload intents, downloads, and cleanup.
/// Uses the MinIO plugin for multipart upload initiation and signed downloads.
/// </summary>
public sealed class SessionAssetService
{
    private readonly IAssetObjectStoragePlugin _storage;

    public SessionAssetService(IAssetObjectStoragePlugin storage)
    {
        _storage = storage;
    }

    /// <summary>
    /// Create an upload intent: begin a multipart upload and return
    /// a session asset record with the allocated object key.
    /// </summary>
    public async Task<SessionAssetRecord> CreateUploadIntentAsync(
        CallerContext caller,
        string botId,
        string sessionId,
        string fileName,
        long sizeBytes,
        string sha256,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentException.ThrowIfNullOrWhiteSpace(botId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var upload = new AssetUploadRequest(
            caller.TenantId,
            botId,
            sessionId,
            Guid.NewGuid().ToString("N"),
            fileName,
            sizeBytes,
            sha256);

        var initResult = await _storage.BeginMultipartUploadAsync(upload, ct);

        return new SessionAssetRecord(
            TenantId: caller.TenantId,
            BotId: botId,
            SessionId: sessionId,
            ResourceId: upload.ResourceId,
            ObjectKey: initResult.ObjectKey,
            SizeBytes: sizeBytes,
            Sha256: sha256);
    }
}
