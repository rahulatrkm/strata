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
            // The self test opens and closes a window. With the default
            // OnLastWindowClose that starts WPF shutting down on its own and the
            // process exit code stops being the one we set, so it is taken over
            // explicitly here and reported with Environment.Exit.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int code = SelfTest.Run();
            Environment.Exit(code);
            return;
        }

        new MainWindow().Show();
    }
}
