using System;
using System.Security.Cryptography;

#if !NET8_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace SecureConfig.Internal
{
    /// <summary>
    /// AES-GCM decryption (this is a read-only library, so only decryption is provided).
    ///  - net8.0 build        : System.Security.Cryptography.AesGcm (.NET 8 / 9 / 10)
    ///  - netstandard2.0 build: calls Windows CNG (bcrypt.dll) directly. netstandard2.0 has no AesGcm, so this
    ///                          lets .NET Framework 4.6.2+ read the same format.
    /// </summary>
    internal static class AesGcmDecryptor
    {
        /// <summary>Decrypts. Returns false if the authentication tag does not match (key mismatch / tampering).</summary>
        public static bool TryDecrypt(byte[] key, byte[] nonce, byte[] ciphertext, byte[] tag, byte[] aad, out byte[] plaintext)
        {
#if NET8_0_OR_GREATER
            plaintext = new byte[ciphertext.Length];
            using (var gcm = new AesGcm(key, tag.Length))
            {
                try
                {
                    gcm.Decrypt(nonce, ciphertext, tag, plaintext, aad);
                    return true;
                }
                catch (AuthenticationTagMismatchException)
                {
                    plaintext = null;
                    return false;
                }
            }
#else
            return Cng.TryDecrypt(key, nonce, ciphertext, tag, aad, out plaintext);
#endif
        }

#if !NET8_0_OR_GREATER
        /// <summary>Windows CNG (bcrypt.dll) AES-GCM</summary>
        private static class Cng
        {
            private const uint StatusSuccess = 0;
            private const uint StatusAuthTagMismatch = 0xC000A002;

            public static bool TryDecrypt(byte[] key, byte[] nonce, byte[] ciphertext, byte[] tag, byte[] aad, out byte[] plaintext)
            {
                if (!IsWindows())
                    throw new PlatformNotSupportedException(
                        "SecureConfig supports Windows only.");

                plaintext = new byte[ciphertext.Length];
                IntPtr hAlg = IntPtr.Zero;
                IntPtr hKey = IntPtr.Zero;
                GCHandle hNonce = GCHandle.Alloc(nonce, GCHandleType.Pinned);
                GCHandle hTag = GCHandle.Alloc(tag, GCHandleType.Pinned);
                GCHandle hAad = GCHandle.Alloc(aad, GCHandleType.Pinned);
                try
                {
                    Check(BCryptOpenAlgorithmProvider(out hAlg, "AES", null, 0), "BCryptOpenAlgorithmProvider");
                    byte[] mode = System.Text.Encoding.Unicode.GetBytes("ChainingModeGCM\0");
                    Check(BCryptSetProperty(hAlg, "ChainingMode", mode, mode.Length, 0), "BCryptSetProperty");
                    Check(BCryptGenerateSymmetricKey(hAlg, out hKey, IntPtr.Zero, 0, key, key.Length, 0), "BCryptGenerateSymmetricKey");

                    var info = new AuthenticatedCipherModeInfo
                    {
                        cbSize = Marshal.SizeOf(typeof(AuthenticatedCipherModeInfo)),
                        dwInfoVersion = 1,
                        pbNonce = hNonce.AddrOfPinnedObject(),
                        cbNonce = nonce.Length,
                        pbTag = hTag.AddrOfPinnedObject(),
                        cbTag = tag.Length,
                        pbAuthData = aad.Length > 0 ? hAad.AddrOfPinnedObject() : IntPtr.Zero,
                        cbAuthData = aad.Length,
                    };

                    int written;
                    uint status = BCryptDecrypt(hKey, ciphertext, ciphertext.Length, ref info, null, 0,
                        plaintext, plaintext.Length, out written, 0);
                    if (status == StatusAuthTagMismatch)
                    {
                        plaintext = null;
                        return false;
                    }
                    Check(status, "BCryptDecrypt");
                    return true;
                }
                finally
                {
                    if (hKey != IntPtr.Zero) BCryptDestroyKey(hKey);
                    if (hAlg != IntPtr.Zero) BCryptCloseAlgorithmProvider(hAlg, 0);
                    hNonce.Free();
                    hTag.Free();
                    hAad.Free();
                }
            }

            private static bool IsWindows()
            {
                return Environment.OSVersion.Platform == PlatformID.Win32NT;
            }

            private static void Check(uint status, string fn)
            {
                if (status != StatusSuccess)
                    throw new CryptographicException(fn + " failed (0x" + status.ToString("X8") + ")");
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct AuthenticatedCipherModeInfo
            {
                public int cbSize;
                public int dwInfoVersion;
                public IntPtr pbNonce;
                public int cbNonce;
                public IntPtr pbAuthData;
                public int cbAuthData;
                public IntPtr pbTag;
                public int cbTag;
                public IntPtr pbMacContext;
                public int cbMacContext;
                public int cbAAD;
                public long cbData;
                public int dwFlags;
            }

            [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
            private static extern uint BCryptOpenAlgorithmProvider(out IntPtr phAlgorithm, string pszAlgId, string pszImplementation, uint dwFlags);

            [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
            private static extern uint BCryptSetProperty(IntPtr hObject, string pszProperty, byte[] pbInput, int cbInput, uint dwFlags);

            [DllImport("bcrypt.dll")]
            private static extern uint BCryptGenerateSymmetricKey(IntPtr hAlgorithm, out IntPtr phKey, IntPtr pbKeyObject, int cbKeyObject,
                byte[] pbSecret, int cbSecret, uint dwFlags);

            [DllImport("bcrypt.dll")]
            private static extern uint BCryptDecrypt(IntPtr hKey, byte[] pbInput, int cbInput, ref AuthenticatedCipherModeInfo pPaddingInfo,
                byte[] pbIV, int cbIV, byte[] pbOutput, int cbOutput, out int pcbResult, uint dwFlags);

            [DllImport("bcrypt.dll")]
            private static extern uint BCryptDestroyKey(IntPtr hKey);

            [DllImport("bcrypt.dll")]
            private static extern uint BCryptCloseAlgorithmProvider(IntPtr hAlgorithm, uint dwFlags);
        }
#endif
    }
}
