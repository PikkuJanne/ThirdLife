namespace ThirdLife.Inventory.Providers;

public interface IInventoryProvider
{
    InventoryProviderDescriptor Descriptor { get; }

    ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default);
}
