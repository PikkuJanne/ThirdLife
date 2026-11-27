using System.Windows;
using System.Windows.Controls;
using ThirdLife.Core.Handover;

namespace ThirdLife.UI.Features.ManualTests;

public partial class AuditWorkspaceView
{
    private static readonly SetupAreaOption[] SetupAreaOptions =
    [
        new(RecipientSetupArea.PersonalAccount, "Personal account", "The recipient signs in or creates their own account."),
        new(RecipientSetupArea.Backup, "Backup", "The recipient chooses a backup destination. Setup remains pending until a restore check succeeds."),
        new(RecipientSetupArea.RecoveryMaterial, "Recovery material", "The recipient keeps control of recovery material and decides where to keep it."),
        new(RecipientSetupArea.Browser, "Browser", "The recipient chooses a browser and whether to sign in."),
        new(RecipientSetupArea.Accessibility, "Accessibility", "The recipient chooses accessibility preferences for their account."),
        new(RecipientSetupArea.OtherPreferences, "Other preferences", "Leave other personal settings to the recipient."),
    ];

    private string? _handoverModeJob;
    private long _displayedModeSequence;
    private string? _lastHandoverModeStatus;
    private SavedRecipientHandover? _displayedHandover;

    private void RefreshRecipientHandover()
    {
        var record = Workspace.RecipientHandover;
        var handover = record?.Handover;
        var modeSequence = record?.ModeSequence ?? 0;
        if (_handoverModeJob != Workspace.JobId || _displayedModeSequence != modeSequence)
        {
            if (_handoverModeJob != Workspace.JobId)
            {
                HandoverModeOperator.Clear();
            }

            _handoverModeJob = Workspace.JobId;
            _displayedModeSequence = modeSequence;
            HandoverModeChoice.SelectedValue = handover?.Mode;
            RecipientSetupAreaChoice.SelectedIndex = -1;
            ResetRecipientChoice();
        }

        if (!ReferenceEquals(_displayedHandover, record))
        {
            _displayedHandover = record;
            RecipientChoiceFields.Visibility = VisibleWhen(handover?.Mode == HandoverMode.Assisted);
            RecipientSetupAreaChoice.ItemsSource ??= SetupAreaOptions;
            RecipientChoicesSummary.Text = handover is null ? string.Empty : string.Join(Environment.NewLine,
                handover.Decisions.Select(decision =>
                    $"{SetupAreaOptions.Single(option => option.Area == decision.Area).Label} · {ChoiceLabel(decision.Choice)}"));
        }

        if (_lastHandoverModeStatus != Workspace.HandoverModeStatus)
        {
            _lastHandoverModeStatus = Workspace.HandoverModeStatus;
            Announce(HandoverModeMessage);
        }
    }

    private async void SaveHandoverMode(object sender, RoutedEventArgs e)
    {
        if (HandoverModeChoice.SelectedValue is not HandoverMode mode)
        {
            SetError(HandoverModeError, "Choose a handover mode.");
            FocusAndReveal(HandoverModeChoice);
            return;
        }

        if (!CheckHandoverOperator())
        {
            return;
        }

        SetError(HandoverModeError, string.Empty);
        FocusAndReveal(HandoverModeMessage);
        await Workspace.SaveHandoverModeAsync(mode, HandoverModeOperator.Text);
    }

    private async void SaveRecipientChoice(object sender, RoutedEventArgs e)
    {
        if (RecipientSetupAreaChoice.SelectedValue is not RecipientSetupArea area)
        {
            SetError(HandoverModeError, "Choose a setup area.");
            FocusAndReveal(RecipientSetupAreaChoice);
            return;
        }

        if (RecipientSetupResult.SelectedValue is not RecipientSetupChoice choice)
        {
            SetError(HandoverModeError, "Choose the guidance result.");
            FocusAndReveal(RecipientSetupResult);
            return;
        }

        if (RecipientPresentCheck.IsChecked != true)
        {
            SetError(HandoverModeError, "Confirm the recipient is present and chose this result.");
            FocusAndReveal(RecipientPresentCheck);
            return;
        }

        if (!CheckHandoverOperator())
        {
            return;
        }

        SetError(HandoverModeError, string.Empty);
        FocusAndReveal(HandoverModeMessage);
        await Workspace.SaveRecipientSetupChoiceAsync(area, choice, recipientPresent: true, HandoverModeOperator.Text);
        ResetRecipientChoice();
    }

    private bool CheckHandoverOperator()
    {
        if (!string.IsNullOrWhiteSpace(HandoverModeOperator.Text))
        {
            return true;
        }

        SetError(HandoverModeError, "Enter your workshop operator code.");
        FocusAndReveal(HandoverModeOperator);
        return false;
    }

    private void RecipientSetupAreaChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_workspace is null)
        {
            return;
        }

        ResetRecipientChoice();
        RecipientSetupGuidance.Text = (RecipientSetupAreaChoice.SelectedItem as SetupAreaOption)?.Guidance ?? string.Empty;
    }

    private void ResetRecipientChoice()
    {
        RecipientPresentCheck.IsChecked = false;
        RecipientSetupResult.SelectedIndex = -1;
        SetError(HandoverModeError, string.Empty);
    }

    private static string ChoiceLabel(RecipientSetupChoice choice) => choice switch
    {
        RecipientSetupChoice.Deferred => "Pending",
        RecipientSetupChoice.GuidanceRequested => "Guidance requested",
        RecipientSetupChoice.Declined => "Recipient declined",
        _ => throw new ArgumentOutOfRangeException(nameof(choice)),
    };

    private sealed record SetupAreaOption(RecipientSetupArea Area, string Label, string Guidance);
}
