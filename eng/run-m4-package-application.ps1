#Requires -Version 5.1
[CmdletBinding(DefaultParameterSetName = 'Inspect')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Inspect')][switch] $Inspect,
    [Parameter(Mandatory, ParameterSetName = 'Preview')][switch] $Preview,
    [Parameter(Mandatory, ParameterSetName = 'ResumePreview')][switch] $ResumePreview,
    [Parameter(Mandatory, ParameterSetName = 'Reopen')][switch] $Reopen,
    [Parameter(Mandatory, ParameterSetName = 'Preview')]
    [ValidatePattern('\A[A-Za-z0-9_.-]{1,64}\z')][string] $OperatorCode,
    [Parameter(Mandatory, ParameterSetName = 'Preview')]
    [ValidatePattern('\A[0-9]{4}-[0-9]{2}-[0-9]{2}\z')][string] $VerifiedNoDonorStorageOn,
    [Parameter(Mandatory, ParameterSetName = 'ResumePreview')]
    [Parameter(Mandatory, ParameterSetName = 'Reopen')]
    [ValidatePattern('\Ajob-[0-9a-f]{32}\z')][string] $Job,
    [Parameter(Mandatory, ParameterSetName = 'Reopen')]
    [ValidatePattern('\A[0-9a-f]{64}\z')][string] $ExpectedPlanSha256,
    [Parameter(Mandatory, ParameterSetName = 'Reopen')]
    [ValidateSet('Verified', 'RequiresReview')][string] $ExpectedRecovery,
    [Parameter(Mandatory)][string] $CandidateDirectory,
    [Parameter(Mandatory)][ValidatePattern('\A[0-9a-f]{64}\z')][string] $AppHostSha256,
    [Parameter(Mandatory)][ValidatePattern('\A[0-9a-f]{64}\z')][string] $UiSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$guest = Get-CimInstance Win32_ComputerSystem
$os = Get-CimInstance Win32_OperatingSystem
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    if ($guest.Name -ne 'ThirdLife-W11' -or $guest.Model -ne 'Virtual Machine' -or
        $guest.Manufacturer -ne 'Microsoft Corporation' -or $os.ProductType -ne 1 -or
        [int]$os.BuildNumber -lt 22000 -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64' -or
        $identity.Name -ne 'ThirdLife-W11\ThirdLifeAdmin' -or
        [Security.Principal.WindowsPrincipal]::new($identity).IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator) -or
        [Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0) {
        throw 'Run in the admitted guest interactive desktop with its unelevated token.'
    }
}
finally { $identity.Dispose() }

$root = (Resolve-Path -LiteralPath $CandidateDirectory).ProviderPath
$exe = Join-Path $root 'ThirdLife.UI.exe'
$ui = Join-Path $root 'ThirdLife.UI.dll'
if ((Get-FileHash -LiteralPath $exe).Hash -ine $AppHostSha256 -or
    (Get-FileHash -LiteralPath $ui).Hash -ine $UiSha256) {
    throw 'The application files differ from the reviewed candidate.'
}
if (Get-Process -Name 'ThirdLife.UI' -ErrorAction SilentlyContinue) {
    throw 'Close the previous application session before opening the candidate.'
}
if ($Preview) {
    $intakeDate = [DateTime]::ParseExact($VerifiedNoDonorStorageOn, 'yyyy-MM-dd',
        [Globalization.CultureInfo]::InvariantCulture)
    if ($intakeDate -gt [DateTime]::Today) { throw 'The external intake date is in the future.' }
}

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

function Wait-Observation([scriptblock] $Condition, [string] $Label, [int] $Seconds = 10) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($process.HasExited) { throw 'The owned application exited before the observation.' }
        try {
            $value = & $Condition
            if ($value) { return $value }
        }
        catch [Windows.Automation.ElementNotAvailableException] { }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt $Seconds)
    throw "Timed out waiting for $Label."
}

function Find-Control([string] $Id) {
    $query = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $query)
}

function Read-Value([string] $Id) {
    $element = Find-Control $Id
    if ($element) { $element.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value }
}

function Set-Value([string] $Id, [string] $Text) {
    $element = Wait-Observation { Find-Control $Id } $Id
    $element.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($Text)
}

function Invoke-Button([string] $Id) {
    $element = Wait-Observation {
        $match = Find-Control $Id
        if ($match -and $match.Current.IsEnabled) { $match }
    } $Id
    $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Select-Option([string] $Id, [string] $Name) {
    $combo = Wait-Observation { Find-Control $Id } $Id
    $expand = $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    try {
        $query = [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::NameProperty, $Name)
        $option = Wait-Observation {
            foreach ($match in $combo.FindAll([Windows.Automation.TreeScope]::Descendants, $query)) {
                for ($control = $match; $control -and -not $control.Equals($combo);
                    $control = [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($control)) {
                    $pattern = $null
                    if ($control.TryGetCurrentPattern(
                        [Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
                        return $pattern
                    }
                }
            }
        } $Name
        $option.Select()
    }
    finally {
        if ($expand.Current.ExpandCollapseState -eq [Windows.Automation.ExpandCollapseState]::Expanded) {
            $expand.Collapse()
        }
    }
}

function Read-Plan() {
    $title = Wait-Observation {
        $element = Find-Control 'PlanResultTitle'
        if ($element -and $element.Current.Name) { $element.Current.Name }
    } 'package proposal' 150
    if ($title -ne 'Machine-wide package proposal') { throw 'The live assessment did not admit the reviewed package plan.' }
    $details = Wait-Observation { Find-Control 'PlanDetailsExpander' } 'plan details'
    $details.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $text = Wait-Observation { Read-Value 'PlanDetailsText' } 'exact package details'
    foreach ($line in @(
        'Package Notepad++.Notepad++',
        'Source notepad-plus-plus-releases',
        'Version 8.9.9.1',
        'Installer SHA-256 e3b0607a092ce2604fbdac027db2c94915995e8e568ebd226f8c5493c2f61a80',
        'Scope Machine',
        'Check exact installed package identity'
    )) {
        if (-not $text.Contains($line)) { throw 'The plan differs from the reviewed route.' }
    }
    $digest = [regex]::Match($text, 'Plan SHA-256 ([0-9a-f]{64})').Groups[1].Value
    if (-not $digest) { throw 'The displayed plan has no digest.' }
    $digest
}

$stopwatch = [Diagnostics.Stopwatch]::StartNew()
$process = Start-Process -FilePath $exe -WorkingDirectory $root -WindowStyle Normal -PassThru
$result = [ordered]@{
    scenario = $PSCmdlet.ParameterSetName
    candidate_apphost_sha256 = $AppHostSha256
    candidate_ui_sha256 = $UiSha256
    last_checkpoint = 'launch'
    result = 'failed'
}
try {
    $window = Wait-Observation {
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        }
    } 'application window' 30
    if ($window.Current.ProcessId -ne $process.Id) { throw 'Unexpected UI Automation window owner.' }
    Invoke-Button 'OpenAuditWorkspaceButton'
    $assess = Wait-Observation { Find-Control 'StartAssessmentButton' } 'intake gate'
    if ((Read-Value 'CurrentJobId') -or $assess.Current.IsEnabled -or (Find-Control 'PrepareButton').Current.IsEnabled) {
        throw 'The initial intake gate was not closed.'
    }
    $result.last_checkpoint = 'intake_gate_closed'
    if ($Preview) {
        Invoke-Button 'CreateJobButton'
        $Job = Wait-Observation { Read-Value 'CurrentJobId' } 'saved job'
        if ($Job -cnotmatch '\Ajob-[0-9a-f]{32}\z') { throw 'Unexpected saved job reference.' }
        $result.local_job = $Job
        Set-Value 'OperatorIdInput' $OperatorCode
        Select-Option 'SanitizationResult' 'Verified no donor storage'
        Set-Value 'SanitizationDate' ($intakeDate.ToString('d', [Globalization.CultureInfo]::CurrentCulture))
        $confirmation = Wait-Observation { Find-Control 'ExternalRecordConfirmed' } 'external confirmation'
        $toggle = $confirmation.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
        if ($toggle.Current.ToggleState -ne [Windows.Automation.ToggleState]::On) { $toggle.Toggle() }
        Invoke-Button 'RecordSanitizationButton'
        $null = Wait-Observation { (Find-Control 'StartAssessmentButton').Current.IsEnabled } 'allowing sanitization state'
        $intake = (Find-Control 'SavedSanitization').Current.Name
        if (-not $intake.Contains('no donor storage, verified. Human confirmed.') -or
            -not $intake.Contains("Operator $OperatorCode,")) {
            throw 'The saved external intake record does not match the supplied attestation.'
        }
        $result.last_checkpoint = 'attributed_intake_saved'
        Invoke-Button 'StartAssessmentButton'
    }
    elseif ($ResumePreview -or $Reopen) {
        Set-Value 'JobIdInput' $Job
        Invoke-Button 'OpenJobButton'
        $null = Wait-Observation { (Read-Value 'CurrentJobId') -ceq $Job } 'exact saved job'
        $result.local_job = $Job
        $result.last_checkpoint = 'job_reopened'
    }

    if ($Preview -or $ResumePreview) {
        $null = Wait-Observation { (Find-Control 'PrepareButton').Current.IsEnabled } 'eligible preparation' 180
        Invoke-Button 'PrepareButton'
        Select-Option 'PlanProfileSelector' 'Plain-text editor (machine-wide setup)'
        Invoke-Button 'BuildPlanReviewButton'
        $result.plan_sha256 = Read-Plan
        $null = Wait-Observation {
            (Find-Control 'PackageSourceMessage').Current.Name -like 'The signed x64 installer matches*'
        } 'signed artifact and initial absence'
        if ((Find-Control 'InstallPackageButton').Current.IsEnabled -or
            (Find-Control 'HandoverButton').Current.IsEnabled) {
            throw 'The unapproved plan exposed installation or handover.'
        }
        $result.last_checkpoint = 'exact_plan_displayed_without_approval'
    }
    elseif ($Reopen) {
        $null = Wait-Observation {
            (Find-Control 'AuditStatus').Current.Name -ceq 'Job opened. The committed records are shown below.'
        } 'finished saved-job reconciliation' 45
        $history = Wait-Observation { Read-Value 'SavedPackageHistory' } 'saved package journal' 35
        if ((Find-Control 'HandoverButton').Current.IsEnabled) { throw 'Unexpected handover availability.' }
        $verified = $history.Contains('Independent verification confirmed')
        if ($ExpectedRecovery -eq 'Verified' -and -not $verified) {
            throw 'The original transaction lacks independent verification.'
        }
        if ($ExpectedRecovery -eq 'RequiresReview') {
            $recovery = Find-Control 'PackageRecoveryMessage'
            if ($verified -or -not $recovery -or $recovery.Current.Name -notmatch
                '(registered machine-wide|was not found|conflicting installation was found|registration could not be checked).*Keep this job in review[.]') {
                throw 'The interrupted transaction did not re-observe registration and retain review.'
            }
        }
        Invoke-Button 'GenerateReportButton'
        Invoke-Button 'OpenReportButton'
        $report = Wait-Observation { Read-Value 'WorkshopReportPreview' } 'reopened workshop report'
        $section = $report.IndexOf('Machine-wide setup journal', [StringComparison]::Ordinal)
        if ($section -lt 0) { throw 'The report has no machine-wide journal.' }
        $journal = $report.Substring($section)
        if (-not $journal.Contains($ExpectedPlanSha256) -or
            -not $journal.Contains('Notepad++.Notepad++ 8.9.9.1') -or
            -not $journal.Contains('Requested scope machine-wide')) {
            throw 'The report does not identify the approved package plan.'
        }
        if ($verified -ne $journal.Contains('Independent verification confirmed')) {
            throw 'The saved journal and report disagree about independent verification.'
        }
        $result.plan_sha256 = $ExpectedPlanSha256
        $result.independently_verified_in_saved_history = $verified
        $result.expected_recovery = $ExpectedRecovery
        $result.last_checkpoint = 'report_agrees_with_reopened_journal'
    }
    $result.result = 'passed'
}
catch {
    $result.error_type = $_.Exception.GetType().Name
    $result.error_line = $_.InvocationInfo.ScriptLineNumber
}
finally {
    $closed = $process.HasExited
    if (-not $closed) {
        $null = $process.CloseMainWindow()
        $closed = $process.WaitForExit(10000)
    }
    $result.application_closed_normally = $closed
    if (-not $closed) {
        # This driver never dispatches an installer. Close only its owned WPF process.
        $process.Kill()
        $closed = $process.WaitForExit(10000)
    }
    $result.application_closed = $closed
    $result.duration_ms = $stopwatch.ElapsedMilliseconds
    $process.Dispose()
}
[pscustomobject]$result
