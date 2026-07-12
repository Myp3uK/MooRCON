using System.IO;
using System.Windows;
using System.Windows.Threading;
using MooRCON.Core;

namespace MooRCON.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.EnsureCreated();

        DispatcherUnhandledException += (_, args) =>
        {
            Log("DispatcherUnhandledException", args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log("UnobservedTaskException", args.Exception);
        };
    }

    private static void Log(string source, Exception? ex)
    {
        try
        {
            var path = Path.Combine(AppPaths.DataDir, "crash.log");
            File.AppendAllText(path,
                $"=== {DateTime.Now:O} [{source}] ==={Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }
}
