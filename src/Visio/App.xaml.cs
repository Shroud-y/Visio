using System.Windows;

namespace Visio;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        // "Visio.exe <file>" is how Explorer's "Open with" launches us.
        var path = e.Args.FirstOrDefault(a => !a.StartsWith('-'));
        new MainWindow(path).Show();
    }
}
