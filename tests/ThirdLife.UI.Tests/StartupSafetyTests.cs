using System.Security.Principal;
using System.Xml;
using System.Xml.Linq;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI.Tests;

public sealed class StartupSafetyTests
{
    [Theory]
    [InlineData(StartupAccess.StandardUser, true)]
    [InlineData(StartupAccess.Unknown, false)]
    [InlineData(StartupAccess.Elevated, false)]
    [InlineData((StartupAccess)(-1), false)]
    [InlineData((StartupAccess)int.MaxValue, false)]
    public void OnlyConfirmedStandardUserAccessCanOpenTheShell(StartupAccess access, bool expected)
    {
        Assert.Equal(expected, StartupSafety.AllowsShell(access));
        Assert.Contains("without Run as administrator", StartupSafety.RecoveryMessage, StringComparison.Ordinal);
        Assert.Contains("No job was opened", StartupSafety.RecoveryMessage, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void CurrentWindowsTokenProducesAnExplicitStartupDecision()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var isAdministrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        var access = StartupSafety.CheckCurrentProcess();
        Assert.Equal(isAdministrator ? StartupAccess.Elevated : StartupAccess.StandardUser, access);
        Assert.Equal(!isAdministrator, StartupSafety.AllowsShell(access));
    }

    [Fact]
    public void PackagedManifestRequestsNoElevationOrUiAccessAndDeclaresPerMonitorScaling()
    {
        using var manifest = typeof(App).Assembly.GetManifestResourceStream("ThirdLife.UI.app.manifest");
        Assert.NotNull(manifest);
        using var reader = XmlReader.Create(manifest, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader);
        XNamespace security = "urn:schemas-microsoft-com:asm.v3";
        XNamespace dpi = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";
        var request = Assert.Single(document.Descendants(security + "requestedExecutionLevel"));
        Assert.Equal("asInvoker", request.Attribute("level")?.Value);
        Assert.Equal("false", request.Attribute("uiAccess")?.Value);
        Assert.Equal("PerMonitorV2", Assert.Single(document.Descendants(dpi + "dpiAwareness")).Value);
    }
}
