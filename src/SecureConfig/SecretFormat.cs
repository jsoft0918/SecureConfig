using System;
using System.Security.Cryptography;
using System.Text;
using SecureConfig.Internal;

namespace SecureConfig
{
    /// <summary>
    /// Ciphertext format definition and decryption.
    ///
    /// <list type="bullet">
    /// <item>Format: <c>"enc:v1:" + Base64( nonce(12) | ciphertext(n) | tag(16) )</c></item>
    /// <item>Algorithm: AES-256-GCM, AAD = <c>"SECURECONFIG/v1"</c></item>
    /// <item>Plaintext: config JSON (UTF-8)</item>
    /// </list>
    ///
    /// Encryption is performed in exactly this format by the editor (SecureConfig.Editor).
    /// To change the format, bump the prefix version (enc:v2:) and first deploy a reader DLL that can read both versions.
    /// </summary>
    public static class SecretFormat
    {
        /// <summary>Ciphertext prefix. Bump it (e.g. enc:v2:) when the format version changes.</summary>
        public const string Prefix = "enc:v1:";

        /// <summary>Master key length in bytes. 32 for AES-256.</summary>
        public const int KeySize = 32;

        /// <summary>GCM nonce length in bytes. A fresh random nonce is used for every encryption.</summary>
        public const int NonceSize = 12;

        /// <summary>GCM authentication tag length in bytes. Used to detect tampering.</summary>
        public const int TagSize = 16;

        /// <summary>
        /// Additional authenticated data (AAD). Prevents AES-GCM values produced by other systems with the same key from being accepted as this format.
        /// </summary>
        public const string AadText = "SECURECONFIG/v1";

        /// <summary>AAD bytes (a new array on every call)</summary>
        public static byte[] Aad { get { return Encoding.UTF8.GetBytes(AadText); } }

        /// <summary>Whether the value is ciphertext in this format (checks the prefix only)</summary>
        public static bool IsEncrypted(string value)
        {
            return value != null && value.Trim().StartsWith(Prefix, StringComparison.Ordinal);
        }

        /// <summary>Base64 key string → 32 bytes. Throws <see cref="FormatException"/> if malformed.</summary>
        public static byte[] ParseKey(string base64)
        {
            if (base64 == null || base64.Trim().Length == 0)
                throw new FormatException("The master key is empty.");
            byte[] key;
            try { key = Convert.FromBase64String(base64.Trim()); }
            catch (FormatException) { throw new FormatException("The master key must be a Base64 string."); }
            if (key.Length != KeySize)
                throw new FormatException("The master key must be 32 bytes (44 Base64 characters), but it is " + key.Length + " bytes.");
            return key;
        }

        /// <summary>Decrypts. Throws <see cref="CryptographicException"/> on a format error, key mismatch, or tampering.</summary>
        public static string Decrypt(string value, byte[] key)
        {
            if (key == null || key.Length != KeySize)
                throw new CryptographicException("The master key must be 32 bytes.");
            if (!IsEncrypted(value))
                throw new CryptographicException("The value is not in ciphertext format (it must start with '" + Prefix + "').");

            byte[] all;
            try { all = Convert.FromBase64String(value.Trim().Substring(Prefix.Length)); }
            catch (FormatException) { throw new CryptographicException("The ciphertext is not valid Base64."); }
            if (all.Length < NonceSize + TagSize)
                throw new CryptographicException("The ciphertext is too short.");

            byte[] nonce = new byte[NonceSize];
            byte[] tag = new byte[TagSize];
            byte[] ct = new byte[all.Length - NonceSize - TagSize];
            Buffer.BlockCopy(all, 0, nonce, 0, NonceSize);
            Buffer.BlockCopy(all, NonceSize, ct, 0, ct.Length);
            Buffer.BlockCopy(all, NonceSize + ct.Length, tag, 0, TagSize);

            byte[] plain;
            if (!AesGcmDecryptor.TryDecrypt(key, nonce, ct, tag, Aad, out plain))
                throw new CryptographicException("Decryption failed: the master key is different or the value was tampered with.");
            return Encoding.UTF8.GetString(plain);
        }
    }
}
