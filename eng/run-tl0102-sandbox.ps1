[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("RoundTrip", "Integration", "Migration", "PathSecurity", "Interruption", "Targeted", "Quick", "Full")]
    [string] $Phase = "Targeted",

    [Parameter(Mandatory = $false)]
    [ValidateSet("TL-0102", "TL-0103")]
    [string] $EvidenceTaskId = "TL-0102",

    [Parameter(Mandatory = $false)]
    [switch] $PersistEvidence,

    [Parameter(Mandatory = $false)]
    [switch] $PreflightOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$TaskId = $EvidenceTaskId
$ResultSchemaVersion = 2
$SandboxMemoryMb = 4096
$SandboxTimeoutMinutes = 30
$ResultLimitBytes = 32768
$TailLimitBytes = 8192
$CompletionMarkerLimitBytes = 32
$MaxSourceFiles = 10000
$MaxSourceBytes = 536870912
$MaxHistoryBundleBytes = 268435456
$MaxNuGetPackages = 64
$MaxNuGetBytes = 536870912
$ResultName = "tl0102-result.json"
$CompletionMarkerName = "complete.marker"
$RequestName = "run-request.json"
$Limitation = "Same-machine disposable Windows Sandbox evidence only; no direct-host policy compatibility or cross-hardware certification claim. Raw command output is discarded and only a bounded sanitized tail is retained."

if (-not ("ThirdLife.TL0102.NativeFile" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ThirdLife.TL0102
{
    public sealed class NativeFileInfo
    {
        public string FinalPath { get; set; }
        public uint LinkCount { get; set; }
    }

    public static class NativeFile
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint Low;
            public uint High;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public FILETIME CreationTime;
            public FILETIME LastAccessTime;
            public FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(
            SafeFileHandle file,
            StringBuilder path,
            uint pathLength,
            uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(
            SafeFileHandle file,
            out BY_HANDLE_FILE_INFORMATION information);

        private static NativeFileInfo InspectOpenHandle(SafeFileHandle handle)
        {
            StringBuilder finalPath = new StringBuilder(32768);
            uint finalLength = GetFinalPathNameByHandle(handle, finalPath, (uint)finalPath.Capacity, 0);
            if (finalLength == 0 || finalLength >= finalPath.Capacity)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to resolve the final path identity.");
            }
            BY_HANDLE_FILE_INFORMATION information;
            if (!GetFileInformationByHandle(handle, out information))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to resolve the file link identity.");
            }
            return new NativeFileInfo { FinalPath = finalPath.ToString(), LinkCount = information.NumberOfLinks };
        }

        public static NativeFileInfo InspectHandle(SafeFileHandle handle)
        {
            if (handle == null || handle.IsInvalid || handle.IsClosed)
            {
                throw new ArgumentException("The file handle is unavailable.", "handle");
            }
            return InspectOpenHandle(handle);
        }

        public static NativeFileInfo Inspect(string path)
        {
            const uint ShareReadWriteDelete = 0x00000007;
            const uint OpenExisting = 3;
            const uint BackupSemantics = 0x02000000;
            SafeFileHandle handle = CreateFile(path, 0, ShareReadWriteDelete, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open the exact path identity.");
            }

            using (handle)
            {
                return InspectOpenHandle(handle);
            }
        }
    }
}
"@
}

function New-OpaqueRunId {
    $bytes = New-Object byte[] 16
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) }
    finally { $generator.Dispose() }
    return -join ($bytes | ForEach-Object { $_.ToString("x2") })
}

function Get-Sha256Text {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string] $Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Text)))).Replace("-", "").ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Get-FileSha512Base64 {
    param([Parameter(Mandatory = $true)][string] $Path)
    $stream = [System.IO.File]::OpenRead($Path)
    $sha = [System.Security.Cryptography.SHA512]::Create()
    try { return [Convert]::ToBase64String($sha.ComputeHash($stream)) }
    finally { $sha.Dispose(); $stream.Dispose() }
}

function Get-NativeOutput {
    param(
        [Parameter(Mandatory = $true)][string] $Label,
        [Parameter(Mandatory = $true)][string] $FilePath,
        [Parameter(Mandatory = $true)][string[]] $Arguments
    )
    $output = @(& $FilePath @Arguments 2>$null)
    if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE." }
    if ($output.Count -eq 0) { throw "$Label returned no bounded value." }
    $value = $output[-1].ToString().Trim()
    if ([System.Text.Encoding]::UTF8.GetByteCount($value) -gt 4096) { throw "$Label returned an oversized value." }
    return $value
}

function Invoke-CheckedNativeCommand {
    param(
        [Parameter(Mandatory = $true)][string] $Label,
        [Parameter(Mandatory = $true)][string] $FilePath,
        [Parameter(Mandatory = $true)][string[]] $Arguments
    )
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE." }
}

function ConvertTo-ComparablePath {
    param([Parameter(Mandatory = $true)][string] $Path)
    $value = $Path
    if ($value.StartsWith("\\?\UNC\", [StringComparison]::OrdinalIgnoreCase)) { $value = "\\" + $value.Substring(8) }
    elseif ($value.StartsWith("\\?\", [StringComparison]::OrdinalIgnoreCase)) { $value = $value.Substring(4) }
    $full = [System.IO.Path]::GetFullPath($value)
    $root = [System.IO.Path]::GetPathRoot($full)
    if ($full.Length -gt $root.Length) { $full = $full.TrimEnd("\") }
    return $full
}

function Assert-ExistingPathIdentity {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $false)][switch] $RequireSingleLink
    )
    $expected = ConvertTo-ComparablePath -Path $Path
    $item = Get-Item -LiteralPath $expected -Force -ErrorAction Stop
    if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw "A governed path is a reparse point." }
    $native = [ThirdLife.TL0102.NativeFile]::Inspect($expected)
    $actual = ConvertTo-ComparablePath -Path $native.FinalPath
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($actual, $expected)) { throw "A governed path resolves outside its exact lexical identity." }
    if ($RequireSingleLink -and -not $item.PSIsContainer -and $native.LinkCount -ne 1) { throw "A governed file has more than one hard-link identity." }
    return $item
}

function Assert-SafeTree {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $false)][switch] $RequireSingleLinks
    )
    [void] (Assert-ExistingPathIdentity -Path $Path)
    foreach ($entry in Get-ChildItem -LiteralPath $Path -Force -Recurse -ErrorAction Stop) {
        if ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw "A governed tree contains a reparse point." }
        [void] (Assert-ExistingPathIdentity -Path $entry.FullName -RequireSingleLink:$RequireSingleLinks)
    }
}

function Assert-BoundedTree {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][int] $MaximumFiles,
        [Parameter(Mandatory = $true)][long] $MaximumBytes
    )
    $files = @(Get-ChildItem -LiteralPath $Path -File -Recurse -Force -ErrorAction Stop)
    if ($files.Count -gt $MaximumFiles) { throw "A governed tree exceeds its file-count bound." }
    [long]$total = 0
    foreach ($file in $files) {
        $total += $file.Length
        if ($total -gt $MaximumBytes) { throw "A governed tree exceeds its aggregate byte bound." }
    }
}

function Assert-SafeRelativePath {
    param([Parameter(Mandatory = $true)][string] $RelativePath)
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or $RelativePath.IndexOfAny([char[]]@(0, 10, 13)) -ge 0) { throw "Git returned an invalid repository-relative path." }
    $normalized = $RelativePath.Replace("/", "\")
    if ([System.IO.Path]::IsPathRooted($normalized)) { throw "Git returned a rooted repository path." }
    foreach ($segment in $normalized.Split([char]'\')) {
        if ([string]::IsNullOrWhiteSpace($segment) -or $segment -in @(".", "..") -or $segment.Contains(":")) { throw "Git returned an unsafe repository path segment." }
    }
    return $normalized
}

function Get-ContainedPath {
    param(
        [Parameter(Mandatory = $true)][string] $Root,
        [Parameter(Mandatory = $true)][string] $RelativePath
    )
    $safeRelative = Assert-SafeRelativePath -RelativePath $RelativePath
    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd("\")
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $rootPath $safeRelative))
    if (-not $candidate.StartsWith("$rootPath\", [StringComparison]::OrdinalIgnoreCase)) { throw "A repository-relative path escaped its governed root." }
    return $candidate
}

function Copy-WorkingTreeFile {
    param(
        [Parameter(Mandatory = $true)][string] $Repository,
        [Parameter(Mandatory = $true)][string] $Destination,
        [Parameter(Mandatory = $true)][string] $RelativePath
    )
    $sourcePath = Get-ContainedPath -Root $Repository -RelativePath $RelativePath
    $destinationPath = Get-ContainedPath -Root $Destination -RelativePath $RelativePath
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        if (Test-Path -LiteralPath $destinationPath -PathType Leaf) {
            [void] (Assert-ExistingPathIdentity -Path $destinationPath -RequireSingleLink)
            Remove-Item -LiteralPath $destinationPath -Force
        }
        return
    }
    $sourceItem = Assert-ExistingPathIdentity -Path $sourcePath -RequireSingleLink
    if ($sourceItem.PSIsContainer) { throw "The governed source overlay accepts regular files only." }
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $destinationPath)) | Out-Null
    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath
}

function Get-SourceDigest {
    param([Parameter(Mandatory = $true)][string] $Root)
    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd("\")
    $records = foreach ($file in Get-ChildItem -LiteralPath $rootPath -File -Recurse -Force | Sort-Object FullName) {
        $relative = $file.FullName.Substring($rootPath.Length).TrimStart("\").Replace("\", "/")
        $segments = @($relative.Split('/'))
        if (@($segments | Where-Object { $_.ToLowerInvariant() -in @(".git", ".venv", "__pycache__", ".pytest_cache", "bin", "obj", "testresults") }).Count -ne 0) { continue }
        "$relative`:$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())"
    }
    return Get-Sha256Text -Text ($records -join "`n")
}

function Get-RepositoryTrackedState {
    param(
        [Parameter(Mandatory = $true)][string] $GitExecutable,
        [Parameter(Mandatory = $true)][string] $Repository
    )
    $lines = @(& $GitExecutable -C $Repository status --porcelain=v1 --untracked-files=no)
    if ($LASTEXITCODE -ne 0) { throw "Unable to inspect the tracked repository state." }
    return [pscustomobject]@{
        clean = $lines.Count -eq 0
        sha256 = Get-Sha256Text -Text ($lines -join "`n")
    }
}

function Assert-GitSnapshotBound {
    param(
        [Parameter(Mandatory = $true)][string] $GitExecutable,
        [Parameter(Mandatory = $true)][string] $Repository,
        [Parameter(Mandatory = $true)][string] $Revision
    )
    $entries = @(& $GitExecutable -C $Repository ls-tree -r -l --full-tree $Revision)
    if ($LASTEXITCODE -ne 0) { throw "Unable to inspect the committed source snapshot bound." }
    if ($entries.Count -gt $MaxSourceFiles) { throw "The committed source snapshot exceeds its file-count bound." }
    [long]$total = 0
    foreach ($entry in $entries) {
        if ($entry -notmatch "^[0-7]{6}\s+(?:blob|commit)\s+[0-9a-f]{40}\s+(\d+|-)\t") { throw "Git returned an unsupported source snapshot entry." }
        if ($Matches[1] -ne "-") {
            $total += [long]$Matches[1]
            if ($total -gt $MaxSourceBytes) { throw "The committed source snapshot exceeds its aggregate byte bound." }
        }
    }
}

function Assert-WorkingTreeSelectionBound {
    param(
        [Parameter(Mandatory = $true)][string] $Repository,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]] $RelativePaths
    )
    $unique = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [long]$total = 0
    foreach ($relative in $RelativePaths) {
        $safeRelative = Assert-SafeRelativePath -RelativePath $relative
        if (-not $unique.Add($safeRelative)) { continue }
        if ($unique.Count -gt $MaxSourceFiles) { throw "The working source overlay exceeds its file-count bound." }
        $path = Get-ContainedPath -Root $Repository -RelativePath $safeRelative
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $item = Assert-ExistingPathIdentity -Path $path -RequireSingleLink
        if ($item.PSIsContainer) { throw "The governed source overlay accepts regular files only." }
        $total += $item.Length
        if ($total -gt $MaxSourceBytes) { throw "The working source overlay exceeds its aggregate byte bound." }
    }
}

function Get-LockedNuGetClosure {
    param(
        [Parameter(Mandatory = $true)][string] $SourceRoot,
        [Parameter(Mandatory = $true)][string] $SelectedPhase
    )
    if ($SelectedPhase -eq "Quick") { return @() }
    $lockFiles = @()
    if ($SelectedPhase -eq "Full") {
        $lockFiles = @(Get-ChildItem -LiteralPath $SourceRoot -Filter "packages.lock.json" -File -Recurse -Force |
            Where-Object { $_.FullName -notmatch "[\\/](?:bin|obj|\.venv|\.git)[\\/]" } |
            Sort-Object FullName)
    }
    elseif ($SelectedPhase -eq "Targeted") {
        $lockFiles = @(
            Get-Item -LiteralPath (Join-Path $SourceRoot "tests\ThirdLife.Core.Tests\packages.lock.json") -Force
            Get-Item -LiteralPath (Join-Path $SourceRoot "tests\ThirdLife.Persistence.Tests\packages.lock.json") -Force
        )
    }
    else {
        $lockFiles = @(Get-Item -LiteralPath (Join-Path $SourceRoot "tests\ThirdLife.Persistence.Tests\packages.lock.json") -Force)
    }
    if ($lockFiles.Count -eq 0) { throw "The selected phase has no locked NuGet closure." }

    $packages = @{}
    foreach ($lockFile in $lockFiles) {
        $lock = Get-Content -Raw -LiteralPath $lockFile.FullName -Encoding UTF8 | ConvertFrom-Json
        if ($lock.version -ne 2 -or $null -eq $lock.dependencies) { throw "A selected NuGet lock file has an unsupported schema." }
        foreach ($framework in $lock.dependencies.PSObject.Properties) {
            foreach ($packageProperty in $framework.Value.PSObject.Properties) {
                $package = $packageProperty.Value
                if ($package.type -eq "Project") { continue }
                $id = $packageProperty.Name
                $version = $package.resolved
                $contentHash = $package.contentHash
                if ($id -notmatch "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$" -or
                    $version -isnot [string] -or $version -notmatch "^[A-Za-z0-9][A-Za-z0-9.+-]{0,127}$" -or
                    $contentHash -isnot [string] -or $contentHash -notmatch "^[A-Za-z0-9+/]{86}==$") {
                    throw "A selected NuGet lock entry is incomplete or unsafe."
                }
                $key = "$($id.ToLowerInvariant())/$($version.ToLowerInvariant())"
                if ($packages.ContainsKey($key) -and $packages[$key].content_hash -ne $contentHash) { throw "The selected lock files disagree about an exact NuGet package hash." }
                $packages[$key] = [pscustomobject]@{ id = $id; version = $version; content_hash = $contentHash }
            }
        }
    }
    return @($packages.Values | Sort-Object @{ Expression = { $_.id.ToLowerInvariant() } }, @{ Expression = { $_.version.ToLowerInvariant() } })
}

function Write-Utf8CreateNew {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Content
    )
    $expected = ConvertTo-ComparablePath -Path $Path
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($Content)
    $stream = [System.IO.FileStream]::new(
        $Path,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None
    )
    try {
        $identity = [ThirdLife.TL0102.NativeFile]::InspectHandle($stream.SafeFileHandle)
        if (-not [StringComparer]::OrdinalIgnoreCase.Equals((ConvertTo-ComparablePath -Path $identity.FinalPath), $expected) -or $identity.LinkCount -ne 1) {
            throw "The create-new file handle does not match its exact single-link destination."
        }
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    }
    finally { $stream.Dispose() }
}

function Stage-LockedNuGetClosure {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]] $Packages,
        [Parameter(Mandatory = $true)][string] $GlobalPackagesRoot,
        [Parameter(Mandatory = $true)][string] $Destination
    )
    if ($Packages.Count -gt $MaxNuGetPackages) { throw "The selected NuGet closure exceeds its package-count bound." }
    $manifestPackages = @()
    [long]$totalBytes = 0
    foreach ($package in $Packages) {
        $idLower = $package.id.ToLowerInvariant()
        $versionLower = $package.version.ToLowerInvariant()
        $sourceNupkg = Join-Path (Join-Path (Join-Path $GlobalPackagesRoot $idLower) $versionLower) "$idLower.$versionLower.nupkg"
        if (-not (Test-Path -LiteralPath $sourceNupkg -PathType Leaf)) { throw "The host cache is missing an exact package from the selected locked closure." }
        [void] (Assert-ExistingPathIdentity -Path $sourceNupkg -RequireSingleLink)
        $archiveSha512 = Get-FileSha512Base64 -Path $sourceNupkg
        $destinationName = "$idLower.$versionLower.nupkg"
        $destinationPath = Join-Path $Destination $destinationName
        $sourceItem = Get-Item -LiteralPath $sourceNupkg -Force
        $totalBytes += $sourceItem.Length
        if ($totalBytes -gt $MaxNuGetBytes) { throw "The selected NuGet closure exceeds its aggregate byte bound." }
        Copy-Item -LiteralPath $sourceNupkg -Destination $destinationPath
        $destinationItem = Assert-ExistingPathIdentity -Path $destinationPath -RequireSingleLink
        if ($destinationItem.Length -le 0 -or $destinationItem.Length -gt 268435456) { throw "A staged NuGet archive is outside its bounded size." }
        $manifestPackages += [ordered]@{
            id = $package.id
            version = $package.version
            file = $destinationName
            content_hash = $package.content_hash
            archive_sha512 = $archiveSha512
            sha256 = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash.ToLowerInvariant()
            bytes = $destinationItem.Length
        }
    }
    $manifest = [ordered]@{ schema_version = 1; packages = @($manifestPackages) } | ConvertTo-Json -Depth 4 -Compress
    Write-Utf8CreateNew -Path (Join-Path $Destination "nuget-closure.json") -Content $manifest
    Assert-SafeTree -Path $Destination -RequireSingleLinks
    Assert-BoundedTree -Path $Destination -MaximumFiles ($MaxNuGetPackages + 1) -MaximumBytes ($MaxNuGetBytes + 131072)
}

function Stage-PythonPackages {
    param(
        [Parameter(Mandatory = $true)][string] $PythonExecutable,
        [Parameter(Mandatory = $true)][string] $RequirementsPath,
        [Parameter(Mandatory = $true)][string] $Destination,
        [Parameter(Mandatory = $true)][bool] $Required
    )
    if (-not $Required) { return }
    $requirements = Get-Content -Raw -LiteralPath $RequirementsPath -Encoding UTF8
    $admittedHashes = @([regex]::Matches($requirements, "(?im)--hash=sha256:([0-9a-f]{64})") | ForEach-Object { $_.Groups[1].Value })
    if ($admittedHashes.Count -eq 0) { throw "The Python bootstrap requirements contain no admitted hashes." }
    Invoke-CheckedNativeCommand -Label "Stage hash-pinned Python wheel" -FilePath $PythonExecutable -Arguments @(
        "-m", "pip", "download", "--quiet", "--disable-pip-version-check", "--no-deps", "--only-binary=:all:",
        "--require-hashes", "--requirement", $RequirementsPath, "--dest", $Destination
    )
    $items = @(Get-ChildItem -LiteralPath $Destination -Force -ErrorAction Stop)
    if ($items.Count -ne 1 -or $items[0].PSIsContainer -or $items[0].Name -notmatch "(?i)^pyyaml-6\.0\.3-.+\.whl$") { throw "The Python bootstrap stage does not contain exactly the admitted PyYAML wheel." }
    [void] (Assert-ExistingPathIdentity -Path $items[0].FullName -RequireSingleLink)
    $digest = (Get-FileHash -LiteralPath $items[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($digest -notin $admittedHashes) { throw "The staged Python wheel does not match an admitted requirements hash." }
    if ($items[0].Length -le 0 -or $items[0].Length -gt 10485760) { throw "The staged Python wheel is outside its bounded size." }
}

function Assert-GuestOutputInProgress {
    param([Parameter(Mandatory = $true)][string] $Path)
    $items = @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction Stop)
    foreach ($item in $items) {
        if ($item.PSIsContainer -or $item.Attributes -band [System.IO.FileAttributes]::ReparsePoint -or $item.Name -notin @($ResultName, $CompletionMarkerName)) { throw "The guest created an unexpected output object." }
    }
    [long] $total = 0
    foreach ($item in $items) { $total += [long] $item.Length }
    if ($total -gt ($ResultLimitBytes + $CompletionMarkerLimitBytes)) { throw "The guest exceeded its writable output bound." }
}

function Assert-GuestOutputDirectory {
    param([Parameter(Mandatory = $true)][string] $Path)
    Assert-SafeTree -Path $Path -RequireSingleLinks
    $items = @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction Stop)
    $expected = @($CompletionMarkerName, $ResultName) | Sort-Object
    $actual = @($items.Name) | Sort-Object
    if (@(Compare-Object -ReferenceObject $expected -DifferenceObject $actual).Count -ne 0) { throw "The guest output directory contains an unexpected object." }
    if (@($items | Where-Object { $_.PSIsContainer }).Count -ne 0) { throw "The guest output directory may contain ordinary files only." }
    $markerPath = Join-Path $Path $CompletionMarkerName
    $marker = Get-Item -LiteralPath $markerPath
    if ($marker.Length -gt $CompletionMarkerLimitBytes -or (Get-Content -Raw -LiteralPath $markerPath -Encoding UTF8) -ne "complete`n") { throw "The guest completion marker is malformed." }
    [long] $total = 0
    foreach ($item in $items) { $total += [long] $item.Length }
    if ($total -gt ($ResultLimitBytes + $CompletionMarkerLimitBytes)) { throw "The guest output directory exceeds its combined bound." }
}

function Assert-ResultSchema {
    param(
        [Parameter(Mandatory = $true)] $Result,
        [Parameter(Mandatory = $true)] [hashtable] $Expected
    )
    $expectedKeys = @(
        "dotnet_sdk", "duration_seconds", "environment", "exit_code", "expected_head_commit",
        "expected_tracked_clean", "failure_code", "finished_utc", "git_version", "launcher_sha256",
        "history_bundle_sha256", "limitation", "network_reason", "networking_enabled", "nuget_audit_enabled", "nuget_closure_sha256",
        "nuget_package_count", "output_tail", "output_truncated", "phase", "python_packages_sha256",
        "python_version", "raw_output_retained", "repository_status_sha256", "result", "run_id",
        "runner_sha256", "same_physical_machine", "sandbox_config_sha256", "sandbox_memory_mb",
        "schema_version", "source_digest", "source_history_included", "source_unchanged_after",
        "started_utc", "task_id"
    ) | Sort-Object
    $actualKeys = @($Result.PSObject.Properties.Name) | Sort-Object
    if (@(Compare-Object -ReferenceObject $expectedKeys -DifferenceObject $actualKeys).Count -ne 0) { throw "The Sandbox result does not match schema version $ResultSchemaVersion." }
    if ($Result.schema_version -ne $ResultSchemaVersion -or $Result.task_id -ne $TaskId -or
        $Result.environment -ne "Windows Sandbox" -or $Result.sandbox_memory_mb -ne $SandboxMemoryMb -or
        $Result.same_physical_machine -isnot [bool] -or -not $Result.same_physical_machine -or
        $Result.source_history_included -isnot [bool] -or
        $Result.raw_output_retained -isnot [bool] -or $Result.raw_output_retained) {
        throw "The Sandbox result contains an invalid fixed contract value."
    }
    foreach ($binding in $Expected.GetEnumerator()) {
        if ($Result.($binding.Key) -ne $binding.Value) {
            if ($binding.Key -eq "source_digest" -and $Result.result -eq "failed" -and
                $Result.source_digest -eq ("0" * 64) -and -not $Result.source_unchanged_after) {
                continue
            }
            if ($binding.Key -like "*_digest" -or $binding.Key -like "*_sha256") {
                throw "The Sandbox result is not bound to the live $($binding.Key): expected $($binding.Value), received $($Result.($binding.Key))."
            }
            throw "The Sandbox result is not bound to the live $($binding.Key)."
        }
    }
    foreach ($digestName in @(
        "history_bundle_sha256", "launcher_sha256", "runner_sha256", "sandbox_config_sha256", "nuget_closure_sha256",
        "python_packages_sha256", "repository_status_sha256", "source_digest"
    )) {
        if ($Result.$digestName -isnot [string] -or $Result.$digestName -notmatch "^[0-9a-f]{64}$") { throw "The Sandbox result contains an invalid digest." }
    }
    if ($Result.expected_head_commit -isnot [string] -or $Result.expected_head_commit -notmatch "^[0-9a-f]{40}$" -or
        $Result.run_id -isnot [string] -or $Result.run_id -notmatch "^[0-9a-f]{32}$") { throw "The Sandbox result contains an invalid source identity." }
    foreach ($booleanName in @("expected_tracked_clean", "networking_enabled", "nuget_audit_enabled", "output_truncated", "source_unchanged_after")) {
        if ($Result.$booleanName -isnot [bool]) { throw "The Sandbox result contains an invalid Boolean field." }
    }
    if ($Result.result -notin @("passed", "failed") -or
        $Result.failure_code -notin @(
            "none", "preflight_failed", "source_binding_failed", "python_bootstrap_failed", "restore_failed",
            "test_failed", "verification_failed", "success_marker_missing", "output_limit_exceeded",
            "command_timeout", "postflight_failed"
        ) -or $Result.network_reason -notin @("disabled_offline_phase", "governed_nuget_audit")) {
        throw "The Sandbox result contains an invalid bounded state."
    }
    if (($Result.exit_code -isnot [int] -and $Result.exit_code -isnot [long]) -or
        ($Result.nuget_package_count -isnot [int] -and $Result.nuget_package_count -isnot [long])) { throw "The Sandbox result contains an invalid integer field." }
    if ([double]$Result.duration_seconds -lt 0 -or [double]$Result.duration_seconds -gt ($SandboxTimeoutMinutes * 60 + 120)) { throw "The Sandbox result duration is outside its bound." }
    foreach ($timestamp in @($Result.started_utc, $Result.finished_utc)) {
        if ($timestamp -isnot [string] -or $timestamp -notmatch "^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$") { throw "The Sandbox result contains an invalid timestamp." }
        [void] [DateTimeOffset]::Parse($timestamp, [Globalization.CultureInfo]::InvariantCulture)
    }
    if ($Result.limitation -ne $Limitation) { throw "The Sandbox result changed the governed claim limitation." }
    $tail = @($Result.output_tail)
    if ($tail.Count -gt 40) { throw "The Sandbox result contains too many diagnostic tail lines." }
    foreach ($line in $tail) {
        if ($line -isnot [string] -or $line.Length -gt 512 -or
            $line -match "(?i)https?://|[A-Za-z]:\\Users\\|(?:password|token|authorization|credential)\s*[:=]|recovery\s+key") { throw "The Sandbox result contains an unsafe diagnostic tail line." }
    }
    if ([System.Text.Encoding]::UTF8.GetByteCount(($tail -join "`n")) -gt $TailLimitBytes) { throw "The Sandbox diagnostic tail exceeds its bounded schema." }
    if ($Result.result -eq "passed") {
        if ($Result.exit_code -ne 0 -or $Result.failure_code -ne "none" -or -not $Result.source_unchanged_after) { throw "The Sandbox passing state contradicts its evidence." }
    }
    elseif ($Result.failure_code -eq "none") { throw "The Sandbox failure lacks a bounded failure code." }
}

function Read-BoundedResult {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][hashtable] $Expected
    )
    $item = Assert-ExistingPathIdentity -Path $Path -RequireSingleLink
    if ($item.Length -le 0 -or $item.Length -gt $ResultLimitBytes) { throw "The Sandbox result is outside its bounded size." }
    $result = Get-Content -Raw -LiteralPath $Path -Encoding UTF8 | ConvertFrom-Json
    Assert-ResultSchema -Result $result -Expected $Expected
    return $result
}

function Assert-VerifiedStagingPath {
    param([Parameter(Mandatory = $true)][string] $Path)
    $resolvedPath = [System.IO.Path]::GetFullPath($Path).TrimEnd("\")
    $temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd("\")
    $leaf = Split-Path -Leaf $resolvedPath
    if (-not $resolvedPath.StartsWith("$temporaryRoot\", [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing cleanup outside the operating-system temporary directory." }
    if ($leaf -notmatch "^ThirdLife-TL0102-[0-9a-f]{32}$") { throw "Refusing cleanup of an unexpected staging directory name." }
    [void] (Assert-ExistingPathIdentity -Path $temporaryRoot)
    [void] (Assert-ExistingPathIdentity -Path $resolvedPath)
    return $resolvedPath
}

function Remove-VerifiedStagingDirectory {
    param([Parameter(Mandatory = $true)][string] $Path)
    $verifiedPath = Assert-VerifiedStagingPath -Path $Path
    $children = @(Get-ChildItem -LiteralPath $verifiedPath -Recurse -Force -ErrorAction Stop |
        Sort-Object @{ Expression = { $_.FullName.Length }; Descending = $true })
    foreach ($entry in $children) {
        if (-not (Test-Path -LiteralPath $entry.FullName)) { continue }
        $current = Get-Item -LiteralPath $entry.FullName -Force -ErrorAction Stop
        if ($current.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw "Refusing cleanup because verified staging contains a reparse point." }
        [void] (Assert-ExistingPathIdentity -Path $current.FullName -RequireSingleLink:(!$current.PSIsContainer))
        if ($current.Attributes -band [System.IO.FileAttributes]::ReadOnly) {
            $current.Attributes = $current.Attributes -band (-bnot [System.IO.FileAttributes]::ReadOnly)
        }
        # Never recurse during deletion. A raced-in child or junction makes the exact
        # directory removal fail closed instead of being traversed.
        Remove-Item -LiteralPath $current.FullName -Force
    }
    $rootItem = Assert-ExistingPathIdentity -Path $verifiedPath
    if ($rootItem.Attributes -band [System.IO.FileAttributes]::ReadOnly) {
        $rootItem.Attributes = $rootItem.Attributes -band (-bnot [System.IO.FileAttributes]::ReadOnly)
    }
    Remove-Item -LiteralPath $verifiedPath -Force
    if (Test-Path -LiteralPath $verifiedPath) { throw "Verified staging cleanup did not complete." }
}

function New-VerifiedDirectoryUnderRoot {
    param(
        [Parameter(Mandatory = $true)][string] $Root,
        [Parameter(Mandatory = $true)][string] $RelativePath
    )
    $rootPath = ConvertTo-ComparablePath -Path $Root
    [void] (Assert-ExistingPathIdentity -Path $rootPath)
    $current = $rootPath
    foreach ($segment in (Assert-SafeRelativePath -RelativePath $RelativePath).Split([char]'\')) {
        $next = Join-Path $current $segment
        $matches = @(Get-ChildItem -LiteralPath $current -Force -ErrorAction Stop | Where-Object { $_.Name -ieq $segment })
        if ($matches.Count -gt 1) { throw "A governed evidence directory component is ambiguous." }
        if ($matches.Count -eq 0) { New-Item -ItemType Directory -Path $next | Out-Null }
        $item = Assert-ExistingPathIdentity -Path $next
        if (-not $item.PSIsContainer) { throw "A governed evidence directory component is not a directory." }
        $current = $next
    }
    return $current
}

function Persist-StructuredEvidence {
    param(
        [Parameter(Mandatory = $true)] $Result,
        [Parameter(Mandatory = $true)][string] $RepositoryRoot
    )
    $directory = New-VerifiedDirectoryUnderRoot -Root $RepositoryRoot -RelativePath "artifacts\audit\$TaskId"
    $baseName = "$($Result.phase.ToLowerInvariant())-$($Result.source_digest)-$($Result.run_id)"
    $evidenceName = "$baseName.json"
    $manifestName = "$baseName.manifest.json"
    $evidencePath = Join-Path $directory $evidenceName
    $manifestPath = Join-Path $directory $manifestName
    $json = $Result | ConvertTo-Json -Depth 5 -Compress
    if ([System.Text.Encoding]::UTF8.GetByteCount($json) -gt $ResultLimitBytes) { throw "Validated evidence unexpectedly exceeds its bounded schema." }
    [void] (Assert-ExistingPathIdentity -Path $directory)
    Write-Utf8CreateNew -Path $evidencePath -Content $json
    [void] (Assert-ExistingPathIdentity -Path $evidencePath -RequireSingleLink)
    $manifest = [ordered]@{
        schema_version = 1; task_id = $TaskId; run_id = $Result.run_id; phase = $Result.phase
        source_digest = $Result.source_digest; expected_head_commit = $Result.expected_head_commit
        repository_status_sha256 = $Result.repository_status_sha256
        launcher_sha256 = $Result.launcher_sha256; runner_sha256 = $Result.runner_sha256
        sandbox_config_sha256 = $Result.sandbox_config_sha256; history_bundle_sha256 = $Result.history_bundle_sha256
        nuget_closure_sha256 = $Result.nuget_closure_sha256; python_packages_sha256 = $Result.python_packages_sha256
        evidence_file = $evidenceName
        evidence_sha256 = (Get-FileHash -LiteralPath $evidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
        created_utc = [DateTime]::UtcNow.ToString("o")
    } | ConvertTo-Json -Compress
    [void] (Assert-ExistingPathIdentity -Path $directory)
    Write-Utf8CreateNew -Path $manifestPath -Content $manifest
    [void] (Assert-ExistingPathIdentity -Path $manifestPath -RequireSingleLink)
    Write-Host "Append-only bounded evidence: $evidencePath"
    Write-Host "Evidence manifest: $manifestPath"
}

function Get-ActiveSandboxProcesses {
    return @(Get-Process -Name @("WindowsSandbox", "WindowsSandboxClient", "WindowsSandboxRemoteSession", "WindowsSandboxServer") -ErrorAction SilentlyContinue)
}

if ($env:OS -ne "Windows_NT") { throw "$TaskId Sandbox verification requires the active Windows Codex machine." }

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$sandboxExecutable = Join-Path $env:WINDIR "System32\WindowsSandbox.exe"
$guestRunnerSource = Join-Path $PSScriptRoot "run-tl0102-sandbox-guest.ps1"
$launcherSource = $PSCommandPath
$gitExecutable = (Get-Command git -ErrorAction Stop).Source
$gitRoot = Split-Path -Parent (Split-Path -Parent $gitExecutable)
$mappedGitExecutable = Join-Path $gitRoot "cmd\git.exe"
$dotnetExecutable = (Get-Command dotnet -ErrorAction Stop).Source
$dotnetRoot = Split-Path -Parent $dotnetExecutable
$pythonCommand = (Get-Command python -ErrorAction Stop).Source
$pythonRoot = [System.IO.Path]::GetFullPath((Get-NativeOutput -Label "Resolve CPython root" -FilePath $pythonCommand -Arguments @("-c", "import sys; print(sys.base_prefix)")))
$pythonExecutable = Join-Path $pythonRoot "python.exe"
$nugetOutput = Get-NativeOutput -Label "Resolve NuGet package cache" -FilePath $dotnetExecutable -Arguments @("nuget", "locals", "global-packages", "--list")
$nugetRoot = [System.IO.Path]::GetFullPath(($nugetOutput -replace "^global-packages:\s*", ""))
$runId = New-OpaqueRunId
$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd("\")
$stagingRoot = Join-Path $temporaryRoot "ThirdLife-TL0102-$runId"
$sourceStage = Join-Path $stagingRoot "Source"
$harnessStage = Join-Path $stagingRoot "Harness"
$requestStage = Join-Path $stagingRoot "Request"
$nugetStage = Join-Path $stagingRoot "NuGet"
$pythonPackagesStage = Join-Path $stagingRoot "PythonPackages"
$historyStage = Join-Path $stagingRoot "History"
$resultStage = Join-Path $stagingRoot "Output"
$archivePath = Join-Path $stagingRoot "source.zip"
$configPath = Join-Path $stagingRoot "TL0102.wsb"
$sandboxProcess = $null
$completed = $false

try {
    Write-Host "$TaskId Windows Sandbox verification"
    Write-Host "Phase: $Phase"
    Write-Host "No action is required inside the guest."

    if (-not (Test-Path -LiteralPath $sandboxExecutable -PathType Leaf)) { throw "Windows Sandbox is not available at its supported system path." }
    foreach ($requiredFile in @($guestRunnerSource, $launcherSource, $mappedGitExecutable, $dotnetExecutable, $pythonExecutable)) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) { throw "A required governed harness or tool executable is unavailable." }
        [void] (Assert-ExistingPathIdentity -Path $requiredFile -RequireSingleLink:($requiredFile -in @($guestRunnerSource, $launcherSource)))
    }
    if (@(Get-ActiveSandboxProcesses).Count -ne 0) { throw "Close the existing Windows Sandbox session before starting this bounded run." }
    if (-not (Test-Path -LiteralPath $nugetRoot -PathType Container)) { throw "The governed host NuGet cache is unavailable." }
    [void] (Assert-ExistingPathIdentity -Path $repositoryRoot)
    [void] (Assert-ExistingPathIdentity -Path $temporaryRoot)

    $head = Get-NativeOutput -Label "Resolve current commit" -FilePath $gitExecutable -Arguments @("-C", $repositoryRoot, "rev-parse", "HEAD")
    if ($head -notmatch "^[0-9a-f]{40}$") { throw "Git returned an invalid HEAD identity." }
    $trackedState = Get-RepositoryTrackedState -GitExecutable $gitExecutable -Repository $repositoryRoot
    if ($PreflightOnly -and $PersistEvidence) { throw "Preflight-only runs cannot persist execution evidence." }
    if ($PersistEvidence) {
        if (-not $trackedState.clean) { throw "Persistent evidence requires an unchanged tracked worktree; use a transient run while implementing." }
        foreach ($harnessPath in @("eng/run-tl0102-sandbox.ps1", "eng/run-tl0102-sandbox-guest.ps1")) {
            & $gitExecutable -C $repositoryRoot ls-files --error-unmatch -- $harnessPath 2>$null | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Persistent evidence requires both harness files to be committed." }
        }
    }

    New-Item -ItemType Directory -Path $stagingRoot | Out-Null
    [void] (Assert-VerifiedStagingPath -Path $stagingRoot)
    foreach ($directory in @($sourceStage, $harnessStage, $requestStage, $nugetStage, $pythonPackagesStage, $historyStage, $resultStage)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
        [void] (Assert-ExistingPathIdentity -Path $directory)
    }

    Assert-GitSnapshotBound -GitExecutable $gitExecutable -Repository $repositoryRoot -Revision $head
    Invoke-CheckedNativeCommand -Label "Export source snapshot base" -FilePath $gitExecutable -Arguments @(
        "-C", $repositoryRoot, "archive", "--format=zip", "--output", $archivePath, $head
    )
    $archiveItem = Assert-ExistingPathIdentity -Path $archivePath -RequireSingleLink
    if ($archiveItem.Length -le 0 -or $archiveItem.Length -gt $MaxSourceBytes) { throw "The source snapshot archive is outside its byte bound." }
    Expand-Archive -LiteralPath $archivePath -DestinationPath $sourceStage
    Remove-Item -LiteralPath $archivePath -Force
    Assert-BoundedTree -Path $sourceStage -MaximumFiles $MaxSourceFiles -MaximumBytes $MaxSourceBytes

    $trackedFiles = @(& $gitExecutable -C $repositoryRoot ls-files)
    if ($LASTEXITCODE -ne 0) { throw "Unable to enumerate tracked source files." }
    $taskUntrackedFiles = @(& $gitExecutable -C $repositoryRoot ls-files --others --exclude-standard -- `
        "src/ThirdLife.Core/Jobs/IJobStore.cs" `
        "src/ThirdLife.Core/Jobs/JobService.cs" `
        "src/ThirdLife.Core/Sanitization/SanitizationGate.cs" `
        "src/ThirdLife.Persistence" `
        "tests/ThirdLife.Core.Tests/JobLifecycleTests.cs" `
        "tests/ThirdLife.Persistence.Tests" `
        "eng/run-tl0102-sandbox.ps1" `
        "eng/run-tl0102-sandbox-guest.ps1")
    if ($LASTEXITCODE -ne 0) { throw "Unable to enumerate TL-0102/TL-0103 working files." }
    Assert-WorkingTreeSelectionBound -Repository $repositoryRoot -RelativePaths @($trackedFiles + $taskUntrackedFiles)
    foreach ($relative in $trackedFiles) { Copy-WorkingTreeFile -Repository $repositoryRoot -Destination $sourceStage -RelativePath $relative }
    foreach ($relative in $taskUntrackedFiles) { Copy-WorkingTreeFile -Repository $repositoryRoot -Destination $sourceStage -RelativePath $relative }
    if (Test-Path -LiteralPath (Join-Path $sourceStage ".git")) { throw "The exported source unexpectedly contains Git history." }
    Assert-SafeTree -Path $sourceStage -RequireSingleLinks
    Assert-BoundedTree -Path $sourceStage -MaximumFiles $MaxSourceFiles -MaximumBytes $MaxSourceBytes
    $sourceDigest = Get-SourceDigest -Root $sourceStage

    Copy-Item -LiteralPath $launcherSource -Destination (Join-Path $harnessStage "run-tl0102-sandbox.ps1")
    Copy-Item -LiteralPath $guestRunnerSource -Destination (Join-Path $harnessStage "run-tl0102-sandbox-guest.ps1")
    Assert-SafeTree -Path $harnessStage -RequireSingleLinks
    $launcherDigest = (Get-FileHash -LiteralPath (Join-Path $harnessStage "run-tl0102-sandbox.ps1") -Algorithm SHA256).Hash.ToLowerInvariant()
    $runnerDigest = (Get-FileHash -LiteralPath (Join-Path $harnessStage "run-tl0102-sandbox-guest.ps1") -Algorithm SHA256).Hash.ToLowerInvariant()

    $lockedPackages = @(Get-LockedNuGetClosure -SourceRoot $sourceStage -SelectedPhase $Phase)
    Stage-LockedNuGetClosure -Packages $lockedPackages -GlobalPackagesRoot $nugetRoot -Destination $nugetStage
    $nugetClosureDigest = Get-SourceDigest -Root $nugetStage
    Stage-PythonPackages `
        -PythonExecutable $pythonExecutable `
        -RequirementsPath (Join-Path $sourceStage "tools\requirements.txt") `
        -Destination $pythonPackagesStage `
        -Required:($Phase -in @("Quick", "Full"))
    Assert-SafeTree -Path $pythonPackagesStage -RequireSingleLinks
    $pythonPackagesDigest = Get-SourceDigest -Root $pythonPackagesStage

    $sourceHistoryIncluded = $Phase -in @("Quick", "Full")
    if ($sourceHistoryIncluded) {
        $historyBundlePath = Join-Path $historyStage "repository-history.bundle"
        Invoke-CheckedNativeCommand -Label "Stage exact reachable Git history" -FilePath $gitExecutable -Arguments @(
            "-C", $repositoryRoot, "bundle", "create", $historyBundlePath, "HEAD"
        )
        $historyItem = Assert-ExistingPathIdentity -Path $historyBundlePath -RequireSingleLink
        if ($historyItem.Length -le 0 -or $historyItem.Length -gt $MaxHistoryBundleBytes) { throw "The staged Git history bundle is outside its byte bound." }
        $bundleHead = Get-NativeOutput -Label "Inspect staged Git history" -FilePath $gitExecutable -Arguments @("bundle", "list-heads", $historyBundlePath)
        if ($bundleHead -ne "$head HEAD") { throw "The staged Git history bundle is not bound exclusively to the expected HEAD." }
    }
    Assert-SafeTree -Path $historyStage -RequireSingleLinks
    Assert-BoundedTree -Path $historyStage -MaximumFiles 1 -MaximumBytes $MaxHistoryBundleBytes
    $historyBundleDigest = Get-SourceDigest -Root $historyStage

    $networkingEnabled = $Phase -eq "Full"
    $networkReason = if ($networkingEnabled) { "governed_nuget_audit" } else { "disabled_offline_phase" }
    $networkingMode = if ($networkingEnabled) { "Enable" } else { "Disable" }
    $sandboxVersionText = (Get-Item -LiteralPath $sandboxExecutable).VersionInfo.FileVersion
    $sandboxVersionMatch = [regex]::Match($sandboxVersionText, "[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+")
    if (-not $sandboxVersionMatch.Success) { throw "Windows Sandbox returned an unsupported version format." }

    $escape = [System.Security.SecurityElement]
    $sourceXml = $escape::Escape($sourceStage)
    $harnessXml = $escape::Escape($harnessStage)
    $requestXml = $escape::Escape($requestStage)
    $dotnetXml = $escape::Escape($dotnetRoot)
    $pythonXml = $escape::Escape($pythonRoot)
    $gitXml = $escape::Escape($gitRoot)
    $nugetXml = $escape::Escape($nugetStage)
    $pythonPackagesXml = $escape::Escape($pythonPackagesStage)
    $historyXml = $escape::Escape($historyStage)
    $resultXml = $escape::Escape($resultStage)
    $configuration = @"
<Configuration>
  <VGpu>Disable</VGpu>
  <Networking>$networkingMode</Networking>
  <AudioInput>Disable</AudioInput>
  <VideoInput>Disable</VideoInput>
  <ProtectedClient>Enable</ProtectedClient>
  <PrinterRedirection>Disable</PrinterRedirection>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <MemoryInMB>$SandboxMemoryMb</MemoryInMB>
  <MappedFolders>
    <MappedFolder><HostFolder>$sourceXml</HostFolder><SandboxFolder>C:\TL0102\Input\Source</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$harnessXml</HostFolder><SandboxFolder>C:\TL0102\Input\Harness</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$requestXml</HostFolder><SandboxFolder>C:\TL0102\Input\Request</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$dotnetXml</HostFolder><SandboxFolder>C:\TL0102\Input\DotNet</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$pythonXml</HostFolder><SandboxFolder>C:\TL0102\Input\Python</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$gitXml</HostFolder><SandboxFolder>C:\TL0102\Input\Git</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$nugetXml</HostFolder><SandboxFolder>C:\TL0102\Input\NuGet</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$pythonPackagesXml</HostFolder><SandboxFolder>C:\TL0102\Input\PythonPackages</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$historyXml</HostFolder><SandboxFolder>C:\TL0102\Input\History</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$resultXml</HostFolder><SandboxFolder>C:\TL0102\Output</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File C:\TL0102\Input\Harness\run-tl0102-sandbox-guest.ps1</Command></LogonCommand>
</Configuration>
"@
    Write-Utf8CreateNew -Path $configPath -Content $configuration
    $configurationDigest = (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Copy-Item -LiteralPath $configPath -Destination (Join-Path $requestStage "TL0102.wsb")

    $request = [ordered]@{
        schema_version = $ResultSchemaVersion
        task_id = $TaskId
        run_id = $runId
        phase = $Phase
        expected_source_digest = $sourceDigest
        expected_head_commit = $head
        expected_tracked_clean = [bool]$trackedState.clean
        repository_status_sha256 = $trackedState.sha256
        source_history_included = $sourceHistoryIncluded
        history_bundle_sha256 = $historyBundleDigest
        launcher_sha256 = $launcherDigest
        runner_sha256 = $runnerDigest
        sandbox_config_sha256 = $configurationDigest
        sandbox_executable_version = $sandboxVersionMatch.Value
        nuget_closure_sha256 = $nugetClosureDigest
        nuget_package_count = $lockedPackages.Count
        python_packages_sha256 = $pythonPackagesDigest
        networking_enabled = $networkingEnabled
        network_reason = $networkReason
        nuget_audit_enabled = $networkingEnabled
    }
    Write-Utf8CreateNew -Path (Join-Path $requestStage $RequestName) -Content ($request | ConvertTo-Json -Compress)
    Assert-SafeTree -Path $requestStage -RequireSingleLinks

    $expectedResult = @{
        task_id = $TaskId
        run_id = $runId
        phase = $Phase
        expected_head_commit = $head
        expected_tracked_clean = [bool]$trackedState.clean
        repository_status_sha256 = $trackedState.sha256
        source_digest = $sourceDigest
        source_history_included = $sourceHistoryIncluded
        history_bundle_sha256 = $historyBundleDigest
        launcher_sha256 = $launcherDigest
        runner_sha256 = $runnerDigest
        sandbox_config_sha256 = $configurationDigest
        nuget_closure_sha256 = $nugetClosureDigest
        nuget_package_count = $lockedPackages.Count
        python_packages_sha256 = $pythonPackagesDigest
        networking_enabled = $networkingEnabled
        network_reason = $networkReason
        nuget_audit_enabled = $networkingEnabled
    }

    if ($PreflightOnly) {
        Remove-VerifiedStagingDirectory -Path $stagingRoot
        $completed = $true
        Write-Host "PASS: $TaskId $Phase staging, binding, offline-dependency, and verified-cleanup preflight completed."
        return
    }

    $sandboxProcess = Start-Process -FilePath $sandboxExecutable -ArgumentList @("`"$configPath`"") -WindowStyle Hidden -PassThru
    $resultPath = Join-Path $resultStage $ResultName
    $markerPath = Join-Path $resultStage $CompletionMarkerName
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes($SandboxTimeoutMinutes)
    while (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        Assert-GuestOutputInProgress -Path $resultStage
        if ($sandboxProcess.HasExited -and @(Get-ActiveSandboxProcesses).Count -eq 0) { throw "Windows Sandbox closed before producing a bounded result." }
        if ([DateTimeOffset]::UtcNow -ge $deadline) { throw "Windows Sandbox did not produce a result within $SandboxTimeoutMinutes minutes." }
        Start-Sleep -Milliseconds 250
    }

    $closeDeadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    while ((-not $sandboxProcess.HasExited -or @(Get-ActiveSandboxProcesses).Count -ne 0) -and [DateTimeOffset]::UtcNow -lt $closeDeadline) {
        Assert-GuestOutputInProgress -Path $resultStage
        Start-Sleep -Milliseconds 250
    }
    if (-not $sandboxProcess.HasExited -or @(Get-ActiveSandboxProcesses).Count -ne 0) { throw "The test finished, but Windows Sandbox did not close within the bounded interval." }

    Assert-GuestOutputDirectory -Path $resultStage
    $currentHead = Get-NativeOutput -Label "Re-verify current commit" -FilePath $gitExecutable -Arguments @("-C", $repositoryRoot, "rev-parse", "HEAD")
    $currentTrackedState = Get-RepositoryTrackedState -GitExecutable $gitExecutable -Repository $repositoryRoot
    if ($currentHead -ne $head -or $currentTrackedState.clean -ne $trackedState.clean -or $currentTrackedState.sha256 -ne $trackedState.sha256) { throw "The repository HEAD or expected tracked state changed during the Sandbox run." }
    if ((Get-SourceDigest -Root $sourceStage) -ne $sourceDigest -or
        (Get-SourceDigest -Root $historyStage) -ne $historyBundleDigest -or
        (Get-SourceDigest -Root $nugetStage) -ne $nugetClosureDigest -or
        (Get-SourceDigest -Root $pythonPackagesStage) -ne $pythonPackagesDigest -or
        (Get-FileHash -LiteralPath (Join-Path $harnessStage "run-tl0102-sandbox.ps1") -Algorithm SHA256).Hash.ToLowerInvariant() -ne $launcherDigest -or
        (Get-FileHash -LiteralPath (Join-Path $harnessStage "run-tl0102-sandbox-guest.ps1") -Algorithm SHA256).Hash.ToLowerInvariant() -ne $runnerDigest -or
        (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $configurationDigest) { throw "A staged source, harness, or configuration binding changed during the run." }

    $result = Read-BoundedResult -Path $resultPath -Expected $expectedResult
    $resultDigest = (Get-FileHash -LiteralPath $resultPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if (@($result.output_tail).Count -gt 0) {
        Write-Host ""
        Write-Host "--- sanitized bounded guest tail ---"
        @($result.output_tail) | ForEach-Object { Write-Host $_ }
        Write-Host "--- end sanitized tail ---"
    }
    if ($PersistEvidence) { Persist-StructuredEvidence -Result $result -RepositoryRoot $repositoryRoot }

    Write-Host ""
    Write-Host "Result: $($result.result)"
    Write-Host "Duration: $($result.duration_seconds) seconds"
    Write-Host "Source digest: $sourceDigest"
    Write-Host "Result SHA-256: $resultDigest"
    Write-Host "Guest networking: $($result.networking_enabled) ($($result.network_reason))"
    if ($result.result -ne "passed" -or $result.exit_code -ne 0) { throw "$TaskId Sandbox phase $Phase failed closed with $($result.failure_code)." }
    $completed = $true
    Write-Host "PASS: $TaskId $Phase completed in Windows Sandbox."
}
finally {
    if ($null -ne $sandboxProcess -and -not $sandboxProcess.HasExited) {
        Stop-Process -Id $sandboxProcess.Id -Force -ErrorAction SilentlyContinue
        try { $sandboxProcess.WaitForExit(15000) }
        catch { }
    }
    if (Test-Path -LiteralPath $stagingRoot) {
        if (@(Get-ActiveSandboxProcesses).Count -eq 0) {
            try { Remove-VerifiedStagingDirectory -Path $stagingRoot }
            catch { Write-Warning "Verified staging cleanup did not complete; the exact task-specific directory was retained." }
        }
        else { Write-Warning "Sandbox staging was retained because the guest may still hold mapped paths." }
    }
    if (-not $completed) { Write-Host "$TaskId Sandbox verification stopped without a passing result." }
}
