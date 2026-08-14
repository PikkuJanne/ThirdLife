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
$sbomScriptPath = Join-Path $repositoryRoot "eng\generate-sbom.ps1"
$sbomTemporaryDirectory = $null

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
        -Label "Run governance validator regression tests" `
        -Command $pythonCommand `
        -CommandArguments @("tools/tests/test_validate_bundle.py")

    Invoke-CheckedCommand `
        -Label "Validate the governed roadmap bundle and portfolio metadata" `
        -Command $pythonCommand `
        -CommandArguments @("tools/validate_bundle.py")

    Invoke-CheckedCommand `
        -Label "Validate repository boundaries, package locks, and CI controls" `
        -Command $pythonCommand `
        -CommandArguments @("tools/validate_repository.py")

    $sbomTemporaryDirectory = Join-Path `
        ([System.IO.Path]::GetTempPath()) `
        ("ThirdLife-SBOM-" + [System.Guid]::NewGuid().ToString("N"))
    $null = New-Item -ItemType Directory -Path $sbomTemporaryDirectory
    $firstSbomPath = Join-Path $sbomTemporaryDirectory "first.cdx.json"
    $secondSbomPath = Join-Path $sbomTemporaryDirectory "second.cdx.json"

    Write-Host "==> Generate and compare deterministic development SBOMs"
    & $sbomScriptPath -OutputPath $firstSbomPath
    & $sbomScriptPath -OutputPath $secondSbomPath

    $firstSbomBytes = [System.IO.File]::ReadAllBytes($firstSbomPath)
    $secondSbomBytes = [System.IO.File]::ReadAllBytes($secondSbomPath)
    if ($firstSbomBytes.Length -ne $secondSbomBytes.Length) {
        throw "Repeated SBOM generation produced different byte lengths."
    }
    for ($index = 0; $index -lt $firstSbomBytes.Length; $index++) {
        if ($firstSbomBytes[$index] -ne $secondSbomBytes[$index]) {
            throw "Repeated SBOM generation differed at byte offset $index."
        }
    }

    $sbomDocument = Get-Content -Raw -LiteralPath $firstSbomPath | ConvertFrom-Json
    if (
        $sbomDocument.bomFormat -ne "CycloneDX" -or
        $sbomDocument.specVersion -ne "1.7" -or
        $sbomDocument.components.Count -lt 1
    ) {
        throw "Generated SBOM is not a populated CycloneDX 1.7 document."
    }
    $sbomDigest = (Get-FileHash -Algorithm SHA256 -LiteralPath $firstSbomPath).Hash.ToLowerInvariant()
    Write-Host "OK: deterministic development SBOM sha256:$sbomDigest"

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
    if ($null -ne $sbomTemporaryDirectory -and (Test-Path -LiteralPath $sbomTemporaryDirectory)) {
        foreach ($temporaryName in @("first.cdx.json", "second.cdx.json")) {
            $temporaryPath = Join-Path $sbomTemporaryDirectory $temporaryName
            if (Test-Path -LiteralPath $temporaryPath) {
                Remove-Item -Force -LiteralPath $temporaryPath
            }
        }
        Remove-Item -Force -LiteralPath $sbomTemporaryDirectory
    }
    Pop-Location
}
