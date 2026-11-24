using ThirdLife.Policy.Evaluation;
using ThirdLife.Policy.Exceptions;

namespace ThirdLife.Policy.Decisions;

public enum DispositionRuleStatus
{
    Pass = 1,
    Fail,
    Exception,
    PendingConfirmation,
    Unavailable,
    Inactive,
}

public sealed record DispositionRuleDetail(
    DispositionSource Source, PolicyRuleOutcome Outcome, PolicyExceptionRecord? Exception)
{
    public DispositionRuleStatus Status => Exception is not null
        ? DispositionRuleStatus.Exception
        : Outcome.State switch
        {
            PolicyRuleState.Met => DispositionRuleStatus.Pass,
            PolicyRuleState.Unmet => DispositionRuleStatus.Fail,
            PolicyRuleState.PendingConfirmation => DispositionRuleStatus.PendingConfirmation,
            PolicyRuleState.Inactive => DispositionRuleStatus.Inactive,
            _ => DispositionRuleStatus.Unavailable,
        };

    public string StatusLabel => Status switch
    {
        DispositionRuleStatus.Pass => "Pass",
        DispositionRuleStatus.Fail => "Fail",
        DispositionRuleStatus.Exception => "Exception",
        DispositionRuleStatus.PendingConfirmation => "Pending confirmation",
        DispositionRuleStatus.Unavailable => "Unavailable",
        DispositionRuleStatus.Inactive => "Inactive",
        _ => throw new InvalidOperationException("The rule report status is invalid."),
    };
}
