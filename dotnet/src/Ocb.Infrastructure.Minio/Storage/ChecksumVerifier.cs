using System.Security.Cryptography;
using System.Text;

namespace Ocb.Infrastructure.Minio.Storage;

/// <summary>
/// Verifies SHA-256 checksums for uploaded objects.
/// </summary>
public static class ChecksumVerifier
{
    /// <summary>
    /// Compute the SHA-256 hash of a stream and return the hex string.
    /// The stream position is reset after reading.
    /// </summary>
    public static async Task<string> ComputeSha256Async(
        Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        long originalPosition = 0;
        var seekable = false;
        if (stream.CanSeek)
        {
            originalPosition = stream.Position;
            stream.Position = 0;
            seekable = true;
        }

        var hash = await SHA256.HashDataAsync(stream, ct);
        var result = Convert.ToHexStringLower(hash);

        if (seekable)
        {
            stream.Position = originalPosition;
        }

        return result;
    }

    /// <summary>
    /// Verify that the stream's SHA-256 matches the expected hex string.
    /// Throws <see cref="InvalidDataException"/> on mismatch.
    /// </summary>
    public static async Task VerifyAsync(
        Stream stream, string expectedSha256, CancellationToken ct = default)
    {
        var actual = await ComputeSha256Async(stream, ct);

        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Checksum mismatch. Expected: {expectedSha256}, Actual: {actual}");
        }
    }

    /// <summary>
    /// Verify that two hex SHA-256 strings match.
    /// Throws <see cref="InvalidDataException"/> on mismatch.
    /// </summary>
    public static void VerifyString(string expectedSha256, string actualSha256)
    {
        if (!string.Equals(expectedSha256, actualSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Checksum mismatch. Expected: {expectedSha256}, Actual: {actualSha256}");
        }
    }
}
