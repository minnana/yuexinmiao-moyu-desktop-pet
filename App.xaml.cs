using System.Windows;

namespace YueXinCat;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        base.OnStartup(e);
    }
}
