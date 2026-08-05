namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class NullDesktopApiLogger : IDesktopApiLogger
{
    public static NullDesktopApiLogger Instance { get; } = new();

    private NullDesktopApiLogger()
    {
    }

    public void Log(DesktopApiLogEntry entry)
    {
    }
}
