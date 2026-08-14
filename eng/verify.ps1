[CmdletBinding()]
param()

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

if ($env:OS -ne "Windows_NT") {
    throw "ThirdLife's authoritative verification requires Windows. Run eng/verify.sh from Git Bash on Windows or use eng/verify.ps1 directly."
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$solutionPath = Join-Path $repositoryRoot "ThirdLife.sln"
$nugetConfigPath = Join-Path $repositoryRoot "NuGet.Config"
$globalJsonPath = Join-Path $repositoryRoot "global.json"
$venvPythonPath = Join-Path $repositoryRoot ".venv\Scripts\python.exe"

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:NUGET_XMLDOC_MODE = "skip"
$env:PIP_DISABLE_PIP_VERSION_CHECK = "1"
$env:PIP_NO_INPUT = "1"

Push-Location $repositoryRoot
try {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw "The .NET SDK is required. Install the exact SDK from global.json before running verification."
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
            throw "Python 3.14.7 and tools/requirements.txt are required. Create the documented .venv before running verification."
        }
        $pythonCommand = $systemPython.Source
    }

    Invoke-CheckedCommand `
        -Label "Validate the pinned Python toolchain" `
        -Command $pythonCommand `
        -CommandArguments @(
            "-c",
            "import sys, yaml; expected=(3, 14, 7); actual=sys.version_info[:3]; actual == expected or sys.exit(f'Python {expected} required; found {actual}'); yaml.__version__ == '6.0.3' or sys.exit(f'PyYAML 6.0.3 required; found {yaml.__version__}')"
        )

    Invoke-CheckedCommand `
        -Label "Validate the governed roadmap bundle and portfolio metadata" `
        -Command $pythonCommand `
        -CommandArguments @("tools/validate_bundle.py")

    Invoke-CheckedCommand `
        -Label "Validate repository boundaries, package locks, and CI controls" `
        -Command $pythonCommand `
        -CommandArguments @("tools/validate_repository.py")

    Invoke-CheckedCommand `
        -Label "Restore the locked dependency graph" `
        -Command $dotnetCommand.Source `
        -CommandArguments @(
            "restore",
            $solutionPath,
            "--configfile",
            $nugetConfigPath,
            "--locked-mode"
        )

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
        -Label "Build Release with compiler warnings treated as errors" `
        -Command $dotnetCommand.Source `
        -CommandArguments @(
            "build",
            $solutionPath,
            "--configuration",
            "Release",
            "--no-restore",
            "--warnaserror"
        )

    Invoke-CheckedCommand `
        -Label "Run the Release test suite" `
        -Command $dotnetCommand.Source `
        -CommandArguments @(
            "test",
            $solutionPath,
            "--configuration",
            "Release",
            "--no-build",
            "--no-restore"
        )

    Write-Host "OK: ThirdLife verification passed."
}
finally {
    Pop-Location
}
