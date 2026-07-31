using System;
using System.Windows;
using System.Windows.Threading;

namespace SystemPerformanceAccelerator.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var splashWindow = new SplashWindow();
        splashWindow.Show();

        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                try
                {
                    var mainWindow = new MainWindow();
                    MainWindow = mainWindow;
                    mainWindow.Show();

                    splashWindow.Close();
                    ShutdownMode = ShutdownMode.OnMainWindowClose;
                }
                catch
                {
                    splashWindow.Close();
                    Shutdown();
                    throw;
                }
            }));
    }
}
