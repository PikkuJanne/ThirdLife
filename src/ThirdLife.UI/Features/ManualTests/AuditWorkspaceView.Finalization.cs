using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ThirdLife.Core.Finalization;

namespace ThirdLife.UI.Features.ManualTests;

public partial class AuditWorkspaceView
{
    private string? _finalizationJobId;
    private FinalizationChecklist? _displayedFinalization;
    private bool _refreshingFinalization;

    private async void RefreshFinalization(object sender, RoutedEventArgs e)
    {
        FocusAndReveal(FinalizationMessage);
        await Workspace.RefreshFinalizationAsync();
    }

    private async void SaveFinalization(object sender, RoutedEventArgs e)
    {
        if (FinalizationCheck.SelectedItem is not FinalizationOption check || !SaveFinalizationButton.IsEnabled)
        {
            return;
        }

        if (FinalizationOutcomeInput.SelectedValue is not FinalizationOutcome outcome)
        {
            SetError(FinalizationInputError, "Choose the result of your check.");
            FocusAndReveal(FinalizationOutcomeInput);
            return;
        }

        var reason = FinalizationReasonInput.SelectedValue as FinalizationReason?;
        if (outcome == FinalizationOutcome.NotApplicable && reason is null)
        {
            SetError(FinalizationInputError, "Choose the reason the check does not apply.");
            FocusAndReveal(FinalizationReasonInput);
            return;
        }

        if (string.IsNullOrWhiteSpace(FinalizationOperatorCode.Text))
        {
            SetError(FinalizationInputError, "Enter your workshop operator code.");
            FocusAndReveal(FinalizationOperatorCode);
            return;
        }

        FocusAndReveal(FinalizationMessage);
        await Workspace.SaveFinalizationAsync(check.Id, outcome, reason, FinalizationOperatorCode.Text);
        if (Workspace.OperationError.Length == 0)
        {
            ResetFinalizationInput();
        }
    }

    private void RefreshFinalizationChecklist()
    {
        var checklist = Workspace.Finalization;
        var jobChanged = !string.Equals(_finalizationJobId, Workspace.JobId, StringComparison.Ordinal);
        FinalizationChecklistFields.Visibility = VisibleWhen(checklist is not null);
        if (jobChanged || !ReferenceEquals(_displayedFinalization, checklist))
        {
            var previous = (FinalizationCheck.SelectedItem as FinalizationOption)?.Id;
            var options = checklist?.Rows.Select(static row =>
                new FinalizationOption(row.Rule.Id, RuleLabel(row.Rule.Id), ResultSummary(row))).ToArray() ?? [];
            if (jobChanged)
            {
                FinalizationOperatorCode.Clear();
                ClearArtifactTargets();
                FinalizationHistoryRegion.IsExpanded = false;
            }

            if (jobChanged || _displayedFinalization?.EvidenceVersion != checklist?.EvidenceVersion)
            {
                ResetFinalizationInput();
            }

            _finalizationJobId = Workspace.JobId;
            _displayedFinalization = checklist;
            _refreshingFinalization = true;
            try
            {
                if (FinalizationCheck.ItemsSource is not FinalizationOption[] oldOptions ||
                    !oldOptions.SequenceEqual(options))
                {
                    FinalizationCheck.ItemsSource = options;
                }

                if (jobChanged || previous is null || !options.Any(option => option.Id == previous))
                {
                    FinalizationCheck.SelectedIndex = options.Length == 0 ? -1 : 0;
                }
                else
                {
                    FinalizationCheck.SelectedValue = previous.Value;
                }
            }
            finally
            {
                _refreshingFinalization = false;
            }
        }

        RefreshFinalizationSelection();
    }

    private void FinalizationCheckChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_workspace is null || _refreshingFinalization)
        {
            return;
        }

        ResetFinalizationInput();
        RefreshFinalizationSelection();
        Announce(FinalizationFinding);
    }

    private void FinalizationOutcomeChanged(object sender, SelectionChangedEventArgs e)
    {
        bool needsReason = FinalizationOutcomeInput.SelectedValue is FinalizationOutcome.NotApplicable;
        FinalizationReasonFields.Visibility = VisibleWhen(needsReason);
        if (!needsReason)
        {
            FinalizationReasonInput.SelectedIndex = -1;
        }

        SetError(FinalizationInputError, string.Empty);
    }

    private void ResetFinalizationInput()
    {
        FinalizationOutcomeInput.SelectedIndex = -1;
        FinalizationReasonInput.SelectedIndex = -1;
        SetError(FinalizationInputError, string.Empty);
    }

    private void RefreshFinalizationSelection()
    {
        var id = (FinalizationCheck.SelectedItem as FinalizationOption)?.Id;
        var row = _displayedFinalization?.Rows.FirstOrDefault(candidate => candidate.Rule.Id == id);
        FinalizationRecordFields.Visibility = VisibleWhen(row?.Rule.AllowsOperatorConfirmation == true);
        FinalizationRecordFields.IsEnabled = Workspace.CanRecordFinalization;
        FinalizationGuidance.Text = row is null ? string.Empty : RuleGuidance(row.Rule.Id);
        FinalizationFinding.Text = row is null ? string.Empty : ResultLabel(row);
        AutomationProperties.SetName(FinalizationFinding, FinalizationFinding.Text);
        FinalizationNotApplicable.Visibility = VisibleWhen(row?.Rule.AllowsNotApplicable == true);

        var reasons = row?.Rule.AllowedReasons.Select(static reason =>
            new FinalizationReasonOption(reason, ReasonLabel(reason))).ToArray() ?? [];
        if (FinalizationReasonInput.ItemsSource is not FinalizationReasonOption[] previous ||
            !previous.SequenceEqual(reasons))
        {
            FinalizationReasonInput.ItemsSource = reasons;
        }

        var history = _displayedFinalization?.History.Where(record => record.RuleId == id)
            .Reverse()
            .Select(record => DescribeConfirmation(record, current: row is { NeedsRecheck: false, Confirmation: { } latest } &&
                latest.Sequence == record.Sequence))
            .ToArray() ?? [];
        var historyText = history.Length == 0 ? "No confirmation is saved for this check."
            : string.Join(Environment.NewLine, history);
        if (!string.Equals(FinalizationHistory.Text, historyText, StringComparison.Ordinal))
        {
            FinalizationHistory.Text = historyText;
        }
    }

    private static string ResultLabel(FinalizationRow row)
    {
        var outcome = row.NeedsRecheck ? "Recheck required. Earlier evidence is out of date."
            : row.Source switch
            {
                FinalizationSource.OperatorConfirmation => $"Operator confirmation. {OutcomeLabel(row.Outcome)}",
                FinalizationSource.AutomatedObservation => $"Automated observation. {OutcomeLabel(row.Outcome)}",
                _ => "No current result recorded.",
            };
        var requirement = row.IsBlocking ? "Required. Blocks handover."
            : row.Rule.IsRequired ? "Required." : "Advisory.";
        var artifact = row.HasBlockingArtifact ? " A saved artifact indication still requires resolution." : string.Empty;
        return $"{requirement} {outcome}{artifact}";
    }

    private static string DescribeConfirmation(FinalizationRecord record, bool current)
    {
        var state = current ? "Current confirmation" : "Earlier confirmation";
        var reason = record.Reason is { } value ? $" {ReasonLabel(value)}." : string.Empty;
        return string.Create(CultureInfo.InvariantCulture,
            $"{state} · {record.RecordedAtUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC · {record.OperatorId.Value} · {OutcomeLabel(record.Outcome)}{reason}");
    }

    private static string ResultSummary(FinalizationRow row) => row.HasBlockingArtifact ? "Open artifact indication"
        : row.NeedsRecheck ? "Needs recheck"
        : row.Outcome switch
        {
            FinalizationOutcome.Passed => "Clear",
            FinalizationOutcome.Failed => "Problem found",
            FinalizationOutcome.NotApplicable => "Not applicable",
            _ => "Unconfirmed",
        };

    private static string RuleLabel(FinalizationRuleId rule) => rule switch
    {
        FinalizationRuleId.WorkshopAccounts => "Workshop accounts",
        FinalizationRuleId.BrowserSessions => "Browser sessions",
        FinalizationRuleId.WiFiProfiles => "Wi-Fi profiles",
        FinalizationRuleId.Credentials => "Workshop credentials",
        FinalizationRuleId.Shares => "Workshop shares",
        FinalizationRuleId.TestMedia => "Test media",
        FinalizationRuleId.Diagnostics => "Diagnostic exports",
        FinalizationRuleId.Downloads => "Downloads and package residue",
        FinalizationRuleId.RemoteSupport => "VPN and remote support",
        FinalizationRuleId.ClipboardKeyMaterial => "Clipboard and key material",
        FinalizationRuleId.PendingRestart => "Pending restart",
        _ => "Workshop check",
    };

    private static string RuleGuidance(FinalizationRuleId rule) => rule switch
    {
        FinalizationRuleId.WorkshopAccounts => "Review known workshop accounts with Windows account management. Remove a workshop account only with authority over that account.",
        FinalizationRuleId.BrowserSessions => "Confirm workshop browser sessions were signed out.",
        FinalizationRuleId.WiFiProfiles => "Check for Wi-Fi profiles added for the workshop.",
        FinalizationRuleId.Credentials => "Confirm workshop credentials are clear. Leave secret values out of the job.",
        FinalizationRuleId.Shares => "Review known workshop shares in Windows share management. Confirm whether each workshop share was removed or remains authorized.",
        FinalizationRuleId.TestMedia => "Check that temporary test media is disconnected or removed.",
        FinalizationRuleId.Diagnostics => "Review diagnostic exports and temporary battery-report files. Preserve required workshop reports in protected workshop storage.",
        FinalizationRuleId.Downloads => "Review temporary workshop downloads and package staging. Confirm there is no active or recoverable package operation before handling its files.",
        FinalizationRuleId.RemoteSupport => "Review Remote Desktop, Remote Assistance, and any workshop VPN or remote-support tools in their Windows or vendor settings.",
        FinalizationRuleId.ClipboardKeyMaterial => "Confirm the workshop cleared temporary clipboard and key material. Leave secret values out of the job.",
        FinalizationRuleId.PendingRestart => "Review independent verification for any restart still required.",
        _ => string.Empty,
    };

    private static string OutcomeLabel(FinalizationOutcome outcome) => outcome switch
    {
        FinalizationOutcome.Passed => "Checked and clear.",
        FinalizationOutcome.Failed => "Problem found.",
        FinalizationOutcome.NotApplicable => "Not applicable.",
        _ => "Could not confirm.",
    };

    private static string ReasonLabel(FinalizationReason reason) => reason switch
    {
        FinalizationReason.NoWorkshopUse => "The workshop did not use this",
        FinalizationReason.CapabilityAbsent => "The capability is absent",
        _ => "Reason unavailable",
    };

    private sealed record FinalizationOption(FinalizationRuleId Id, string Label, string Result)
    {
        public string Caption => $"{Label} · {Result}";
    }

    private sealed record FinalizationReasonOption(FinalizationReason Reason, string Label);
}
