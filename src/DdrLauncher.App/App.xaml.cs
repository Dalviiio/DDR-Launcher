using System.Windows;

namespace DdrLauncher.App;

public partial class App : Application
{
    // Two launchers writing the same library index would corrupt it.
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Local\DalviiioDDRLauncher", out var first);
        if (!first)
        {
            MessageBox.Show("Dalviiio DDR Launcher is already open.", "Dalviiio DDR Launcher");
            Shutdown();
            return;
        }

        base.OnStartup(e);
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
