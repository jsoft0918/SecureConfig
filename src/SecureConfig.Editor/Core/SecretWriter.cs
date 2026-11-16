using System.Security.Cryptography;
using System.Text;

namespace SecureConfig.Editor.Core;

/// <summary>Encryption (same format as SecureConfig.SecretFormat)</summary>
public static class SecretWriter
{
    /// <summary>New master key (32 cryptographically random bytes, Base64)</summary>
    public static string GenerateKeyBase64() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretFormat.KeySize));

    /// <summary>
    /// AES-256-GCM encryption → "enc:v1:" + Base64(nonce | ciphertext | tag).
    /// The nonce is fresh random data on every call, so the same plaintext yields a different ciphertext each time.
    /// </summary>
    public static string Encrypt(string plaintext, byte[] key)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(SecretFormat.NonceSize);
        byte[] pt = Encoding.UTF8.GetBytes(plaintext);
        byte[] ct = new byte[pt.Length];
        byte[] tag = new byte[SecretFormat.TagSize];
        using (var gcm = new AesGcm(key, SecretFormat.TagSize))
        {
            gcm.Encrypt(nonce, pt, ct, tag, SecretFormat.Aad);
        }
        byte[] all = new byte[nonce.Length + ct.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, all, 0, nonce.Length);
        Buffer.BlockCopy(ct, 0, all, nonce.Length, ct.Length);
        Buffer.BlockCopy(tag, 0, all, nonce.Length + ct.Length, tag.Length);
        return SecretFormat.Prefix + Convert.ToBase64String(all);
    }

    /// <summary>Encrypts, then decrypts with the read-only DLL to verify it matches the original (only values the app can definitely read are saved)</summary>
    public static string EncryptVerified(string plaintext, byte[] key)
    {
        string enc = Encrypt(plaintext, key);
        if (SecretFormat.Decrypt(enc, key) != plaintext)
            throw new CryptographicException("Encryption check failed: the decrypted result does not match the original.");
        return enc;
    }
}
