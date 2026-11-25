using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Inventory;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Versioning;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Features.Disposition;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

public sealed class AuditPolicyEvidenceTests
{
    private const string LifecycleSource = "os.lifecycle.catalog";
    private const string LifecycleRevision = "thirdlife.windows-lifecycle.2026-09-30.v1";
    private static readonly DateTimeOffset SampleTime = new(2026, 10, 1, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FreshThrough = new(2026, 10, 30, 23, 59, 0, TimeSpan.Zero);

    [Fact]
    public async Task ScopedAliasesAndManualAndIntakeRetainStoredSourceMetadata()
    {
        using var disk = new AuditTestStore();
        await using var workspace = OpenLive(disk,
            new ProjectionProvider("unrelated-signals",
                [EnumFact("ownership.lock_state", "clear", "fixture.unrelated"),
                 EnumFact("storage.system.health_state", "healthy", "fixture.unrelated"),
                 EnumFact("processor.windows_11_eligibility", "eligible", "fixture.unrelated")]));
        await AssessAsync(workspace);
        await workspace.RecordManualAsync("MHT-002", HumanTestResult.Pass, "policy-test-operator", "Keyboard check passed", reason: null);
        var job = Assert.IsType<StoredJob>(workspace.Job);
        var assessment = Assert.IsType<AssessmentSnapshot>(workspace.Assessment);

        var projected = AuditPolicyEvidence.Create(job, assessment, SampleTime);

        var family = Fact(assessment.Observations, "os.family");
        var release = Fact(projected, "operating_system.release");
        Assert.Same(family.Metadata, release.Metadata);
        Assert.Equal(family.Value, release.Value);
        Assert.Null(release.LimitationCode);
        Assert.Equal("25H2", Fact(projected, "os.lifecycle.release").Value!.StringValue);
        Assert.Same(Fact(assessment.Observations, "os.support_target").Metadata,
            Fact(projected, "operating_system.support_state").Metadata);
        Assert.Same(Fact(assessment.Observations, "system.architecture"), Fact(projected, "system.architecture"));
        var sanitization = Assert.Single(job.SanitizationEvidence);
        Assert.Same(sanitization.Metadata, Fact(projected, "sanitization.state").Metadata);
        Assert.Equal(sanitization.OperatorId, Fact(projected, "sanitization.state").OperatorId);
        var manual = Assert.Single(job.HumanTests);
        var keyboard = Fact(projected, "function.keyboard");
        Assert.Same(manual.Metadata, keyboard.Metadata);
        Assert.Equal(manual.OperatorId, keyboard.OperatorId);
        Assert.Equal("passed", keyboard.Value!.StringValue);
        Assert.DoesNotContain(projected, fact => fact.EvidenceKey.Value is "ownership.lock_state" or
            "storage.system.health_state" or "processor.windows_11_eligibility" or "function.network");
        Assert.Equal(projected.Count, projected.Select(fact => fact.Metadata.EvidenceId).Distinct().Count());
    }

    [Fact]
    public async Task EarlierLifecycleSnapshotExpiresForNewEvaluationWithoutChangingItsObservations()
    {
        using var disk = new AuditTestStore();
        await using var workspace = OpenLive(disk);
        await AssessAsync(workspace);
        var assessment = Assert.IsType<AssessmentSnapshot>(workspace.Assessment);
        var job = Assert.IsType<StoredJob>(workspace.Job);
        var originalStatus = Fact(assessment.Observations, "os.support_target");

        Assert.Null(Fact(AuditPolicyEvidence.Create(job, assessment, FreshThrough), "operating_system.support_state").LimitationCode);
        var expired = AuditPolicyEvidence.Create(job, assessment, FreshThrough.AddMinutes(1));

        var status = Fact(expired, "operating_system.support_state");
        Assert.Equal("provider_value_stale", status.LimitationCode);
        var family = Fact(expired, "operating_system.release");
        Assert.Equal("windows_11", family.Value!.StringValue);
        Assert.Null(family.LimitationCode);
        Assert.Same(originalStatus.Metadata, status.Metadata);
        Assert.Null(originalStatus.LimitationCode);
        var stored = Assert.Single(job.Observations, fact => fact.Metadata.EvidenceId == originalStatus.Metadata.EvidenceId);
        Assert.Equal(originalStatus, stored);
    }

    [Theory]
    [InlineData("windows_10", "audit_only", "legacy_windows_10_audit")]
    [InlineData("windows_10", "unsupported", null)]
    [InlineData("windows_server", "unsupported", null)]
    [InlineData("windows_10", "supported", "provider_values_conflict")]
    [InlineData("windows_11", "audit_only", "provider_values_conflict")]
    public async Task CurrentProjectionPreservesFamilyAndRetiresHistoricalAuditEntitlement(
        string family, string support, string? expectedLimit)
    {
        using var disk = new AuditTestStore();
        await using var workspace = disk.OpenWorkspace(
            [OperatingSystem(LifecycleSource, "x64", family: family, support: support), SystemInventory()], new FixedClock());
        await AssessAsync(workspace);

        var observed = Fact(workspace.Assessment!.Observations, "os.support_target");
        var projected = AuditPolicyEvidence.Create(workspace.Job!, workspace.Assessment, SampleTime);
        var projectedFamily = Fact(projected, "operating_system.release");
        var projectedSupport = Fact(projected, "operating_system.support_state");

        Assert.Equal(family, projectedFamily.Value!.StringValue);
        Assert.Equal(support, projectedSupport.Value!.StringValue);
        Assert.Equal(expectedLimit, projectedSupport.LimitationCode);
        Assert.Equal(expectedLimit == "provider_values_conflict" ? expectedLimit : null, projectedFamily.LimitationCode);
        Assert.Null(observed.LimitationCode);
        Assert.Same(observed.Metadata, projectedSupport.Metadata);
    }

    [Theory]
    [InlineData("windows_11", "supported", Disposition.HumanReviewRequired, Disposition.HumanReviewRequired)]
    [InlineData("windows_10", "audit_only", Disposition.AlternativeOperatingSystemCandidate, Disposition.DoNotDeploy)]
    public async Task HistoricalPolicyAndObservationsReopenAndExportBeforeExplicitReevaluation(
        string family, string support, Disposition historicalOutcome, Disposition currentOutcome)
    {
        using var disk = new AuditTestStore();
        var os = OperatingSystem(LifecycleSource, "x64", ProviderEvidenceOrigin.SyntheticFixture, family: family, support: support,
            identitySource: LifecycleSource);
        var system = SystemInventory(origin: ProviderEvidenceOrigin.SyntheticFixture);
        var originalCandidate = PolicySnapshot.Restore(
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "community-laptop-policy-v1.json")),
            "967ef27449426a99f001ff09cd11b5e637b62a3f1e33566dd935bb8fd876ac88");
        var legacy = family == "windows_11" ? originalCandidate : HistoricalIntakePolicy(originalCandidate);
        PolicyDecisionRecord historical;
        Observation[] originalObservations;
        await using (var original = disk.OpenWorkspace([os, system], new FixedClock()))
        {
            await AssessAsync(original);
            var assessment = Assert.IsType<AssessmentSnapshot>(original.Assessment);
            var job = Assert.IsType<StoredJob>(original.Job);
            originalObservations = job.Observations.ToArray();
            var facts = AuditPolicyEvidence.Create(job, assessment, SampleTime)
                .Select(fact => fact.EvidenceKey.Value == "operating_system.support_state"
                    ? new Observation(fact.Metadata, fact.EvidenceKey, fact.Value, fact.Unit, null)
                    : fact)
                .ToArray();
            var decision = DispositionEngine.Evaluate(legacy.Document, facts, activeCapabilityIds: null);
            Assert.Equal(historicalOutcome, decision.Disposition);
            historical = PolicyDecisionRecord.Create(legacy, assessment.JobId, assessment.AssessmentId.Value,
                decision, facts.Select(fact => fact.Metadata.EvidenceId), SampleTime);
            await using var store = await disk.OpenSqliteAsync();
            await store.AppendPolicyDecisionAsync(legacy, historical);
        }

        var reportStore = new WorkshopReportStore(Path.Combine(disk.Root, "historical-workshop-reports"));
        await using var reopened = new AuditWorkspace(
            async token => await disk.OpenSqliteAsync(token).ConfigureAwait(false), [os, system], new FixedClock(), reportStore);
        await reopened.OpenJobAsync(historical.JobId.Value);
        Assert.Equal(historical.Title, reopened.PolicyDispositionTitle);
        Assert.StartsWith("Historical policy decision.", reopened.PolicyStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("Consider a supported alternative", reopened.PolicyDispositionSummary, StringComparison.Ordinal);
        Assert.Equal(originalObservations, reopened.Job!.Observations);

        await reopened.GenerateReportAsync();
        await reopened.OpenReportAsync();
        var historicalExport = reopened.ReportPreview;
        var artifact = Assert.IsType<WorkshopReportArtifact>(reopened.ReportArtifact);
        Assert.Contains("Historical policy evaluation.", historicalExport, StringComparison.Ordinal);
        Assert.Contains($"os.family: {family}", historicalExport, StringComparison.Ordinal);
        Assert.Contains($"os.support_target: {support}", historicalExport, StringComparison.Ordinal);
        Assert.Contains(historical.PolicySha256, historicalExport, StringComparison.Ordinal);
        Assert.Contains(historical.Explanation, historicalExport, StringComparison.Ordinal);

        await using var readback = await disk.OpenSqliteAsync();
        var before = Assert.IsType<StoredPolicyDecision>(
            await readback.LoadLatestPolicyDecisionAsync(historical.JobId, historical.AssessmentId));
        Assert.Equal(historical.DecisionId, before.Decision.DecisionId);
        Assert.Equal(legacy.Utf8Json.AsEnumerable(), before.Snapshot.Utf8Json.AsEnumerable());

        await reopened.ReevaluatePolicyAsync();
        var current = Assert.IsType<StoredPolicyDecision>(
            await readback.LoadLatestPolicyDecisionAsync(historical.JobId, historical.AssessmentId));
        Assert.True(current.Snapshot.Document.IsCurrentSchema);
        Assert.Equal(currentOutcome, current.Decision.Disposition);
        Assert.Equal(historical.DecisionId, current.Decision.PreviousDecisionId);
        Assert.NotEqual(historical.PolicySha256, current.Decision.PolicySha256);
        var old = Assert.IsType<StoredPolicyDecision>(await readback.LoadPolicyDecisionAsync(historical.JobId, historical.DecisionId));
        Assert.Equal(historical.Explanation, old.Decision.Explanation);
        Assert.Equal(originalObservations, (await readback.LoadJobAsync(historical.JobId))!.Observations);
        Assert.Equal(historicalExport, await File.ReadAllTextAsync(Path.Combine(disk.Root, "historical-workshop-reports", artifact.FileName)));
    }

    private static PolicySnapshot HistoricalIntakePolicy(PolicySnapshot originalCandidate)
    {
        // The v1 Windows 10 fixture has a previously valid intake policy without unrelated pending checks.
        var json = JsonNode.Parse(originalCandidate.Utf8Json.AsSpan())!;
        var intake = json["rules"]!.AsArray().Single(rule => rule!["id"]!.GetValue<string>() == "POL-SAN-001");
        json["rules"] = new JsonArray(intake!.DeepClone());
        return PolicySnapshot.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));
    }

    [Theory]
    [InlineData(null, "x64", "x64", LifecycleRevision, "provider_value_missing")]
    [InlineData("fixture.different_scope", "x64", "x64", LifecycleRevision, "provider_values_conflict")]
    [InlineData(LifecycleSource, "arm64", "x64", LifecycleRevision, "provider_values_conflict")]
    [InlineData(LifecycleSource, "arm64", "arm64", LifecycleRevision, "provider_values_conflict")]
    [InlineData(LifecycleSource, "x64", "x64", "thirdlife.windows-lifecycle.2026-10-01.v1", "provider_value_malformed")]
    [InlineData(LifecycleSource, "x64", "x64", "thirdlife.windows-lifecycle.2026-09-30.v01", "provider_value_malformed")]
    public async Task IncompleteScopedSignalsAndArchitectureDisagreementCannotPass(
        string? freshnessSource, string osArchitecture, string systemArchitecture, string revision, string expectedLimitation)
    {
        using var disk = new AuditTestStore();
        await using var workspace = disk.OpenWorkspace(
            [OperatingSystem(freshnessSource, osArchitecture, revision: revision), SystemInventory(systemArchitecture)], new FixedClock());
        await AssessAsync(workspace);

        var projected = AuditPolicyEvidence.Create(workspace.Job!, workspace.Assessment!, SampleTime);

        Assert.Equal(expectedLimitation, Fact(projected, "operating_system.support_state").LimitationCode);
        if (osArchitecture != "x64")
        {
            Assert.Equal(expectedLimitation, Fact(projected, "system.architecture").LimitationCode);
            Assert.Equal("arm64", Fact(projected, "os.architecture").Value!.StringValue);
        }
    }

    [Fact]
    public async Task ChangedGateCannotBeCombinedWithAnEarlierAssessment()
    {
        using var disk = new AuditTestStore();
        await using var workspace = OpenLive(disk);
        await AssessAsync(workspace);
        var previousJob = Assert.IsType<StoredJob>(workspace.Job);
        var assessment = Assert.IsType<AssessmentSnapshot>(workspace.Assessment);
        await workspace.RecordSanitizationAsync(VerifiedSample() with { MediaIdentifier = "replacement-reference" });
        var currentJob = Assert.IsType<StoredJob>(workspace.Job);

        Assert.NotEqual(Assert.Single(previousJob.SanitizationGateDecisions).DecisionId, currentJob.SanitizationGateDecisions[^1].DecisionId);
        Assert.Throws<InvalidOperationException>(() => AuditPolicyEvidence.Create(currentJob, assessment, SampleTime));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SyntheticPolicyReadySignalsCannotOverrideAliasedLifecycleFacts(bool includeAlias)
    {
        using var disk = new AuditTestStore();
        var direct = new ProjectionProvider("fixture-direct-policy",
            [EnumFact("operating_system.release", "windows_11", "fixture.direct"),
             EnumFact("operating_system.support_state", "supported", "fixture.direct"),
             EnumFact("processor.windows_11_eligibility", "eligible", "fixture.direct")],
            ProviderEvidenceOrigin.SyntheticFixture);
        IInventoryProvider[] roster = includeAlias
            ? [direct, OperatingSystem(LifecycleSource, "x64", ProviderEvidenceOrigin.SyntheticFixture)]
            : [direct];
        await using var workspace = disk.OpenWorkspace(roster, new FixedClock());
        await AssessAsync(workspace);

        var projected = AuditPolicyEvidence.Create(workspace.Job!, workspace.Assessment!, SampleTime);

        Assert.Equal(ProvenanceKind.SyntheticFixture,
            Fact(projected, "processor.windows_11_eligibility").Metadata.Provenance.Kind);
        var support = projected.Where(fact => fact.EvidenceKey.Value == "operating_system.support_state").ToArray();
        Assert.Equal(includeAlias ? 2 : 1, support.Length);
        Assert.All(support, fact => Assert.Equal(includeAlias ? "provider_values_conflict" : null, fact.LimitationCode));
    }

    private static AuditWorkspace OpenLive(AuditTestStore disk, params IInventoryProvider[] extra) =>
        disk.OpenWorkspace([OperatingSystem(LifecycleSource, "x64"), SystemInventory(), .. extra], new FixedClock());

    private static async Task AssessAsync(AuditWorkspace workspace)
    {
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());
        await workspace.StartAssessmentAsync();
        Assert.NotNull(workspace.Assessment);
        Assert.Equal(AssessmentOutcome.Completed, workspace.Assessment.Outcome);
    }

    private static ProjectionProvider OperatingSystem(
        string? freshnessSource, string architecture, ProviderEvidenceOrigin origin = ProviderEvidenceOrigin.ActiveMachine,
        string revision = LifecycleRevision, string family = "windows_11", string support = "supported",
        string identitySource = "os.windows.identity")
    {
        List<NormalizedEvidence> facts =
        [
            NormalizedEvidence.Inferred(new EvidenceKey("os.family"), EvidenceValue.FromEnum(family), sourceReference: identitySource),
            LifecycleEnum("os.support_target", support),
            LifecycleText("os.lifecycle.source", "microsoft.windows-client.release-health"),
            LifecycleText("os.lifecycle.revision", revision),
            LifecycleText("os.lifecycle.as_of", "2026-09-30"),
            LifecycleText("os.lifecycle.evaluated_on", "2026-10-01"),
            LifecycleText("os.lifecycle.release", "25H2"),
            EnumFact("os.architecture", architecture, "os.dotnet.os_architecture"),
        ];
        if (freshnessSource is not null)
        {
            facts.Add(NormalizedEvidence.Inferred(new EvidenceKey("os.lifecycle.fresh_through"),
                EvidenceValue.FromString("2026-10-30"), sourceReference: freshnessSource));
        }

        return new ProjectionProvider("windows-operating-system", facts.ToArray(), origin);
    }

    private static ProjectionProvider SystemInventory(
        string architecture = "x64", ProviderEvidenceOrigin origin = ProviderEvidenceOrigin.ActiveMachine) => new("windows-system-inventory",
        [EnumFact("system.architecture", architecture, "system_inventory.dotnet.os_architecture")], origin);

    private static NormalizedEvidence LifecycleEnum(string key, string value) =>
        NormalizedEvidence.Inferred(new EvidenceKey(key), EvidenceValue.FromEnum(value), sourceReference: LifecycleSource);

    private static NormalizedEvidence LifecycleText(string key, string value) =>
        NormalizedEvidence.Inferred(new EvidenceKey(key), EvidenceValue.FromString(value), sourceReference: LifecycleSource);

    private static NormalizedEvidence EnumFact(string key, string value, string source) =>
        NormalizedEvidence.Observed(new EvidenceKey(key), EvidenceValue.FromEnum(value), sourceReference: source);

    private static Observation Fact(IEnumerable<Observation> observations, string key) =>
        Assert.Single(observations, fact => fact.EvidenceKey.Value == key);

    private sealed class ProjectionProvider(
        string id, NormalizedEvidence[] facts, ProviderEvidenceOrigin origin = ProviderEvidenceOrigin.ActiveMachine) : IInventoryProvider
    {
        public InventoryProviderDescriptor Descriptor { get; } = new(new ProviderId(id),
            ProviderPrivilegeRequirement.StandardUser, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(15),
            ProviderNetworkUse.None, origin, [ProviderOperatingSystem.Windows11],
            new ProviderFailureDefinition(new EvidenceKey("test." + id + ".failure"), "fixture." + id + ".failure"),
            facts.Select(fact => new ProviderEvidenceDefinition(fact.EvidenceKey, fact.Value!.Kind, unit: null, fact.SourceReference!)));

        public ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ProviderReadResult.Collected(facts));
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => SampleTime;
    }
}
