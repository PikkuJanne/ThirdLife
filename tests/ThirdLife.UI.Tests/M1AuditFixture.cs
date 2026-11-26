using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ThirdLife.Core.Sanitization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Persistence;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

internal sealed class M1AuditFixture : IDisposable
{
    internal const string WindowTitle = "ThirdLife Setup Core - M1 audit review (synthetic fixture)";
    internal const string OperatorCode = "m1-fixture-operator";
    internal const string MediaCode = "m1-fixture-media";
    internal const string KeyboardCase = "MHT-002";
    internal const string ManualNote = "No donor keyboard is available in this synthetic fixture.";
    internal const string PrivateFailureMarker = "M1-PRIVATE-FIXTURE-FAILURE";
    internal static readonly DateOnly FixtureDate = new(2026, 10, 1);

    private readonly AuditTestStore _store = new();

    internal string WorkspaceRoot => _store.Root;
    internal string ReportRoot => Path.Combine(WorkspaceRoot, "reports");

    internal Task<SqliteJobStore> OpenSqliteAsync(CancellationToken cancellationToken = default) =>
        _store.OpenSqliteAsync(cancellationToken);

    internal AuditWorkspace CreateWorkspace() => new(
        async cancellationToken => await OpenSqliteAsync(cancellationToken).ConfigureAwait(false),
        [
            AuditTestProvider.Available("keyboard"),
            new AuditTestProvider("unavailable", _ => ValueTask.FromResult(ProviderReadResult.Unavailable())),
            new AuditTestProvider("failed", _ => throw new InvalidOperationException(PrivateFailureMarker)),
        ],
        new FixtureClock(),
        new WorkshopReportStore(ReportRoot));

    internal MainWindow CreateWindow(AuditWorkspace workspace, bool prefillInputs = false)
    {
        var window = new MainWindow(workspace)
        {
            Title = WindowTitle,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        if (!prefillInputs)
        {
            return window;
        }

        var audit = ShellTestHost.Element<Features.ManualTests.AuditWorkspaceView>(window, "AuditView");
        workspace.PropertyChanged += FillInputsOnce;
        window.Closed += (_, _) => workspace.PropertyChanged -= FillInputsOnce;
        return window;

        void FillInputsOnce(object? sender, PropertyChangedEventArgs args)
        {
            if (workspace.Job is null || workspace.IsBusy)
            {
                return;
            }

            workspace.PropertyChanged -= FillInputsOnce;
            Assert.IsType<TextBox>(audit.FindName("OperatorIdInput")).Text = OperatorCode;
            Assert.IsType<ComboBox>(audit.FindName("SanitizationResult")).SelectedValue = SanitizationState.Verified;
            Assert.IsType<ComboBox>(audit.FindName("SanitizationMethod")).SelectedValue = "clear";
            Assert.IsType<DatePicker>(audit.FindName("SanitizationDate")).SelectedDate = FixtureDate.ToDateTime(TimeOnly.MinValue);
            Assert.IsType<TextBox>(audit.FindName("SanitizationMediaId")).Text = MediaCode;
            Assert.IsType<CheckBox>(audit.FindName("ExternalRecordConfirmed")).IsChecked = false;
            Assert.IsType<ComboBox>(audit.FindName("ManualCheck")).SelectedValue = KeyboardCase;
            Assert.IsType<TextBox>(audit.FindName("ManualNotes")).Text = ManualNote;
        }
    }

    public void Dispose() => _store.Dispose();

    private sealed class FixtureClock : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => new(2026, 10, 1, 14, 0, 0, TimeSpan.Zero);
    }
}
