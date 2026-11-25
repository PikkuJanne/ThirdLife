using ThirdLife.Inventory.Windows;

namespace ThirdLife.Broker;

internal static class Program
{
    private static int Main() => WindowsPlatform.CheckCurrent() == WindowsPlatformEligibility.Supported ? 0 : 2;
}
