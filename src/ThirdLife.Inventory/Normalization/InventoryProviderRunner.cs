using System.IO;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Providers;

namespace ThirdLife.Inventory.Normalization;

public sealed class InventoryProviderRunner
{
    private readonly TimeProvider _timeProvider;

    public InventoryProviderRunner()
        : this(TimeProvider.System)
    {
    }

    public InventoryProviderRunner(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async ValueTask<ProviderRunResult> RunAsync(
        IInventoryProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var descriptor = provider.Descriptor
            ?? throw new ArgumentException("A provider must expose its descriptor.", nameof(provider));
        var collectedAtUtc = _timeProvider.GetUtcNow();

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateFailure(
                descriptor,
                ProviderRunOutcome.Cancelled,
                collectedAtUtc,
                providerInvoked: false);
        }

        ProviderReadResult? readResult;
        Task<ProviderReadResult>? providerTask = null;
        using var providerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            providerTask = provider.ObserveAsync(providerCancellation.Token).AsTask();
            readResult = await providerTask
                .WaitAsync(descriptor.Timeout, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            RequestCancellation(providerCancellation);
            ObserveBackgroundFault(providerTask);
            return CreateFailure(descriptor, ProviderRunOutcome.Cancelled, collectedAtUtc);
        }
        catch (TimeoutException)
        {
            RequestCancellation(providerCancellation);
            ObserveBackgroundFault(providerTask);
            return CreateFailure(descriptor, ProviderRunOutcome.TimedOut, collectedAtUtc);
        }
        catch (UnauthorizedAccessException)
        {
            return CreateFailure(descriptor, ProviderRunOutcome.AccessDenied, collectedAtUtc);
        }
        catch (Exception exception) when (IsInvalidProviderData(exception))
        {
            return CreateFailure(descriptor, ProviderRunOutcome.InvalidData, collectedAtUtc);
        }
        catch (Exception)
        {
            return CreateFailure(descriptor, ProviderRunOutcome.Failed, collectedAtUtc);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateFailure(descriptor, ProviderRunOutcome.Cancelled, collectedAtUtc);
        }

        if (readResult is null)
        {
            return CreateFailure(descriptor, ProviderRunOutcome.ContractInvalid, collectedAtUtc);
        }

        var readFailure = readResult.Status switch
        {
            ProviderReadStatus.Collected => (ProviderRunOutcome?)null,
            ProviderReadStatus.Unavailable => ProviderRunOutcome.Unavailable,
            ProviderReadStatus.AccessDenied => ProviderRunOutcome.AccessDenied,
            ProviderReadStatus.InvalidData => ProviderRunOutcome.InvalidData,
            ProviderReadStatus.CleanupIncomplete => ProviderRunOutcome.CleanupIncomplete,
            ProviderReadStatus.Failed => ProviderRunOutcome.Failed,
            _ => ProviderRunOutcome.ContractInvalid,
        };

        try
        {
            if (readFailure is null)
            {
                var completedObservations = MaterializeEvidence(
                    descriptor,
                    readResult.Evidence,
                    requireEveryDefinition: true,
                    collectedAtUtc);

                return cancellationToken.IsCancellationRequested
                    ? CreateFailure(descriptor, ProviderRunOutcome.Cancelled, collectedAtUtc)
                    : ProviderRunResult.Completed(descriptor.ProviderId, completedObservations);
            }

            var failureObservations = MaterializeFailureEvidence(
                descriptor,
                readResult.Evidence,
                readFailure.Value,
                collectedAtUtc);
            if (cancellationToken.IsCancellationRequested)
            {
                return CreateFailure(descriptor, ProviderRunOutcome.Cancelled, collectedAtUtc);
            }

            var error = new SanitizedProviderError(readFailure.Value);
            return ProviderRunResult.Failed(
                descriptor.ProviderId,
                readFailure.Value,
                failureObservations,
                error);
        }
        catch (Exception exception) when (IsInvalidProviderData(exception))
        {
            return CreateFailure(descriptor, ProviderRunOutcome.ContractInvalid, collectedAtUtc);
        }
        catch (Exception)
        {
            return CreateFailure(descriptor, ProviderRunOutcome.Failed, collectedAtUtc);
        }
    }

    private static bool IsInvalidProviderData(Exception exception) => exception is
        ArgumentException or
        FormatException or
        InvalidDataException or
        OverflowException;

    private static void RequestCancellation(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel(throwOnFirstException: false);
        }
        catch (AggregateException)
        {
            // A provider callback cannot replace the typed cancellation or timeout result.
        }
    }

    private static void ObserveBackgroundFault(Task? providerTask)
    {
        if (providerTask is null)
        {
            return;
        }

        if (providerTask.IsFaulted)
        {
            _ = providerTask.Exception;
            return;
        }

        if (providerTask.IsCompleted)
        {
            return;
        }

        _ = providerTask.ContinueWith(
            static completedTask => _ = completedTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static ProviderRunResult CreateFailure(
        InventoryProviderDescriptor descriptor,
        ProviderRunOutcome outcome,
        DateTimeOffset collectedAtUtc,
        bool providerInvoked = true)
    {
        var error = new SanitizedProviderError(outcome);
        ProvenanceKind? provenanceOverride = providerInvoked ? null : ProvenanceKind.SystemGenerated;
        var observations = descriptor.EvidenceDefinitions.Select(definition => CreateObservation(
            descriptor,
            definition,
            NormalizedEvidence.RunFailure(definition.EvidenceKey, error.Limitation),
            collectedAtUtc,
            provenanceOverride)).ToList();
        observations.Add(CreateProviderFailureObservation(
            descriptor,
            error,
            collectedAtUtc,
            provenanceOverride));

        return ProviderRunResult.Failed(
            descriptor.ProviderId,
            outcome,
            OrderObservations(observations),
            error);
    }

    private static List<Observation> MaterializeFailureEvidence(
        InventoryProviderDescriptor descriptor,
        IReadOnlyList<NormalizedEvidence> evidence,
        ProviderRunOutcome outcome,
        DateTimeOffset collectedAtUtc)
    {
        var observations = MaterializeEvidence(
            descriptor,
            evidence,
            requireEveryDefinition: false,
            collectedAtUtc).ToList();
        var error = new SanitizedProviderError(outcome);
        var returnedKeys = evidence.Select(static value => value.EvidenceKey).ToHashSet();
        var definitionKeys = descriptor.EvidenceDefinitions
            .Select(static value => value.EvidenceKey)
            .ToHashSet();

        foreach (var definition in descriptor.EvidenceDefinitions)
        {
            if (!returnedKeys.Contains(definition.EvidenceKey))
            {
                observations.Add(CreateObservation(
                    descriptor,
                    definition,
                    NormalizedEvidence.RunFailure(definition.EvidenceKey, error.Limitation),
                    collectedAtUtc));
            }
        }

        if (outcome != ProviderRunOutcome.CleanupIncomplete &&
            !observations.Any(observation =>
                definitionKeys.Contains(observation.EvidenceKey) &&
                observation.Metadata.EvidenceClassification == EvidenceClassification.NotAvailable &&
                observation.Metadata.ValueAvailability == ValueAvailability.Unknown))
        {
            throw new InvalidDataException(
                "A non-cleanup provider failure must identify at least one affected value-evidence definition.");
        }

        observations.Add(CreateProviderFailureObservation(descriptor, error, collectedAtUtc));

        return OrderObservations(observations);
    }

    private static Observation[] MaterializeEvidence(
        InventoryProviderDescriptor descriptor,
        IReadOnlyList<NormalizedEvidence> normalized,
        bool requireEveryDefinition,
        DateTimeOffset collectedAtUtc)
    {
        var values = normalized.Take(InventoryProviderDescriptor.MaximumEvidenceKeyCount + 1).ToArray();
        if (values.Length > InventoryProviderDescriptor.MaximumEvidenceKeyCount ||
            values.Any(static value => value is null))
        {
            throw new InvalidDataException("Provider evidence must remain within its declared bound.");
        }

        var definitions = descriptor.EvidenceDefinitions.ToDictionary(static value => value.EvidenceKey);
        if (values.Any(value => !definitions.ContainsKey(value.EvidenceKey)))
        {
            throw new InvalidDataException("A provider returned an undeclared evidence key.");
        }

        var valuesByKey = values.GroupBy(static value => value.EvidenceKey).ToArray();
        if (requireEveryDefinition && valuesByKey.Length != definitions.Count)
        {
            throw new InvalidDataException("A completed provider must return every declared evidence definition.");
        }

        foreach (var group in valuesByKey)
        {
            var definition = definitions[group.Key];
            var groupedValues = group.ToArray();
            if (groupedValues.Length > definition.MaximumCardinality)
            {
                throw new InvalidDataException("A provider exceeded a declared evidence cardinality.");
            }

            if (groupedValues.Any(value => value.Value is not null && value.Value.Kind != definition.ValueKind))
            {
                throw new InvalidDataException("A normalized value does not match its declared scalar kind.");
            }

            var sourceReferences = groupedValues
                .Select(value => value.SourceReference ?? definition.SourceReference)
                .ToArray();
            if (sourceReferences.Distinct(StringComparer.Ordinal).Count() != sourceReferences.Length)
            {
                throw new InvalidDataException("Repeated evidence requires a distinct bounded source reference.");
            }
        }

        return values
            .OrderBy(static value => value.EvidenceKey.Value, StringComparer.Ordinal)
            .ThenBy(
                value => value.SourceReference ?? definitions[value.EvidenceKey].SourceReference,
                StringComparer.Ordinal)
            .Select(value => CreateObservation(
                descriptor,
                definitions[value.EvidenceKey],
                value,
                collectedAtUtc))
            .ToArray();
    }

    private static Observation CreateObservation(
        InventoryProviderDescriptor descriptor,
        ProviderEvidenceDefinition definition,
        NormalizedEvidence value,
        DateTimeOffset collectedAtUtc,
        ProvenanceKind? provenanceOverride = null)
    {
        var provenanceKind = provenanceOverride ?? descriptor.EvidenceOrigin switch
        {
            ProviderEvidenceOrigin.ActiveMachine => ProvenanceKind.ProviderObservation,
            ProviderEvidenceOrigin.CapturedSample => ProvenanceKind.ImportedRecord,
            ProviderEvidenceOrigin.SyntheticFixture => ProvenanceKind.SyntheticFixture,
            _ => throw new InvalidDataException("The provider evidence origin is not defined."),
        };
        const PrivacyClassification privacyClassification = PrivacyClassification.WorkshopRestricted;
        var provenance = new EvidenceProvenance(
            provenanceKind,
            value.SourceReference ?? definition.SourceReference);

        return new Observation(
            new EvidenceMetadata(
                EvidenceId.New(),
                privacyClassification,
                value.EvidenceClassification,
                descriptor.ProviderId,
                collectedAtUtc,
                provenance,
                value.ValueAvailability),
            value.EvidenceKey,
            value.Value,
            definition.Unit,
            value.Limitation?.ToCode());
    }

    private static Observation CreateProviderFailureObservation(
        InventoryProviderDescriptor descriptor,
        SanitizedProviderError error,
        DateTimeOffset collectedAtUtc,
        ProvenanceKind? provenanceOverride = null)
    {
        var provenanceKind = provenanceOverride ?? descriptor.EvidenceOrigin switch
        {
            ProviderEvidenceOrigin.ActiveMachine => ProvenanceKind.ProviderObservation,
            ProviderEvidenceOrigin.CapturedSample => ProvenanceKind.ImportedRecord,
            ProviderEvidenceOrigin.SyntheticFixture => ProvenanceKind.SyntheticFixture,
            _ => throw new InvalidDataException("The provider evidence origin is not defined."),
        };
        const PrivacyClassification privacyClassification = PrivacyClassification.WorkshopRestricted;

        return new Observation(
            new EvidenceMetadata(
                EvidenceId.New(),
                privacyClassification,
                EvidenceClassification.NotAvailable,
                descriptor.ProviderId,
                collectedAtUtc,
                new EvidenceProvenance(provenanceKind, descriptor.FailureDefinition.SourceReference),
                ValueAvailability.Unknown),
            descriptor.FailureDefinition.EvidenceKey,
            value: null,
            unit: null,
            error.Limitation.ToCode());
    }

    private static List<Observation> OrderObservations(IEnumerable<Observation> observations) => observations
        .OrderBy(static observation => observation.EvidenceKey.Value, StringComparer.Ordinal)
        .ThenBy(static observation => observation.Metadata.Provenance.SourceReference, StringComparer.Ordinal)
        .ToList();
}
