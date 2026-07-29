using System.Windows;
using SystemPerformanceAccelerator.Desktop.ViewModels;
using SystemPerformanceAccelerator.Infrastructure.Services;

namespace SystemPerformanceAccelerator.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(new TemporaryFileService());
    }
}
