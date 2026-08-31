using System.Windows;

namespace Strata.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Headless check of the real scan, layout and render path, so the UI can
        // be verified on a machine with nobody watching the screen.
        if (e.Args.Contains("--selftest"))
        {
            int code = SelfTest.Run();
            Shutdown(code);
            return;
        }

        new MainWindow().Show();
    }
}
