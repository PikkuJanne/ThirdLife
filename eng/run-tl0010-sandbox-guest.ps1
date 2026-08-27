[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$TaskId = "TL-0010"
$CandidateCommit = "17975419badd4154b82895d9d92a4a904790c7c0"
$GateRecordSha256 = "b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153"
$SourceBranch = "codex/tl-0010-m0-foundation-gate"
$ReferenceProfile = "REF-CODEX-001 revision 2026-08-21.1"
$HostedConstraintProfile = "TL0010-WSB-2026-08-27.1"
$SandboxMemoryMb = 8192
$ExpectedDotNetSdk = "10.0.400"
$ExpectedPythonVersion = "3.14.7"
$ExpectedPyYamlVersion = "6.0.3"
$ResultSchemaVersion = 1
$ResultLimitBytes = 16384
$Limitation = "One Windows Sandbox session on the active physical Codex machine; no cross-hardware certification or host-compatibility claim."
$QuickCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\verify.ps1 -Tier Quick"
$FullCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\verify.ps1 -Tier Full"

$sourceInput = "C:\TL0010\Input\Source"
$harnessInput = "C:\TL0010\Input\Harness\run-tl0010-sandbox-guest.ps1"
$requestInput = "C:\TL0010\Input\Request\run-request.json"
$dotnetRoot = "C:\TL0010\Input\DotNet"
$pythonRoot = "C:\TL0010\Input\Python"
$gitRoot = "C:\TL0010\Input\Git"
$resultDirectory = "C:\TL0010\Output"
$workDirectory = "C:\TL0010\Work"
$stateDirectory = "C:\TL0010\State"
$logDirectory = Join-Path $stateDirectory "logs"
$pendingResultPath = Join-Path $resultDirectory "tl0010-result.pending.json"
$completionMarkerPath = Join-Path $resultDirectory "complete.marker"
$phase = "preflight"
$sandboxIdentityVerified = $env:USERNAME -eq "WDAGUtilityAccount"
$sandboxMappedInvocation = [StringComparer]::OrdinalIgnoreCase.Equals(
    [IO.Path]::GetFullPath($PSCommandPath),
    [IO.Path]::GetFullPath($harnessInput)
)

if (-not $sandboxIdentityVerified) {
    throw "The internal TL-0010 guest runner may execute only inside Windows Sandbox."
}

function Invoke-CheckedNativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Label,

        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}

function Get-NativeOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Label,

        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    $output = & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
    return ($output | Select-Object -Last 1).ToString().Trim()
}

function Get-CodeIntegrityObservation {
    $observation = [ordered] @{
        query = "unavailable"
        smart_app_control = "unavailable"
        policy_fingerprint = "0000000000000000000000000000000000000000000000000000000000000000"
    }
    try {
        $json = & "$env:WINDIR\System32\CiTool.exe" -lp -json 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $json) {
            return [pscustomobject] $observation
        }
        $document = ($json -join [Environment]::NewLine) | ConvertFrom-Json
        $policiesProperty = $document.PSObject.Properties["Policies"]
        if ($null -eq $policiesProperty) {
            throw "CiTool JSON omitted the Policies collection."
        }
        $policies = @($policiesProperty.Value)
        if ($policies.Count -eq 0) {
            throw "CiTool JSON returned no policies."
        }
        $normalizedPolicies = @()
        foreach ($policy in $policies) {
            $policyIdProperty = $policy.PSObject.Properties["PolicyID"]
            $friendlyNameProperty = $policy.PSObject.Properties["FriendlyName"]
            $isEnforcedProperty = $policy.PSObject.Properties["IsEnforced"]
            if (
                $null -eq $policyIdProperty -or
                $policyIdProperty.Value -isnot [string] -or
                [string]::IsNullOrWhiteSpace($policyIdProperty.Value) -or
                $null -eq $friendlyNameProperty -or
                $friendlyNameProperty.Value -isnot [string] -or
                [string]::IsNullOrWhiteSpace($friendlyNameProperty.Value) -or
                $null -eq $isEnforcedProperty -or
                $isEnforcedProperty.Value -isnot [bool]
            ) {
                throw "CiTool JSON returned an invalid policy object."
            }
            $normalizedFields = @()
            foreach ($propertyName in @(
                "PolicyID",
                "BasePolicyID",
                "FriendlyName",
                "Version",
                "IsSystemPolicy",
                "IsSignedPolicy",
                "IsOnDisk",
                "IsEnforced",
                "IsAuthorized"
            )) {
                $property = $policy.PSObject.Properties[$propertyName]
                $normalizedFields += if ($null -eq $property -or $null -eq $property.Value) {
                    ""
                }
                else {
                    $property.Value.ToString().ToLowerInvariant()
                }
            }
            $normalizedPolicies += ($normalizedFields -join "|")
        }
        $normalizedText = (@($normalizedPolicies | Sort-Object) -join "`n")
        $sha256 = [Security.Cryptography.SHA256]::Create()
        try {
            $fingerprintBytes = $sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($normalizedText))
        }
        finally {
            $sha256.Dispose()
        }
        $observation.policy_fingerprint = ([BitConverter]::ToString($fingerprintBytes) -replace "-", "").ToLowerInvariant()
        $observation.query = "succeeded"
        if (@($policies | Where-Object { $_.FriendlyName -eq "VerifiedAndReputableDesktop" -and $_.IsEnforced -eq $true }).Count -gt 0) {
            $observation.smart_app_control = "enforced"
        }
        elseif (@($policies | Where-Object { $_.FriendlyName -eq "VerifiedAndReputableDesktopEvaluation" -and $_.IsEnforced -eq $true }).Count -gt 0) {
            $observation.smart_app_control = "evaluation"
        }
        else {
            $observation.smart_app_control = "not_detected"
        }
    }
    catch {
        $observation.query = "unavailable"
        $observation.smart_app_control = "unavailable"
        $observation.policy_fingerprint = "0000000000000000000000000000000000000000000000000000000000000000"
    }
    return [pscustomobject] $observation
}

function Test-TrackedClean {
    param(
        [Parameter(Mandatory = $true)]
        [string] $GitExecutable,

        [Parameter(Mandatory = $true)]
        [string] $Repository
    )

    $status = & $GitExecutable -C $Repository status --porcelain=v1 --untracked-files=all
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect the staged candidate."
    }
    return @($status).Count -eq 0
}

function Invoke-GovernedTier {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("Quick", "Full")]
        [string] $Tier,

        [Parameter(Mandatory = $true)]
        [string] $Repository
    )

    $stdoutPath = Join-Path $logDirectory "$($Tier.ToLowerInvariant()).stdout.txt"
    $stderrPath = Join-Path $logDirectory "$($Tier.ToLowerInvariant()).stderr.txt"
    $arguments = @(
        "-NoLogo",
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        (Join-Path $Repository "eng\verify.ps1"),
        "-Tier",
        $Tier
    )
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process `
        -FilePath "powershell.exe" `
        -ArgumentList $arguments `
        -WorkingDirectory $Repository `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -NoNewWindow `
        -Wait `
        -PassThru
    $stopwatch.Stop()
    $successText = if ($Tier -eq "Quick") {
        "OK: ThirdLife quick documentation/schema/static verification passed."
    }
    else {
        "OK: ThirdLife full verification passed."
    }
    $successMarker = $false
    foreach ($path in @($stdoutPath, $stderrPath)) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            if (Select-String -LiteralPath $path -SimpleMatch $successText -Quiet) {
                $successMarker = $true
            }
        }
    }
    return [pscustomobject] @{
        exit_code = [int] $process.ExitCode
        duration_seconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
        success_marker = $successMarker
        stdout_path = $stdoutPath
        stderr_path = $stderrPath
    }
}

function Test-LogContains {
    param(
        [Parameter(Mandatory = $true)]
        $TierResult,

        [Parameter(Mandatory = $true)]
        [string] $Text
    )

    foreach ($path in @($TierResult.stdout_path, $TierResult.stderr_path)) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            if (Select-String -LiteralPath $path -SimpleMatch $Text -Quiet) {
                return $true
            }
        }
    }
    return $false
}

function Get-FullLastCompletedStage {
    param(
        [Parameter(Mandatory = $true)]
        $TierResult
    )

    $stage = "not_started"
    if (Test-LogContains -TierResult $TierResult -Text "==> Restore the locked dependency graph") {
        $stage = "governance"
    }
    if (Test-LogContains -TierResult $TierResult -Text "==> Verify formatting") {
        $stage = "restore"
    }
    if (Test-LogContains -TierResult $TierResult -Text "==> Build Release with compiler warnings treated as errors") {
        $stage = "format"
    }
    if (Test-LogContains -TierResult $TierResult -Text "==> Run the Release test suite") {
        $stage = "build"
    }
    if (Test-LogContains -TierResult $TierResult -Text "OK: ThirdLife full verification passed.") {
        $stage = "tests"
    }
    return $stage
}

function Write-Utf8Atomic {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Content
    )

    $temporaryPath = "$Path.tmp"
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($temporaryPath, $Content, $encoding)
    Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
}

$result = [ordered] @{
    schema_version = $ResultSchemaVersion
    task = $TaskId
    run_id = "00000000000000000000000000000000"
    candidate_commit = $CandidateCommit
    source_branch = $SourceBranch
    gate_record_sha256 = $GateRecordSha256
    harness_commit = "0000000000000000000000000000000000000000"
    launcher_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
    runner_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
    sandbox_config_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
    environment = "Windows Sandbox"
    reference_profile = $ReferenceProfile
    hosted_constraint_profile = $HostedConstraintProfile
    sandbox_memory_mb = $SandboxMemoryMb
    sandbox_executable_version = "0.0.0.0"
    windows_build = [Environment]::OSVersion.Version.ToString(4)
    architecture = $env:PROCESSOR_ARCHITECTURE
    git_version = "unavailable"
    dotnet_sdk = "unavailable"
    python_version = "unavailable"
    pyyaml_version = "unavailable"
    smart_app_control_before = "unavailable"
    smart_app_control_after = "unavailable"
    code_integrity_policy_fingerprint_before = "0000000000000000000000000000000000000000000000000000000000000000"
    code_integrity_policy_fingerprint_after = "0000000000000000000000000000000000000000000000000000000000000000"
    code_integrity_query_before = "unavailable"
    code_integrity_query_after = "unavailable"
    guest_policy_state_unchanged = $false
    security_mutation_attempted = $false
    networking_enabled = $true
    protected_client_enabled = $true
    source_mapping_read_only = $true
    tool_mappings_read_only = $true
    only_result_mapping_writable = $true
    started_utc = [DateTime]::UtcNow.ToString("o")
    completed_utc = [DateTime]::UtcNow.ToString("o")
    quick_command = $QuickCommand
    quick_result = "not_run"
    quick_exit_code = $null
    quick_duration_seconds = 0.0
    quick_success_marker = $false
    full_command = $FullCommand
    full_result = "not_run"
    full_exit_code = $null
    full_duration_seconds = 0.0
    full_success_marker = $false
    full_last_completed_stage = "not_started"
    tracked_clean_before = $false
    tracked_clean_after = $false
    candidate_unchanged_after = $false
    gate_record_unchanged_after = $false
    overall_result = "failed"
    not_run_reason = "preflight_failed"
    failure_phase = "preflight"
    failure_code = "preflight_failed"
    affected_assemblies = @()
    limitation = $Limitation
    sandbox_closed = $false
    host_staging_cleanup = "pending"
    host_result_validated = $false
}

$beforeObservation = [pscustomobject] @{ query = "unavailable"; smart_app_control = "unavailable" }
$afterObservation = [pscustomobject] @{ query = "unavailable"; smart_app_control = "unavailable" }

try {
    Write-Host "ThirdLife TL-0010 hosted verification"
    Write-Host "Preparing the exact candidate; no human input is required."

    foreach ($requiredPath in @($sourceInput, $harnessInput, $requestInput, $dotnetRoot, $pythonRoot, $gitRoot, $resultDirectory)) {
        if (-not (Test-Path -LiteralPath $requiredPath)) {
            throw "A required read-only input or result mapping is unavailable."
        }
    }
    $requestItem = Get-Item -LiteralPath $requestInput
    if ($requestItem.Length -le 0 -or $requestItem.Length -gt 4096) {
        throw "The run request is outside its governed size bound."
    }
    $request = Get-Content -Raw -LiteralPath $requestInput -Encoding UTF8 | ConvertFrom-Json
    $requestKeys = @($request.PSObject.Properties.Name) | Sort-Object
    $expectedRequestKeys = @(
        "candidate_commit",
        "gate_record_sha256",
        "git_version",
        "harness_commit",
        "launcher_sha256",
        "run_id",
        "runner_sha256",
        "sandbox_config_sha256",
        "sandbox_executable_version",
        "schema_version",
        "task"
    ) | Sort-Object
    if (@(Compare-Object $expectedRequestKeys $requestKeys).Count -ne 0) {
        throw "The run request does not match its exact schema."
    }
    if ($request.schema_version -ne $ResultSchemaVersion -or $request.task -ne $TaskId) {
        throw "The run request targets a different task or schema."
    }
    if ($request.candidate_commit -ne $CandidateCommit -or $request.gate_record_sha256 -ne $GateRecordSha256) {
        throw "The run request does not bind the exact TL-0010 candidate."
    }
    if ($request.run_id -notmatch "^[0-9a-f]{32}$" -or $request.harness_commit -notmatch "^[0-9a-f]{40}$") {
        throw "The run request contains an invalid identifier."
    }
    foreach ($digest in @($request.launcher_sha256, $request.runner_sha256, $request.sandbox_config_sha256)) {
        if ($digest -notmatch "^[0-9a-f]{64}$") {
            throw "The run request contains an invalid digest."
        }
    }

    $result.run_id = $request.run_id
    $result.harness_commit = $request.harness_commit
    $result.launcher_sha256 = $request.launcher_sha256
    $result.runner_sha256 = $request.runner_sha256
    $result.sandbox_config_sha256 = $request.sandbox_config_sha256
    $result.sandbox_executable_version = $request.sandbox_executable_version
    $actualRunnerDigest = (Get-FileHash -LiteralPath $harnessInput -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualRunnerDigest -ne $request.runner_sha256) {
        throw "The mapped guest runner digest does not match the host request."
    }
    if (-not $sandboxMappedInvocation) {
        throw "The internal TL-0010 guest runner was not invoked from its read-only Sandbox mapping."
    }

    $beforeObservation = Get-CodeIntegrityObservation
    $result.smart_app_control_before = $beforeObservation.smart_app_control
    $result.code_integrity_query_before = $beforeObservation.query
    $result.code_integrity_policy_fingerprint_before = $beforeObservation.policy_fingerprint
    if ($beforeObservation.query -ne "succeeded") {
        throw "The guest Code Integrity state could not be observed safely."
    }

    New-Item -ItemType Directory -Path $workDirectory, $stateDirectory, $logDirectory -Force | Out-Null
    $copyProcess = Start-Process `
        -FilePath "$env:WINDIR\System32\robocopy.exe" `
        -ArgumentList @($sourceInput, $workDirectory, "/MIR", "/COPY:DAT", "/DCOPY:DAT", "/R:2", "/W:1", "/NFL", "/NDL", "/NJH", "/NJS", "/NP") `
        -NoNewWindow `
        -Wait `
        -PassThru
    if ($copyProcess.ExitCode -ge 8) {
        throw "The read-only candidate could not be copied into guest-local work storage."
    }

    $gitExecutable = Join-Path $gitRoot "cmd\git.exe"
    $dotnetExecutable = Join-Path $dotnetRoot "dotnet.exe"
    $pythonExecutable = Join-Path $pythonRoot "python.exe"
    $env:PATH = "$dotnetRoot;$pythonRoot;$gitRoot\cmd;$gitRoot\mingw64\bin;$env:PATH"
    $env:DOTNET_ROOT = $dotnetRoot
    $env:DOTNET_CLI_HOME = Join-Path $stateDirectory "dotnet-home"
    $env:NUGET_PACKAGES = Join-Path $stateDirectory "nuget-packages"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
    $env:DOTNET_NOLOGO = "1"
    $env:NUGET_XMLDOC_MODE = "skip"
    $env:PIP_DISABLE_PIP_VERSION_CHECK = "1"
    $env:PIP_NO_INPUT = "1"

    $actualCommit = Get-NativeOutput -Label "Verify candidate commit" -FilePath $gitExecutable -Arguments @("-C", $workDirectory, "rev-parse", "HEAD")
    if ($actualCommit -ne $CandidateCommit) {
        throw "The guest-local source does not match the exact candidate."
    }
    $result.tracked_clean_before = Test-TrackedClean -GitExecutable $gitExecutable -Repository $workDirectory
    if (-not $result.tracked_clean_before) {
        throw "The guest-local candidate is not clean before bootstrap."
    }
    $gateDigest = (Get-FileHash -LiteralPath (Join-Path $workDirectory "artifacts\gates\M0-foundation.md") -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($gateDigest -ne $GateRecordSha256) {
        throw "The guest-local gate record digest does not match the exact candidate."
    }

    $actualGitText = Get-NativeOutput -Label "Verify Git" -FilePath $gitExecutable -Arguments @("--version")
    if ($actualGitText -ne "git version $($request.git_version)") {
        throw "The mapped Git version differs from host preflight."
    }
    $result.git_version = $request.git_version
    $actualDotNet = Get-NativeOutput -Label "Verify .NET SDK" -FilePath $dotnetExecutable -Arguments @("--version")
    if ($actualDotNet -ne $ExpectedDotNetSdk) {
        throw "The mapped .NET SDK is not the exact governed version."
    }
    $result.dotnet_sdk = $actualDotNet
    $actualPython = Get-NativeOutput -Label "Verify CPython" -FilePath $pythonExecutable -Arguments @("-c", "import platform; print(platform.python_version())")
    if ($actualPython -ne $ExpectedPythonVersion) {
        throw "The mapped CPython is not the exact governed version."
    }
    $result.python_version = $actualPython

    $venvDirectory = Join-Path $workDirectory ".venv"
    Invoke-CheckedNativeCommand -Label "Create guest-local Python environment" -FilePath $pythonExecutable -Arguments @("-m", "venv", $venvDirectory)
    $venvPython = Join-Path $venvDirectory "Scripts\python.exe"
    Invoke-CheckedNativeCommand -Label "Install only hash-pinned Python tools" -FilePath $venvPython -Arguments @(
        "-m", "pip", "install", "--require-hashes", "-r", (Join-Path $workDirectory "tools\requirements.txt")
    )
    $actualPyYaml = Get-NativeOutput -Label "Verify PyYAML" -FilePath $venvPython -Arguments @("-c", "import yaml; print(yaml.__version__)")
    if ($actualPyYaml -ne $ExpectedPyYamlVersion) {
        throw "The guest-local PyYAML is not the exact governed version."
    }
    $result.pyyaml_version = $actualPyYaml

    $phase = "quick"
    Write-Host "Running governed Quick verification..."
    $quick = Invoke-GovernedTier -Tier "Quick" -Repository $workDirectory
    $result.quick_exit_code = $quick.exit_code
    $result.quick_duration_seconds = $quick.duration_seconds
    $result.quick_success_marker = $quick.success_marker
    if ($quick.exit_code -eq 0 -and $quick.success_marker) {
        $result.quick_result = "passed"
    }
    else {
        $result.quick_result = "failed"
        $result.overall_result = "failed"
        $result.failure_phase = "quick"
        $result.failure_code = if ($quick.exit_code -eq 0) { "quick_marker_missing" } else { "quick_failed" }
        $result.not_run_reason = "quick_failed"
        Write-Host "STOP - Quick failed; Full was not run."
    }

    if ($result.quick_result -eq "passed") {
        $result.not_run_reason = "none"
        $phase = "full"
        Write-Host "Running governed Full verification..."
        $full = Invoke-GovernedTier -Tier "Full" -Repository $workDirectory
        $result.full_exit_code = $full.exit_code
        $result.full_duration_seconds = $full.duration_seconds
        $result.full_success_marker = $full.success_marker
        $result.full_last_completed_stage = Get-FullLastCompletedStage -TierResult $full
        $affected = @()
        foreach ($assembly in @(
            "ThirdLife.Packages.dll",
            "ThirdLife.Persistence.dll",
            "ThirdLife.Reports.dll",
            "ThirdLife.Verification.dll"
        )) {
            if (Test-LogContains -TierResult $full -Text $assembly) {
                $affected += $assembly
            }
        }
        if ($full.exit_code -eq 0 -and $full.success_marker) {
            $result.full_result = "passed"
            $result.overall_result = "passed"
            $result.failure_phase = "none"
            $result.failure_code = "none"
        }
        elseif (
            (Test-LogContains -TierResult $full -Text "0x800711C7") -and
            (Test-LogContains -TierResult $full -Text "An Application Control policy has blocked this file") -and
            (Test-LogContains -TierResult $full -Text "System.IO.FileLoadException") -and
            $affected.Count -gt 0
        ) {
            $result.full_result = "blocked"
            $result.overall_result = "blocked"
            $result.failure_phase = "full"
            $result.failure_code = "0x800711C7"
            $result.affected_assemblies = @($affected)
        }
        else {
            $result.full_result = "failed"
            $result.overall_result = "failed"
            $result.failure_phase = "full"
            $result.failure_code = if ($full.exit_code -eq 0) { "full_marker_missing" } else { "full_failed" }
        }
    }
}
catch {
    $result.overall_result = "failed"
    if ($phase -eq "quick") {
        $result.quick_result = "failed"
        if ($null -eq $result.quick_exit_code) {
            $result.quick_exit_code = 1
        }
        $result.quick_success_marker = $false
        $result.failure_phase = "quick"
        $result.failure_code = "quick_failed"
        $result.not_run_reason = "quick_failed"
    }
    elseif ($phase -eq "full") {
        $result.full_result = "failed"
        if ($null -eq $result.full_exit_code) {
            $result.full_exit_code = 1
        }
        $result.full_success_marker = $false
        $result.failure_phase = "full"
        $result.failure_code = "full_failed"
        $result.not_run_reason = "none"
    }
    else {
        $result.failure_phase = "preflight"
        $result.failure_code = "preflight_failed"
    }
    Write-Host "FAILED - the current phase stopped safely; no policy setting was changed."
}
finally {
    try {
        $phase = "postflight"
        try {
            $afterObservation = Get-CodeIntegrityObservation
            $result.smart_app_control_after = $afterObservation.smart_app_control
            $result.code_integrity_query_after = $afterObservation.query
            $result.code_integrity_policy_fingerprint_after = $afterObservation.policy_fingerprint
            $result.guest_policy_state_unchanged = (
                $result.code_integrity_query_before -eq "succeeded" -and
                $result.code_integrity_query_after -eq "succeeded" -and
                $result.smart_app_control_before -eq $result.smart_app_control_after -and
                $result.code_integrity_query_before -eq $result.code_integrity_query_after -and
                $result.code_integrity_policy_fingerprint_before -eq $result.code_integrity_policy_fingerprint_after
            )

            $gitExecutable = Join-Path $gitRoot "cmd\git.exe"
            if (
                (Test-Path -LiteralPath $gitExecutable -PathType Leaf) -and
                (Test-Path -LiteralPath (Join-Path $workDirectory ".git"))
            ) {
                $result.tracked_clean_after = Test-TrackedClean -GitExecutable $gitExecutable -Repository $workDirectory
                $postflightCommit = Get-NativeOutput -Label "Re-verify candidate commit" -FilePath $gitExecutable -Arguments @(
                    "-C", $workDirectory, "rev-parse", "HEAD"
                )
                $result.candidate_unchanged_after = $postflightCommit -eq $CandidateCommit
                $postflightGateDigest = (Get-FileHash -LiteralPath (Join-Path $workDirectory "artifacts\gates\M0-foundation.md") -Algorithm SHA256).Hash.ToLowerInvariant()
                $result.gate_record_unchanged_after = $postflightGateDigest -eq $GateRecordSha256
            }
            $postflightInvalid = (
                -not $result.tracked_clean_after -or
                -not $result.candidate_unchanged_after -or
                -not $result.gate_record_unchanged_after -or
                -not $result.guest_policy_state_unchanged -or
                $result.code_integrity_query_before -ne "succeeded" -or
                $result.code_integrity_query_after -ne "succeeded"
            )
            if ($result.overall_result -in @("passed", "blocked") -and $postflightInvalid) {
                $result.overall_result = "failed"
                $result.failure_phase = "postflight"
                $result.failure_code = "postflight_failed"
            }
        }
        catch {
            $result.tracked_clean_after = $false
            if ($result.overall_result -in @("passed", "blocked")) {
                $result.overall_result = "failed"
                $result.failure_phase = "postflight"
                $result.failure_code = "postflight_failed"
            }
        }

        if (Test-Path -LiteralPath $logDirectory -PathType Container) {
            Get-ChildItem -LiteralPath $logDirectory -File -Force -ErrorAction SilentlyContinue |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }
        $result.completed_utc = [DateTime]::UtcNow.ToString("o")
        $json = $result | ConvertTo-Json -Depth 4 -Compress
        if ([Text.Encoding]::UTF8.GetByteCount($json) -gt $ResultLimitBytes) {
            throw "The bounded result unexpectedly exceeds its size limit."
        }
        Write-Utf8Atomic -Path $pendingResultPath -Content $json
        Write-Utf8Atomic -Path $completionMarkerPath -Content "complete`n"

        if ($result.overall_result -eq "passed") {
            Write-Host "AUTOMATED VERIFICATION PASSED - human M0 review is still required."
        }
        elseif ($result.overall_result -eq "blocked") {
            Write-Host "BLOCKED - Application Control also rejected the unsigned assemblies in Sandbox."
        }
        else {
            Write-Host "FAILED - use the bounded host summary; raw guest logs will be discarded."
        }
    }
    finally {
        Write-Host "The disposable Sandbox will close in 15 seconds."
        if ($sandboxMappedInvocation -and $sandboxIdentityVerified) {
            Start-Process `
                -FilePath "$env:WINDIR\System32\shutdown.exe" `
                -ArgumentList @("/s", "/t", "15", "/d", "p:0:0") `
                -WindowStyle Hidden | Out-Null
        }
    }
}
