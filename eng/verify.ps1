[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("Quick", "Targeted", "Full")]
    [string] $Tier = "Quick",

    [Parameter(Mandatory = $false)]
    [string[]] $TestProject = @(),

    [Parameter(Mandatory = $false)]
    [string] $Filter = "",

    [Parameter(Mandatory = $false)]
    [switch] $Restore,

    [Parameter(Mandatory = $false)]
    [switch] $SupplementaryCi
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Label,

        [Parameter(Mandatory = $true)]
        [string] $Command,

        [Parameter(Mandatory = $true)]
        [string[]] $CommandArguments
    )

    Write-Host "==> $Label"
    & $Command @CommandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}

function Invoke-TestProject {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Dotnet,

        [Parameter(Mandatory = $true)]
        [string] $Project,

        [Parameter(Mandatory = $false)]
        [string] $TestFilter = ""
    )

    $arguments = @(
        "test",
        $Project,
        "--configuration",
        "Release",
        "--no-build",
        "--no-restore",
        "--nologo"
    )

    if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
        $arguments += @("--filter", $TestFilter)
    }
    $arguments += @("--", "RunConfiguration.TreatNoTestsAsError=true")

    Invoke-CheckedCommand `
        -Label "Test $Project" `
        -Command $Dotnet `
        -CommandArguments $arguments
}

if ($env:OS -ne "Windows_NT") {
    throw "ThirdLife verification runs on the active Windows Codex machine. Use eng/verify.sh only as its Windows Git Bash wrapper."
}

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$solutionPath = Join-Path $repositoryRoot "ThirdLife.sln"
$nugetConfigPath = Join-Path $repositoryRoot "NuGet.Config"
$globalJsonPath = Join-Path $repositoryRoot "global.json"
$manifestPath = Join-Path $repositoryRoot "eng\test-manifest.json"
$venvPythonPath = Join-Path $repositoryRoot ".venv\Scripts\python.exe"

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:NUGET_XMLDOC_MODE = "skip"
$env:PIP_DISABLE_PIP_VERSION_CHECK = "1"
$env:PIP_NO_INPUT = "1"

Push-Location $repositoryRoot
try {
    if ($Tier -eq "Targeted" -and $TestProject.Count -eq 0) {
        throw "Targeted requires at least one -TestProject. Use an optional -Filter to narrow the subsystem."
    }
    if ($Tier -ne "Targeted" -and ($TestProject.Count -ne 0 -or -not [string]::IsNullOrWhiteSpace($Filter))) {
        throw "-TestProject and -Filter are supported only with -Tier Targeted."
    }
    if ($SupplementaryCi -and $Tier -ne "Quick") {
        throw "Supplementary CI runs only the deterministic Quick selection."
    }
    if ($SupplementaryCi) {
        Write-Warning "This hosted run is supplementary only. It is not authoritative ThirdLife runtime, hardware, accessibility, milestone, pilot, or release evidence."
    }

    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Missing eng/test-manifest.json."
    }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json

    $requestedProjects = @(
        if ($Tier -in @("Quick", "Targeted")) {
            if (-not $manifest.tiers.quick.test_projects) {
                throw "The Quick selection requires at least one test project."
            }
            $manifest.tiers.quick.test_projects
        }
        if ($Tier -eq "Targeted") {
            $selectedFilter = if ([string]::IsNullOrWhiteSpace($Filter)) {
                [string]$manifest.tiers.targeted.default_filter
            }
            else { $Filter }
            foreach ($project in $TestProject) {
                [pscustomobject]@{ path = $project; filter = $selectedFilter }
            }
        }
    )

    # Paths resolve from the repository root. The selection lives only in this invocation.
    $filtersByProject = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.HashSet[string]]]::new(
        [System.StringComparer]::OrdinalIgnoreCase
    )
    $testSelection = @(
        foreach ($entry in $requestedProjects) {
            $project = [string]$entry.path
            if ([string]::IsNullOrWhiteSpace($project) -or
                [System.IO.Path]::GetExtension($project) -ne ".csproj" -or
                -not (Test-Path -LiteralPath $project -PathType Leaf)) {
                throw "Each selected project must identify an existing test .csproj. Pass multiple -TestProject values as a PowerShell array."
            }
            $resolvedProject = Resolve-Path -LiteralPath $project
            if ($resolvedProject.Provider.Name -ne "FileSystem") {
                throw "Each selected project must be a file system path."
            }
            $projectPath = $resolvedProject.ProviderPath
            if (-not $filtersByProject.ContainsKey($projectPath)) {
                $filtersByProject[$projectPath] = [System.Collections.Generic.HashSet[string]]::new(
                    [System.StringComparer]::Ordinal
                )
            }
            if ($filtersByProject[$projectPath].Add([string]$entry.filter)) {
                $entry
            }
        }
        if ($Tier -eq "Full") {
            [pscustomobject]@{ path = $solutionPath; filter = $manifest.tiers.full.filter }
        }
    )

    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw "The exact .NET SDK from global.json is required."
    }

    $expectedSdk = (Get-Content -Raw -LiteralPath $globalJsonPath | ConvertFrom-Json).sdk.version
    $actualSdkOutput = & $dotnetCommand.Source --version
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve the .NET SDK selected by global.json."
    }

    $actualSdk = ($actualSdkOutput | Select-Object -Last 1).Trim()
    if ($actualSdk -ne $expectedSdk) {
        throw "ThirdLife requires .NET SDK $expectedSdk; selected SDK was $actualSdk."
    }

    if (Test-Path -LiteralPath $venvPythonPath -PathType Leaf) {
        $pythonCommand = $venvPythonPath
    }
    else {
        $systemPython = Get-Command python -ErrorAction SilentlyContinue
        if ($null -eq $systemPython) {
            throw "Python 3.14.7 and tools/requirements.txt are required."
        }
        $pythonCommand = $systemPython.Source
    }

    Invoke-CheckedCommand `
        -Label "Validate pinned Python toolchain" `
        -Command $pythonCommand `
        -CommandArguments @(
            "-c",
            "import sys, yaml; expected=(3,14,7); actual=sys.version_info[:3]; actual == expected or sys.exit(f'Python {expected} required; found {actual}'); yaml.__version__ == '6.0.3' or sys.exit(f'PyYAML 6.0.3 required; found {yaml.__version__}')"
        )

    if ($Tier -eq "Full") {
        Invoke-CheckedCommand `
            -Label "Run complete governed validator regression tests" `
            -Command $pythonCommand `
            -CommandArguments @(
                "-m",
                "unittest",
                "discover",
                "-s",
                "tools/tests",
                "-p",
                "test_*.py"
            )
    }

    Invoke-CheckedCommand `
        -Label "Validate governed roadmap bundle" `
        -Command $pythonCommand `
        -CommandArguments @("tools/validate_bundle.py")

    Invoke-CheckedCommand `
        -Label "Validate repository boundaries, locks, and controls" `
        -Command $pythonCommand `
        -CommandArguments @("tools/validate_repository.py")

    if ($Restore -or $Tier -eq "Full") {
        Invoke-CheckedCommand `
            -Label "Restore locked dependency graph" `
            -Command $dotnetCommand.Source `
            -CommandArguments @(
                "restore",
                $solutionPath,
                "--configfile",
                $nugetConfigPath,
                "--locked-mode"
            )
    }

    Invoke-CheckedCommand `
        -Label "Verify formatting" `
        -Command $dotnetCommand.Source `
        -CommandArguments @(
            "format",
            $solutionPath,
            "--verify-no-changes",
            "--no-restore"
        )

    Invoke-CheckedCommand `
        -Label "Build Release with warnings as errors" `
        -Command $dotnetCommand.Source `
        -CommandArguments @(
            "build",
            $solutionPath,
            "--configuration",
            "Release",
            "--no-restore",
            "--warnaserror",
            "--nologo"
        )

    foreach ($entry in $testSelection) {
        Invoke-TestProject `
            -Dotnet $dotnetCommand.Source `
            -Project ([string]$entry.path) `
            -TestFilter ([string]$entry.filter)
    }

    $stopwatch.Stop()
    $elapsedSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)

    if ($Tier -in @("Quick", "Targeted")) {
        $targetSeconds = [double]$manifest.tiers.($Tier.ToLowerInvariant()).target_seconds
        if ($elapsedSeconds -gt $targetSeconds) {
            Write-Warning "$Tier completed but exceeded the $targetSeconds second target: $elapsedSeconds seconds. Record and investigate the regression."
        }
    }

    $classification = if ($SupplementaryCi) { "supplementary" } else { "active-machine" }
    Write-Host "OK: ThirdLife $Tier verification passed in $elapsedSeconds seconds ($classification evidence context)."
}
finally {
    if ($stopwatch.IsRunning) {
        $stopwatch.Stop()
    }
    $duration = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
    Write-Host "Duration: $Tier verification ran for $duration seconds."
    Pop-Location
}
