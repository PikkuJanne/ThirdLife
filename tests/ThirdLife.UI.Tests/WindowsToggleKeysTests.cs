using ThirdLife.UI.Features.Finalization;

namespace ThirdLife.UI.Tests;

public sealed class WindowsToggleKeysTests
{
    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void NativeReadIsStableWithinTheSameUnelevatedUserContext()
    {
        var adapter = new WindowsToggleKeys();
        var initial = adapter.Read();
        Assert.NotNull(initial.Enabled);
        Assert.NotEqual(Guid.Empty, initial.UserBinding);
        Assert.Equal(initial, adapter.Read());
    }
}
