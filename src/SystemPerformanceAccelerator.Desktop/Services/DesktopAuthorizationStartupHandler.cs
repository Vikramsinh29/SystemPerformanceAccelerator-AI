using SystemPerformanceAccelerator.Infrastructure.Services;

namespace SystemPerformanceAccelerator.Desktop.Services;

public static class DesktopAuthorizationStartupHandler
{
    public static string? SelectAuthorizationActivation(
        IReadOnlyList<string>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return null;
        }

        foreach (var argument in arguments)
        {
            var parsed =
                DesktopAuthorizationHandoffParser.Parse(
                    argument);

            if (parsed.Success)
            {
                return argument;
            }
        }

        return null;
    }

    public static async Task<
        DesktopAuthorizationHandoffResult?>
        TryHandleAsync(
            IReadOnlyList<string>? arguments,
            DesktopAuthorizationHandoffService handoffService,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            handoffService);

        var activation =
            SelectAuthorizationActivation(
                arguments);

        if (activation is null)
        {
            return null;
        }

        return await handoffService
            .HandleAsync(
                activation,
                cancellationToken)
            .ConfigureAwait(false);
    }
}