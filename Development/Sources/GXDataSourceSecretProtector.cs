using System.Security.Cryptography;
using System.Text;
using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Sources;

public sealed class GXDataSourceSecretProtector(Func<string?> masterKey)
{
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("Gurux.Data.Relay/DataSources/v1");

    public GXDataSourceSecret Encrypt(string name, string plaintext)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] value = Encoding.UTF8.GetBytes(plaintext);
        byte[] ciphertext = new byte[value.Length];
        byte[] tag = new byte[16];
        using var cipher = new AesGcm(DeriveKey(), tag.Length);
        cipher.Encrypt(nonce, value, ciphertext, tag, Encoding.UTF8.GetBytes(name));
        return new() { Name = name, Nonce = nonce, Ciphertext = ciphertext, Tag = tag };
    }

    public string Decrypt(string name, GXDataSourceSecret secret)
    {
        byte[] plaintext = new byte[secret.Ciphertext.Length];
        using var cipher = new AesGcm(DeriveKey(), secret.Tag.Length);
        cipher.Decrypt(secret.Nonce, secret.Ciphertext, secret.Tag, plaintext, Encoding.UTF8.GetBytes(name));
        return Encoding.UTF8.GetString(plaintext);
    }

    private byte[] DeriveKey()
    {
        string? value = masterKey();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("GURUX_RELAY_SECRETS_KEY is required to use data-source secrets.");
        byte[] key = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(value), key, Salt, Encoding.UTF8.GetBytes("AES-GCM"));
        return key;
    }
}
