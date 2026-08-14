[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputPath,

    [switch] $Release,

    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string] $ProductVersion,

    [ValidatePattern('^[0-9a-f]{40}$')]
    [string] $SourceRevision
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$generatorPath = Join-Path $repositoryRoot "tools\generate_sbom.py"
$venvPythonPath = Join-Path $repositoryRoot ".venv\Scripts\python.exe"
$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)

if ($Release) {
    if ([string]::IsNullOrWhiteSpace($ProductVersion) -or [string]::IsNullOrWhiteSpace($SourceRevision)) {
        throw "Release SBOM generation requires both -ProductVersion and -SourceRevision."
    }
}
elseif (-not [string]::IsNullOrEmpty($ProductVersion) -or -not [string]::IsNullOrEmpty($SourceRevision)) {
    throw "-ProductVersion and -SourceRevision may be used only with -Release."
}

if (Test-Path -LiteralPath $venvPythonPath -PathType Leaf) {
    $pythonCommand = $venvPythonPath
}
else {
    $systemPython = Get-Command python -ErrorAction SilentlyContinue
    if ($null -eq $systemPython) {
        throw "Python 3.14.7 is required. Create the documented .venv before generating an SBOM."
    }
    $pythonCommand = $systemPython.Source
}

& $pythonCommand -c "import sys; expected=(3, 14, 7); actual=sys.version_info[:3]; raise SystemExit(0 if actual == expected else f'Python 3.14.7 required; found {actual[0]}.{actual[1]}.{actual[2]}')"
if ($LASTEXITCODE -ne 0) {
    throw "Python 3.14.7 is required for deterministic SBOM generation."
}

$generatorArguments = @(
    $generatorPath,
    "--output",
    $resolvedOutputPath
)
if ($Release) {
    $generatorArguments += @(
        "--release",
        "--product-version",
        $ProductVersion,
        "--source-revision",
        $SourceRevision
    )
}

Push-Location $repositoryRoot
try {
    & $pythonCommand @generatorArguments
    if ($LASTEXITCODE -ne 0) {
        throw "SBOM generation failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
