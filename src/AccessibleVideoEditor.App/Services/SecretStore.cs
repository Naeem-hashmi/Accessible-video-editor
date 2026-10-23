using System.Runtime.InteropServices;
using System.Text;

namespace AccessibleVideoEditor.App.Services;

/// <summary>
/// Keeps an API key encrypted with Windows DPAPI for the current Windows user: the settings file only holds
/// scrambled text that other users, and copies of the file on other computers, can't read.
/// </summary>
public static class SecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ClipScribe AI key");

    public static string Protect(string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return "";
        }

        return Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(secret), protect: true));
    }

    /// <summary>The key, or empty when there is none or it was saved by another user or computer.</summary>
    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return "";
        }

        try
        {
            return Encoding.UTF8.GetString(Transform(Convert.FromBase64String(stored), protect: false));
        }
        catch (Exception ex) when (ex is FormatException or System.ComponentModel.Win32Exception)
        {
            return "";
        }
    }

    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = new DataBlob(data);
        var entropy = new DataBlob(Entropy);
        try
        {
            var ok = protect
                ? CryptProtectData(ref input.Blob, "ClipScribe", ref entropy.Blob, IntPtr.Zero, IntPtr.Zero, UiForbidden, out var output)
                : CryptUnprotectData(ref input.Blob, IntPtr.Zero, ref entropy.Blob, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
            if (!ok)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally
            {
                LocalFree(output.Data);
            }
        }
        finally
        {
            input.Dispose();
            entropy.Dispose();
        }
    }

    private const int UiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Size;
        public IntPtr Data;
    }

    private sealed class DataBlob : IDisposable
    {
        public Blob Blob;

        public DataBlob(byte[] bytes)
        {
            Blob.Size = bytes.Length;
            Blob.Data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
            Marshal.Copy(bytes, 0, Blob.Data, bytes.Length);
        }

        public void Dispose() => Marshal.FreeHGlobal(Blob.Data);
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob dataIn, string description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Blob dataIn, IntPtr description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);
}
