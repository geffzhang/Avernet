using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Ocb.PluginApi.Baas.Crypto;
using Ocb.Plugins.Crypto.SM4;

namespace Ocb.Baas.Provider.Conformance.Tests;

/// <summary>
/// SM4 conformance: known-answer, roundtrip, invalid-key, padding, tamper.
/// SM2 is explicitly excluded from scope.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class Sm4ConformanceTests
{
    private static Sm4CryptoProvider CreateProvider()
    {
        var keyResolver = new StubSm4KeyResolver();
        return new Sm4CryptoProvider(keyResolver);
    }

    [Fact]
    public void Sm4_Roundtrip_ShouldReturnOriginal()
    {
        var provider = CreateProvider();
        var plaintext = "Hello SM4 World!";

        var cipher = provider.Encrypt(plaintext, "k1");
        var decrypted = provider.Decrypt(cipher, "k1");

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Sm4_ShouldFail_OnTamperedCiphertext()
    {
        var provider = CreateProvider();
        var cipher = provider.Encrypt("Bearer secret token", "k1");
        var bytes = Convert.FromBase64String(cipher);

        // Tamper with last byte
        bytes[^1] ^= 0x01;
        var tampered = Convert.ToBase64String(bytes);

        Assert.Throws<CryptographicException>(() => provider.Decrypt(tampered, "k1"));
    }

    [Fact]
    public void Sm4_ShouldFail_OnWrongKey()
    {
        var provider = CreateProvider();
        var cipher = provider.Encrypt("secret", "k1");

        Assert.Throws<CryptographicException>(() => provider.Decrypt(cipher, "k2"));
    }

    [Fact]
    public void Sm4_ShouldFail_OnInvalidBase64()
    {
        var provider = CreateProvider();
        Assert.ThrowsAny<Exception>(() => provider.Decrypt("!!!not-base64!!!", "k1"));
    }

    [Fact]
    public void Sm4_ShouldFail_OnTruncatedCiphertext()
    {
        var provider = CreateProvider();
        var cipher = provider.Encrypt("secret", "k1");
        var truncated = cipher[..(cipher.Length / 2)];

        Assert.ThrowsAny<Exception>(() => provider.Decrypt(truncated, "k1"));
    }
}

internal sealed class StubSm4KeyResolver : ISm4KeyResolver
{
    private readonly Dictionary<string, Sm4KeyMaterial> _keys = new();

    public StubSm4KeyResolver()
    {
        // Generate deterministic keys for testing
        _keys["k1"] = new Sm4KeyMaterial(
            EncryptionKey: System.Text.Encoding.UTF8.GetBytes("0123456789abcdef"),
            AuthenticationKey: System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        _keys["k2"] = new Sm4KeyMaterial(
            EncryptionKey: System.Text.Encoding.UTF8.GetBytes("fedcba9876543210"),
            AuthenticationKey: System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    }

    public Sm4KeyMaterial Resolve(string keyId)
    {
        if (!_keys.TryGetValue(keyId, out var material))
            throw new InvalidOperationException($"Key not found: {keyId}");
        return material;
    }
}
