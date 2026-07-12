using System.Windows;
using MooRCON.Core;

namespace MooRCON.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.EnsureCreated();
    }
}
