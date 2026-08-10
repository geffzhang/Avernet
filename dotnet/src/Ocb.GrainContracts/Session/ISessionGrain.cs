namespace Ocb.GrainContracts.Session;

using Orleans;

/// <summary>
/// Grain that owns the lifecycle of a bot session and its associated assets.
/// Each grain is keyed <c>session/{tenantId}/{sessionId}</c>.
/// </summary>
[Alias("Ocb.GrainContracts.Session.ISessionGrain")]
public interface ISessionGrain : IGrainWithStringKey
{
    /// <summary>
    /// Record a newly uploaded asset for this session.
    /// </summary>
    Task RecordAssetAsync(
        string tenantId, string botId, string resourceId, string objectKey,
        long sizeBytes, string sha256);

    /// <summary>
    /// List all assets associated with this session.
    /// Returns the asset metadata as a serializable snapshot.
    /// </summary>
    Task<SessionAssetSnapshot> GetAssetSnapshotAsync();

    /// <summary>
    /// Close the session so no further assets can be recorded.
    /// </summary>
    Task CloseAsync();
}

/// <summary>
/// Serializable snapshot of a session's assets.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Session.SessionAssetSnapshot")]
public sealed record SessionAssetSnapshot(
    [property: Id(0)] IReadOnlyList<SessionAssetEntry> Entries,
    [property: Id(1)] bool IsClosed);

/// <summary>
/// A single asset entry within a session snapshot.
/// </summary>
[GenerateSerializer]
[Alias("Ocb.GrainContracts.Session.SessionAssetEntry")]
public sealed record SessionAssetEntry(
    [property: Id(0)] string ResourceId,
    [property: Id(1)] string ObjectKey,
    [property: Id(2)] long SizeBytes,
    [property: Id(3)] string Sha256);
