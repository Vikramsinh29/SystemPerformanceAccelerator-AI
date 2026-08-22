namespace SystemPerformanceAccelerator.Infrastructure.Services;

public static class DesktopAuthorizationHandoffParser
{
    public const string Scheme = "pcspa";
    public const string Host = "authorize";
    public const string CodeParameter = "code";

    private const int MaximumUriLength = 2048;
    private const int MaximumAuthorizationCodeLength = 1024;

    public static DesktopAuthorizationHandoffParseResult Parse(
        string? activationValue)
    {
        if (string.IsNullOrWhiteSpace(activationValue))
        {
            return DesktopAuthorizationHandoffParseResult.Failed(
                "authorization_handoff_missing");
        }

        var candidate = activationValue.Trim();

        if (candidate.Length > MaximumUriLength)
        {
            return DesktopAuthorizationHandoffParseResult.Failed(
                "authorization_handoff_invalid");
        }

        if (!Uri.TryCreate(
                candidate,
                UriKind.Absolute,
                out var uri))
        {
            return DesktopAuthorizationHandoffParseResult.Failed(
                "authorization_handoff_invalid");
        }

        if (!string.Equals(
                uri.Scheme,
                Scheme,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                uri.Host,
                Host,
                StringComparison.OrdinalIgnoreCase))
        {
            return DesktopAuthorizationHandoffParseResult.Failed(
                "authorization_handoff_invalid");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) ||
            uri.Port != -1 ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.AbsolutePath) &&
                uri.AbsolutePath != "/")
        {
            return DesktopAuthorizationHandoffParseResult.Failed(
                "authorization_handoff_invalid");
        }

        var fragment = uri.Fragment;

        if (string.IsNullOrWhiteSpace(fragment) ||
            fragment.Length < 2)
        {
            return DesktopAuthorizationHandoffParseResult.Failed(
                "authorization_code_missing");
        }

        var fragmentValue = fragment[1..];

        string? authorizationCode = null;

        foreach (var pair in fragmentValue.Split(
                     '&',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');

            if (separatorIndex <= 0)
            {
                return DesktopAuthorizationHandoffParseResult.Failed(
                    "authorization_handoff_invalid");
            }

            var rawName = pair[..separatorIndex];
            var rawValue = pair[(separatorIndex + 1)..];

            string name;
            string value;

            try
            {
                name = Uri.UnescapeDataString(rawName);
                value = Uri.UnescapeDataString(rawValue);
            }
            catch (UriFormatException)
            {
                return DesktopAuthorizationHandoffParseResult.Failed(
                    "authorization_handoff_invalid");
            }

            if (!string.Equals(
                    name,
                    CodeParameter,
                    StringComparison.Ordinal))
            {
                return DesktopAuthorizationHandoffParseResult.Failed(
                    "authorization_handoff_invalid");
            }

            if (authorizationCode is not null)
            {
                return DesktopAuthorizationHandoffParseResult.Failed(
                    "authorization_handoff_invalid");
            }

            authorizationCode = value;
        }

        if (string.IsNullOrWhiteSpace(authorizationCode))
        {
            return DesktopAuthorizationHandoffParseResult.Failed(
                "authorization_code_missing");
        }

        var code = authorizationCode.Trim();

        if (code.Length > MaximumAuthorizationCodeLength ||
            code.Any(char.IsWhiteSpace))
        {
            return DesktopAuthorizationHandoffParseResult.Failed(
                "authorization_code_invalid");
        }

        return DesktopAuthorizationHandoffParseResult.Succeeded(
            code);
    }
}

public sealed record DesktopAuthorizationHandoffParseResult(
    bool Success,
    string? AuthorizationCode,
    string Code)
{
    public static DesktopAuthorizationHandoffParseResult Succeeded(
        string authorizationCode) =>
        new(
            true,
            authorizationCode,
            "authorization_handoff_valid");

    public static DesktopAuthorizationHandoffParseResult Failed(
        string code) =>
        new(
            false,
            null,
            code);
}