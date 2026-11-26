using System.Text;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Policy.Exceptions;

namespace ThirdLife.Reports.WorkshopReport;

public enum WorkshopPreparationState
{
    Verified = 1,
    Skipped,
    Failed,
    ConsentDeclined,
    Unverified,
    RolledBack,
}

public enum WorkshopPreparationEffect
{
    Verified = 1,
    OptionalOmission,
    ReviewRequired,
    AuthorizedException,
}

public sealed record WorkshopPreparationAction(
    string CapabilityId,
    bool Essential,
    WorkshopPreparationState State,
    WorkshopPreparationEffect Effect,
    bool LatestForAction,
    string? ResultCode,
    DateTimeOffset? OccurredAtUtc,
    OperatorId? SkipOperator,
    PolicyExceptionRecord? Exception);

public sealed record WorkshopPreparationPlan(
    JobId JobId,
    string PlanSha256,
    bool SyntheticData,
    Disposition? CurrentDisposition,
    IReadOnlyList<WorkshopPreparationAction> Actions);

public static partial class WorkshopReportRenderer
{
    public static string DescribePreparationDisposition(Disposition? disposition, bool syntheticData) => (disposition, syntheticData) switch
    {
        (null, _) => "No current preparation disposition is established from the saved action history.",
        (Disposition.ReadyToPrepare, true) => "Synthetic plan outcomes meet the essential requirements for this test.",
        (Disposition.ReadyToPrepare, false) => "The current plan meets its essential action requirements.",
        (Disposition.HumanReviewRequired, true) => "The synthetic plan requires review of its saved action outcomes.",
        (Disposition.HumanReviewRequired, false) => "Human review required. The current plan has unresolved action outcomes.",
        (Disposition.RepairAndRetest, true) => "The synthetic plan requires repair and retest of an essential action.",
        (Disposition.RepairAndRetest, false) => "Repair and retest. An essential action did not meet the current plan requirements.",
        (Disposition.DoNotDeploy, _) => "Do not deploy. Review the current plan's blocking action outcomes.",
        _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
    };

    public static string DescribePreparationAction(WorkshopPreparationAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var state = action.State switch
        {
            WorkshopPreparationState.Verified => "Independently verified.",
            WorkshopPreparationState.Skipped => "Skipped by the workshop operator. The action remains incomplete.",
            WorkshopPreparationState.Failed => "The attempt failed.",
            WorkshopPreparationState.ConsentDeclined => "Windows consent was declined.",
            WorkshopPreparationState.Unverified => "Independent verification is pending or unavailable.",
            WorkshopPreparationState.RolledBack => "Rollback was recorded.",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        var effect = action.Effect switch
        {
            WorkshopPreparationEffect.Verified => "This action meets its requirement.",
            WorkshopPreparationEffect.OptionalOmission => "The preparation disposition allows this optional outcome.",
            WorkshopPreparationEffect.ReviewRequired => "This outcome blocks a ready preparation disposition.",
            WorkshopPreparationEffect.AuthorizedException => "A named policy exception permits this essential outcome.",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        return action.LatestForAction ? $"{state} {effect}" : $"{state} A later record controls this action's current disposition.";
    }

    private static void WritePreparationRecords(StringBuilder report, JobId jobId, IReadOnlyList<WorkshopPreparationPlan> plans,
        CancellationToken cancellationToken)
    {
        // The input comes from bounded journal entries and at most one current plan's unrecorded actions.
        if (plans.Count > 128 || plans.Count(plan => plan.CurrentDisposition is not null) > 1 ||
            plans.Any(plan => plan.JobId != jobId) ||
            plans.Sum(plan => (long)plan.Actions.Count) > 1_088)
        {
            throw new ArgumentException("The workshop report requires bounded preparation records.", nameof(plans));
        }

        Line(report, "");
        Line(report, "Preparation action disposition");
        var current = plans.SingleOrDefault(plan => plan.CurrentDisposition is not null);
        Line(report, DescribePreparationDisposition(current?.CurrentDisposition, current?.SyntheticData == true));
        Line(report, "Whole-device acceptance still requires applicable device checks, cold-boot verification and finalization.");
        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Line(report, "");
            var scope = plan.CurrentDisposition is null ? "Earlier or no longer applicable plan" : "Current plan";
            Line(report, $"{scope}{(plan.SyntheticData ? " (synthetic test)" : "")}. Plan SHA-256 {SafeCode(plan.PlanSha256)}.");
            if (plan.Actions.Count == 0)
            {
                Line(report, "There are no skipped, failed or unresolved actions to list for this earlier plan.");
            }

            foreach (var action in plan.Actions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Line(report, $"- {SafeCode(action.CapabilityId)} ({(action.Essential ? "essential" : "optional")}). {DescribePreparationAction(action)}");
                if (action.ResultCode is { } result && action.OccurredAtUtc is { } recordedAt)
                {
                    Line(report, $"  Journal result {SafeCode(result)} at {Timestamp(recordedAt)}.");
                }

                if (action.SkipOperator is { } skippedBy)
                {
                    Line(report, $"  Workshop operator code {SafeCode(skippedBy.Value)}.");
                }

                if (action.Exception is { } exception)
                {
                    if (exception.JobId != jobId)
                    {
                        throw new ArgumentException("The preparation exception belongs to another job.", nameof(plans));
                    }

                    Line(report, $"  Exception {SafeCode(exception.ExceptionId)} under authorization {SafeCode(exception.AuthorizationId)}.");
                    Line(report, $"  Workshop operator code {SafeCode(exception.OperatorId.Value)} at {Timestamp(exception.RecordedAtUtc)}.");
                    Line(report, $"  Reason recorded by the operator. {SafePolicyPresentation(exception.Reason)}");
                }
            }
        }
    }
}
