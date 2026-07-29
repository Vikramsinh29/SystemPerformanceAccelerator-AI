using System.Windows;
using SystemPerformanceAccelerator.Desktop.ViewModels;
using SystemPerformanceAccelerator.Infrastructure.Services;

namespace SystemPerformanceAccelerator.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var largeFileCleanupService = new LargeFileCleanupService();
        DataContext = new MainWindowViewModel(
            new TemporaryFileService(),
            new LargeFileService(),
            largeFileCleanupService,
            new DuplicateFileService(),
            new DuplicateFileCleanupService(largeFileCleanupService),
            new StartupItemService());
    }
}
