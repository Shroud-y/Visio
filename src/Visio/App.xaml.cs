using System.Windows;

namespace Visio;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        // Setup switches (used by register.ps1) run without a window.
        if (e.Args.Contains("--register", StringComparer.OrdinalIgnoreCase))
        {
            DefaultApp.Register();
            Shutdown();
            return;
        }
        if (e.Args.Contains("--unregister", StringComparer.OrdinalIgnoreCase))
        {
            DefaultApp.Unregister();
            Shutdown();
            return;
        }

        // "Visio.exe <file>" is how Explorer's "Open with" launches us.
        var path = e.Args.FirstOrDefault(a => !a.StartsWith('-'));
        new MainWindow(path).Show();
    }
}
