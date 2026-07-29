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
    }

    public Task<StartupItemScanResult> ScanAsync(
        IProgress<StartupItemScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(progress, cancellationToken), cancellationToken);

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

    private static IReadOnlyList<RegistrySource> CreateRegistrySources()
    {
        var views = Environment.Is64BitOperatingSystem
            ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
            : new[] { RegistryView.Registry32 };
        var sources = new List<RegistrySource>();

        foreach (var view in views)
        {
            var viewLabel = view == RegistryView.Registry64 ? "64-bit" : "32-bit";
            var approvalLeaf = Environment.Is64BitOperatingSystem && view == RegistryView.Registry32
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

                    if (string.IsNullOrWhiteSpace(command))
                    {
                        AddOrMerge(items, new StartupItem(
                            displayName,
                            value?.ToString() ?? string.Empty,
                            source.Source,
                            source.DisplayLocation,
                            GetApprovalState(approvalStates, valueName),
                            StartupTargetState.Malformed));
                        errors.Add($"Startup entry '{displayName}' in '{source.DisplayLocation}' has an empty or unsupported command value.");
                        continue;
                    }

                    var targetState = StartupCommandInspector.DetermineTargetState(command);
                    AddOrMerge(items, new StartupItem(
                        displayName,
                        command,
                        source.Source,
                        source.DisplayLocation,
                        GetApprovalState(approvalStates, valueName),
                        targetState));

                    if (targetState == StartupTargetState.Malformed)
                    {
                        errors.Add($"Startup entry '{displayName}' in '{source.DisplayLocation}' contains a malformed command.");
                    }
                }
                catch (Exception ex) when (IsRecoverableRegistryException(ex))
                {
                    errors.Add($"Could not read startup entry '{valueName}' in '{source.DisplayLocation}': {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (IsRecoverableRegistryException(ex))
        {
            errors.Add($"Could not read startup location '{source.DisplayLocation}' ({source.Source}): {ex.Message}");
        }
    }

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
                new Dictionary<string, StartupItemState>(StringComparer.OrdinalIgnoreCase),
                WasReadable: true);

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var fileName = Path.GetFileName(filePath);
                    var displayName = Path.GetFileNameWithoutExtension(filePath);
                    var state = GetApprovalState(approvalStates, fileName);

                    if (string.Equals(Path.GetExtension(filePath), ".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        var shortcut = ShortcutResolver.Resolve(filePath);
                        if (!shortcut.Succeeded)
                        {
                            AddOrMerge(items, new StartupItem(
                                displayName,
                                filePath,
                                source,
                                filePath,
                                state,
                                StartupTargetState.Unresolved));
                            errors.Add($"Could not resolve startup shortcut '{filePath}': {shortcut.ErrorMessage}");
                            continue;
                        }

                        var command = QuoteCommandPath(shortcut.TargetPath);
                        AddOrMerge(items, new StartupItem(
                            displayName,
                            command,
                            source,
                            filePath,
                            state,
                            StartupCommandInspector.DetermineTargetState(command)));
                        continue;
                    }

                    AddOrMerge(items, new StartupItem(
                        displayName,
                        filePath,
                        source,
                        filePath,
                        state,
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
                    errors.Add($"Could not inspect startup item '{filePath}': {ex.Message}");
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
            errors.Add($"Could not enumerate startup location '{folderPath}': {ex.Message}");
        }
    }

    private static ApprovalLookup ReadFolderApprovalStates(
        RegistryHive primaryHive,
        ICollection<string> errors,
        string displayLocation)
    {
        var states = new Dictionary<string, StartupItemState>(StringComparer.OrdinalIgnoreCase);
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
                    if (!states.ContainsKey(pair.Key) || pair.Value == StartupItemState.Disabled)
                    {
                        states[pair.Key] = pair.Value;
                    }
                }
            }
        }

        return new ApprovalLookup(states, allSourcesReadable);
    }

    private static ApprovalLookup ReadApprovalStates(
        RegistryHive hive,
        RegistryView view,
        string approvalLeaf,
        ICollection<string> errors,
        string displayLocation)
    {
        var states = new Dictionary<string, StartupItemState>(StringComparer.OrdinalIgnoreCase);
        var keyPath = $@"{StartupApprovedRoot}\{approvalLeaf}";

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var approvalKey = baseKey.OpenSubKey(keyPath, writable: false);
            if (approvalKey is null)
            {
                return new ApprovalLookup(states, WasReadable: true);
            }

            foreach (var valueName in approvalKey.GetValueNames())
            {
                try
                {
                    var value = approvalKey.GetValue(
                        valueName,
                        defaultValue: null,
                        RegistryValueOptions.DoNotExpandEnvironmentNames);
                    states[valueName] = value is byte[] { Length: > 0 } data
                        ? data[0] switch
                        {
                            0x02 => StartupItemState.Enabled,
                            0x03 => StartupItemState.Disabled,
                            _ => StartupItemState.Unknown
                        }
                        : StartupItemState.Unknown;
                }
                catch (Exception ex) when (IsRecoverableRegistryException(ex))
                {
                    states[valueName] = StartupItemState.Unknown;
                    errors.Add($"Could not read startup status '{valueName}' for '{displayLocation}': {ex.Message}");
                }
            }

            return new ApprovalLookup(states, WasReadable: true);
        }
        catch (Exception ex) when (IsRecoverableRegistryException(ex))
        {
            errors.Add($"Could not read startup status metadata for '{displayLocation}': {ex.Message}");
            return new ApprovalLookup(states, WasReadable: false);
        }
    }

    private static StartupItemState GetApprovalState(
        ApprovalLookup approvalLookup,
        string valueName) =>
        approvalLookup.States.TryGetValue(valueName, out var state)
            ? state
            : approvalLookup.WasReadable
                ? StartupItemState.Enabled
                : StartupItemState.Unknown;

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

        var mergedSource = existing.Source.Contains(item.Source, StringComparison.OrdinalIgnoreCase)
            ? existing.Source
            : $"{existing.Source}; {item.Source}";
        var mergedState = existing.State == item.State
            ? existing.State
            : StartupItemState.Unknown;

        items[key] = existing with
        {
            Source = mergedSource,
            State = mergedState
        };
    }

    private static string NormalizeForComparison(string value) =>
        value.Trim().Trim('"').Replace('/', '\\');

    private static string QuoteCommandPath(string targetPath) =>
        targetPath.Contains(' ')
            ? $"\"{targetPath}\""
            : targetPath;

    private static bool IsRecoverableRegistryException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException;

    private sealed record ApprovalLookup(
        IReadOnlyDictionary<string, StartupItemState> States,
        bool WasReadable);

    private sealed record RegistrySource(
        RegistryHive Hive,
        RegistryView View,
        string Source,
        string DisplayLocation,
        string ApprovalLeaf);
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
