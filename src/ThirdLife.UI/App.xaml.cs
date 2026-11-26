using System.Windows;
using ThirdLife.Inventory.Windows;
using ThirdLife.UI.Shell;
using ThirdLife.UI.Themes;

namespace ThirdLife.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var refusal = StartupSafety.OpenShell(
            WindowsPlatform.CheckCurrent(), StartupSafety.CheckCurrentProcess(), () =>
            {
                MainWindow = new MainWindow(AuditWorkspace.CreateLive(), persistAppearance: true);
                MainWindow.Show();
            });
        if (refusal is not null)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            TraceNotice.ShowMessage("ThirdLife Setup Core", refusal);
            Shutdown(1);
        }
    }
}
