using System.Windows;

namespace ThirdLife.UI.Themes;

internal partial class TraceNotice : Window
{
    private TraceNotice(string heading, string message, Window? owner)
    {
        InitializeComponent();
        Title = heading;
        Heading.Text = heading;
        Message.Text = message;
        if (owner is null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _ = new TraceAppearance(this, persistPreference: true);
        }
        else
        {
            Owner = owner;
            if (Application.Current is not App)
            {
                // Isolated WPF hosts use window resources because they do not own an App.
                foreach (var dictionary in owner.Resources.MergedDictionaries)
                {
                    Resources.MergedDictionaries.Add(dictionary);
                }
            }
        }
    }

    internal static void ShowMessage(string heading, string message, Window? owner = null) =>
        new TraceNotice(heading, message, owner).ShowDialog();
}
