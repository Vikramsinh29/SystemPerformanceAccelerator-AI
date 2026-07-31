using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class StartupItemService : IStartupItemService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    private readonly string _currentUserStartupFolder;
    private readonly string _allUsersStartupFolder;
    private readonly bool _scanRegistry;
    private readonly IStartupItemStateBackend _stateBackend;

    public StartupItemService()
        : this(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            scanRegistry: true)
    {
    }

    public StartupItemService(
        string currentUserStartupFolder,
        string allUsersStartupFolder,
        bool scanRegistry)
    {
        _currentUserStartupFolder = currentUserStartupFolder;
        _allUsersStartupFolder = allUsersStartupFolder;
        _scanRegistry = scanRegistry;
        _stateBackend = new WindowsStartupItemStateBackend(
            currentUserStartupFolder,
            allUsersStartupFolder);
    }

    internal StartupItemService(
        string currentUserStartupFolder,
        string allUsersStartupFolder,
        bool scanRegistry,
        IStartupItemStateBackend stateBackend)
    {
        _currentUserStartupFolder = currentUserStartupFolder;
        _allUsersStartupFolder = allUsersStartupFolder;
        _scanRegistry = scanRegistry;
        _stateBackend = stateBackend ??
            throw new ArgumentNullException(nameof(stateBackend));
    }

    public Task<StartupItemScanResult> ScanAsync(
        IProgress<StartupItemScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(progress, cancellationToken), cancellationToken);

    public Task<StartupItemStateChangeResult> SetStateAsync(
        StartupItem item,
        StartupItemState requestedState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        return Task.Run(
            () => SetState(item, requestedState, cancellationToken),
            cancellationToken);
    }

    private StartupItemScanResult Scan(
        IProgress<StartupItemScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var items = new Dictionary<string, StartupItem>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        var registrySources = _scanRegistry ? CreateRegistrySources() : [];
        var totalLocations = registrySources.Count + 2;
        var locationsScanned = 0;

        foreach (var source in registrySources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanRegistrySource(source, items, errors, cancellationToken);
            locationsScanned++;
            progress?.Report(new StartupItemScanProgress(
                locationsScanned,
                totalLocations,
                source.DisplayLocation));
        }

        cancellationToken.ThrowIfCancellationRequested();
        ScanStartupFolder(
            _currentUserStartupFolder,
            "Startup folder — Current user",
            RegistryHive.CurrentUser,
            _scanRegistry,
            items,
            errors,
            cancellationToken);
        locationsScanned++;
        progress?.Report(new StartupItemScanProgress(
            locationsScanned,
            totalLocations,
            _currentUserStartupFolder));

        cancellationToken.ThrowIfCancellationRequested();
        ScanStartupFolder(
            _allUsersStartupFolder,
            "Startup folder — All users",
            RegistryHive.LocalMachine,
            _scanRegistry,
            items,
            errors,
            cancellationToken);
        locationsScanned++;
        progress?.Report(new StartupItemScanProgress(
            locationsScanned,
            totalLocations,
            _allUsersStartupFolder));

        return new StartupItemScanResult(
            items.Values
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Location, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            errors.ToArray(),
            locationsScanned,
            stopwatch.Elapsed);
    }

    private StartupItemStateChangeResult SetState(
        StartupItem item,
        StartupItemState requestedState,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (requestedState is not (
            StartupItemState.Enabled or StartupItemState.Disabled))
        {
            return Unsupported(
                requestedState,
                "Only Enabled or Disabled is a supported startup state.");
        }

        if (item.State == requestedState)
        {
            return new StartupItemStateChangeResult(
                StartupItemStateChangeOutcome.NoChange,
                requestedState,
                $"'{item.Name}' is already {requestedState.ToString().ToLowerInvariant()}.");
        }

        if (requestedState == StartupItemState.Disabled && !item.CanDisable)
        {
            return Unsupported(
                requestedState,
                string.IsNullOrWhiteSpace(item.StateChangeUnavailableReason)
                    ? "This startup entry cannot be disabled safely."
                    : item.StateChangeUnavailableReason);
        }

        if (requestedState == StartupItemState.Enabled && !item.CanEnable)
        {
            return Unsupported(
                requestedState,
                string.IsNullOrWhiteSpace(item.StateChangeUnavailableReason)
                    ? "This startup entry cannot be enabled safely."
                    : item.StateChangeUnavailableReason);
        }

        try
        {
            var snapshot = _stateBackend.Read(item);
            var validationMessage = ValidateSnapshot(
                item,
                snapshot,
                requestedState);

            if (validationMessage is not null)
            {
                return Stale(requestedState, validationMessage);
            }

            if (snapshot.State == requestedState)
            {
                return new StartupItemStateChangeResult(
                    StartupItemStateChangeOutcome.NoChange,
                    requestedState,
                    $"'{item.Name}' is already {requestedState.ToString().ToLowerInvariant()} in Windows.");
            }

            if (snapshot.State != item.State)
            {
                return Stale(
                    requestedState,
                    $"The state of '{item.Name}' changed after the scan. Run a fresh scan and try again.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var preWriteSnapshot = _stateBackend.Read(item);
            validationMessage = ValidateSnapshot(
                item,
                preWriteSnapshot,
                requestedState);

            if (validationMessage is not null ||
                preWriteSnapshot.State != snapshot.State)
            {
                return Stale(
                    requestedState,
                    validationMessage ??
                        $"The state of '{item.Name}' changed before Windows could be updated. Run a fresh scan.");
            }

            _stateBackend.Write(item, requestedState);
            cancellationToken.ThrowIfCancellationRequested();

            var verification = _stateBackend.Read(item);
            if (!verification.Exists ||
                verification.State != requestedState)
            {
                return new StartupItemStateChangeResult(
                    StartupItemStateChangeOutcome.Failed,
                    requestedState,
                    $"Windows did not confirm the requested state for '{item.Name}'. No startup command or file was deleted.");
            }

            var action = requestedState == StartupItemState.Enabled
                ? "enabled"
                : "disabled";
            return new StartupItemStateChangeResult(
                StartupItemStateChangeOutcome.Changed,
                requestedState,
                $"'{item.Name}' was {action}. Its original startup command or file was preserved.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            return new StartupItemStateChangeResult(
                StartupItemStateChangeOutcome.AccessDenied,
                requestedState,
                $"Windows denied permission to change '{item.Name}'. All-users entries may require administrator access. No startup command or file was deleted.");
        }
        catch (Exception ex) when (ex is
            IOException or
            InvalidOperationException or
            ArgumentException or
            NotSupportedException)
        {
            return new StartupItemStateChangeResult(
                StartupItemStateChangeOutcome.Failed,
                requestedState,
                $"Could not change '{item.Name}' safely: {ex.Message}");
        }
    }

    private static string? ValidateSnapshot(
        StartupItem item,
        StartupItemStateSnapshot snapshot,
        StartupItemState requestedState)
    {
        if (!snapshot.Exists)
        {
            return $"'{item.Name}' no longer exists in the scanned startup location. Run a fresh scan.";
        }

        if (!string.Equals(
            snapshot.Command,
            item.Command,
            StringComparison.Ordinal))
        {
            return $"The command or target for '{item.Name}' changed after the scan. Run a fresh scan.";
        }

        if (item.Kind == StartupItemKind.StartupFolder &&
            (snapshot.SourceLengthBytes != item.SourceLengthBytes ||
             snapshot.SourceLastWriteUtc != item.SourceLastWriteUtc))
        {
            return $"The Startup-folder file for '{item.Name}' changed after the scan. Run a fresh scan.";
        }

        if (snapshot.State == StartupItemState.Unknown)
        {
            return $"Windows could not confirm the current state of '{item.Name}'. No change was made.";
        }

        if (requestedState == StartupItemState.Enabled &&
            StartupCommandInspector.DetermineTargetState(snapshot.Command) !=
                StartupTargetState.Available)
        {
            return $"The target for '{item.Name}' is not currently available, so it cannot be enabled safely.";
        }

        return null;
    }

    private static StartupItemStateChangeResult Unsupported(
        StartupItemState requestedState,
        string message) =>
        new(
            StartupItemStateChangeOutcome.Unsupported,
            requestedState,
            message);

    private static StartupItemStateChangeResult Stale(
        StartupItemState requestedState,
        string message) =>
        new(
            StartupItemStateChangeOutcome.Stale,
            requestedState,
            message);

    private static IReadOnlyList<RegistrySource> CreateRegistrySources()
    {
        var views = Environment.Is64BitOperatingSystem
            ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
            : new[] { RegistryView.Registry32 };
        var sources = new List<RegistrySource>();

        foreach (var view in views)
        {
            var viewLabel = view == RegistryView.Registry64 ? "64-bit" : "32-bit";
            var approvalLeaf = Environment.Is64BitOperatingSystem &&
                view == RegistryView.Registry32
                    ? "Run32"
                    : "Run";

            sources.Add(new RegistrySource(
                RegistryHive.CurrentUser,
                view,
                $"Registry — Current user ({viewLabel})",
                @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
                approvalLeaf));
            sources.Add(new RegistrySource(
                RegistryHive.LocalMachine,
                view,
                $"Registry — All users ({viewLabel})",
                @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run",
                approvalLeaf));
        }

        return sources;
    }

    private static void ScanRegistrySource(
        RegistrySource source,
        IDictionary<string, StartupItem> items,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(source.Hive, source.View);
            using var runKey = baseKey.OpenSubKey(RunKeyPath, writable: false);
            if (runKey is null)
            {
                return;
            }

            var approvalStates = ReadApprovalStates(
                source.Hive,
                source.View,
                source.ApprovalLeaf,
                errors,
                source.DisplayLocation);

            foreach (var valueName in runKey.GetValueNames())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var value = runKey.GetValue(
                        valueName,
                        defaultValue: null,
                        RegistryValueOptions.DoNotExpandEnvironmentNames);
                    var command = value as string;
                    var displayName = string.IsNullOrWhiteSpace(valueName)
                        ? "(Default)"
                        : valueName;
                    var approval = GetApprovalRecord(
                        approvalStates,
                        valueName);

                    if (string.IsNullOrWhiteSpace(command))
                    {
                        AddOrMerge(items, CreateRegistryItem(
                            displayName,
                            value?.ToString() ?? string.Empty,
                            source,
                            valueName,
                            approval,
                            StartupTargetState.Malformed));
                        errors.Add(
                            $"Startup entry '{displayName}' in '{source.DisplayLocation}' has an empty or unsupported command value.");
                        continue;
                    }

                    var targetState =
                        StartupCommandInspector.DetermineTargetState(command);
                    AddOrMerge(items, CreateRegistryItem(
                        displayName,
                        command,
                        source,
                        valueName,
                        approval,
                        targetState));

                    if (targetState == StartupTargetState.Malformed)
                    {
                        errors.Add(
                            $"Startup entry '{displayName}' in '{source.DisplayLocation}' contains a malformed command.");
                    }
                }
                catch (Exception ex) when (IsRecoverableRegistryException(ex))
                {
                    errors.Add(
                        $"Could not read startup entry '{valueName}' in '{source.DisplayLocation}': {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (IsRecoverableRegistryException(ex))
        {
            errors.Add(
                $"Could not read startup location '{source.DisplayLocation}' ({source.Source}): {ex.Message}");
        }
    }

    private static StartupItem CreateRegistryItem(
        string displayName,
        string command,
        RegistrySource source,
        string valueName,
        ApprovalRecord approval,
        StartupTargetState targetState) =>
        new(
            displayName,
            command,
            source.Source,
            source.DisplayLocation,
            approval.State,
            targetState)
        {
            Kind = StartupItemKind.RegistryRun,
            SourceScope = ToItemScope(source.Hive),
            SourceRegistryView = ToItemView(source.View),
            EntryIdentifier = valueName,
            ApprovalScope = ToItemScope(approval.Hive),
            ApprovalRegistryView = ToItemView(approval.View),
            ApprovalCategory = approval.Category,
            HasAmbiguousStateIdentity = approval.IsAmbiguous
        };

    private static void ScanStartupFolder(
        string folderPath,
        string source,
        RegistryHive approvalHive,
        bool readApprovalMetadata,
        IDictionary<string, StartupItem> items,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            errors.Add($"The {source.ToLowerInvariant()} path is unavailable.");
            return;
        }

        try
        {
            var attributes = File.GetAttributes(folderPath);
            if ((attributes & FileAttributes.Directory) == 0)
            {
                errors.Add($"Startup location is not a folder: {folderPath}");
                return;
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            errors.Add($"Startup location does not exist: {folderPath}");
            return;
        }
        catch (Exception ex) when (ex is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            NotSupportedException or
            ArgumentException)
        {
            errors.Add($"Could not access startup location '{folderPath}': {ex.Message}");
            return;
        }

        var approvalStates = readApprovalMetadata
            ? ReadFolderApprovalStates(
                approvalHive,
                errors,
                folderPath)
            : new ApprovalLookup(
                new Dictionary<string, ApprovalRecord>(
                    StringComparer.OrdinalIgnoreCase),
                WasReadable: true,
                approvalHive,
                GetNativeRegistryView(),
                "StartupFolder");

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(
                folderPath,
                "*",
                SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var fileInfo = new FileInfo(filePath);
                    fileInfo.Refresh();
                    var fileName = fileInfo.Name;
                    var displayName = Path.GetFileNameWithoutExtension(filePath);
                    var approval = GetApprovalRecord(
                        approvalStates,
                        fileName);

                    if (string.Equals(
                        fileInfo.Extension,
                        ".lnk",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        var shortcut = ShortcutResolver.Resolve(filePath);
                        if (!shortcut.Succeeded)
                        {
                            AddOrMerge(items, CreateFolderItem(
                                displayName,
                                filePath,
                                source,
                                approvalHive,
                                fileInfo,
                                approval,
                                StartupTargetState.Unresolved));
                            errors.Add(
                                $"Could not resolve startup shortcut '{filePath}': {shortcut.ErrorMessage}");
                            continue;
                        }

                        var command = QuoteCommandPath(shortcut.TargetPath);
                        AddOrMerge(items, CreateFolderItem(
                            displayName,
                            command,
                            source,
                            approvalHive,
                            fileInfo,
                            approval,
                            StartupCommandInspector.DetermineTargetState(command)));
                        continue;
                    }

                    AddOrMerge(items, CreateFolderItem(
                        displayName,
                        filePath,
                        source,
                        approvalHive,
                        fileInfo,
                        approval,
                        StartupCommandInspector.DetermineTargetState(
                            QuoteCommandPath(filePath))));
                }
                catch (Exception ex) when (ex is
                    IOException or
                    UnauthorizedAccessException or
                    SecurityException or
                    NotSupportedException or
                    ArgumentException)
                {
                    errors.Add(
                        $"Could not inspect startup item '{filePath}': {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            NotSupportedException or
            ArgumentException)
        {
            errors.Add(
                $"Could not enumerate startup location '{folderPath}': {ex.Message}");
        }
    }

    private static StartupItem CreateFolderItem(
        string displayName,
        string command,
        string source,
        RegistryHive sourceHive,
        FileInfo fileInfo,
        ApprovalRecord approval,
        StartupTargetState targetState) =>
        new(
            displayName,
            command,
            source,
            fileInfo.FullName,
            approval.State,
            targetState)
        {
            Kind = StartupItemKind.StartupFolder,
            SourceScope = ToItemScope(sourceHive),
            SourceRegistryView = StartupRegistryView.NotApplicable,
            EntryIdentifier = fileInfo.Name,
            ApprovalScope = ToItemScope(approval.Hive),
            ApprovalRegistryView = ToItemView(approval.View),
            ApprovalCategory = approval.Category,
            SourceLengthBytes = fileInfo.Length,
            HasAmbiguousStateIdentity = approval.IsAmbiguous,
            SourceLastWriteUtc = new DateTimeOffset(
                fileInfo.LastWriteTimeUtc,
                TimeSpan.Zero)
        };

    private static ApprovalLookup ReadFolderApprovalStates(
        RegistryHive primaryHive,
        ICollection<string> errors,
        string displayLocation)
    {
        var states = new Dictionary<string, ApprovalRecord>(
            StringComparer.OrdinalIgnoreCase);
        var allSourcesReadable = true;
        var hives = primaryHive == RegistryHive.LocalMachine
            ? new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine }
            : new[] { RegistryHive.CurrentUser };
        var views = Environment.Is64BitOperatingSystem
            ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
            : new[] { RegistryView.Registry32 };

        foreach (var hive in hives)
        {
            foreach (var view in views)
            {
                var lookup = ReadApprovalStates(
                    hive,
                    view,
                    "StartupFolder",
                    errors,
                    displayLocation);
                allSourcesReadable &= lookup.WasReadable;

                foreach (var pair in lookup.States)
                {
                    if (!states.TryGetValue(pair.Key, out var existing))
                    {
                        states[pair.Key] = pair.Value;
                        continue;
                    }

                    var preferred = GetStatePriority(pair.Value.State) >
                        GetStatePriority(existing.State)
                            ? pair.Value
                            : existing;
                    states[pair.Key] = preferred with
                    {
                        IsAmbiguous = true
                    };
                }
            }
        }

        return new ApprovalLookup(
            states,
            allSourcesReadable,
            primaryHive,
            GetNativeRegistryView(),
            "StartupFolder");
    }

    private static ApprovalLookup ReadApprovalStates(
        RegistryHive hive,
        RegistryView view,
        string approvalLeaf,
        ICollection<string> errors,
        string displayLocation)
    {
        var states = new Dictionary<string, ApprovalRecord>(
            StringComparer.OrdinalIgnoreCase);
        var keyPath = $@"{StartupApprovedRoot}\{approvalLeaf}";

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var approvalKey = baseKey.OpenSubKey(
                keyPath,
                writable: false);
            if (approvalKey is null)
            {
                return new ApprovalLookup(
                    states,
                    WasReadable: true,
                    hive,
                    view,
                    approvalLeaf);
            }

            foreach (var valueName in approvalKey.GetValueNames())
            {
                try
                {
                    var value = approvalKey.GetValue(
                        valueName,
                        defaultValue: null,
                        RegistryValueOptions.DoNotExpandEnvironmentNames);
                    var state = value is byte[] { Length: > 0 } data
                        ? data[0] switch
                        {
                            0x02 => StartupItemState.Enabled,
                            0x03 => StartupItemState.Disabled,
                            _ => StartupItemState.Unknown
                        }
                        : StartupItemState.Unknown;
                    states[valueName] = new ApprovalRecord(
                        state,
                        hive,
                        view,
                        approvalLeaf,
                        IsAmbiguous: false);
                }
                catch (Exception ex) when (IsRecoverableRegistryException(ex))
                {
                    states[valueName] = new ApprovalRecord(
                        StartupItemState.Unknown,
                        hive,
                        view,
                        approvalLeaf,
                        IsAmbiguous: false);
                    errors.Add(
                        $"Could not read startup status '{valueName}' for '{displayLocation}': {ex.Message}");
                }
            }

            return new ApprovalLookup(
                states,
                WasReadable: true,
                hive,
                view,
                approvalLeaf);
        }
        catch (Exception ex) when (IsRecoverableRegistryException(ex))
        {
            errors.Add(
                $"Could not read startup status metadata for '{displayLocation}': {ex.Message}");
            return new ApprovalLookup(
                states,
                WasReadable: false,
                hive,
                view,
                approvalLeaf);
        }
    }

    private static ApprovalRecord GetApprovalRecord(
        ApprovalLookup approvalLookup,
        string valueName) =>
        approvalLookup.States.TryGetValue(valueName, out var approval)
            ? approval
            : new ApprovalRecord(
                approvalLookup.WasReadable
                    ? StartupItemState.Enabled
                    : StartupItemState.Unknown,
                approvalLookup.DefaultHive,
                approvalLookup.DefaultView,
                approvalLookup.Category,
                IsAmbiguous: false);

    private static void AddOrMerge(
        IDictionary<string, StartupItem> items,
        StartupItem item)
    {
        var key = string.Join(
            "\u001F",
            item.Name.Trim(),
            NormalizeForComparison(item.Command),
            NormalizeForComparison(item.Location));

        if (!items.TryGetValue(key, out var existing))
        {
            items.Add(key, item);
            return;
        }

        var mergedSource = existing.Source.Contains(
            item.Source,
            StringComparison.OrdinalIgnoreCase)
                ? existing.Source
                : $"{existing.Source}; {item.Source}";
        var mergedState = existing.State == item.State
            ? existing.State
            : StartupItemState.Unknown;

        items[key] = existing with
        {
            Source = mergedSource,
            State = mergedState,
            HasAmbiguousStateIdentity =
                existing.HasAmbiguousStateIdentity ||
                item.HasAmbiguousStateIdentity ||
                !HasSameStateIdentity(existing, item)
        };
    }

    private static bool HasSameStateIdentity(
        StartupItem first,
        StartupItem second) =>
        first.Kind == second.Kind &&
        first.SourceScope == second.SourceScope &&
        first.SourceRegistryView == second.SourceRegistryView &&
        string.Equals(
            first.EntryIdentifier,
            second.EntryIdentifier,
            StringComparison.OrdinalIgnoreCase) &&
        first.ApprovalScope == second.ApprovalScope &&
        first.ApprovalRegistryView == second.ApprovalRegistryView &&
        string.Equals(
            first.ApprovalCategory,
            second.ApprovalCategory,
            StringComparison.OrdinalIgnoreCase);

    private static int GetStatePriority(StartupItemState state) => state switch
    {
        StartupItemState.Disabled => 3,
        StartupItemState.Enabled => 2,
        _ => 1
    };

    private static RegistryView GetNativeRegistryView() =>
        Environment.Is64BitOperatingSystem
            ? RegistryView.Registry64
            : RegistryView.Registry32;

    private static StartupItemScope ToItemScope(RegistryHive hive) => hive switch
    {
        RegistryHive.CurrentUser => StartupItemScope.CurrentUser,
        RegistryHive.LocalMachine => StartupItemScope.AllUsers,
        _ => StartupItemScope.Unknown
    };

    private static StartupRegistryView ToItemView(RegistryView view) => view switch
    {
        RegistryView.Registry64 => StartupRegistryView.Registry64,
        RegistryView.Registry32 => StartupRegistryView.Registry32,
        _ => StartupRegistryView.NotApplicable
    };

    private static string NormalizeForComparison(string value) =>
        value.Trim().Trim('"').Replace('/', '\\');

    private static string QuoteCommandPath(string targetPath) =>
        targetPath.Contains(' ')
            ? $"\"{targetPath}\""
            : targetPath;

    private static bool IsRecoverableRegistryException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException;

    private sealed record ApprovalRecord(
        StartupItemState State,
        RegistryHive Hive,
        RegistryView View,
        string Category,
        bool IsAmbiguous);

    private sealed record ApprovalLookup(
        IReadOnlyDictionary<string, ApprovalRecord> States,
        bool WasReadable,
        RegistryHive DefaultHive,
        RegistryView DefaultView,
        string Category);

    private sealed record RegistrySource(
        RegistryHive Hive,
        RegistryView View,
        string Source,
        string DisplayLocation,
        string ApprovalLeaf);
}

internal interface IStartupItemStateBackend
{
    StartupItemStateSnapshot Read(StartupItem item);

    void Write(StartupItem item, StartupItemState requestedState);
}

internal sealed record StartupItemStateSnapshot(
    bool Exists,
    string Command,
    StartupItemState State,
    long? SourceLengthBytes,
    DateTimeOffset? SourceLastWriteUtc);

internal sealed class WindowsStartupItemStateBackend : IStartupItemStateBackend
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRoot =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    private readonly string _currentUserStartupFolder;
    private readonly string _allUsersStartupFolder;

    public WindowsStartupItemStateBackend(
        string currentUserStartupFolder,
        string allUsersStartupFolder)
    {
        _currentUserStartupFolder = currentUserStartupFolder;
        _allUsersStartupFolder = allUsersStartupFolder;
    }

    public StartupItemStateSnapshot Read(StartupItem item) =>
        item.Kind switch
        {
            StartupItemKind.RegistryRun => ReadRegistryItem(item),
            StartupItemKind.StartupFolder => ReadStartupFolderItem(item),
            _ => new StartupItemStateSnapshot(
                false,
                string.Empty,
                StartupItemState.Unknown,
                null,
                null)
        };

    public void Write(
        StartupItem item,
        StartupItemState requestedState)
    {
        if (requestedState is not (
            StartupItemState.Enabled or StartupItemState.Disabled))
        {
            throw new InvalidOperationException(
                "Only Enabled or Disabled can be written.");
        }

        if (!IsSupportedApprovalCategory(
            item.Kind,
            item.ApprovalCategory))
        {
            throw new InvalidOperationException(
                "The startup entry points to an unsupported Windows state location.");
        }

        var hive = ToRegistryHive(item.ApprovalScope);
        var view = ToRegistryView(item.ApprovalRegistryView);
        var keyPath =
            $@"{StartupApprovedRoot}\{item.ApprovalCategory}";
        var data = CreateApprovalData(
            requestedState,
            DateTimeOffset.UtcNow);

        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var approvalKey = baseKey.CreateSubKey(
            keyPath,
            writable: true) ??
            throw new IOException(
                $"Windows could not open startup state metadata '{keyPath}'.");

        approvalKey.SetValue(
            item.EntryIdentifier,
            data,
            RegistryValueKind.Binary);
    }

    internal static byte[] CreateApprovalData(
        StartupItemState state,
        DateTimeOffset timestampUtc)
    {
        if (state is not (
            StartupItemState.Enabled or StartupItemState.Disabled))
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "Only Enabled or Disabled has a Windows approval payload.");
        }

        var data = new byte[12];
        data[0] = state == StartupItemState.Enabled
            ? (byte)0x02
            : (byte)0x03;

        if (state == StartupItemState.Disabled)
        {
            BinaryPrimitives.WriteInt64LittleEndian(
                data.AsSpan(4, sizeof(long)),
                timestampUtc.UtcDateTime.ToFileTimeUtc());
        }

        return data;
    }

    private StartupItemStateSnapshot ReadRegistryItem(
        StartupItem item)
    {
        var hive = ToRegistryHive(item.SourceScope);
        var view = ToRegistryView(item.SourceRegistryView);

        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var runKey = baseKey.OpenSubKey(
            RunKeyPath,
            writable: false);
        if (runKey is null ||
            !runKey.GetValueNames().Contains(
                item.EntryIdentifier,
                StringComparer.OrdinalIgnoreCase))
        {
            return Missing();
        }

        var value = runKey.GetValue(
            item.EntryIdentifier,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is not string command)
        {
            return Missing();
        }

        return new StartupItemStateSnapshot(
            true,
            command,
            ReadApprovalState(item),
            null,
            null);
    }

    private StartupItemStateSnapshot ReadStartupFolderItem(
        StartupItem item)
    {
        var expectedRoot = item.SourceScope switch
        {
            StartupItemScope.CurrentUser => _currentUserStartupFolder,
            StartupItemScope.AllUsers => _allUsersStartupFolder,
            _ => string.Empty
        };

        if (!IsDirectChild(item.Location, expectedRoot) ||
            !File.Exists(item.Location))
        {
            return Missing();
        }

        var attributes = File.GetAttributes(item.Location);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            return Missing();
        }

        var fileInfo = new FileInfo(item.Location);
        fileInfo.Refresh();

        string command;
        if (string.Equals(
            fileInfo.Extension,
            ".lnk",
            StringComparison.OrdinalIgnoreCase))
        {
            var shortcut = ShortcutResolver.Resolve(fileInfo.FullName);
            command = shortcut.Succeeded
                ? QuoteCommandPath(shortcut.TargetPath)
                : fileInfo.FullName;
        }
        else
        {
            command = fileInfo.FullName;
        }

        return new StartupItemStateSnapshot(
            true,
            command,
            ReadApprovalState(item),
            fileInfo.Length,
            new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero));
    }

    private static StartupItemState ReadApprovalState(StartupItem item)
    {
        var hive = ToRegistryHive(item.ApprovalScope);
        var view = ToRegistryView(item.ApprovalRegistryView);
        var keyPath =
            $@"{StartupApprovedRoot}\{item.ApprovalCategory}";

        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var approvalKey = baseKey.OpenSubKey(
            keyPath,
            writable: false);
        if (approvalKey is null)
        {
            return StartupItemState.Enabled;
        }

        var value = approvalKey.GetValue(
            item.EntryIdentifier,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames);

        if (value is null)
        {
            return StartupItemState.Enabled;
        }

        return value is byte[] { Length: > 0 } data
            ? data[0] switch
            {
                0x02 => StartupItemState.Enabled,
                0x03 => StartupItemState.Disabled,
                _ => StartupItemState.Unknown
            }
            : StartupItemState.Unknown;
    }

    private static bool IsDirectChild(
        string candidatePath,
        string expectedRoot)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) ||
            string.IsNullOrWhiteSpace(expectedRoot))
        {
            return false;
        }

        try
        {
            var candidate = Path.GetFullPath(candidatePath);
            var root = Path.GetFullPath(expectedRoot)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            var parent = Path.GetDirectoryName(candidate)?
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            return string.Equals(
                parent,
                root,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is
            ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsSupportedApprovalCategory(
        StartupItemKind kind,
        string category) => kind switch
        {
            StartupItemKind.RegistryRun => category is "Run" or "Run32",
            StartupItemKind.StartupFolder => category == "StartupFolder",
            _ => false
        };

    private static RegistryHive ToRegistryHive(
        StartupItemScope scope) => scope switch
        {
            StartupItemScope.CurrentUser => RegistryHive.CurrentUser,
            StartupItemScope.AllUsers => RegistryHive.LocalMachine,
            _ => throw new InvalidOperationException(
                "The startup entry has no supported registry scope.")
        };

    private static RegistryView ToRegistryView(
        StartupRegistryView view) => view switch
        {
            StartupRegistryView.Registry64 => RegistryView.Registry64,
            StartupRegistryView.Registry32 => RegistryView.Registry32,
            _ => throw new InvalidOperationException(
                "The startup entry has no supported registry view.")
        };

    private static string QuoteCommandPath(string targetPath) =>
        targetPath.Contains(' ')
            ? $"\"{targetPath}\""
            : targetPath;

    private static StartupItemStateSnapshot Missing() =>
        new(
            false,
            string.Empty,
            StartupItemState.Unknown,
            null,
            null);
}

public static partial class StartupCommandInspector
{
    private static readonly string[] ExecutableExtensions = [
        ".exe",
        ".com",
        ".bat",
        ".cmd",
        ".ps1",
        ".vbs",
        ".js",
        ".dll"
    ];

    [GeneratedRegex(@"^(?<path>.+?\.(?:exe|com|bat|cmd|ps1|vbs|js|dll))(?=$|[\s,])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExecutablePathRegex();

    public static StartupTargetState DetermineTargetState(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return StartupTargetState.Malformed;
        }

        string expandedCommand;
        try
        {
            expandedCommand = Environment.ExpandEnvironmentVariables(command).Trim();
        }
        catch (ArgumentException)
        {
            return StartupTargetState.Malformed;
        }

        if (expandedCommand.Length == 0)
        {
            return StartupTargetState.Malformed;
        }

        string target;
        if (expandedCommand[0] == '"')
        {
            var closingQuote = expandedCommand.IndexOf('"', 1);
            if (closingQuote < 0)
            {
                return StartupTargetState.Malformed;
            }

            target = expandedCommand[1..closingQuote].Trim();
        }
        else
        {
            var existingPath = FindExistingPathPrefix(expandedCommand);
            var match = ExecutablePathRegex().Match(expandedCommand);
            target = existingPath ?? (match.Success
                ? match.Groups["path"].Value.Trim()
                : ReadFirstToken(expandedCommand));
        }

        if (target.Length == 0)
        {
            return StartupTargetState.Malformed;
        }

        if (File.Exists(target) || Directory.Exists(target))
        {
            return StartupTargetState.Available;
        }

        if (Path.IsPathFullyQualified(target))
        {
            return InspectFullyQualifiedTarget(target);
        }

        if (target.Contains(Path.DirectorySeparatorChar) ||
            target.Contains(Path.AltDirectorySeparatorChar))
        {
            return StartupTargetState.Unresolved;
        }

        return FindOnSearchPath(target)
            ? StartupTargetState.Available
            : HasExecutableExtension(target)
                ? StartupTargetState.Missing
                : StartupTargetState.Unresolved;
    }


    private static string? FindExistingPathPrefix(string command)
    {
        if (File.Exists(command) || Directory.Exists(command))
        {
            return command;
        }

        for (var index = command.Length - 1; index >= 0; index--)
        {
            if (!char.IsWhiteSpace(command[index]))
            {
                continue;
            }

            var candidate = command[..index].Trim();
            if (candidate.Length > 0 &&
                (File.Exists(candidate) || Directory.Exists(candidate)))
            {
                return candidate;
            }
        }

        return null;
    }

    private static StartupTargetState InspectFullyQualifiedTarget(string target)
    {
        try
        {
            _ = File.GetAttributes(target);
            return StartupTargetState.Available;
        }
        catch (FileNotFoundException)
        {
            return StartupTargetState.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return StartupTargetState.Missing;
        }
        catch (Exception ex) when (ex is
            UnauthorizedAccessException or
            SecurityException or
            IOException)
        {
            return StartupTargetState.Unresolved;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return StartupTargetState.Malformed;
        }
    }

    private static string ReadFirstToken(string command)
    {
        var whitespaceIndex = command.IndexOfAny([' ', '\t', '\r', '\n']);
        return whitespaceIndex < 0
            ? command.Trim()
            : command[..whitespaceIndex].Trim();
    }

    private static bool FindOnSearchPath(string target)
    {
        var candidateNames = HasExecutableExtension(target)
            ? new[] { target }
            : GetPathExtensions().Select(extension => target + extension).Prepend(target);
        var searchFolders = GetSearchFolders();

        foreach (var folder in searchFolders)
        {
            foreach (var candidateName in candidateNames)
            {
                try
                {
                    if (File.Exists(Path.Combine(folder, candidateName)))
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (ex is
                    ArgumentException or
                    NotSupportedException or
                    PathTooLongException)
                {
                    return false;
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> GetSearchFolders()
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddIfPresent(Environment.SystemDirectory);
        AddIfPresent(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddIfPresent(folder.Trim('"'));
        }

        return folders;

        void AddIfPresent(string folder)
        {
            if (!string.IsNullOrWhiteSpace(folder))
            {
                folders.Add(folder);
            }
        }
    }

    private static IEnumerable<string> GetPathExtensions()
    {
        var configured = (Environment.GetEnvironmentVariable("PATHEXT") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(extension => extension.StartsWith(".", StringComparison.Ordinal));

        return configured.Any()
            ? configured
            : ExecutableExtensions;
    }

    private static bool HasExecutableExtension(string path) =>
        ExecutableExtensions.Contains(
            Path.GetExtension(path),
            StringComparer.OrdinalIgnoreCase);
}

internal static class ShortcutResolver
{
    private const int MaximumPathLength = 32_768;

    public static ShortcutResolution Resolve(string shortcutPath)
    {
        object? shellLinkObject = null;

        try
        {
            shellLinkObject = new ShellLink();
            var shellLink = (IShellLinkW)shellLinkObject;
            var persistFile = (IPersistFile)shellLinkObject;
            persistFile.Load(shortcutPath, 0);

            var targetBuilder = new StringBuilder(MaximumPathLength);
            shellLink.GetPath(targetBuilder, targetBuilder.Capacity, IntPtr.Zero, 0);

            var targetPath = Environment.ExpandEnvironmentVariables(targetBuilder.ToString()).Trim();
            if (targetPath.Length == 0)
            {
                return new ShortcutResolution(false, string.Empty, "The shortcut target is empty.");
            }

            return new ShortcutResolution(
                true,
                targetPath,
                string.Empty);
        }
        catch (Exception ex) when (ex is
            COMException or
            InvalidCastException or
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            ArgumentException or
            NotSupportedException)
        {
            return new ShortcutResolution(false, string.Empty, ex.Message);
        }
        finally
        {
            try
            {
                if (shellLinkObject is not null && Marshal.IsComObject(shellLinkObject))
                {
                    Marshal.FinalReleaseComObject(shellLinkObject);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidComObjectException)
            {
                // The scan is read-only; a failed COM release must not crash it.
            }
        }
    }

    internal sealed record ShortcutResolution(
        bool Succeeded,
        string TargetPath,
        string ErrorMessage);

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink
    {
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file,
            int maximumPath,
            IntPtr findData,
            uint flags);
    }
}
