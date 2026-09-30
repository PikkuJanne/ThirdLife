[CmdletBinding(DefaultParameterSetName = "Run")]
param(
    [Parameter(Mandatory = $false, ParameterSetName = "Run")]
    [switch] $PreflightOnly,

    [Parameter(Mandatory = $true, ParameterSetName = "Validate")]
    [ValidateNotNullOrEmpty()]
    [string] $ValidateResultOnly
)

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
$ResultSchemaVersion = 2
$ResultLimitBytes = 16384
$SandboxTimeoutMinutes = 30
$RemoteVerificationTimeoutSeconds = 30
$RemoteVerificationUrl = "https://github.com/PikkuJanne/ThirdLife.git"
$Limitation = "One Windows Sandbox session on the active physical Codex machine; no cross-hardware certification or host-compatibility claim. Guest policy-change evidence covers the SAC registry state and readable system-volume Code Integrity policy files; EFI-resident policy enumeration is not claimed."
$QuickCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\verify.ps1 -Tier Quick"
$FullCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\verify.ps1 -Tier Full"
$GuestResultName = "tl0010-result.pending.json"
$CompletionMarkerName = "complete.marker"
$FinalResultName = "tl0010-result.json"

function New-OpaqueRunId {
    $bytes = New-Object byte[] 16
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }
    return -join ($bytes | ForEach-Object { $_.ToString("x2") })
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

function Get-BoundedRemoteBranchHash {
    param(
        [Parameter(Mandatory = $true)]
        [string] $GitExecutable,

        [Parameter(Mandatory = $true)]
        [string] $Repository,

        [Parameter(Mandatory = $true)]
        [string] $RemoteUrl,

        [Parameter(Mandatory = $true)]
        [string] $Branch,

        [Parameter(Mandatory = $true)]
        [int] $TimeoutSeconds
    )

    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $GitExecutable
    $startInfo.Arguments = "-C `"$Repository`" ls-remote --exit-code --refs `"$RemoteUrl`" `"refs/heads/$Branch`""
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0"
    $startInfo.EnvironmentVariables["GCM_INTERACTIVE"] = "Never"

    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Remote branch verification could not start."
        }
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try {
                $process.Kill()
                $process.WaitForExit()
            }
            catch {
                # The bounded verification already failed closed; process cleanup is best effort.
            }
            throw "Remote branch verification exceeded its bounded timeout."
        }
        $standardOutput = $process.StandardOutput.ReadToEnd().Trim()
        [void] $process.StandardError.ReadToEnd()
        if ($process.ExitCode -ne 0) {
            throw "Remote branch verification failed without changing repository state."
        }
        $lines = @($standardOutput -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($lines.Count -ne 1 -or $lines[0] -notmatch "^([0-9a-f]{40})\s+refs/heads/$([regex]::Escape($Branch))$") {
            throw "Remote branch verification returned an unexpected bounded response."
        }
        return $Matches[1]
    }
    finally {
        $process.Dispose()
    }
}

function Assert-ExactValue {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [AllowNull()]
        $Actual,

        [AllowNull()]
        $Expected
    )

    if ($Actual -ne $Expected) {
        throw "Result field '$Name' has an unexpected value."
    }
}

function Assert-Matches {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [AllowNull()]
        $Actual,

        [Parameter(Mandatory = $true)]
        [string] $Pattern
    )

    if ($Actual -isnot [string] -or $Actual -notmatch $Pattern) {
        throw "Result field '$Name' has an invalid format."
    }
}

function Assert-Boolean {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [AllowNull()]
        $Actual
    )

    if ($Actual -isnot [bool]) {
        throw "Result field '$Name' must be Boolean."
    }
}

function Assert-EnumValue {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [AllowNull()]
        $Actual,

        [Parameter(Mandatory = $true)]
        [string[]] $Allowed
    )

    if ($Actual -isnot [string] -or $Actual -notin $Allowed) {
        throw "Result field '$Name' is outside its allowed values."
    }
}

function Assert-Duration {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [AllowNull()]
        $Actual
    )

    $isNumber = (
        $Actual -is [byte] -or $Actual -is [sbyte] -or
        $Actual -is [int16] -or $Actual -is [uint16] -or
        $Actual -is [int32] -or $Actual -is [uint32] -or
        $Actual -is [int64] -or $Actual -is [uint64] -or
        $Actual -is [single] -or $Actual -is [double] -or
        $Actual -is [decimal]
    )
    if (-not $isNumber) {
        throw "Result field '$Name' must be numeric."
    }
    $value = [double] $Actual
    if ([double]::IsNaN($value) -or [double]::IsInfinity($value) -or $value -lt 0 -or $value -gt 3600) {
        throw "Result field '$Name' is outside the bounded duration range."
    }
}

function Assert-BoundedInteger {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [AllowNull()]
        $Actual,

        [Parameter(Mandatory = $true)]
        [long] $Minimum,

        [Parameter(Mandatory = $true)]
        [long] $Maximum
    )

    $isInteger = (
        $Actual -is [byte] -or $Actual -is [sbyte] -or
        $Actual -is [int16] -or $Actual -is [uint16] -or
        $Actual -is [int32] -or $Actual -is [uint32] -or
        $Actual -is [int64] -or $Actual -is [uint64]
    )
    if (-not $isInteger) {
        throw "Result field '$Name' must be an integer."
    }
    $value = [long] $Actual
    if ($value -lt $Minimum -or $value -gt $Maximum) {
        throw "Result field '$Name' is outside its bounded integer range."
    }
}

function Assert-TierResult {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Tier,

        [Parameter(Mandatory = $true)]
        [string] $Result,

        [Parameter(Mandatory = $true)]
        [AllowNull()]
        $ExitCode,

        [Parameter(Mandatory = $true)]
        [bool] $SuccessMarker
    )

    if ($Result -eq "passed") {
        Assert-BoundedInteger "$Tier exit code" $ExitCode ([int32]::MinValue) ([int32]::MaxValue)
        if ($ExitCode -ne 0 -or -not $SuccessMarker) {
            throw "$Tier cannot pass without exit 0 and its governed success marker."
        }
        return
    }
    if ($Result -eq "not_run") {
        if ($null -ne $ExitCode -or $SuccessMarker) {
            throw "$Tier not_run must use a null exit and no success marker."
        }
        return
    }
    Assert-BoundedInteger "$Tier exit code" $ExitCode ([int32]::MinValue) ([int32]::MaxValue)
    if ($SuccessMarker) {
        throw "$Tier non-pass cannot use a success marker."
    }
}

function Assert-TL0010Result {
    param(
        [Parameter(Mandatory = $true)]
        $Result,

        [Parameter(Mandatory = $true)]
        [ValidateSet("Guest", "Final")]
        [string] $Phase
    )

    $expectedKeys = @(
        "affected_assemblies",
        "architecture",
        "candidate_commit",
        "candidate_unchanged_after",
        "code_integrity_policy_fingerprint_after",
        "code_integrity_policy_fingerprint_before",
        "code_integrity_observation_method_after",
        "code_integrity_observation_method_before",
        "code_integrity_query_after",
        "code_integrity_query_before",
        "completed_utc",
        "dotnet_sdk",
        "environment",
        "failure_code",
        "failure_phase",
        "full_command",
        "full_duration_seconds",
        "full_exit_code",
        "full_last_completed_stage",
        "full_result",
        "full_success_marker",
        "gate_record_sha256",
        "gate_record_unchanged_after",
        "git_version",
        "guest_policy_state_unchanged",
        "harness_commit",
        "hosted_constraint_profile",
        "host_result_validated",
        "host_staging_cleanup",
        "launcher_sha256",
        "limitation",
        "networking_enabled",
        "not_run_reason",
        "only_result_mapping_writable",
        "overall_result",
        "protected_client_enabled",
        "pyyaml_version",
        "python_version",
        "quick_command",
        "quick_duration_seconds",
        "quick_exit_code",
        "quick_result",
        "quick_success_marker",
        "reference_profile",
        "run_id",
        "runner_sha256",
        "sandbox_closed",
        "sandbox_config_sha256",
        "sandbox_executable_version",
        "sandbox_memory_mb",
        "schema_version",
        "security_mutation_attempted",
        "smart_app_control_after",
        "smart_app_control_before",
        "source_branch",
        "source_mapping_read_only",
        "started_utc",
        "task",
        "tool_mappings_read_only",
        "tracked_clean_after",
        "tracked_clean_before",
        "windows_build"
    ) | Sort-Object
    $actualKeys = @($Result.PSObject.Properties.Name) | Sort-Object
    $keyDifference = @(Compare-Object -ReferenceObject $expectedKeys -DifferenceObject $actualKeys)
    if ($keyDifference.Count -ne 0) {
        throw "Result keys do not match schema version $ResultSchemaVersion."
    }

    Assert-BoundedInteger "schema_version" $Result.schema_version $ResultSchemaVersion $ResultSchemaVersion
    Assert-ExactValue "schema_version" $Result.schema_version $ResultSchemaVersion
    Assert-ExactValue "task" $Result.task $TaskId
    Assert-ExactValue "candidate_commit" $Result.candidate_commit $CandidateCommit
    Assert-ExactValue "source_branch" $Result.source_branch $SourceBranch
    Assert-ExactValue "gate_record_sha256" $Result.gate_record_sha256 $GateRecordSha256
    Assert-ExactValue "environment" $Result.environment "Windows Sandbox"
    Assert-ExactValue "reference_profile" $Result.reference_profile $ReferenceProfile
    Assert-ExactValue "hosted_constraint_profile" $Result.hosted_constraint_profile $HostedConstraintProfile
    foreach ($toolExpectation in @(
        @("dotnet_sdk", $ExpectedDotNetSdk),
        @("python_version", $ExpectedPythonVersion),
        @("pyyaml_version", $ExpectedPyYamlVersion)
    )) {
        $toolName = $toolExpectation[0]
        $toolValue = $Result.$toolName
        if ($toolValue -ne "unavailable") {
            Assert-ExactValue $toolName $toolValue $toolExpectation[1]
        }
    }
    Assert-ExactValue "quick_command" $Result.quick_command $QuickCommand
    Assert-ExactValue "full_command" $Result.full_command $FullCommand
    Assert-ExactValue "limitation" $Result.limitation $Limitation
    Assert-BoundedInteger "sandbox_memory_mb" $Result.sandbox_memory_mb $SandboxMemoryMb $SandboxMemoryMb

    Assert-Matches "run_id" $Result.run_id "^[0-9a-f]{32}$"
    Assert-Matches "harness_commit" $Result.harness_commit "^[0-9a-f]{40}$"
    Assert-Matches "launcher_sha256" $Result.launcher_sha256 "^[0-9a-f]{64}$"
    Assert-Matches "runner_sha256" $Result.runner_sha256 "^[0-9a-f]{64}$"
    Assert-Matches "sandbox_config_sha256" $Result.sandbox_config_sha256 "^[0-9a-f]{64}$"
    Assert-Matches "code_integrity_policy_fingerprint_before" $Result.code_integrity_policy_fingerprint_before "^[0-9a-f]{64}$"
    Assert-Matches "code_integrity_policy_fingerprint_after" $Result.code_integrity_policy_fingerprint_after "^[0-9a-f]{64}$"
    foreach ($identityField in @("run_id", "harness_commit", "launcher_sha256", "runner_sha256", "sandbox_config_sha256")) {
        if ($Result.$identityField -match "^0+$") {
            throw "Result field '$identityField' cannot use an all-zero placeholder."
        }
    }
    Assert-Matches "sandbox_executable_version" $Result.sandbox_executable_version "^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$"
    Assert-Matches "windows_build" $Result.windows_build "^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$"
    if ($Result.git_version -ne "unavailable") {
        Assert-Matches "git_version" $Result.git_version "^[0-9]+\.[0-9]+\.[0-9]+(?:\.windows\.[0-9]+)?$"
    }
    Assert-EnumValue "architecture" $Result.architecture @("AMD64", "ARM64", "x86")
    Assert-EnumValue "smart_app_control_before" $Result.smart_app_control_before @("off", "enforced", "evaluation", "not_detected", "unavailable")
    Assert-EnumValue "smart_app_control_after" $Result.smart_app_control_after @("off", "enforced", "evaluation", "not_detected", "unavailable")
    Assert-EnumValue "code_integrity_query_before" $Result.code_integrity_query_before @("succeeded", "unavailable")
    Assert-EnumValue "code_integrity_query_after" $Result.code_integrity_query_after @("succeeded", "unavailable")
    Assert-EnumValue "code_integrity_observation_method_before" $Result.code_integrity_observation_method_before @("citool", "registry_and_system_policy_files", "unavailable")
    Assert-EnumValue "code_integrity_observation_method_after" $Result.code_integrity_observation_method_after @("citool", "registry_and_system_policy_files", "unavailable")
    foreach ($observationSuffix in @("before", "after")) {
        $query = $Result.("code_integrity_query_$observationSuffix")
        $method = $Result.("code_integrity_observation_method_$observationSuffix")
        $smartAppControl = $Result.("smart_app_control_$observationSuffix")
        $fingerprint = $Result.("code_integrity_policy_fingerprint_$observationSuffix")
        if ($query -eq "succeeded") {
            if ($method -eq "unavailable" -or $smartAppControl -eq "unavailable" -or $fingerprint -match "^0+$") {
                throw "A successful Code Integrity query requires a concrete normalized observation."
            }
            if ($method -eq "citool" -and $smartAppControl -eq "off") {
                throw "A CiTool observation cannot report the registry-only Smart App Control 'off' state."
            }
        }
        elseif ($method -ne "unavailable" -or $smartAppControl -ne "unavailable" -or $fingerprint -notmatch "^0+$") {
            throw "An unavailable Code Integrity query cannot claim a concrete policy state."
        }
    }
    Assert-EnumValue "quick_result" $Result.quick_result @("passed", "failed", "not_run")
    Assert-EnumValue "full_result" $Result.full_result @("passed", "failed", "blocked", "not_run")
    Assert-EnumValue "full_last_completed_stage" $Result.full_last_completed_stage @("not_started", "governance", "restore", "format", "build", "tests")
    Assert-EnumValue "overall_result" $Result.overall_result @("passed", "failed", "blocked")
    Assert-EnumValue "not_run_reason" $Result.not_run_reason @("none", "preflight_failed", "quick_failed")
    Assert-EnumValue "failure_phase" $Result.failure_phase @("none", "preflight", "quick", "full", "postflight")
    Assert-EnumValue "failure_code" $Result.failure_code @("none", "preflight_failed", "quick_failed", "quick_marker_missing", "full_failed", "full_marker_missing", "0x800711C7", "postflight_failed")

    foreach ($booleanField in @(
        "candidate_unchanged_after",
        "full_success_marker",
        "gate_record_unchanged_after",
        "guest_policy_state_unchanged",
        "host_result_validated",
        "networking_enabled",
        "only_result_mapping_writable",
        "protected_client_enabled",
        "quick_success_marker",
        "sandbox_closed",
        "security_mutation_attempted",
        "source_mapping_read_only",
        "tool_mappings_read_only",
        "tracked_clean_after",
        "tracked_clean_before"
    )) {
        Assert-Boolean $booleanField $Result.$booleanField
    }

    foreach ($requiredTrue in @(
        "networking_enabled",
        "only_result_mapping_writable",
        "protected_client_enabled",
        "source_mapping_read_only",
        "tool_mappings_read_only"
    )) {
        Assert-ExactValue $requiredTrue $Result.$requiredTrue $true
    }
    Assert-ExactValue "security_mutation_attempted" $Result.security_mutation_attempted $false

    foreach ($timestampField in @("started_utc", "completed_utc")) {
        Assert-Matches $timestampField $Result.$timestampField "^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$"
    }
    $started = [DateTimeOffset]::Parse($Result.started_utc, [Globalization.CultureInfo]::InvariantCulture)
    $completed = [DateTimeOffset]::Parse($Result.completed_utc, [Globalization.CultureInfo]::InvariantCulture)
    if ($completed -lt $started) {
        throw "Result completion time precedes its start time."
    }

    Assert-Duration "quick_duration_seconds" $Result.quick_duration_seconds
    Assert-Duration "full_duration_seconds" $Result.full_duration_seconds
    Assert-TierResult "Quick" $Result.quick_result $Result.quick_exit_code $Result.quick_success_marker
    Assert-TierResult "Full" $Result.full_result $Result.full_exit_code $Result.full_success_marker
    if ($Result.quick_result -ne "not_run") {
        Assert-ExactValue "dotnet_sdk" $Result.dotnet_sdk $ExpectedDotNetSdk
        Assert-ExactValue "python_version" $Result.python_version $ExpectedPythonVersion
        Assert-ExactValue "pyyaml_version" $Result.pyyaml_version $ExpectedPyYamlVersion
        if ($Result.git_version -eq "unavailable") {
            throw "A started Quick tier requires an observed Git version."
        }
    }
    if ($Result.quick_result -ne "passed" -and $Result.full_result -ne "not_run") {
        throw "Full may run only after Quick passes."
    }
    if ($Result.quick_result -eq "passed" -and $Result.full_result -eq "not_run") {
        throw "Full must run after a passing Quick result."
    }

    if ($Result.affected_assemblies -isnot [System.Array]) {
        throw "affected_assemblies must be an array."
    }
    $allowedAssemblies = @(
        "ThirdLife.Packages.dll",
        "ThirdLife.Persistence.dll",
        "ThirdLife.Reports.dll",
        "ThirdLife.Verification.dll"
    )
    $assemblies = @($Result.affected_assemblies)
    if ($assemblies.Count -ne @($assemblies | Select-Object -Unique).Count) {
        throw "affected_assemblies contains duplicates."
    }
    foreach ($assembly in $assemblies) {
        if ($assembly -notin $allowedAssemblies) {
            throw "affected_assemblies contains an unexpected value."
        }
    }

    $policyStateMatches = (
        $Result.code_integrity_query_before -eq "succeeded" -and
        $Result.code_integrity_query_after -eq "succeeded" -and
        $Result.smart_app_control_before -eq $Result.smart_app_control_after -and
        $Result.code_integrity_query_before -eq $Result.code_integrity_query_after -and
        $Result.code_integrity_observation_method_before -eq $Result.code_integrity_observation_method_after -and
        $Result.code_integrity_policy_fingerprint_before -eq $Result.code_integrity_policy_fingerprint_after
    )
    if ($Result.guest_policy_state_unchanged -ne $policyStateMatches) {
        throw "guest_policy_state_unchanged contradicts the normalized policy observations."
    }
    $postflightIntegrityInvalid = (
        -not $Result.tracked_clean_after -or
        -not $Result.candidate_unchanged_after -or
        -not $Result.gate_record_unchanged_after -or
        -not $Result.guest_policy_state_unchanged -or
        $Result.code_integrity_query_before -ne "succeeded" -or
        $Result.code_integrity_query_after -ne "succeeded"
    )
    if ($Result.quick_result -eq "not_run" -and $Result.quick_duration_seconds -ne 0) {
        throw "Quick not_run must have zero duration."
    }
    if ($Result.full_result -eq "not_run") {
        if ($Result.full_duration_seconds -ne 0 -or $Result.full_last_completed_stage -ne "not_started") {
            throw "Full not_run must have zero duration and no completed stage."
        }
        $expectedNotRunReason = if ($Result.quick_result -eq "failed") { "quick_failed" } else { "preflight_failed" }
        if ($Result.not_run_reason -ne $expectedNotRunReason) {
            throw "Full not_run has an incorrect bounded rationale."
        }
    }
    elseif ($Result.full_result -eq "passed" -and $Result.full_last_completed_stage -ne "tests") {
        throw "A passing Full result must complete the test stage."
    }
    elseif ($Result.full_result -ne "passed" -and $Result.full_last_completed_stage -eq "tests") {
        throw "A non-passing Full result cannot claim the test stage completed."
    }
    elseif ($Result.not_run_reason -ne "none") {
        throw "A Full tier that ran cannot retain a not-run rationale."
    }

    if ($Result.overall_result -eq "passed") {
        foreach ($requiredPass in @(
            ($Result.quick_result -eq "passed"),
            ($Result.full_result -eq "passed"),
            $Result.tracked_clean_before,
            $Result.tracked_clean_after,
            $Result.candidate_unchanged_after,
            $Result.gate_record_unchanged_after,
            $Result.guest_policy_state_unchanged,
            ($Result.code_integrity_policy_fingerprint_before -eq $Result.code_integrity_policy_fingerprint_after),
            ($Result.code_integrity_query_before -eq "succeeded"),
            ($Result.code_integrity_query_after -eq "succeeded"),
            ($Result.failure_phase -eq "none"),
            ($Result.failure_code -eq "none")
        )) {
            if (-not $requiredPass) {
                throw "Overall pass contradicts one or more required pass conditions."
            }
        }
        if ($assemblies.Count -ne 0) {
            throw "Overall pass cannot retain affected assembly names."
        }
    }
    elseif ($Result.overall_result -eq "blocked") {
        if (
            $Result.quick_result -ne "passed" -or
            $Result.full_result -ne "blocked" -or
            $Result.full_exit_code -le 0 -or
            $Result.failure_phase -ne "full" -or
            $Result.failure_code -ne "0x800711C7" -or
            $assemblies.Count -eq 0 -or
            -not $Result.tracked_clean_before -or
            -not $Result.tracked_clean_after -or
            -not $Result.candidate_unchanged_after -or
            -not $Result.gate_record_unchanged_after -or
            -not $Result.guest_policy_state_unchanged -or
            $Result.code_integrity_query_before -ne "succeeded" -or
            $Result.code_integrity_query_after -ne "succeeded"
        ) {
            throw "Overall blocked contradicts the bounded Application Control state."
        }
    }
    else {
        $validFailureState = switch ($Result.failure_phase) {
            "preflight" {
                $Result.failure_code -eq "preflight_failed" -and
                $Result.quick_result -eq "not_run" -and
                $Result.full_result -eq "not_run"
            }
            "quick" {
                (
                    ($Result.failure_code -eq "quick_failed" -and $Result.quick_exit_code -ne 0) -or
                    ($Result.failure_code -eq "quick_marker_missing" -and $Result.quick_exit_code -eq 0)
                ) -and
                $Result.quick_result -eq "failed" -and
                $Result.full_result -eq "not_run"
            }
            "full" {
                (
                    ($Result.failure_code -eq "full_failed" -and $Result.full_exit_code -ne 0) -or
                    ($Result.failure_code -eq "full_marker_missing" -and $Result.full_exit_code -eq 0)
                ) -and
                $Result.quick_result -eq "passed" -and
                $Result.full_result -eq "failed"
            }
            "postflight" {
                $Result.failure_code -eq "postflight_failed" -and
                $Result.quick_result -eq "passed" -and
                $Result.full_result -in @("passed", "blocked") -and
                $postflightIntegrityInvalid
            }
            default { $false }
        }
        $assemblyStateValid = (
            ($Result.full_result -eq "blocked" -and $assemblies.Count -gt 0) -or
            ($Result.full_result -ne "blocked" -and $assemblies.Count -eq 0)
        )
        if (-not $validFailureState -or -not $assemblyStateValid) {
            throw "Overall failed contradicts its phase, code, tier, or assembly state."
        }
    }

    if ($Phase -eq "Guest") {
        Assert-ExactValue "sandbox_closed" $Result.sandbox_closed $false
        Assert-ExactValue "host_staging_cleanup" $Result.host_staging_cleanup "pending"
        Assert-ExactValue "host_result_validated" $Result.host_result_validated $false
    }
    else {
        Assert-ExactValue "sandbox_closed" $Result.sandbox_closed $true
        Assert-ExactValue "host_staging_cleanup" $Result.host_staging_cleanup "passed"
        Assert-ExactValue "host_result_validated" $Result.host_result_validated $true
    }
}

function Read-BoundedResult {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [ValidateSet("Guest", "Final")]
        [string] $Phase
    )

    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($item.Length -le 0 -or $item.Length -gt $ResultLimitBytes) {
        throw "Result file size is outside the governed bound."
    }
    $body = Get-Content -Raw -LiteralPath $Path -Encoding UTF8
    $result = $body | ConvertFrom-Json
    Assert-TL0010Result -Result $result -Phase $Phase
    return $result
}

function Assert-GuestOutputDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $directory = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "The writable result directory must not be a reparse point."
    }
    $items = @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction Stop)
    $expectedNames = @($CompletionMarkerName, $GuestResultName) | Sort-Object
    $actualNames = @($items.Name) | Sort-Object
    if (@(Compare-Object $expectedNames $actualNames).Count -ne 0) {
        throw "The guest result directory contains unexpected output."
    }
    if (@($items | Where-Object { $_.PSIsContainer -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) }).Count -ne 0) {
        throw "The guest result directory may contain only ordinary bounded files."
    }
    $markerPath = Join-Path $Path $CompletionMarkerName
    $markerItem = Get-Item -LiteralPath $markerPath
    if ($markerItem.Length -gt 32 -or (Get-Content -Raw -LiteralPath $markerPath -Encoding UTF8) -ne "complete`n") {
        throw "The guest completion marker is malformed."
    }
    $totalBytes = ($items | Measure-Object -Property Length -Sum).Sum
    if ($totalBytes -gt ($ResultLimitBytes + 32)) {
        throw "The guest output exceeds its combined size bound."
    }
}

function Assert-FinalOutputDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $items = @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction Stop)
    $expectedNames = @($FinalResultName, "summary.md") | Sort-Object
    $actualNames = @($items.Name) | Sort-Object
    if (@(Compare-Object $expectedNames $actualNames).Count -ne 0) {
        throw "The finalized evidence directory contains unexpected output."
    }
    if (@($items | Where-Object { $_.PSIsContainer -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) }).Count -ne 0) {
        throw "Final evidence may contain only ordinary bounded files."
    }
    $totalBytes = ($items | Measure-Object -Property Length -Sum).Sum
    if ($totalBytes -gt 32768) {
        throw "Final evidence exceeds its combined size bound."
    }
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
    [System.IO.File]::WriteAllText($temporaryPath, $Content, $encoding)
    Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
}

function Assert-VerifiedStagingPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path).TrimEnd("\")
    $temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd("\")
    $expectedPrefix = "$temporaryRoot\"
    $leaf = Split-Path -Leaf $resolvedPath
    if (-not $resolvedPath.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup outside the operating-system temporary directory."
    }
    if ($leaf -notmatch "^ThirdLife-TL0010-[0-9a-f]{32}$") {
        throw "Refusing cleanup of an unexpected staging directory name."
    }
    return $resolvedPath
}

function Get-ActiveWindowsSandboxProcesses {
    return @(
        Get-Process -Name @(
            "WindowsSandbox",
            "WindowsSandboxClient",
            "WindowsSandboxRemoteSession",
            "WindowsSandboxServer"
        ) -ErrorAction SilentlyContinue
    )
}

function Remove-VerifiedStagingDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $verifiedPath = Assert-VerifiedStagingPath -Path $Path
    if (Test-Path -LiteralPath $verifiedPath) {
        $stagingEntries = @(
            Get-Item -LiteralPath $verifiedPath -Force
            Get-ChildItem -LiteralPath $verifiedPath -Recurse -Force -ErrorAction Stop
        )
        if (@($stagingEntries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -ne 0) {
            throw "Refusing recursive cleanup because verified staging contains a reparse point."
        }
        $stagingEntries |
            Where-Object { $_.Attributes -band [IO.FileAttributes]::ReadOnly } |
            ForEach-Object { $_.Attributes = $_.Attributes -band (-bnot [IO.FileAttributes]::ReadOnly) }
        Remove-Item -LiteralPath $verifiedPath -Recurse -Force
    }
    if (Test-Path -LiteralPath $verifiedPath) {
        throw "Verified staging cleanup did not complete."
    }
}

if ($PSCmdlet.ParameterSetName -eq "Validate") {
    $validated = Read-BoundedResult -Path $ValidateResultOnly -Phase "Final"
    Write-Host "PASS: validated bounded TL-0010 result for $($validated.candidate_commit)."
    return
}

if ($env:OS -ne "Windows_NT") {
    throw "TL-0010 Windows Sandbox verification requires the active Windows Codex machine."
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$guestRunnerSource = Join-Path $PSScriptRoot "run-tl0010-sandbox-guest.ps1"
$sandboxExecutable = Join-Path $env:WINDIR "System32\WindowsSandbox.exe"
$runId = New-OpaqueRunId
$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ThirdLife-TL0010-$runId"
$sourceStage = Join-Path $stagingRoot "source"
$harnessStage = Join-Path $stagingRoot "harness"
$requestStage = Join-Path $stagingRoot "request"
$configPath = Join-Path $stagingRoot "TL0010.wsb"
$durableResultDirectory = Join-Path $repositoryRoot "artifacts\audit\TL-0010\$runId"
$resultDirectory = if ($PreflightOnly) { Join-Path $stagingRoot "results" } else { $durableResultDirectory }
$sandboxProcess = $null
$stagingRemoved = $false

try {
    Write-Host "TL-0010 Windows Sandbox test"
    Write-Host "[1/5] Checking the host and exact candidate..."

    if (-not (Test-Path -LiteralPath $sandboxExecutable -PathType Leaf)) {
        throw "Windows Sandbox is not available at its supported system path."
    }
    if (-not (Test-Path -LiteralPath $guestRunnerSource -PathType Leaf)) {
        throw "The governed Sandbox guest runner is missing."
    }
    if (-not $PreflightOnly -and @(Get-ActiveWindowsSandboxProcesses).Count -ne 0) {
        throw "Close any existing Windows Sandbox session before starting this bounded run."
    }

    $gitCommand = Get-Command git -ErrorAction Stop
    $gitExecutable = $gitCommand.Source
    $gitRoot = Split-Path -Parent (Split-Path -Parent $gitExecutable)
    $mappedGitExecutable = Join-Path $gitRoot "cmd\git.exe"
    if (-not (Test-Path -LiteralPath $mappedGitExecutable -PathType Leaf)) {
        throw "The complete Git for Windows installation root could not be resolved."
    }
    $gitOutput = Get-NativeOutput -Label "Resolve Git version" -FilePath $gitExecutable -Arguments @("--version")
    $gitVersionMatch = [regex]::Match($gitOutput, "^git version ([0-9]+\.[0-9]+\.[0-9]+(?:\.windows\.[0-9]+)?)$")
    if (-not $gitVersionMatch.Success) {
        throw "Git returned an unsupported version format."
    }
    $gitVersion = $gitVersionMatch.Groups[1].Value

    $dotnetCommand = Get-Command dotnet -ErrorAction Stop
    $dotnetExecutable = $dotnetCommand.Source
    $dotnetRoot = Split-Path -Parent $dotnetExecutable
    $dotnetVersion = Get-NativeOutput -Label "Resolve .NET SDK" -FilePath $dotnetExecutable -Arguments @("--version")
    if ($dotnetVersion -ne $ExpectedDotNetSdk) {
        throw "TL-0010 requires .NET SDK $ExpectedDotNetSdk; found $dotnetVersion."
    }

    $pythonCommand = Get-Command python -ErrorAction Stop
    $pythonBase = Get-NativeOutput -Label "Resolve CPython base" -FilePath $pythonCommand.Source -Arguments @("-c", "import sys; print(sys.base_prefix)")
    $pythonRoot = [System.IO.Path]::GetFullPath($pythonBase)
    $pythonExecutable = Join-Path $pythonRoot "python.exe"
    if (-not (Test-Path -LiteralPath $pythonExecutable -PathType Leaf)) {
        throw "The complete CPython installation root could not be resolved."
    }
    $pythonVersion = Get-NativeOutput -Label "Resolve CPython version" -FilePath $pythonExecutable -Arguments @("-c", "import platform; print(platform.python_version())")
    if ($pythonVersion -ne $ExpectedPythonVersion) {
        throw "TL-0010 requires Python $ExpectedPythonVersion; found $pythonVersion."
    }

    $trackedChanges = & $gitExecutable -C $repositoryRoot status --porcelain=v1 --untracked-files=no
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to verify the repository tracked state."
    }
    if (@($trackedChanges).Count -ne 0) {
        throw "Commit or restore tracked repository changes before starting the governed Sandbox test."
    }
    foreach ($governedHarnessPath in @(
        "eng/run-tl0010-sandbox.ps1",
        "eng/run-tl0010-sandbox-guest.ps1"
    )) {
        & $gitExecutable -C $repositoryRoot ls-files --error-unmatch -- $governedHarnessPath 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "The Sandbox harness must be committed before it can produce evidence."
        }
    }
    $harnessCommit = Get-NativeOutput -Label "Resolve harness commit" -FilePath $gitExecutable -Arguments @("-C", $repositoryRoot, "rev-parse", "HEAD")
    if ($harnessCommit -notmatch "^[0-9a-f]{40}$") {
        throw "The harness source commit is invalid."
    }
    $currentBranch = Get-NativeOutput -Label "Resolve harness branch" -FilePath $gitExecutable -Arguments @(
        "-C", $repositoryRoot, "symbolic-ref", "--short", "HEAD"
    )
    if ($currentBranch -ne $SourceBranch) {
        throw "Run the governed Sandbox test only from branch $SourceBranch."
    }
    $upstream = Get-NativeOutput -Label "Resolve harness upstream" -FilePath $gitExecutable -Arguments @(
        "-C", $repositoryRoot, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"
    )
    if ($upstream -ne "origin/$SourceBranch") {
        throw "The governed Sandbox branch must track origin/$SourceBranch."
    }
    $divergence = Get-NativeOutput -Label "Verify published harness checkpoint" -FilePath $gitExecutable -Arguments @(
        "-C", $repositoryRoot, "rev-list", "--left-right", "--count", "HEAD...$upstream"
    )
    if ($divergence -notmatch "^0\s+0$") {
        throw "Publish and fetch the clean harness checkpoint before starting the governed Sandbox test."
    }
    $remoteBranchHash = Get-BoundedRemoteBranchHash `
        -GitExecutable $gitExecutable `
        -Repository $repositoryRoot `
        -RemoteUrl $RemoteVerificationUrl `
        -Branch $SourceBranch `
        -TimeoutSeconds $RemoteVerificationTimeoutSeconds
    if ($remoteBranchHash -ne $harnessCommit) {
        throw "The current harness checkpoint is not the exact commit published on the governed remote branch."
    }

    $sandboxVersionText = (Get-Item -LiteralPath $sandboxExecutable).VersionInfo.FileVersion
    $sandboxVersionMatch = [regex]::Match($sandboxVersionText, "[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+")
    if (-not $sandboxVersionMatch.Success) {
        throw "Windows Sandbox returned an unsupported version format."
    }
    $sandboxVersion = $sandboxVersionMatch.Value

    New-Item -ItemType Directory -Path $stagingRoot, $harnessStage, $requestStage, $resultDirectory -Force | Out-Null
    Invoke-CheckedNativeCommand -Label "Create standalone candidate clone" -FilePath $gitExecutable -Arguments @(
        "clone", "--local", "--no-hardlinks", "--no-checkout", "--", $repositoryRoot, $sourceStage
    )
    Invoke-CheckedNativeCommand -Label "Detach exact TL-0010 candidate" -FilePath $gitExecutable -Arguments @(
        "-C", $sourceStage, "-c", "advice.detachedHead=false", "checkout", "--detach", $CandidateCommit
    )
    $stagedCommit = Get-NativeOutput -Label "Verify staged candidate" -FilePath $gitExecutable -Arguments @("-C", $sourceStage, "rev-parse", "HEAD")
    if ($stagedCommit -ne $CandidateCommit) {
        throw "The staged source does not match the exact TL-0010 candidate."
    }
    $stagedStatus = & $gitExecutable -C $sourceStage status --porcelain=v1 --untracked-files=all
    if ($LASTEXITCODE -ne 0 -or @($stagedStatus).Count -ne 0) {
        throw "The staged candidate is not clean."
    }
    $stagedGatePath = Join-Path $sourceStage "artifacts\gates\M0-foundation.md"
    $stagedGateDigest = (Get-FileHash -LiteralPath $stagedGatePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($stagedGateDigest -ne $GateRecordSha256) {
        throw "The staged M0 gate record does not match its approved candidate digest."
    }

    Copy-Item -LiteralPath $guestRunnerSource -Destination (Join-Path $harnessStage "run-tl0010-sandbox-guest.ps1")
    $stagedRunnerPath = Join-Path $harnessStage "run-tl0010-sandbox-guest.ps1"
    $launcherDigest = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $runnerDigest = (Get-FileHash -LiteralPath $stagedRunnerPath -Algorithm SHA256).Hash.ToLowerInvariant()

    Write-Host "[2/5] Building the protected Sandbox configuration..."
    $escape = [System.Security.SecurityElement]
    $sourceXml = $escape::Escape($sourceStage)
    $harnessXml = $escape::Escape($harnessStage)
    $requestXml = $escape::Escape($requestStage)
    $dotnetXml = $escape::Escape($dotnetRoot)
    $pythonXml = $escape::Escape($pythonRoot)
    $gitXml = $escape::Escape($gitRoot)
    $resultXml = $escape::Escape($resultDirectory)
    $configuration = @"
<Configuration>
  <VGpu>Disable</VGpu>
  <Networking>Enable</Networking>
  <ProtectedClient>Enable</ProtectedClient>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <AudioInput>Disable</AudioInput>
  <VideoInput>Disable</VideoInput>
  <PrinterRedirection>Disable</PrinterRedirection>
  <MemoryInMB>$SandboxMemoryMb</MemoryInMB>
  <MappedFolders>
    <MappedFolder><HostFolder>$sourceXml</HostFolder><SandboxFolder>C:\TL0010\Input\Source</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$harnessXml</HostFolder><SandboxFolder>C:\TL0010\Input\Harness</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$requestXml</HostFolder><SandboxFolder>C:\TL0010\Input\Request</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$dotnetXml</HostFolder><SandboxFolder>C:\TL0010\Input\DotNet</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$pythonXml</HostFolder><SandboxFolder>C:\TL0010\Input\Python</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$gitXml</HostFolder><SandboxFolder>C:\TL0010\Input\Git</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$resultXml</HostFolder><SandboxFolder>C:\TL0010\Output</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File C:\TL0010\Input\Harness\run-tl0010-sandbox-guest.ps1</Command>
  </LogonCommand>
</Configuration>
"@
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($configPath, $configuration, $encoding)
    $configurationDigest = (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $request = [ordered] @{
        schema_version = $ResultSchemaVersion
        task = $TaskId
        run_id = $runId
        candidate_commit = $CandidateCommit
        gate_record_sha256 = $GateRecordSha256
        harness_commit = $harnessCommit
        launcher_sha256 = $launcherDigest
        runner_sha256 = $runnerDigest
        sandbox_config_sha256 = $configurationDigest
        sandbox_executable_version = $sandboxVersion
        git_version = $gitVersion
    }
    $requestJson = $request | ConvertTo-Json -Compress
    [System.IO.File]::WriteAllText((Join-Path $requestStage "run-request.json"), $requestJson, $encoding)

    if ($PreflightOnly) {
        Write-Host "[3/5] Preflight passed; Sandbox launch intentionally skipped."
        Remove-VerifiedStagingDirectory -Path $stagingRoot
        $stagingRemoved = $true
        Write-Host "PASS: TL-0010 Sandbox preflight and verified cleanup completed."
        return
    }

    Write-Host "[3/5] Starting Windows Sandbox. No action is required inside the guest."
    $sandboxProcess = Start-Process -FilePath $sandboxExecutable -ArgumentList @("`"$configPath`"") -PassThru
    $sandboxStarted = [DateTimeOffset]::UtcNow
    $markerPath = Join-Path $resultDirectory $CompletionMarkerName
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes($SandboxTimeoutMinutes)
    while (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        if ([DateTimeOffset]::UtcNow -ge $deadline) {
            throw "Sandbox did not produce a bounded result within $SandboxTimeoutMinutes minutes. Close the guest; staging was retained for safe diagnosis."
        }
        if (
            [DateTimeOffset]::UtcNow -ge $sandboxStarted.AddSeconds(15) -and
            $sandboxProcess.HasExited -and
            @(Get-ActiveWindowsSandboxProcesses).Count -eq 0
        ) {
            throw "Windows Sandbox closed before producing a bounded result."
        }
        Start-Sleep -Seconds 2
    }

    Write-Host "[4/5] Waiting for the disposable Sandbox to close..."
    $closeDeadline = [DateTimeOffset]::UtcNow.AddMinutes(5)
    while (-not $sandboxProcess.HasExited -or @(Get-ActiveWindowsSandboxProcesses).Count -ne 0) {
        if ([DateTimeOffset]::UtcNow -ge $closeDeadline) {
            throw "The test finished, but Windows Sandbox did not close. Close it manually; staging was retained."
        }
        Start-Sleep -Seconds 2
    }

    Write-Host "[5/5] Validating the bounded result and cleaning verified staging..."
    Assert-GuestOutputDirectory -Path $resultDirectory
    $pendingPath = Join-Path $resultDirectory $GuestResultName
    $result = Read-BoundedResult -Path $pendingPath -Phase "Guest"
    $liveBindings = [ordered] @{
        run_id = $runId
        harness_commit = $harnessCommit
        launcher_sha256 = $launcherDigest
        runner_sha256 = $runnerDigest
        sandbox_config_sha256 = $configurationDigest
        sandbox_executable_version = $sandboxVersion
    }
    if ($result.git_version -ne "unavailable") {
        $liveBindings.git_version = $gitVersion
    }
    foreach ($binding in $liveBindings.GetEnumerator()) {
        Assert-ExactValue $binding.Key $result.($binding.Key) $binding.Value
    }

    Remove-VerifiedStagingDirectory -Path $stagingRoot
    $stagingRemoved = $true
    $result.sandbox_closed = $true
    $result.host_staging_cleanup = "passed"
    $result.host_result_validated = $true
    Assert-TL0010Result -Result $result -Phase "Final"

    $finalPath = Join-Path $resultDirectory $FinalResultName
    $finalJson = $result | ConvertTo-Json -Depth 4 -Compress
    if ([Text.Encoding]::UTF8.GetByteCount($finalJson) -gt $ResultLimitBytes) {
        throw "Final result exceeds the governed size bound."
    }
    Write-Utf8Atomic -Path $finalPath -Content $finalJson
    $summaryPath = Join-Path $resultDirectory "summary.md"
    $summary = @"
# TL-0010 Windows Sandbox result

- Result: ``$($result.overall_result)``
- Candidate: ``$($result.candidate_commit)``
- Source branch: ``$($result.source_branch)``
- Hosted constraint profile: ``$($result.hosted_constraint_profile)``; memory: ``$($result.sandbox_memory_mb) MiB``
- Quick: ``$($result.quick_result)`` ($($result.quick_duration_seconds) seconds)
- Full: ``$($result.full_result)`` ($($result.full_duration_seconds) seconds); last completed stage: ``$($result.full_last_completed_stage)``
- Failure phase/code: ``$($result.failure_phase)`` / ``$($result.failure_code)``
- Full not-run rationale: ``$($result.not_run_reason)``
- Recognized affected assemblies: ``$(@($result.affected_assemblies) -join ', ')``
- Guest Smart App Control: ``$($result.smart_app_control_before)``; observation method: ``$($result.code_integrity_observation_method_before)``; unchanged: ``$($result.guest_policy_state_unchanged)``
- Harness declaration - security mutation attempted: ``$($result.security_mutation_attempted)``
- Source tracked-clean before/after: ``$($result.tracked_clean_before)`` / ``$($result.tracked_clean_after)``; candidate/gate unchanged: ``$($result.candidate_unchanged_after)`` / ``$($result.gate_record_unchanged_after)``
- Sandbox closed and staging cleanup: ``$($result.sandbox_closed)`` / ``$($result.host_staging_cleanup)``
- Limitation: $Limitation

Raw command and Code Integrity logs were not retained. A passing automated result moves TL-0010 only to human review; it does not complete the gate.
"@
    Write-Utf8Atomic -Path $summaryPath -Content $summary
    Remove-Item -LiteralPath $pendingPath, $markerPath -Force
    Assert-FinalOutputDirectory -Path $resultDirectory
    [void] (Read-BoundedResult -Path $finalPath -Phase "Final")

    if ($result.overall_result -eq "passed") {
        Write-Host "AUTOMATED VERIFICATION PASSED - TL-0010 may move to human review; it is not done."
        Write-Host "Bounded evidence: $finalPath"
        return
    }
    if ($result.overall_result -eq "blocked") {
        Write-Host "BLOCKED - Sandbox also rejected unsigned assemblies; no security setting was changed."
        Write-Host "Bounded evidence: $finalPath"
        exit 2
    }
    Write-Host "FAILED - no blind rerun or policy change was performed."
    Write-Host "Bounded evidence: $finalPath"
    exit 1
}
finally {
    if (-not $stagingRemoved -and (Test-Path -LiteralPath $stagingRoot)) {
        $sandboxStillRunning = (
            $null -ne $sandboxProcess -and
            (-not $sandboxProcess.HasExited -or @(Get-ActiveWindowsSandboxProcesses).Count -ne 0)
        )
        if (-not $sandboxStillRunning) {
            try {
                Remove-VerifiedStagingDirectory -Path $stagingRoot
                $stagingRemoved = $true
            }
            catch {
                Write-Warning "Verified staging cleanup did not complete; the exact temporary directory was retained."
            }
        }
    }
}
