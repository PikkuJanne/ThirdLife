using System.ComponentModel;
using System.Globalization;

namespace ThirdLife.UI.Shell;

public enum ShellStage
{
    Intake,
    Inspect,
    Decide,
    Prepare,
    Verify,
    Handover,
}

public enum ShellActivity
{
    Idle,
    Running,
    Cancelling,
    Cancelled,
    Completed,
    Failed,
    RequiresReview,
}

/// <summary>
/// Bounded presentation state for the audit shell. It neither collects evidence nor starts work.
/// </summary>
public sealed class ShellViewModel : INotifyPropertyChanged
{
    private int completedWorkUnits;
    private int totalWorkUnits;
    private bool auditAvailable;
    private bool auditHasOpenJob;
    private string auditGateStatus = "";
    private string auditAssessmentStatus = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ShellStage SelectedStage { get; private set; } = ShellStage.Intake;

    public ShellActivity Activity { get; private set; } = ShellActivity.Idle;

    public string Introduction => auditAvailable
        ? "Create or open a job in the audit controls. Reviewing a stage does not authorize device changes."
        : "No job is open. Reviewing a stage does not complete it or authorize device changes.";

    public string StageHeading => SelectedStage == ShellStage.Intake ? "Intake" : "Inspect";

    public string StageSummary => auditHasOpenJob
        ? SelectedStage == ShellStage.Intake
            ? "Review the starting requirements and the open job's external sanitization record."
            : "Review the inspection requirements and the assessment evidence saved to the open job."
        : SelectedStage == ShellStage.Intake
        ? "Review the starting requirements. No job is open and no intake evidence has been recorded."
        : "Review the inspection requirements. No job is open and no assessment has been run.";

    public string RequirementsText => SelectedStage == ShellStage.Intake
        ? "Record lawful ownership and the external media sanitization result before preparation. Unknown or failed sanitization blocks preparation. Windows installation is an external prerequisite."
        : "Recorded intake and an approved sanitization gate are required before assessment. Missing provider data remains unknown. Hardware function requires human inspection where automated evidence cannot establish it.";

    public string EvidenceText => auditHasOpenJob
        ? SelectedStage == ShellStage.Intake ? auditGateStatus : auditAssessmentStatus
        : SelectedStage == ShellStage.Intake
        ? "Not available: no job or intake evidence is open. Ownership and sanitization have not been confirmed."
        : "Not available: no assessment evidence is open. Device requirements have not been evaluated and no checks have passed.";

    public string PlanText => auditAvailable
        ? "The audit controls show evidence and candidate decisions. Use Prepare to review a plan for the current assessment."
        : SelectedStage == ShellStage.Intake
        ? "No plan exists. Job entry and sanitization recording are not available in this shell. You can review the Inspect stage."
        : "No plan exists. Assessment collection is not available in this shell. Decide, Prepare, Verify, and Handover remain unavailable until their required evidence and workflows exist.";

    public string StatusText => auditHasOpenJob
        ? $"{StageHeading} selected. An audit job is open. Device readiness is unknown."
        : $"{StageHeading} selected. No job is open. Device readiness is unknown.";

    public string ActivityText => auditAvailable ? auditAssessmentStatus : Activity switch
    {
        ShellActivity.Idle => "No operation is running.",
        ShellActivity.Running => totalWorkUnits == 0
            ? "Operation running. Progress is not yet available."
            : string.Format(CultureInfo.CurrentCulture, "Operation running. {0} of {1} work units reported.", completedWorkUnits, totalWorkUnits),
        ShellActivity.Cancelling => "Cancellation requested. Waiting for the operation to stop safely.",
        ShellActivity.Cancelled => "Operation cancelled. Review any partial evidence before retrying.",
        ShellActivity.Completed => "Operation completed. Device readiness has not been verified.",
        ShellActivity.Failed => "Operation failed. Review the evidence before retrying.",
        ShellActivity.RequiresReview => "Human review required. Review the evidence before continuing.",
        _ => throw new InvalidOperationException("The activity is not recognized."),
    };

    public bool IsBusy => Activity is ShellActivity.Running or ShellActivity.Cancelling;

    public int ProgressValue => completedWorkUnits;

    public int ProgressMaximum => Math.Max(1, totalWorkUnits);

    public bool IsIndeterminate => IsBusy && totalWorkUnits == 0;

    public static bool CanNavigate(ShellStage stage) => stage is ShellStage.Intake or ShellStage.Inspect;

    internal void SetAuditContext(bool hasOpenJob, string gateStatus, string assessmentStatus)
    {
        if (auditAvailable && auditHasOpenJob == hasOpenJob &&
            auditGateStatus == gateStatus && auditAssessmentStatus == assessmentStatus)
        {
            return;
        }

        bool initialized = auditAvailable;
        string previousStatus = StatusText;
        string previousActivity = ActivityText;
        auditAvailable = true;
        auditHasOpenJob = hasOpenJob;
        auditGateStatus = gateStatus;
        auditAssessmentStatus = assessmentStatus;
        if (!initialized)
        {
            Notify(nameof(Introduction));
            Notify(nameof(PlanText));
        }

        Notify(nameof(StageSummary));
        Notify(nameof(EvidenceText));
        if (StatusText != previousStatus) { Notify(nameof(StatusText)); }
        if (ActivityText != previousActivity) { Notify(nameof(ActivityText)); }
    }

    public bool Navigate(ShellStage stage)
    {
        if (!CanNavigate(stage))
        {
            return false;
        }

        if (SelectedStage == stage)
        {
            return true;
        }

        SelectedStage = stage;
        Notify(nameof(SelectedStage));
        Notify(nameof(StageHeading));
        Notify(nameof(StageSummary));
        Notify(nameof(RequirementsText));
        Notify(nameof(EvidenceText));
        Notify(nameof(PlanText));
        Notify(nameof(StatusText));
        return true;
    }

    /// <summary>
    /// Projects a supplied activity state for announcement tests and future typed callers.
    /// The shell starts idle and has no operation runner. Counts describe work, not device health.
    /// </summary>
    public void SetActivity(ShellActivity activity, int completed = 0, int total = 0)
    {
        if (!Enum.IsDefined(activity))
        {
            throw new ArgumentOutOfRangeException(nameof(activity));
        }

        if (total is < 0 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(total));
        }

        if (completed < 0 || completed > total)
        {
            throw new ArgumentOutOfRangeException(nameof(completed));
        }

        if (activity == ShellActivity.Idle && total != 0)
        {
            throw new ArgumentException("An idle operation cannot have progress.", nameof(total));
        }

        if (activity == ShellActivity.Completed && completed != total)
        {
            throw new ArgumentException("A completed operation cannot contain unfinished work units.", nameof(completed));
        }

        if (Activity == activity && completedWorkUnits == completed && totalWorkUnits == total)
        {
            return;
        }

        Activity = activity;
        completedWorkUnits = completed;
        totalWorkUnits = total;
        Notify(nameof(Activity));
        Notify(nameof(ActivityText));
        Notify(nameof(IsBusy));
        Notify(nameof(ProgressValue));
        Notify(nameof(ProgressMaximum));
        Notify(nameof(IsIndeterminate));
    }

    private void Notify(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
