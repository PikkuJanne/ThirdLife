using System.Windows;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var startup = StartupSafety.CheckCurrentProcess();
        if (!StartupSafety.AllowsShell(startup))
        {
            MessageBox.Show(StartupSafety.RecoveryMessage, "ThirdLife Setup Core", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
