#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Candidate', 'OptionalFailure', 'EssentialFailure', 'NetworkInterruption', 'UiKill',
        'BrokerKill', 'LowFreeSpace', 'ChangedMetadata', 'WrongClock', 'BrokenSource', 'SuccessWithoutInstall',
        'LaunchCrash', 'MultiRestart', 'Offline', 'Accessibility', 'SoftwareSingleWorker', 'SlowDestination')]
    [string[]] $Scenario
)

# Run after a Release build. Each receipt contains only test identity, counts, hashes and timing.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if ($env:OS -ne 'Windows_NT') { throw 'Run the M5 matrix on the active Windows machine.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    if ([Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run the M5 fixture matrix from an unelevated terminal.'
    }
}
finally { $identity.Dispose() }

# Project / exact method name. Each scenario can be rerun without the others.
$checks = @{
    Candidate = @('UI/PlanReviewTests.JobSeekerCandidateConvergesAndReopensWithoutLiveReadiness')
    OptionalFailure = @('UI/PlanReviewTests.OptionalCandidateFailurePreservesEssentialActions')
    EssentialFailure = @('UI/PlanReviewTests.EssentialCandidateFailureBlocksPreparation')
    NetworkInterruption = @(
        'Packages/WindowsInstallerPackageRuntimeTests.InterruptedInstallerStreamRemovesPartialStagingAndReportsNetworkLoss'
        'UI/PackageSetupTests.NetworkLossKeepsResolvedPlanAndApprovalWithoutStoringBackendDetail'
    )
    UiKill = @(
        'UI/PackageSetupTests.KilledPackageApplicationReopensTheSameJobForReviewWithoutDispatch'
        'UI/PackageSetupTests.KilledPackageApplicationReopensTheAppliedAttemptAndVerifiesWithoutDispatch'
    )
    BrokerKill = @(
        'Actions/ExecutionCoordinatorTests.InterruptedBrokerPreservesAuthenticatedPrefixAndFlagsMissingResults'
        'UI/PackageSetupTests.TerminatedOwnedPackageBackendLeavesTheReopenedAttemptInReview'
    )
    LowFreeSpace = @(
        'UI/PackageSetupTests.PackagePreflightStopsSourceDownloadAndExposesTheBlocker'
        'UI/PackageSetupTests.PackagePreflightChangeAfterAcquisitionPreventsApplyAndReopensWithTheSavedDeferral'
    )
    ChangedMetadata = @(
        'Actions/PlanDiffTests.ExactPackageBindingChangesRequireReapproval'
        'Packages/PilotPackageServiceTests.MaterialChangesNeverReachAcquisitionOrDispatch'
        'UI/PlanReviewTests.ApprovalClickShowsAChangedPolicySnapshotAndRequiresASecondExplicitClick'
    )
    WrongClock = @(
        'Persistence/RebootCheckpointStoreTests.ClockRollbackAndExpiryDenyResumeButAnExpiredOfferCanBeAbandoned'
        'Persistence/WindowsUpdateWorkflowTests.ACorrelatedSentReceiptSurvivesClockRollbackAndVerifies'
    )
    BrokenSource = @('Packages/PilotPackageServiceTests.AcquisitionClassifiesSourceFailuresWithoutBackendText')
    SuccessWithoutInstall = @('UI/PackageSetupTests.BackendSuccessCannotVerifyMissingOrWrongPackage')
    LaunchCrash = @('UI/PackageSetupTests.MatchingRegistrationWithAnEarlyLaunchExitStaysUnverified')
    MultiRestart = @(
        'Persistence/WindowsUpdateWorkflowTests.ThreeExactPassesRequireFreshReviewAndStopWhileFurtherUpdatesApply'
        'Persistence/WindowsUpdateWorkflowTests.RestartResultRequiresChangedBootObservationAndSuccessfulScan'
        'UI/PackageSetupTests.FullMaintenanceReviewResumesThroughFinalRestartWithoutReinstalling'
    )
    Offline = @('UI/PackageSetupTests.OfflineMaintenancePreservesTheApprovedJobAndResumesAfterReopen')
    Accessibility = @('UI/PackageSetupTests.M5KeyboardJourneyPreservesApprovalAndVerificationRecovery')
    SoftwareSingleWorker = @('UI/M5ResourceTests.SoftwareRenderingAndSingleWorkerRetainInventoryOnCancellation')
    SlowDestination = @('UI/M5ResourceTests.BelowNormalWithDelayedDestinationDrainsTheCommitAndReopens')
}

$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
$repository = Split-Path -Parent $PSScriptRoot
Push-Location $repository
$scratch = $null
try {
    $source = & git rev-parse --verify HEAD
    if ($LASTEXITCODE -ne 0 -or $source -cnotmatch '^[0-9a-f]{40}$') { throw 'Cannot identify the source revision.' }
    $changes = @(& git status --porcelain=v1)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the worktree.' }
    $scratch = [IO.Directory]::CreateTempSubdirectory('ThirdLife-M5-matrix-')
    $started = [DateTime]::UtcNow
    $results = @(
        foreach ($name in ($Scenario | Select-Object -Unique)) {
            foreach ($check in $checks[$name]) {
                $component, $method = $check -split '/', 2
                $project = "ThirdLife.$component.Tests"
                $filter = "FullyQualifiedName=$project.$method"
                $projectDirectory = Join-Path 'tests' $project
                $assembly = @(Get-ChildItem -LiteralPath (Join-Path $projectDirectory 'bin/Release') -Filter "$project.dll" -File -Recurse)
                if ($assembly.Count -ne 1) { throw "Expected one Release test assembly for $project." }
                if ($changes.Count -eq 0 -and -not $assembly[0].VersionInfo.ProductVersion.EndsWith("+$source", [StringComparison]::Ordinal)) {
                    throw "The Release test assembly for $project was built from a different revision."
                }
                $assemblyHash = (Get-FileHash -LiteralPath $assembly[0].FullName).Hash.ToLowerInvariant()
                $resultName = "$name-$method.trx"
                $resultPath = Join-Path $scratch.FullName $resultName
                $timer = [Diagnostics.Stopwatch]::StartNew()
                & $dotnet test "$projectDirectory/$project.csproj" -c Release --no-build --no-restore --nologo `
                    --filter $filter --results-directory $scratch.FullName --logger "trx;LogFileName=$resultName" `
                    -- 'RunConfiguration.TreatNoTestsAsError=true' 'RunConfiguration.TestSessionTimeout=120000' 2>&1 | Out-Null
                $exitCode = $LASTEXITCODE
                $timer.Stop()
                $total = $passed = 0
                $measurements = @()
                if (Test-Path -LiteralPath $resultPath -PathType Leaf) {
                    [xml] $testRun = Get-Content -LiteralPath $resultPath -Raw
                    $counters = $testRun.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
                    if ($null -ne $counters) {
                        $total = [int] $counters.GetAttribute('total')
                        $passed = [int] $counters.GetAttribute('passed')
                    }
                    if ($name -in 'SoftwareSingleWorker', 'SlowDestination') {
                        $measurements = @(
                            foreach ($output in $testRun.SelectNodes("//*[local-name()='StdOut']")) {
                                foreach ($line in ($output.InnerText -split '\r?\n')) {
                                    if ($line.Trim() -match '^TL0510_RESOURCE\s+(\{.+\})$') {
                                        $Matches[1] | ConvertFrom-Json -AsHashtable
                                    }
                                }
                            }
                        )
                    }
                    [IO.File]::Delete($resultPath)
                }
                $hasMeasurements = $name -notin 'SoftwareSingleWorker', 'SlowDestination' -or $measurements.Count -eq 1
                $receipt = [ordered]@{
                    scenario = $name
                    project = $project
                    filter = $filter
                    assembly_sha256 = $assemblyHash
                    result = if ($exitCode -eq 0 -and $total -gt 0 -and $passed -eq $total -and $hasMeasurements) { 'passed' } else { 'failed' }
                    total = $total
                    passed = $passed
                    exit_code = $exitCode
                    elapsed_ms = $timer.ElapsedMilliseconds
                }
                if ($measurements.Count -gt 0) { $receipt.measurements = $measurements }
                $receipt
            }
        }
    )
}
finally {
    if ($null -ne $scratch -and $scratch.Exists) { $scratch.Delete($true) }
    Pop-Location
}

[ordered]@{
    task = 'TL-0510'
    source_head = $source
    source_clean = $changes.Count -eq 0
    started_utc = $started.ToString('o')
    scope = 'Controlled test inputs. Native guest observations retain separate receipts.'
    cleanup = 'owned test-results directory removed'
    checks = $results
} | ConvertTo-Json -Depth 8
if (@($results | Where-Object result -ne 'passed').Count -ne 0) { exit 1 }
