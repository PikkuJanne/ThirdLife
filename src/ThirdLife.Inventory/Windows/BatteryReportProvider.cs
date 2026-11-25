using System.Globalization;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;

namespace ThirdLife.Inventory.Windows;

public sealed class BatteryReportProvider : IInventoryProvider
{
    private const string ReportSource = "battery.powercfg.report";
    private const string LifecycleSource = "battery.report.lifecycle";
    private static readonly string[] PackFields =
    [
        "design_capacity_mwh", "full_charge_capacity_mwh", "cycle_count", "full_charge_ratio_percent",
    ];
    private readonly IBatteryReportSource _source;
    private int _collectionInProgress;

    public BatteryReportProvider()
        : this(new WindowsBatteryReportSource(), ProviderEvidenceOrigin.ActiveMachine)
    {
    }

    internal BatteryReportProvider(IBatteryReportSource source, ProviderEvidenceOrigin origin)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Descriptor = CreateDescriptor(origin);
    }

    public InventoryProviderDescriptor Descriptor { get; }

    public async ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _collectionInProgress, 1, 0) != 0)
        {
            return ProviderReadResult.Unavailable();
        }

        try
        {
            var snapshot = await Task.Run(() => _source.Read(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            if (!snapshot.CleanupComplete || snapshot.Status == BatteryReportSourceStatus.CleanupIncomplete)
            {
                return Normalize(snapshot);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Normalize(snapshot);
        }
        finally
        {
            // The runner can stop waiting before the native operation and cleanup return.
            // Retain this slot until the source actually finishes.
            Volatile.Write(ref _collectionInProgress, 0);
        }
    }

    private static InventoryProviderDescriptor CreateDescriptor(ProviderEvidenceOrigin origin)
    {
        var definitions = new List<ProviderEvidenceDefinition>
        {
            new(Key("present"), EvidenceValueKind.Boolean, null, ReportSource),
            new(Key("count"), EvidenceValueKind.WholeNumber, "count", ReportSource),
            new(Key("report.collection_status"), EvidenceValueKind.Enum, null, LifecycleSource),
            new(Key("report.cleanup_complete"), EvidenceValueKind.Boolean, null, LifecycleSource),
            new(Key("report.native_error_code"), EvidenceValueKind.WholeNumber, null, LifecycleSource),
            new(Key("report.exit_code"), EvidenceValueKind.WholeNumber, null, LifecycleSource),
        };
        foreach (var field in PackFields)
        {
            var ratio = field == "full_charge_ratio_percent";
            definitions.Add(new(PackKey(field), ratio ? EvidenceValueKind.DecimalNumber : EvidenceValueKind.WholeNumber,
                ratio ? "percent" : field == "cycle_count" ? "count" : "mwh", ReportSource, BatteryReportParser.MaximumBatteries));
        }

        return new(new ProviderId("windows-battery-report"), ProviderPrivilegeRequirement.StandardUser,
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30), ProviderNetworkUse.None, origin,
            [ProviderOperatingSystem.Windows11],
            new(Key("provider_status"), LifecycleSource), definitions);
    }

    private static ProviderReadResult Normalize(BatteryReportSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var cleanupComplete = snapshot.CleanupComplete && snapshot.Status != BatteryReportSourceStatus.CleanupIncomplete;
        var status = !cleanupComplete
            ? BatteryReportSourceStatus.CleanupIncomplete
            : Enum.IsDefined(snapshot.Status) ? snapshot.Status : BatteryReportSourceStatus.InvalidData;
        var report = snapshot.Report;
        var collected = status == BatteryReportSourceStatus.Collected && report?.Status == BatteryReportParseStatus.Collected &&
            report.Batteries.Count is > 0 and <= BatteryReportParser.MaximumBatteries &&
            report.Batteries.Select(static entry => entry.Ordinal).Order().SequenceEqual(Enumerable.Range(0, report.Batteries.Count));
        var absent = status == BatteryReportSourceStatus.NoBattery &&
            (report is null || report.Status == BatteryReportParseStatus.NoBattery);
        if ((status == BatteryReportSourceStatus.Collected && !collected) ||
            (status == BatteryReportSourceStatus.NoBattery && !absent))
        {
            status = BatteryReportSourceStatus.InvalidData;
        }

        var source = absent && report is null ? "battery.kernel32.system_power_status" : ReportSource;
        var evidence = new List<NormalizedEvidence>
        {
            NormalizedEvidence.Observed(Key("report.collection_status"), EvidenceValue.FromEnum(StatusCode(status)), sourceReference: LifecycleSource),
            NormalizedEvidence.Observed(Key("report.cleanup_complete"), EvidenceValue.FromBoolean(cleanupComplete), sourceReference: LifecycleSource),
            NativeCode("report.native_error_code", snapshot.NativeErrorCode),
            NativeCode("report.exit_code", snapshot.ExitCode),
            collected || absent
                ? NormalizedEvidence.Observed(Key("present"), EvidenceValue.FromBoolean(collected), sourceReference: source)
                : Missing(Key("present"), status == BatteryReportSourceStatus.InvalidData, source),
            collected || absent
                ? NormalizedEvidence.Observed(Key("count"), EvidenceValue.FromInteger(collected ? report!.Batteries.Count : 0), sourceReference: source)
                : Missing(Key("count"), status == BatteryReportSourceStatus.InvalidData, source),
        };

        if (collected)
        {
            foreach (var entry in report!.Batteries.OrderBy(static entry => entry.Ordinal))
            {
                var packSource = string.Create(CultureInfo.InvariantCulture, $"{ReportSource}.pack.{entry.Ordinal:D2}");
                evidence.Add(Number("design_capacity_mwh", entry.DesignCapacityMwh, packSource));
                evidence.Add(Number("full_charge_capacity_mwh", entry.FullChargeCapacityMwh, packSource));
                evidence.Add(Number("cycle_count", entry.CycleCount, packSource));
                var validDesign = IsKnownPositive(entry.DesignCapacityMwh);
                var validFull = IsKnownPositive(entry.FullChargeCapacityMwh);
                evidence.Add(validDesign && validFull
                    ? NormalizedEvidence.Inferred(PackKey("full_charge_ratio_percent"),
                        EvidenceValue.FromDecimal(decimal.Round(100m * entry.FullChargeCapacityMwh.GetRequiredValue() /
                            entry.DesignCapacityMwh.GetRequiredValue(), 2, MidpointRounding.ToEven)), sourceReference: packSource)
                    : Missing(PackKey("full_charge_ratio_percent"), IsMalformed(entry.DesignCapacityMwh) || IsMalformed(entry.FullChargeCapacityMwh), packSource));
            }
        }
        else
        {
            foreach (var field in PackFields)
            {
                evidence.Add(Missing(PackKey(field), status == BatteryReportSourceStatus.InvalidData, ReportSource));
            }
        }

        if (collected && evidence.Any(static item => item.Limitation == ProviderLimitation.SourceValueMalformed))
        {
            status = BatteryReportSourceStatus.InvalidData;
        }
        else if (collected && report!.Batteries.Any(static entry =>
            entry.DesignCapacityMwh.Status == SystemInventorySourceStatus.AccessDenied ||
            entry.FullChargeCapacityMwh.Status == SystemInventorySourceStatus.AccessDenied ||
            entry.CycleCount.Status == SystemInventorySourceStatus.AccessDenied))
        {
            status = BatteryReportSourceStatus.AccessDenied;
        }

        return status switch
        {
            BatteryReportSourceStatus.Collected or BatteryReportSourceStatus.NoBattery => ProviderReadResult.Collected(evidence),
            BatteryReportSourceStatus.AccessDenied => ProviderReadResult.AccessDenied(evidence),
            BatteryReportSourceStatus.InvalidData => ProviderReadResult.InvalidData(evidence),
            BatteryReportSourceStatus.CleanupIncomplete => ProviderReadResult.CleanupIncomplete(evidence),
            BatteryReportSourceStatus.Failed => ProviderReadResult.Failed(evidence),
            _ => ProviderReadResult.Unavailable(evidence),
        };
    }

    private static NormalizedEvidence Number(string field, SystemInventorySourceValue<uint>? value, string source) =>
        IsKnownPositive(value)
            ? NormalizedEvidence.Observed(PackKey(field), EvidenceValue.FromInteger(value!.GetRequiredValue()), sourceReference: source)
            : Missing(PackKey(field), IsMalformed(value), source);

    private static bool IsKnownPositive(SystemInventorySourceValue<uint>? value) =>
        value?.Status == SystemInventorySourceStatus.Available && value.GetRequiredValue() is > 0 and < uint.MaxValue;

    private static bool IsMalformed(SystemInventorySourceValue<uint>? value) => value is null ||
        value.Status == SystemInventorySourceStatus.InvalidData ||
        (value.Status == SystemInventorySourceStatus.Available && !IsKnownPositive(value));

    private static NormalizedEvidence NativeCode(string suffix, uint? value) => value is null
        ? Missing(Key(suffix), false, LifecycleSource)
        : NormalizedEvidence.Observed(Key(suffix), EvidenceValue.FromInteger(value.Value), sourceReference: LifecycleSource);

    private static NormalizedEvidence Missing(EvidenceKey key, bool malformed, string source) =>
        NormalizedEvidence.NotAvailable(key, malformed ? ProviderLimitation.SourceValueMalformed : ProviderLimitation.SourceValueMissing, source);

    private static EvidenceKey Key(string suffix) => new($"battery.{suffix}");
    private static EvidenceKey PackKey(string suffix) => Key($"pack.{suffix}");

    private static string StatusCode(BatteryReportSourceStatus status) => status switch
    {
        BatteryReportSourceStatus.Collected => "collected",
        BatteryReportSourceStatus.NoBattery => "no_battery",
        BatteryReportSourceStatus.AccessDenied => "access_denied",
        BatteryReportSourceStatus.InvalidData => "invalid_data",
        BatteryReportSourceStatus.Failed => "failed",
        BatteryReportSourceStatus.TimedOut => "timed_out",
        BatteryReportSourceStatus.Cancelled => "cancelled",
        BatteryReportSourceStatus.CleanupIncomplete => "cleanup_incomplete",
        _ => "unavailable",
    };
}
