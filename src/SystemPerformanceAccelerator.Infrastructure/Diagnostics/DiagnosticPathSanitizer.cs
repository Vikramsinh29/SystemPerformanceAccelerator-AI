using System.Text.RegularExpressions;

namespace SystemPerformanceAccelerator.Infrastructure.Diagnostics;

public sealed class DiagnosticPathSanitizer
{
    private static readonly Regex EmailRegex = new(
        @"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UserProfilePathRegex = new(
        @"(?i)\b[A-Z]:\\Users\\[^\\\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex QuotedAbsolutePathRegex = new(
        @"(?i)(?<quote>[""'])(?:(?:[A-Z]:\\)|(?:\\\\))[^""'\r\n]+[""']",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RemainingAbsolutePathRegex = new(
        @"(?i)(?<![%A-Z0-9_])(?:(?:[A-Z]:\\)|(?:\\\\))[^\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IReadOnlyList<(string Path, string Token)> _knownPaths;
    private readonly Regex? _userNameRegex;

    public DiagnosticPathSanitizer(
        string? userProfile = null,
        string? userName = null,
        IReadOnlyDictionary<string, string>? knownPaths = null)
    {
        var resolvedUserName = userName ?? Environment.UserName;
        _userNameRegex = string.IsNullOrWhiteSpace(resolvedUserName)
            ? null
            : new Regex(
                $@"(?i)(?<![A-Z0-9_]){Regex.Escape(resolvedUserName)}(?![A-Z0-9_])",
                RegexOptions.Compiled |
                RegexOptions.CultureInvariant);
        _knownPaths = BuildKnownPaths(userProfile, knownPaths);
    }

    public string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sanitized = EmailRegex.Replace(value, "<redacted-email>");

        foreach (var knownPath in _knownPaths)
        {
            sanitized = ReplaceOrdinalIgnoreCase(
                sanitized,
                knownPath.Path,
                knownPath.Token);
        }

        sanitized = UserProfilePathRegex.Replace(
            sanitized,
            "%USERPROFILE%");

        sanitized = QuotedAbsolutePathRegex.Replace(
            sanitized,
            match => $"{match.Groups["quote"].Value}<redacted-path>{match.Groups["quote"].Value}");

        sanitized = RemainingAbsolutePathRegex.Replace(
            sanitized,
            "<redacted-path>");

        if (_userNameRegex is not null)
        {
            sanitized = _userNameRegex.Replace(
                sanitized,
                "<redacted-user>");
        }

        return sanitized;
    }

    private static IReadOnlyList<(string Path, string Token)> BuildKnownPaths(
        string? userProfile,
        IReadOnlyDictionary<string, string>? additionalPaths)
    {
        var paths = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        AddPath(
            paths,
            userProfile ?? Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            "%USERPROFILE%");
        AddPath(
            paths,
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "%LOCALAPPDATA%");
        AddPath(
            paths,
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "%APPDATA%");
        AddPath(paths, Path.GetTempPath(), "%TEMP%");
        AddPath(
            paths,
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "%WINDIR%");
        AddPath(
            paths,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "%PROGRAMFILES%");
        AddPath(
            paths,
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86),
            "%PROGRAMFILES(X86)%");
        AddPath(
            paths,
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData),
            "%PROGRAMDATA%");
        AddPath(paths, AppContext.BaseDirectory, "%APPDIR%");

        if (additionalPaths is not null)
        {
            foreach (var item in additionalPaths)
            {
                AddPath(paths, item.Key, item.Value);
            }
        }

        return paths
            .OrderByDescending(item => item.Key.Length)
            .Select(item => (item.Key, item.Value))
            .ToArray();
    }

    private static void AddPath(
        IDictionary<string, string> paths,
        string? path,
        string token)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalized = path.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        if (normalized.Length > 2)
        {
            paths[normalized] = token;
        }
    }

    private static string ReplaceOrdinalIgnoreCase(
        string source,
        string oldValue,
        string newValue)
    {
        var startIndex = 0;
        while (true)
        {
            var index = source.IndexOf(
                oldValue,
                startIndex,
                StringComparison.OrdinalIgnoreCase);

            if (index < 0)
            {
                return source;
            }

            source =
                source[..index] +
                newValue +
                source[(index + oldValue.Length)..];
            startIndex = index + newValue.Length;
        }
    }
}
