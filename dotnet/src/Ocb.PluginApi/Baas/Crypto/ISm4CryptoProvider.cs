namespace Ocb.PluginApi.Baas.Crypto;

/// <summary>
/// SM4 symmetric encryption provider for device header encryption.
/// SM2 is explicitly excluded from scope.
/// </summary>
public interface ISm4CryptoProvider
{
    /// <summary>
    /// Encrypts plaintext using SM4-CBC with PKCS7 padding
    /// and Encrypt-then-MAC authentication.
    /// Returns the base64-encoded envelope.
    /// </summary>
    string Encrypt(string plaintext, string keyId);

    /// <summary>
    /// Decrypts a base64-encoded SM4 envelope.
    /// Throws <see cref="System.Security.Cryptography.CryptographicException"/>
    /// on authentication failure or tampered ciphertext.
    /// </summary>
    string Decrypt(string ciphertext, string keyId);
}

/// <summary>
/// Resolves SM4 key material by key identifier.
/// Key material must not enter request DTOs, logs, or Grain state.
/// </summary>
public interface ISm4KeyResolver
{
    /// <summary>
    /// Resolves encryption and authentication keys for the given key ID.
    /// </summary>
    Sm4KeyMaterial Resolve(string keyId);
}

public sealed record Sm4KeyMaterial(
    byte[] EncryptionKey,
    byte[] AuthenticationKey);
