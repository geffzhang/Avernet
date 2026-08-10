using System.Security.Cryptography;
using Ocb.PluginApi.Baas.Crypto;

namespace Ocb.Plugins.Crypto.SM4;

/// <summary>
/// SM4 encryption provider using SM4-CBC with PKCS7 padding
/// and Encrypt-then-MAC (HMAC-SHA256) authentication.
///
/// Wire envelope format: version(1 byte) || IV(16 bytes) ||
/// ciphertext || HMAC-SHA256 tag(32 bytes), base64-encoded.
/// </summary>
public sealed class Sm4CryptoProvider : ISm4CryptoProvider
{
    private readonly ISm4KeyResolver _keyResolver;
    private const byte Version = 1;
    private const int IvSize = 16;
    private const int TagSize = 32;

    public Sm4CryptoProvider(ISm4KeyResolver keyResolver)
    {
        _keyResolver = keyResolver;
    }

    public string Encrypt(string plaintext, string keyId)
    {
        var keys = _keyResolver.Resolve(keyId);
        var iv = GenerateIv();
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);

        // Encrypt with SM4-CBC
        var cipherBytes = Sm4Engine.EncryptCbc(keys.EncryptionKey, iv, plainBytes);

        // Build envelope: version || IV || ciphertext
        var authenticatedBytes = new byte[1 + IvSize + cipherBytes.Length];
        authenticatedBytes[0] = Version;
        Buffer.BlockCopy(iv, 0, authenticatedBytes, 1, IvSize);
        Buffer.BlockCopy(cipherBytes, 0, authenticatedBytes, 1 + IvSize, cipherBytes.Length);

        // Compute HMAC-SHA256 over authenticated bytes
        var tag = HMACSHA256.HashData(keys.AuthenticationKey, authenticatedBytes);

        // Full envelope: authenticatedBytes || tag
        var envelope = new byte[authenticatedBytes.Length + TagSize];
        Buffer.BlockCopy(authenticatedBytes, 0, envelope, 0, authenticatedBytes.Length);
        Buffer.BlockCopy(tag, 0, envelope, authenticatedBytes.Length, TagSize);

        return Convert.ToBase64String(envelope);
    }

    public string Decrypt(string ciphertext, string keyId)
    {
        var keys = _keyResolver.Resolve(keyId);
        var envelope = Convert.FromBase64String(ciphertext);

        if (envelope.Length < 1 + IvSize + TagSize)
            throw new CryptographicException("SM4 envelope too short.");

        // Extract components
        var version = envelope[0];
        if (version != Version)
            throw new CryptographicException($"Unsupported SM4 envelope version: {version}");

        var authenticatedLength = envelope.Length - TagSize;
        var authenticatedBytes = envelope.AsSpan(0, authenticatedLength);
        var tag = envelope.AsSpan(authenticatedLength, TagSize);

        // Verify HMAC
        var computedTag = HMACSHA256.HashData(keys.AuthenticationKey, authenticatedBytes);
        if (!CryptographicOperations.FixedTimeEquals(tag, computedTag))
            throw new CryptographicException("SM4 envelope authentication failed — ciphertext tampered or key mismatch.");

        // Extract IV and ciphertext
        var iv = authenticatedBytes.Slice(1, IvSize).ToArray();
        var cipherBytes = authenticatedBytes.Slice(1 + IvSize).ToArray();

        // Decrypt
        var plainBytes = Sm4Engine.DecryptCbc(keys.EncryptionKey, iv, cipherBytes);

        return System.Text.Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] GenerateIv()
    {
        var iv = new byte[IvSize];
        RandomNumberGenerator.Fill(iv);
        return iv;
    }
}
