namespace SystemPerformanceAccelerator.Core.Models;

public sealed record CustomCleanPreviewItem(
    CustomCleanCategory Category,
    string FullPath,
    long SizeBytes,
    DateTime LastWriteTimeUtc)
{
    public string CategoryName => Category switch
    {
        CustomCleanCategory.TemporaryFiles => "Current-user temporary files",
        _ => Category.ToString()
    };

    public string Name => Path.GetFileName(FullPath);
}
