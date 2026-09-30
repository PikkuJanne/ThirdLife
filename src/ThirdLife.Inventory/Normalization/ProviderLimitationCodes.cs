namespace ThirdLife.Inventory.Normalization;

internal static class ProviderLimitationCodes
{
    public static string ToCode(this ProviderLimitation limitation) => limitation switch
    {
        ProviderLimitation.ProviderUnavailable => "provider_unavailable",
        ProviderLimitation.AccessDenied => "provider_access_denied",
        ProviderLimitation.CollectionCancelled => "provider_collection_cancelled",
        ProviderLimitation.CollectionTimedOut => "provider_collection_timed_out",
        ProviderLimitation.InvalidProviderData => "provider_data_invalid",
        ProviderLimitation.ProviderContractInvalid => "provider_contract_invalid",
        ProviderLimitation.CleanupIncomplete => "provider_cleanup_incomplete",
        ProviderLimitation.UnexpectedProviderFailure => "provider_failed",
        ProviderLimitation.SourceValueMissing => "provider_value_missing",
        ProviderLimitation.SourceValueMalformed => "provider_value_malformed",
        ProviderLimitation.SourceValuesConflict => "provider_values_conflict",
        ProviderLimitation.SourceValueStale => "provider_value_stale",
        ProviderLimitation.CapabilityNotPresent => "capability_not_present",
        _ => throw new ArgumentOutOfRangeException(nameof(limitation), limitation, "The limitation is not defined."),
    };
}
