using System.Windows;
using System.Windows.Threading;
using BurhanNexus.LicenseKeyGenerator.Core;

namespace BurhanNexus.LicenseKeyGenerator;

public partial class App : Application
{
    public const string Title = "Burhan Nexus License Key Generator";

    public static string DataFolder { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Burhan Nexus", "License Key Generator");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, a) =>
        {
            a.Handled = true;
            MessageBox.Show("Unexpected error: " + a.Exception.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        };

        // Automated test mode used by CI:  --selftest <outDir> [--import-private <file>] [--device <id>]
        int i = Array.IndexOf(e.Args, "--selftest");
        if (i >= 0 && i + 1 < e.Args.Length)
        {
            string? Arg(string n) { int k = Array.IndexOf(e.Args, n); return k >= 0 && k + 1 < e.Args.Length ? e.Args[k + 1] : null; }
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(async () => Shutdown(await SelfTest.RunAsync(e.Args[i + 1], Arg("--import-private"), Arg("--device"))));
            return;
        }
        var w = new MainWindow();
        MainWindow = w;
        w.Show();
    }
}
