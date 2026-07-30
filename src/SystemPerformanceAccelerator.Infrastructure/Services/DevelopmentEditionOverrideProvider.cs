using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class DevelopmentEditionOverrideProvider :
    IDevelopmentEditionOverrideProvider
{
    public const string EnvironmentVariableName =
        "SPA_DEVELOPMENT_EDITION";

    private readonly bool _isEnabled;
    private readonly Func<string?> _valueReader;

    public DevelopmentEditionOverrideProvider(
        bool isEnabled = false,
        Func<string?>? valueReader = null)
    {
        _isEnabled = isEnabled;
        _valueReader = valueReader ??
            (() => Environment.GetEnvironmentVariable(EnvironmentVariableName));
    }

    public ApplicationEdition? GetOverride()
    {
        if (!_isEnabled)
        {
            return null;
        }

        string? value;
        try
        {
            value = _valueReader();
        }
        catch
        {
            return null;
        }

        return value?.Trim().ToUpperInvariant() switch
        {
            "TRIAL" => ApplicationEdition.Trial,
            "FREE" => ApplicationEdition.Free,
            "STANDARD" => ApplicationEdition.Standard,
            "PRO" => ApplicationEdition.Pro,
            "BUSINESS" => ApplicationEdition.Business,
            _ => null
        };
    }
}
