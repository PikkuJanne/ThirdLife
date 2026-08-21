[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string] $OutputPath,

    [Parameter(Mandatory = $false)]
    [ValidatePattern("^[0-9A-Za-z][0-9A-Za-z.+-]{0,63}$")]
    [string] $ProductVersion = "0.3.0-dev",

    [Parameter(Mandatory = $false)]
    [string] $SourceRevision = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$generatorPath = Join-Path $repositoryRoot "tools\supply_chain.py"
$venvPythonPath = Join-Path $repositoryRoot ".venv\Scripts\python.exe"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutputPath = Join-Path $repositoryRoot "artifacts\sbom\thirdlife-setup-core.cdx.json"
}
elseif ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
}
else {
    $resolvedOutputPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
}

if ($SourceRevision -ne "" -and $SourceRevision -notmatch "^[0-9a-f]{40}$") {
    throw "SourceRevision must be an empty value or a lowercase 40-character Git commit hash."
}

if (Test-Path -LiteralPath $venvPythonPath -PathType Leaf) {
    $pythonCommand = $venvPythonPath
}
else {
    $systemPython = Get-Command python -ErrorAction SilentlyContinue
    if ($null -eq $systemPython) {
        throw "Python 3.14.7 is required to generate the SBOM."
    }
    $pythonCommand = $systemPython.Source
}

& $pythonCommand -c "import sys; expected=(3, 14, 7); actual=sys.version_info[:3]; actual == expected or sys.exit(f'Python {expected} required; found {actual}')"
if ($LASTEXITCODE -ne 0) {
    throw "The pinned Python toolchain check failed."
}

$generatorArguments = @(
    $generatorPath,
    "--root",
    $repositoryRoot,
    "--output",
    $resolvedOutputPath,
    "--product-version",
    $ProductVersion
)
if ($SourceRevision -ne "") {
    $generatorArguments += @("--source-revision", $SourceRevision)
}

& $pythonCommand @generatorArguments
if ($LASTEXITCODE -ne 0) {
    throw "SBOM generation failed with exit code $LASTEXITCODE."
}

$digest = (Get-FileHash -LiteralPath $resolvedOutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "OK: generated deterministic CycloneDX 1.6 SBOM"
Write-Host "Path: $resolvedOutputPath"
Write-Host "SHA-256: $digest"
