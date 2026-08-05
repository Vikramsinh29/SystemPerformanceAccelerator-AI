using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32;
using SystemPerformanceAccelerator.Core.Interfaces;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class WindowsDeviceIdentityProvider : IDeviceIdentityProvider
{
    private const string AppNamespace = "PC-SPA/DesktopAuth/V1";

    private readonly Func<string?> _machineGuidAccessor;
    private readonly Func<string?> _userSidAccessor;
    private readonly string _fallbackPath;
    private readonly object _syncRoot = new();

    public WindowsDeviceIdentityProvider(string fallbackPath)
        : this(
            fallbackPath,
            GetMachineGuid,
            () => WindowsIdentity.GetCurrent().User?.Value)
    {
    }

    internal WindowsDeviceIdentityProvider(
        string fallbackPath,
        Func<string?> machineGuidAccessor,
        Func<string?> userSidAccessor)
    {
        if (string.IsNullOrWhiteSpace(fallbackPath))
        {
            throw new ArgumentException(
                "A fallback device identity path is required.",
                nameof(fallbackPath));
        }

        _fallbackPath = Path.GetFullPath(fallbackPath);
        _machineGuidAccessor = machineGuidAccessor ??
            throw new ArgumentNullException(nameof(machineGuidAccessor));
        _userSidAccessor = userSidAccessor ??
            throw new ArgumentNullException(nameof(userSidAccessor));
    }

    public Task<string> GetDeviceIdAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            return Task.FromResult(GetOrCreate());
        }
    }

    private string GetOrCreate()
    {
        var source = BuildSource();
        if (!string.IsNullOrWhiteSpace(source))
        {
            return DeriveId(source);
        }

        var existing = TryReadFallback();
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var generated = DeriveId(Guid.NewGuid().ToString("N"));
        PersistFallback(generated);
        return generated;
    }

    private string? BuildSource()
    {
        var machineGuid = _machineGuidAccessor();
        var userSid = _userSidAccessor();
        if (string.IsNullOrWhiteSpace(machineGuid) ||
            string.IsNullOrWhiteSpace(userSid))
        {
            return null;
        }

        return $"{AppNamespace}|{machineGuid}|{userSid}";
    }

    private static string DeriveId(string source)
    {
        var bytes = SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private string? TryReadFallback()
    {
        try
        {
            return File.Exists(_fallbackPath)
                ? File.ReadAllText(_fallbackPath).Trim()
                : null;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            return null;
        }
    }

    private void PersistFallback(string deviceId)
    {
        var directory = Path.GetDirectoryName(_fallbackPath)
            ?? throw new InvalidOperationException(
                "The fallback identity path has no parent directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(_fallbackPath, deviceId);
    }

    private static string? GetMachineGuid()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Cryptography",
            writable: false);
        return key?.GetValue("MachineGuid") as string;
    }
}
