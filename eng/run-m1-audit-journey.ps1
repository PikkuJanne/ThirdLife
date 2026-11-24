[CmdletBinding()]
param([switch] $VerifyAccessibility)

# An external keyboard driver for the Release fixture. Only the nonce-bound window receives input.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms

if ($VerifyAccessibility) {
    Add-Type -ReferencedAssemblies UIAutomationClient.dll, UIAutomationTypes.dll, WindowsBase.dll, System.Threading.dll -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Windows.Automation;

public sealed class AuditAccessibilityProbe : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y, Delta, Flags, Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public MouseInput Mouse; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr handle, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    private readonly AutomationElement window;
    private readonly AutomationEventHandler listener;
    private readonly string[] events = new string[32];
    private int eventIndex;

    public AuditAccessibilityProbe(AutomationElement element)
    {
        window = element;
        listener = OnAnnouncement;
        Automation.AddAutomationEventHandler(AutomationElementIdentifiers.LiveRegionChangedEvent,
            window, TreeScope.Descendants, listener);
    }

    public void Dispose()
    {
        Automation.RemoveAutomationEventHandler(AutomationElementIdentifiers.LiveRegionChangedEvent, window, listener);
    }

    private void OnAnnouncement(object sender, AutomationEventArgs args)
    {
        var element = sender as AutomationElement;
        if (element == null) return;
        try
        {
            var current = element.Current;
            var entry = current.AutomationId + "|" + current.Name;
            lock (events) { events[eventIndex++ % events.Length] = entry; }
        }
        catch (ElementNotAvailableException) { }
    }

    public bool HasAnnouncement(string id, string message)
    {
        lock (events)
        {
            return Array.Exists(events, entry => entry != null &&
                entry.StartsWith(id + "|" + message, StringComparison.Ordinal));
        }
    }

    public void Wheel(int delta)
    {
        IntPtr owned = new IntPtr(window.Current.NativeWindowHandle);
        if (GetForegroundWindow() != owned || !GetCursorPos(out Point cursor) ||
            GetAncestor(WindowFromPoint(cursor), 2) != owned)
            throw new InvalidOperationException("Wheel input requires the pointer over the foreground fixture.");
        var input = new Input();
        input.Mouse.Delta = delta;
        input.Mouse.Flags = 0x0800;
        if (SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input))) != 1)
            throw new InvalidOperationException("Could not deliver wheel input to the fixture.");
    }
}
'@
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$binaryRoot = Join-Path $repositoryRoot 'tests/ThirdLife.UI.Tests/bin/Release/net10.0-windows'
$scenarioId = [Guid]::NewGuid().ToString('N')
$result = [ordered]@{
    schema_version = 1
    scenario = 'm1_external_audit_journey'
    result = 'failed'
    source_commit = (& git -C $repositoryRoot rev-parse HEAD)
    tracked_source_clean = [string]::IsNullOrWhiteSpace((& git -C $repositoryRoot status --porcelain=v1 --untracked-files=no | Out-String))
    fixture_sha256 = (Get-FileHash -LiteralPath (Join-Path $binaryRoot 'ThirdLife.UI.Tests.dll')).Hash.ToLowerInvariant()
    ui_sha256 = (Get-FileHash -LiteralPath (Join-Path $binaryRoot 'ThirdLife.UI.dll')).Hash.ToLowerInvariant()
    reports_sha256 = (Get-FileHash -LiteralPath (Join-Path $binaryRoot 'ThirdLife.Reports.dll')).Hash.ToLowerInvariant()
    steps = @()
    cleanup = 'pending'
}

function Wait-For([scriptblock] $Condition, [string] $Description, [int] $Seconds = 5) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $found = & $Condition
        if ($found) { return $found }
        Start-Sleep -Milliseconds 75
    } while ($clock.Elapsed.TotalSeconds -lt $Seconds)
    throw "Timed out waiting for $Description."
}

function Find-FixtureElement([string] $Id) {
    $query = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $query)
}

function Read-Value([string] $Id) {
    $element = Find-FixtureElement $Id
    $element.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
}

function Read-FixtureFocus {
    $focused = [Windows.Automation.AutomationElement]::FocusedElement
    if (-not $focused -or $focused.Current.ProcessId -ne $window.Current.ProcessId) {
        throw 'Keyboard focus left the owned fixture.'
    }
    $focused.Current.AutomationId
}

function Send-At([string] $Id, [string] $Keys) {
    $null = Wait-For {
        (Read-FixtureFocus) -eq $Id
    } "keyboard focus on $Id" 3
    [Windows.Forms.SendKeys]::SendWait($Keys)
}

function Move-TabTo([string] $Id, [switch] $Backward) {
    $keys = if ($Backward) { '+{TAB}' } else { '{TAB}' }
    for ($attempt = 0; $attempt -lt 32; $attempt++) {
        if ((Read-FixtureFocus) -eq $Id) { return }
        [Windows.Forms.SendKeys]::SendWait($keys)
    }
    throw "The keyboard did not reach $Id within the bounded tab sequence."
}

function Assert-Announcement([string] $Id, [string] $Message) {
    if ($VerifyAccessibility) {
        $null = Wait-For { $probe.HasAnnouncement($Id, $Message) } "live-region event for $Id ($Message)"
        $result.live_region_events += $Message
    }
}

function Measure-FractionalWheel {
    $viewport = Find-FixtureElement 'ShellScroll'
    $scroll = $viewport.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
    $bounds = $viewport.Current.BoundingRectangle
    $cursor = [Windows.Forms.Cursor]::Position
    try {
        [Windows.Forms.Cursor]::Position = [Drawing.Point]::new(
            [int]($bounds.X + $bounds.Width / 2), [int]($bounds.Y + $bounds.Height / 3))
        $scroll.SetScrollPercent(-1, 0)
        $null = Wait-For { $scroll.Current.VerticalScrollPercent -eq 0 } 'wheel baseline'
        $probe.Wheel(-120)
        $detent = Wait-For { $scroll.Current.VerticalScrollPercent } 'one wheel detent'
        $scroll.SetScrollPercent(-1, 0)
        $null = Wait-For { $scroll.Current.VerticalScrollPercent -eq 0 } 'fractional wheel baseline'
        $previous = 0
        $partial = @(for ($pulse = 0; $pulse -lt 6; $pulse++) {
            $probe.Wheel(-20)
            $previous = Wait-For { if ($scroll.Current.VerticalScrollPercent -gt $previous) { $scroll.Current.VerticalScrollPercent } } 'one fractional wheel pulse'
            $previous
        })
        if ($partial[0] -ge $detent -or [Math]::Abs($partial[-1] - $detent) -gt 0.2) {
            throw 'Six fractional wheel pulses did not match one wheel detent.'
        }
        $result.wheel = @{ detent_percent = $detent; fractional_percent = $partial }
    }
    finally { [Windows.Forms.Cursor]::Position = $cursor }
}

$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = (Get-Command dotnet -CommandType Application).Source
$startInfo.WorkingDirectory = $repositoryRoot
$startInfo.Arguments = 'test tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName=ThirdLife.UI.Tests.M1FixtureWindowTests.ExternalAuditJourneyPersistsItsReport -- RunConfiguration.TreatNoTestsAsError=true'
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.EnvironmentVariables['THIRDLIFE_M1_JOURNEY_ID'] = $scenarioId
$startInfo.EnvironmentVariables['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
$timer = [Diagnostics.Stopwatch]::StartNew()
$testRun = [Diagnostics.Process]::Start($startInfo)
$stdout = $testRun.StandardOutput.ReadToEndAsync()
$stderr = $testRun.StandardError.ReadToEndAsync()
$window = $null
$probe = $null

try {
    $windowQuery = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $scenarioId)
    $window = Wait-For {
        if ($testRun.HasExited) { throw 'Fixture runner exited before showing its window.' }
        [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, $windowQuery)
    } 'the owned fixture window' 15

    if ((Read-Value 'CurrentJobId') -or (Find-FixtureElement 'StartAssessmentButton').Current.IsEnabled) {
        throw 'Fresh fixture must start with no job and a disabled assessment.'
    }
    $result.steps += 'empty_fixture_and_disabled_assessment'

    $controller = New-Object -ComObject WScript.Shell
    if (-not $controller.AppActivate([int]$window.Current.ProcessId)) { throw 'Could not activate the owned fixture window.' }
    if ($VerifyAccessibility) {
        $probe = [AuditAccessibilityProbe]::new($window)
        $result.live_region_events = @()
        Measure-FractionalWheel
    }
    Move-TabTo 'OpenAuditWorkspaceButton' -Backward
    Send-At 'OpenAuditWorkspaceButton' '{ENTER}'
    Move-TabTo 'CreateJobButton'
    Send-At 'CreateJobButton' '{ENTER}'
    $jobId = Wait-For { Read-Value 'CurrentJobId' } 'a created job identifier'
    Assert-Announcement 'AuditStatus' 'Job created.'
    $confirmation = Wait-For { Find-FixtureElement 'ExternalRecordConfirmed' } 'fixture confirmation'
    $toggle = $confirmation.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    if ($toggle.Current.ToggleState -ne 'Off' -or (Find-FixtureElement 'StartAssessmentButton').Current.IsEnabled) {
        throw 'The new job must still require explicit external confirmation.'
    }
    $result.steps += 'created_job_requires_explicit_confirmation'

    # The handed instructions offered Enter on the confirmation check box, followed by Tab to Save.
    Send-At 'ExternalRecordConfirmed' '{ENTER}'
    $null = Wait-For { $toggle.Current.ToggleState -eq 'On' } 'Enter to confirm the external record'
    Send-At 'ExternalRecordConfirmed' '{TAB}'
    Send-At 'RecordSanitizationButton' '{ENTER}'
    $null = Wait-For { (Find-FixtureElement 'StartAssessmentButton').Current.IsEnabled } 'assessment enabled by the saved intake and gate'
    if ((Find-FixtureElement 'SavedSanitization').Current.Name -notmatch 'clear, verified\. Synthetic fixture') {
        throw 'Saved intake did not identify the verified synthetic record.'
    }
    $result.steps += 'enter_confirmation_and_save_enable_assessment'
    Assert-Announcement 'AuditStatus' 'Sanitization permits read-only assessment.'

    Send-At 'StartAssessmentButton' '{ENTER}'
    $null = Wait-For {
        (Find-FixtureElement 'AssessmentMessage').Current.Name -match 'needs review.*failed, unavailable and unfinished findings' -and
            (Find-FixtureElement 'CloseJobButton').Current.IsEnabled
    } 'assessment completed with a review outcome'
    $result.steps += 'read_only_assessment_requires_review'
    Assert-Announcement 'AssessmentMessage' 'Assessment needs review.'

    Move-TabTo 'ManualOutcome'
    Send-At 'ManualOutcome' '{END}'
    Move-TabTo 'ManualUnavailableReason'
    Send-At 'ManualUnavailableReason' '{HOME}{DOWN}'
    Move-TabTo 'SaveManualResultButton'
    Send-At 'SaveManualResultButton' '{ENTER}'
    $null = Wait-For { (Find-FixtureElement 'ManualCurrentResult').Current.Name -match 'Latest saved result: Not available.*equipment missing' } 'persisted manual result'
    $result.steps += 'manual_unavailable_result_saved'

    Move-TabTo 'CloseJobButton' -Backward
    Send-At 'CloseJobButton' '{ENTER}'
    $null = Wait-For {
        -not (Read-Value 'CurrentJobId') -and -not (Find-FixtureElement 'StartAssessmentButton').Current.IsEnabled
    } 'closing to clear the open job and disable assessment'
    Send-At 'JobIdInput' '{TAB}'
    Send-At 'OpenJobButton' '{ENTER}'
    $null = Wait-For {
        (Read-Value 'CurrentJobId') -eq $jobId -and
            (Find-FixtureElement 'AuditStatus').Current.Name -match 'Job opened' -and
            (Find-FixtureElement 'AssessmentMessage').Current.Name -match 'needs review'
    } 'the same saved job and assessment after reopening'
    Move-TabTo 'ManualCheck'
    Send-At 'ManualCheck' '{HOME}{DOWN}'
    $null = Wait-For { (Find-FixtureElement 'ManualCurrentResult').Current.Name -match 'Latest saved result: Not available.*equipment missing' } 'the saved Keyboard result after reopening'
    $result.steps += 'closed_and_reopened_same_job_assessment_and_manual_result'

    Move-TabTo 'GenerateReportButton'
    Send-At 'GenerateReportButton' '{ENTER}'
    Assert-Announcement 'ReportMessage' 'Workshop report saved.'
    Send-At 'OpenReportButton' '{ENTER}'
    $null = Wait-For {
        $preview = Find-FixtureElement 'WorkshopReportPreview'
        $preview -and (Read-Value 'WorkshopReportPreview').IndexOf($jobId, [StringComparison]::Ordinal) -ge 0
    } 'the same job identifier in the visible report preview'
    $result.steps += 'generated_and_opened_matching_report'
}
catch { $result.error = $_.Exception.Message }
finally {
    if ($probe) {
        try { $probe.Dispose() }
        catch { $result.probe_cleanup_error = $_.Exception.Message }
    }
    if ($window) {
        try { $window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close() }
        catch { $result.close_error = $_.Exception.Message }
    }
    if ($testRun.WaitForExit(12000)) {
        $result.cleanup = 'normal_test_runner_exit'
        $result.test_exit_code = $testRun.ExitCode
        if ($testRun.ExitCode -eq 0 -and -not $result.Contains('error') -and
            -not $result.Contains('close_error') -and -not $result.Contains('probe_cleanup_error')) {
            $result.steps += 'durable_intake_assessment_manual_and_report_agree'
            $result.result = 'passed'
        }
        else { [Console]::Error.WriteLine(($stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()).Trim()) }
    }
    else {
        $testRun.Kill($true)
        $result.cleanup = if ($testRun.WaitForExit(5000)) { 'owned_test_runner_terminated' } else { 'termination_unconfirmed' }
    }
    $testRun.Dispose()
    $result.total_elapsed_ms = $timer.ElapsedMilliseconds
}

$result | ConvertTo-Json -Depth 3
if ($result.result -ne 'passed') { exit 1 }
