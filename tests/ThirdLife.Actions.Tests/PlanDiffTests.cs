using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json.Nodes;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Planning;
using ThirdLife.Actions.Registry;

namespace ThirdLife.Actions.Tests;

public sealed class PlanDiffTests
{
    [Fact]
    public void IndependentlyResolvedIdenticalPlansAndImpactKeepApproval()
    {
        var approved = PlanResolverTests.CreateReviewedPlan();
        var current = PlanResolverTests.CreateReviewedPlan();

        Assert.Null(approved.BlockedReason);
        Assert.Null(current.BlockedReason);
        Assert.NotSame(approved, current);
        Assert.Equal(approved.ContentSha256, current.ContentSha256);
        Assert.NotSame(approved.Actions[0].Request, current.Actions[0].Request);

        var diff = Compare(approved, current);

        Assert.False(diff.RequiresReapproval);
        Assert.Empty(diff.Changes);
        Assert.Equal("The approved plan and impact are unchanged.", diff.PlainLanguage);
    }

    [Theory]
    [InlineData("source_id", "second-repository", "expected_source_id", PlanChangeKind.PackageSource,
        "The package source for web browsing changed from test-repository to second-repository.")]
    [InlineData("package_id", "Fictional.Tests.OtherBrowser", "expected_package_id", PlanChangeKind.PackageIdentity,
        "The package identity or supported platform changed for web browsing.")]
    [InlineData("exact_version", "2.15.236.9", "expected_version", PlanChangeKind.PackageVersion,
        "The exact package version for web browsing changed from 2.15.236.8 to 2.15.236.9.")]
    public void ExactPackageBindingChangesRequireReapproval(
        string field, string replacement, string verificationField, PlanChangeKind expected, string expectedMessage)
    {
        var approved = PlanResolverTests.CreateReviewedPlan();
        var current = PlanResolverTests.CreateReviewedPlan(adjustCatalog: catalog =>
        {
            var app = FirstApplication(catalog);
            app["package"]![field] = replacement;
            app["verification"]![verificationField] = replacement;
        });
        Assert.Null(current.BlockedReason);

        var diff = Compare(approved, current);

        AssertReapproval(diff, expected);
        Assert.Equal(expectedMessage + "\nThe verification requirement changed for web browsing.", diff.PlainLanguage);
    }

    [Fact]
    public void PublisherChangesRequireReapproval()
    {
        var approved = PlanResolverTests.CreateReviewedPlan();
        var current = PlanResolverTests.CreateReviewedPlan(adjustCatalog: catalog =>
            FirstApplication(catalog)["review"]!["publisher"] = "Replacement Publisher");
        Assert.Null(current.BlockedReason);

        AssertReapproval(Compare(approved, current), PlanChangeKind.PackagePublisher);
    }

    [Fact]
    public void CatalogVersionChangesRequireReapprovalEvenWithIdenticalPackageMetadata()
    {
        var approved = PlanResolverTests.CreateReviewedPlan();
        var current = PlanResolverTests.CreateReviewedPlan(adjustCatalog: catalog => catalog["version"] = "1.0.1");
        Assert.Null(current.BlockedReason);

        AssertReapproval(Compare(approved, current), PlanChangeKind.ReviewContext);
    }

    [Theory]
    [InlineData("reboot", "\"required\"", PlanChangeKind.Restart)]
    [InlineData("network_use", "\"optional\"", PlanChangeKind.Network)]
    [InlineData("download_bytes", "1024", PlanChangeKind.Impact)]
    [InlineData("installed_bytes", "4096", PlanChangeKind.Impact)]
    [InlineData("background_service", "\"yes\"", PlanChangeKind.Impact)]
    [InlineData("auto_update", "\"yes\"", PlanChangeKind.Impact)]
    public void ImpactFieldChangesRequireReapproval(string field, string json, PlanChangeKind expected)
    {
        var approved = PlanResolverTests.CreateReviewedPlan();
        var current = PlanResolverTests.CreateReviewedPlan(adjustCatalog: catalog =>
            FirstApplication(catalog)["behavior"]![field] = JsonNode.Parse(json));
        Assert.Null(current.BlockedReason);

        AssertReapproval(Compare(approved, current), expected);
    }

    [Fact]
    public void AddedOrRemovedActionRequiresReapproval()
    {
        var oneAction = PlanResolverTests.CreateReviewedPlan();
        var twoActions = PlanResolverTests.CreateReviewedPlan(adjustCatalog: AddDocumentEditor);
        Assert.Null(oneAction.BlockedReason);
        Assert.Null(twoActions.BlockedReason);
        Assert.Single(oneAction.Actions);
        Assert.Equal(2, twoActions.Actions.Length);

        AssertReapproval(Compare(oneAction, twoActions), PlanChangeKind.ActionSelection);
        AssertReapproval(Compare(twoActions, oneAction), PlanChangeKind.ActionSelection);
    }

    [Fact]
    public void AChangeInOrderRequiresReapprovalForTheSameActions()
    {
        var approved = PlanResolverTests.CreateReviewedPlan(adjustCatalog: AddDocumentEditor);
        var current = ReplaceActions(approved, approved.Actions.Reverse().ToImmutableArray());

        AssertReapproval(Compare(approved, current), PlanChangeKind.ActionOrder);
    }

    [Theory]
    [InlineData(PlanChangeKind.Privilege)]
    [InlineData(PlanChangeKind.Verification)]
    [InlineData(PlanChangeKind.Rollback)]
    [InlineData(PlanChangeKind.Restart)]
    [InlineData(PlanChangeKind.Action)]
    public void CompiledActionMetadataAndReasonAreMaterial(PlanChangeKind expected)
    {
        var approved = PlanResolverTests.CreateReviewedPlan();
        var action = Assert.Single(approved.Actions);
        var changed = expected switch
        {
            PlanChangeKind.Privilege => action with
            {
                Definition = action.Definition with { Privilege = ActionPrivilege.Unelevated },
            },
            PlanChangeKind.Verification => action with
            {
                Definition = action.Definition with { Verification = ActionVerification.NoMachineChange },
            },
            PlanChangeKind.Rollback => action with
            {
                Definition = action.Definition with { Rollback = ActionRollback.NoChanges },
            },
            PlanChangeKind.Restart => action with
            {
                Definition = action.Definition with { Reboot = ActionReboot.None },
            },
            PlanChangeKind.Action => action with { ReasonCode = "alternate_reviewed_reason" },
            _ => throw new ArgumentOutOfRangeException(nameof(expected)),
        };
        var current = ReplaceActions(approved, [changed]);

        AssertReapproval(Compare(approved, current), expected);
    }

    [Fact]
    public void AChangeInTypedExecutionScopeRequiresReapproval()
    {
        var approved = PlanResolverTests.CreateReviewedPlan();
        var action = Assert.Single(approved.Actions);
        var package = Assert.IsType<InstallPackageActionRequest>(action.Request);
        var userPackage = ActionRegistry.CreateInstallPackage(
            package.ActionId, package.SourceId, package.PackageId, package.ExactVersion, package.Publisher,
            ActionScope.User, package.Architecture);
        var current = ReplaceActions(approved,
            [action with { Request = userPackage, Definition = ActionRegistry.Describe(userPackage) }]);

        AssertReapproval(Compare(approved, current), PlanChangeKind.Scope);
    }

    [Theory]
    [InlineData("scope", "\"user\"")]
    [InlineData("architectures", "[\"arm64\"]")]
    public void CatalogScopeAndArchitectureConflictsBlockBeforeReapproval(string field, string json)
    {
        var approved = PlanResolverTests.CreateReviewedPlan();
        var current = PlanResolverTests.CreateReviewedPlan(adjustCatalog: catalog =>
            FirstApplication(catalog)["package"]![field] = JsonNode.Parse(json));

        Assert.Equal(PlanBlockReason.PackageNotApplicable, current.BlockedReason);
        Assert.Empty(current.Actions);
        var diff = Compare(approved, current);
        AssertReapproval(diff, PlanChangeKind.CurrentPlanBlocked);
        Assert.Equal("This comparison includes a blocked or empty plan. Preparation cannot proceed.", diff.PlainLanguage);
    }

    [Fact]
    public void ABlockedCurrentPlanCannotRetainApproval()
    {
        var approved = PlanResolverTests.CreateSyntheticPlan();
        var current = PlanResolverTests.CreateSyntheticPlan(archived: true);

        Assert.Equal(PlanBlockReason.SanitizationRequired, current.BlockedReason);
        AssertReapproval(Compare(approved, current), PlanChangeKind.CurrentPlanBlocked);
    }

    [Fact]
    public void RejectsImpactBoundToAnotherPlanEvenWhenTheComparedPlansMatch()
    {
        var approved = PlanResolverTests.CreateReviewedPlan();
        var wrong = PlanResolverTests.CreateSyntheticPlan();
        var correctImpact = ImpactEstimator.Estimate(approved);
        var wrongImpact = ImpactEstimator.Estimate(wrong);

        var invalidApprovedImpact = PlanDiff.Compare(approved, wrongImpact, approved, correctImpact);
        AssertReapproval(invalidApprovedImpact, PlanChangeKind.InvalidImpact);
        Assert.Equal("The impact estimate does not match its plan. Review the current plan again.", invalidApprovedImpact.PlainLanguage);
        AssertReapproval(PlanDiff.Compare(approved, correctImpact, approved, wrongImpact), PlanChangeKind.InvalidImpact);
    }

    private static JsonNode FirstApplication(JsonObject catalog) => catalog["applications"]![0]!;

    internal static void AddDocumentEditor(JsonObject catalog)
    {
        var applications = catalog["applications"]!.AsArray();
        var editor = applications[0]!.DeepClone();
        editor["id"] = "fictional.document-editor";
        editor["capability_id"] = "document_editing";
        editor["package"]!["package_id"] = "Fictional.Tests.DocumentEditor";
        editor["verification"]!["expected_package_id"] = "Fictional.Tests.DocumentEditor";
        applications.Add(editor);
    }

    private static ProposedPlan ReplaceActions(ProposedPlan plan, ImmutableArray<PlannedAction> actions)
    {
        // The normal resolver fixes action metadata and order. Use its own immutable construction
        // path to exercise isolated changes from a hypothetical later contract in this comparison.
        var constructor = Assert.Single(typeof(ProposedPlan).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance));
        var subjectType = constructor.GetParameters()[0].ParameterType;
        var subject = Activator.CreateInstance(subjectType,
            [plan.JobId, plan.GateDecisionId, plan.ProfileSha256, plan.CatalogSha256, plan.PolicySha256,
                plan.EvidenceSha256, plan.SyntheticData, plan.ActiveCapabilityIds]);
        return Assert.IsType<ProposedPlan>(constructor.Invoke(
            [subject, plan.PolicyDecision, plan.BlockedReason, actions, plan.RecipientChoices, null]));
    }

    private static PlanDiffResult Compare(ProposedPlan approved, ProposedPlan current) =>
        PlanDiff.Compare(approved, ImpactEstimator.Estimate(approved), current, ImpactEstimator.Estimate(current));

    private static void AssertReapproval(PlanDiffResult diff, PlanChangeKind expected)
    {
        Assert.True(diff.RequiresReapproval);
        Assert.Contains(diff.Changes, change => change.Kind == expected);
    }
}
