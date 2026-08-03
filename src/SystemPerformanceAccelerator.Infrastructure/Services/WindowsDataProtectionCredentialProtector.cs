using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class WindowsDataProtectionCredentialProtector :
    ICredentialProtector
{
    private static readonly byte[] OptionalEntropy =
        "PC-SPA controlled beta entitlement v1"u8.ToArray();

    public byte[] Protect(byte[] plaintext) =>
        Transform(plaintext, protect: true);

    public byte[] Unprotect(byte[] protectedData) =>
        Transform(protectedData, protect: false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows Data Protection is available only on Windows.");
        }

        var inputBlob = CreateBlob(input);
        var entropyBlob = CreateBlob(OptionalEntropy);
        DataBlob outputBlob = default;
        try
        {
            var succeeded = protect
                ? CryptProtectData(
                    ref inputBlob,
                    null,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob)
                : CryptUnprotectData(
                    ref inputBlob,
                    IntPtr.Zero,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob);

            if (!succeeded)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var result = new byte[outputBlob.Size];
            Marshal.Copy(outputBlob.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            FreeBlob(ref inputBlob, localFree: false);
            FreeBlob(ref entropyBlob, localFree: false);
            FreeBlob(ref outputBlob, localFree: true);
        }
    }

    private static DataBlob CreateBlob(byte[] value)
    {
        var data = Marshal.AllocHGlobal(value.Length);
        Marshal.Copy(value, 0, data, value.Length);
        return new DataBlob { Size = value.Length, Data = data };
    }

    private static void FreeBlob(ref DataBlob blob, bool localFree)
    {
        if (blob.Data == IntPtr.Zero)
        {
            return;
        }

        if (localFree)
        {
            LocalFree(blob.Data);
        }
        else
        {
            Marshal.FreeHGlobal(blob.Data);
        }

        blob = default;
    }

    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("Kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
