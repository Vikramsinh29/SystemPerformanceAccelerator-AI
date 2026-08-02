using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Diagnostics;

public sealed class LocalDiagnosticService : IDiagnosticService
{
    private const int DefaultMaximumEventCount = 50;
    private static readonly TimeSpan DefaultMaximumEventAge =
        TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly DiagnosticPathSanitizer _sanitizer;
    private readonly InstallationIdentityService _identityService;
    private readonly DiagnosticPackageExporter _packageExporter;
    private readonly Func<DiagnosticEnvironment> _environmentProvider;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly int _maximumEventCount;
    private readonly TimeSpan _maximumEventAge;
    private readonly string _eventsDirectory;
    private volatile bool _isEnabled;
    private volatile bool _includeHardwareSummary;

    public LocalDiagnosticService(
        string? diagnosticsRoot = null,
        DiagnosticPathSanitizer? sanitizer = null,
        Func<DiagnosticEnvironment>? environmentProvider = null,
        Func<DateTimeOffset>? utcNow = null,
        int maximumEventCount = DefaultMaximumEventCount,
        TimeSpan? maximumEventAge = null)
    {
        if (maximumEventCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumEventCount));
        }

        DiagnosticsRoot = Path.GetFullPath(
            diagnosticsRoot ??
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "SystemPerformanceAccelerator",
                "diagnostics"));

        _eventsDirectory = Path.Combine(DiagnosticsRoot, "events");
        _sanitizer = sanitizer ?? new DiagnosticPathSanitizer();
        _environmentProvider = environmentProvider ??
            CaptureEnvironment;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _maximumEventCount = maximumEventCount;
        _maximumEventAge = maximumEventAge ??
            DefaultMaximumEventAge;

        _identityService = new InstallationIdentityService(
            Path.Combine(DiagnosticsRoot, "installation.json"));
        _packageExporter = new DiagnosticPackageExporter(
            _eventsDirectory,
            _sanitizer,
            _environmentProvider);
    }

    public bool IsEnabled => _isEnabled;

    public bool IncludeHardwareSummary =>
        _includeHardwareSummary;

    public string DiagnosticsRoot { get; }

    public string? InstallationId =>
        _identityService.TryGet();

    public DiagnosticEnvironment CurrentEnvironment =>
        _environmentProvider();

    public string? LatestErrorReference
    {
        get
        {
            if (!Directory.Exists(_eventsDirectory))
            {
                return null;
            }

            try
            {
                return Directory
                    .EnumerateFiles(
                        _eventsDirectory,
                        "ERR-*.json",
                        SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Select(file => Path.GetFileNameWithoutExtension(
                        file.Name))
                    .FirstOrDefault();
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException)
            {
                return null;
            }
        }
    }

    public void Configure(
        bool enabled,
        bool includeHardwareSummary)
    {
        _includeHardwareSummary = includeHardwareSummary;

        if (!enabled)
        {
            _isEnabled = false;
            return;
        }

        try
        {
            _identityService.GetOrCreate();
            _isEnabled = true;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            NotSupportedException or
            System.Security.SecurityException)
        {
            _isEnabled = false;
        }
    }

    public async Task<string?> RecordExceptionAsync(
        Exception exception,
        string feature,
        string operationStage,
        bool recovered,
        bool userDataMayHaveBeenAffected,
        DiagnosticSeverity severity = DiagnosticSeverity.Error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (!IsEnabled)
        {
            return null;
        }

        await _writeLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_eventsDirectory);

            var timestamp = _utcNow().ToUniversalTime();
            var referenceId =
                $"ERR-{timestamp:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..26]
                .ToUpperInvariant();

            var diagnosticEvent = new DiagnosticEvent(
                referenceId,
                _identityService.GetOrCreate(),
                timestamp,
                severity,
                _sanitizer.Sanitize(feature),
                _sanitizer.Sanitize(operationStage),
                exception.GetType().FullName ??
                    exception.GetType().Name,
                _sanitizer.Sanitize(exception.Message),
                _sanitizer.Sanitize(exception.ToString()),
                recovered,
                userDataMayHaveBeenAffected,
                _environmentProvider());

            var eventPath = Path.Combine(
                _eventsDirectory,
                referenceId + ".json");
            var temporaryPath = eventPath + ".tmp";

            try
            {
                var json = JsonSerializer.Serialize(
                    diagnosticEvent,
                    SerializerOptions);
                await File.WriteAllTextAsync(
                        temporaryPath,
                        json,
                        new UTF8Encoding(
                            encoderShouldEmitUTF8Identifier: false),
                        cancellationToken)
                    .ConfigureAwait(false);
                File.Move(
                    temporaryPath,
                    eventPath,
                    overwrite: true);
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }

            ApplyRetention(timestamp);
            return referenceId;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public DiagnosticExportPreview CreateExportPreview() =>
        _packageExporter.CreatePreview(
            DiagnosticsRoot,
            IncludeHardwareSummary);

    public Task<DiagnosticExportResult> ExportAsync(
        string destinationZipPath,
        CancellationToken cancellationToken = default) =>
        _packageExporter.ExportAsync(
            destinationZipPath,
            IncludeHardwareSummary,
            cancellationToken);

    public Task<DiagnosticExportResult> ExportFeedbackAsync(
        string destinationZipPath,
        DiagnosticFeedbackRequest feedback,
        CancellationToken cancellationToken = default) =>
        _packageExporter.ExportFeedbackAsync(
            destinationZipPath,
            feedback,
            IncludeHardwareSummary,
            cancellationToken);

    public void DeleteHistory()
    {
        DeleteDirectoryIfPresent(_eventsDirectory);
        DeleteDirectoryIfPresent(
            Path.Combine(DiagnosticsRoot, "exports"));
    }

    public void ResetInstallationId()
    {
        DeleteHistory();
        _identityService.Reset();

        if (IsEnabled)
        {
            _identityService.GetOrCreate();
        }
    }

    private void ApplyRetention(DateTimeOffset currentUtc)
    {
        if (!Directory.Exists(_eventsDirectory))
        {
            return;
        }

        try
        {
            var cutoffUtc = currentUtc.UtcDateTime -
                _maximumEventAge;

            var files = Directory
                .EnumerateFiles(
                    _eventsDirectory,
                    "*.json",
                    SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .ToArray();

            foreach (var file in files.Where(
                         file => file.LastWriteTimeUtc < cutoffUtc))
            {
                TryDeleteFile(file.FullName);
            }

            files = Directory
                .EnumerateFiles(
                    _eventsDirectory,
                    "*.json",
                    SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();

            foreach (var file in files.Skip(_maximumEventCount))
            {
                TryDeleteFile(file.FullName);
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            // Retention failure must not hide the diagnostic event
            // that was successfully written.
        }
    }

    private static DiagnosticEnvironment CaptureEnvironment()
    {
        var assembly = Assembly.GetEntryAssembly() ??
            typeof(LocalDiagnosticService).Assembly;
        var version = assembly.GetName().Version;
        var applicationVersion = version is null
            ? "1.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";

        var buildIdentifier = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? applicationVersion;

        var memory = GetMemorySnapshot();

        return new DiagnosticEnvironment(
            applicationVersion,
            buildIdentifier,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            IsProcessElevated(),
            memory.AvailableBytes,
            GetSystemDriveFreeBytes(),
            Environment.GetEnvironmentVariable(
                "PROCESSOR_IDENTIFIER"),
            memory.TotalBytes);
    }

    private static bool IsProcessElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(
                WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            return false;
        }
    }

    private static long? GetSystemDriveFreeBytes()
    {
        try
        {
            var windowsFolder = Environment.GetFolderPath(
                Environment.SpecialFolder.Windows);
            var root = Path.GetPathRoot(windowsFolder);
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            return drive.IsReady
                ? drive.AvailableFreeSpace
                : null;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            System.Security.SecurityException)
        {
            return null;
        }
    }

    private static MemorySnapshot GetMemorySnapshot()
    {
        if (!OperatingSystem.IsWindows())
        {
            var total = GC.GetGCMemoryInfo()
                .TotalAvailableMemoryBytes;
            var available = Math.Max(
                0,
                total - GC.GetTotalMemory(
                    forceFullCollection: false));
            return new MemorySnapshot(total, available);
        }

        var status = new MemoryStatusEx();
        if (GlobalMemoryStatusEx(ref status))
        {
            return new MemorySnapshot(
                status.TotalPhysical > long.MaxValue
                    ? long.MaxValue
                    : (long)status.TotalPhysical,
                status.AvailablePhysical > long.MaxValue
                    ? long.MaxValue
                    : (long)status.AvailablePhysical);
        }

        return new MemorySnapshot(null, null);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
        }
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(
        ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;

        public MemoryStatusEx()
        {
            this = default;
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        }
    }

    private sealed record MemorySnapshot(
        long? TotalBytes,
        long? AvailableBytes);
}
