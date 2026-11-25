using System.Windows;
using ThirdLife.Inventory.Windows;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var refusal = StartupSafety.OpenShell(
            WindowsPlatform.CheckCurrent(), StartupSafety.CheckCurrentProcess(), () =>
            {
                MainWindow = new MainWindow(new ShellViewModel(), AuditWorkspace.CreateLive());
                MainWindow.Show();
            });
        if (refusal is not null)
        {
            MessageBox.Show(refusal, "ThirdLife Setup Core", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
        }
    }
}
