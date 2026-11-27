#Requires -Version 7.2
[CmdletBinding()]
param(
    [ValidatePattern('^[a-z0-9](?:[a-z0-9-]{0,46}[a-z0-9])?$')]
    [string] $RunName = 'private-preview',

    [ValidatePattern('^[0-9A-Za-z][0-9A-Za-z.+-]{0,63}$')]
    [string] $ProductVersion = '0.3.0-dev'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$source = & git -C $repo rev-parse --verify HEAD
if ($LASTEXITCODE -ne 0 -or $source -cnotmatch '^[0-9a-f]{40}$') { throw 'The source revision could not be identified.' }
$changes = @(& git -C $repo status --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0 -or $changes.Count -ne 0) { throw 'Use a clean tracked source checkout.' }
$extraSources = @(& git -C $repo ls-files --others --exclude-standard -- src)
if ($LASTEXITCODE -ne 0 -or $extraSources.Count -ne 0) { throw 'The source directory contains untracked inputs.' }

$project = Join-Path $repo 'src/ThirdLife.UI/ThirdLife.UI.csproj'
$parent = Join-Path $repo 'TestResults/development-preview'
$path = Join-Path $parent "$RunName-$($source.Substring(0, 8))"
if (Test-Path -LiteralPath $path) { throw 'Choose an unused development preview run name.' }
New-Item -ItemType Directory -Path $path | Out-Null
$payload = Join-Path $path 'payload'

& dotnet restore $project --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
# A serial build avoids duplicate project instances writing the same output.
& dotnet publish $project -c Release --no-restore --self-contained false -m:1 `
    -p:BuildInParallel=false -p:UseAppHost=true -p:DebugSymbols=false -p:DebugType=None -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Development publish failed.' }

$app = Join-Path $payload 'ThirdLife.UI.exe'
$appBytes = [IO.File]::ReadAllBytes($app)
$header = [BitConverter]::ToInt32($appBytes, 0x3c)
if ([BitConverter]::ToUInt16($appBytes, $header + 4) -ne 0x8664) { throw 'The development apphost is not AMD64.' }

[string[]] $entries = @(Get-ChildItem -LiteralPath $payload -File -Recurse | ForEach-Object {
    [IO.Path]::GetRelativePath($payload, $_.FullName).Replace([IO.Path]::DirectorySeparatorChar, [char]'/')
})
[Array]::Sort($entries, [StringComparer]::Ordinal)
if ($entries -match '\.pdb$') { throw 'The development payload contains debugging symbols.' }
$archive = Join-Path $path 'thirdlife-development-preview-x64.zip'
$stream = [IO.File]::Open($archive, [IO.FileMode]::CreateNew)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($name in $entries) {
            $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $memberStream = $entry.Open()
            try {
                $sourceStream = [IO.File]::OpenRead((Join-Path $payload $name))
                try { $sourceStream.CopyTo($memberStream) } finally { $sourceStream.Dispose() }
            } finally { $memberStream.Dispose() }
        }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }

$sbom = Join-Path $path 'thirdlife-development-preview.cdx.json'
& (Join-Path $PSScriptRoot 'generate-sbom.ps1') -OutputPath $sbom -SourceRevision $source -ProductVersion $ProductVersion
$receipt = [ordered]@{
    classification = 'private_unsigned_development'
    source = $source
    archive = [IO.Path]::GetFileName($archive)
    archive_sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    archive_bytes = (Get-Item -LiteralPath $archive).Length
    apphost = [IO.Path]::GetFileName($app)
    apphost_sha256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash.ToLowerInvariant()
    sbom = [IO.Path]::GetFileName($sbom)
    sbom_sha256 = (Get-FileHash -LiteralPath $sbom -Algorithm SHA256).Hash.ToLowerInvariant()
    entries = $entries
}
$receipt | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $path 'development-package-manifest.json') -Encoding utf8
$receipt | ConvertTo-Json -Depth 3
