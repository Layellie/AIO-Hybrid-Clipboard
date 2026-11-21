using System;
using System.Runtime.InteropServices;
using System.Text;
using static AIO_Hybrid_Clipboard.Services.Win32Api;

namespace AIO_Hybrid_Clipboard.Services
{
    /// <summary>
    /// Zero-dependency Windows DPAPI (CurrentUser scope) encryption for local session storage.
    /// Uses native <c>crypt32.dll</c> directly and releases every unmanaged buffer deterministically.
    /// </summary>
    internal static class SessionCrypto
    {
        // 4-byte file header identifying DPAPI-encrypted session payloads ("AIOE").
        private static readonly byte[] MagicHeader = { (byte)'A', (byte)'I', (byte)'O', (byte)'E' };
        private static readonly byte[] Entropy     = Encoding.UTF8.GetBytes("AIO-Hybrid-Clipboard::Session::v2");

        public static bool IsEncrypted(ReadOnlySpan<byte> data) =>
            data.Length > MagicHeader.Length && data[..MagicHeader.Length].SequenceEqual(MagicHeader);

        public static byte[] Protect(byte[] plaintext)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));

            DATA_BLOB inBlob      = AllocBlob(plaintext);
            DATA_BLOB entropyBlob = AllocBlob(Entropy);
            DATA_BLOB outBlob     = default;

            try
            {
                if (!CryptProtectData(ref inBlob, "AIO Hybrid Clipboard Session", ref entropyBlob,
                                      IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out outBlob))
                {
                    throw new InvalidOperationException($"DPAPI CryptProtectData failed (Win32 error {Marshal.GetLastWin32Error()}).");
                }

                byte[] result = new byte[MagicHeader.Length + outBlob.cbData];
                Buffer.BlockCopy(MagicHeader, 0, result, 0, MagicHeader.Length);
                Marshal.Copy(outBlob.pbData, result, MagicHeader.Length, outBlob.cbData);
                return result;
            }
            finally
            {
                FreeHGlobalBlob(ref inBlob);
                FreeHGlobalBlob(ref entropyBlob);
                if (outBlob.pbData != IntPtr.Zero)
                    LocalFree(outBlob.pbData);
            }
        }

        public static bool TryUnprotect(byte[] payload, out byte[] plaintext)
        {
            plaintext = Array.Empty<byte>();
            if (payload == null || !IsEncrypted(payload))
                return false;

            byte[] cipher = new byte[payload.Length - MagicHeader.Length];
            Buffer.BlockCopy(payload, MagicHeader.Length, cipher, 0, cipher.Length);

            DATA_BLOB inBlob      = AllocBlob(cipher);
            DATA_BLOB entropyBlob = AllocBlob(Entropy);
            DATA_BLOB outBlob     = default;

            try
            {
                if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob,
                                        IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out outBlob))
                {
                    return false;
                }

                plaintext = new byte[outBlob.cbData];
                if (outBlob.cbData > 0)
                    Marshal.Copy(outBlob.pbData, plaintext, 0, outBlob.cbData);
                return true;
            }
            finally
            {
                FreeHGlobalBlob(ref inBlob);
                FreeHGlobalBlob(ref entropyBlob);
                if (outBlob.pbData != IntPtr.Zero)
                    LocalFree(outBlob.pbData);
            }
        }

        private static DATA_BLOB AllocBlob(byte[] bytes)
        {
            if (bytes.Length == 0)
                return new DATA_BLOB { cbData = 0, pbData = IntPtr.Zero };

            IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            return new DATA_BLOB { cbData = bytes.Length, pbData = ptr };
        }

        private static void FreeHGlobalBlob(ref DATA_BLOB blob)
        {
            if (blob.pbData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(blob.pbData);
                blob.pbData = IntPtr.Zero;
                blob.cbData = 0;
            }
        }
    }
}
