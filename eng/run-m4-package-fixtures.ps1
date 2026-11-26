#Requires -Version 7.2
[CmdletBinding()]
param([string] $Scenario)

# Build Release with eng/verify.ps1 before using this independently selectable fixture runner.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

if ($env:OS -ne 'Windows_NT') { throw 'Run these fixtures on the active Windows machine.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    if ([Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run the package fixtures from an unelevated terminal.'
    }
}
finally { $identity.Dispose() }

$cases = [ordered]@{
    NetworkStream = @('Packages', 'WindowsInstallerPackageRuntimeTests.InterruptedInstallerStreamRemovesPartialStagingAndReportsNetworkLoss', 'FI-002', 'targeted')
    NetworkRecovery = @('UI', 'PackageSetupTests.NetworkLossKeepsResolvedPlanAndApprovalWithoutStoringBackendDetail', 'FI-002', 'targeted')
    AuthorizationDeclined = @('UI', 'PackageSetupTests.PackageVerificationWaitsForCompletedApply', 'FI-006', 'targeted')
    MissingRegistration = @('UI', 'PackageSetupTests.BackendSuccessCannotVerifyMissingOrWrongPackage', 'FI-008', 'targeted')
    WorkshopReport = @('UI', 'PackageSetupTests.PackagePlanUsesDurableApprovalAndReportsTheReopenedJournal', 'package_journey', 'targeted')
    ApplicationLossDuringApply = @('UI', 'PackageSetupTests.KilledPackageApplicationReopensTheSameJobForReviewWithoutDispatch', 'FI-004', 'extended')
    ApplicationLossAfterApply = @('UI', 'PackageSetupTests.KilledPackageApplicationReopensTheAppliedAttemptAndVerifiesWithoutDispatch', 'FI-004', 'extended')
    OwnedBackendLoss = @('UI', 'PackageSetupTests.TerminatedOwnedPackageBackendLeavesTheReopenedAttemptInReview', 'FI-005', 'extended')
    ModeledBoot = @('UI', 'PackageSetupTests.RestartRequiredWaitsForWindowsBootEvidenceThenVerifiesTheSavedAttempt', 'restart_model', 'targeted')
    CheckpointCommit = @('Persistence', 'RebootCheckpointStoreTests.PackageRebootVerificationAndTokenConsumeCommitTogether', 'FI-012', 'extended')
}
if ($Scenario -and -not $cases.Contains($Scenario)) { throw 'Unknown package fixture scenario.' }

$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    $source = & git rev-parse --verify HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not identify the repository source.' }
    $started = [DateTime]::UtcNow
    $results = @(
        foreach ($name in $cases.Keys) {
            if ($Scenario -and $name -ne $Scenario) { continue }
            $component, $method, $failureId, $tier = $cases[$name]
            $project = "ThirdLife.$component.Tests"
            $filter = "FullyQualifiedName=$project.$method"
            $timer = [Diagnostics.Stopwatch]::StartNew()
            & $dotnet test "tests/$project/$project.csproj" -c Release --no-build --no-restore --nologo `
                --filter $filter -- 'RunConfiguration.TreatNoTestsAsError=true' 'RunConfiguration.TestSessionTimeout=90000' 2>&1 | Out-Null
            $exitCode = $LASTEXITCODE
            [ordered]@{
                scenario = $name
                failure_id = $failureId
                tier = $tier
                filter = $filter
                result = if ($exitCode -eq 0) { 'passed' } else { 'failed' }
                exit_code = $exitCode
                elapsed_ms = $timer.ElapsedMilliseconds
            }
        }
    )
    [ordered]@{
        task = 'TL-0410'
        source_head = $source
        started_utc = $started.ToString('o')
        scope = 'Deterministic fixtures using existing Release binaries. Consent decline and restart are modeled.'
        checks = $results
    } | ConvertTo-Json -Depth 4
    if ($results.exit_code -ne 0) { exit 1 }
}
finally { Pop-Location }
