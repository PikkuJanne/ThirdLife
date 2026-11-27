using System.Collections.Immutable;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;

namespace ThirdLife.Actions.Planning;

public static class MaintenancePhases
{
    // An approved package has not started. Only an outstanding package attempt holds
    // Windows servicing. Keep old plans in scope when their result is unresolved.
    public static bool RequiresPackageRecovery(ImmutableArray<JournalHistory> histories) =>
        histories.Where(history => !history.Plan.SyntheticData).Any(history => history.Plan.Actions.Any(action =>
            action.Request is InstallPackageActionRequest &&
            history.StateOf(action.ActionId) is JournalState.Started or JournalState.Applied or JournalState.RequiresReview));
}
