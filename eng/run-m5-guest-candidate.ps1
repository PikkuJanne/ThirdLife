#Requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('Candidate', 'OptionalFailure', 'EssentialFailure')]
    [string[]] $Scenario = @('Candidate')
)

# Stage only the controlled profile test and its dependencies. No servicing or guest power action.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$profile = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'thirdlife-hyperv-profile.json') -Raw | ConvertFrom-Json
$project = 'ThirdLife.UI.Tests'
$assembly = "$project.dll"
$methods = @{
    Candidate = 'JobSeekerCandidateConvergesAndReopensWithoutLiveReadiness'
    OptionalFailure = 'OptionalCandidateFailurePreservesEssentialActions'
    EssentialFailure = 'EssentialCandidateFailureBlocksPreparation'
}
$tests = @($Scenario | ForEach-Object { "FullyQualifiedName=$project.PlanReviewTests.$($methods[$_])" } | Select-Object -Unique)
if ($tests.Count -eq 0) { throw 'Select at least one controlled candidate scenario.' }
$filter = $tests -join '|'
$output = Join-Path $repository "tests/$project/bin/Release/net10.0-windows"
$localRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ThirdLife'
$sdk = Join-Path $localRoot 'HyperV/dotnet-sdk-win-x64.zip'
$credential = Import-Clixml -LiteralPath (Join-Path $localRoot 'Credentials/guest.credential.xml')
if ($credential -isnot [pscredential] -or $credential.UserName -ine "$($profile.computer)\$($profile.account)") {
    throw 'The existing guest PowerShell Direct credential is unavailable.'
}
if ((Get-FileHash -LiteralPath $sdk -Algorithm SHA512).Hash -ine $profile.sdkSha512) {
    throw 'The private SDK archive does not match the pinned SHA-512.'
}
$vm = Get-VM -Name $profile.vm
if ($vm.State -ne 'Running' -or $vm.Generation -ne 2 -or $vm.Id -eq [guid]::Empty) {
    throw 'The configured guest identity or running state could not be verified.'
}
$source = & git -C $repository rev-parse --verify HEAD
if ($LASTEXITCODE -ne 0 -or $source -cnotmatch '^[0-9a-f]{40}$') { throw 'Cannot identify the source revision.' }
$changes = @(& git -C $repository status --porcelain=v1)
if ($LASTEXITCODE -ne 0 -or $changes.Count -ne 0) { throw 'Use Release binaries from a clean checkpoint.' }
$artifact = Get-Item -LiteralPath (Join-Path $output $assembly)
if (-not $artifact.VersionInfo.ProductVersion.EndsWith("+$source", [StringComparison]::Ordinal)) {
    throw 'The Release test assembly was built from a different revision.'
}
$assemblyHash = (Get-FileHash -LiteralPath $artifact.FullName).Hash.ToLowerInvariant()
$outputSize = (Get-ChildItem -LiteralPath $output -Recurse -File | Measure-Object -Property Length -Sum).Sum
if ($outputSize -gt 256MB) { throw 'The Release test output exceeds 256 MiB.' }
$scratch = [IO.Directory]::CreateTempSubdirectory('ThirdLife-M5-guest-')
$payload = Join-Path $scratch.FullName 'tests.zip'
$stage = 'ThirdLife-TL0510-' + [guid]::NewGuid().ToString('N')
$session = $null
$guestPath = $null
$started = [DateTime]::UtcNow
$timer = [Diagnostics.Stopwatch]::StartNew()
$lease = [Threading.Mutex]::new($false, ('ThirdLifeVerification-' + $vm.Id.ToString('N')))
$ownsLease = $false

function Invoke-DirectCommand([string] $Command, [hashtable] $Parameters, [int] $TimeoutSeconds) {
    $operation = [PowerShell]::Create()
    try {
        $null = $operation.AddCommand($Command).AddParameters($Parameters).AddParameter('ErrorAction', 'Stop')
        $pending = $operation.BeginInvoke()
        if (-not $pending.AsyncWaitHandle.WaitOne([TimeSpan]::FromSeconds($TimeoutSeconds))) {
            $null = $operation.BeginStop($null, $null)
            throw "PowerShell Direct $Command exceeded its time limit."
        }
        $operation.EndInvoke($pending) | ForEach-Object { $_.PSObject.BaseObject }
    }
    finally {
        if ($operation.InvocationStateInfo.State -ne 'Stopping') { $operation.Dispose() }
    }
}

function Invoke-Guest([int] $TimeoutSeconds, [object[]] $Arguments, [scriptblock] $Script) {
    $job = Invoke-Command -Session $session -AsJob -ArgumentList $Arguments -ScriptBlock $Script
    try {
        if (-not (Wait-Job -Job $job -Timeout $TimeoutSeconds)) { throw 'The guest operation exceeded its time limit.' }
        Receive-Job -Job $job -ErrorAction Stop
        if ($job.State -ne 'Completed') { throw 'The guest operation did not complete.' }
    }
    finally { Remove-Job -Job $job -Force }
}

try {
    $ownsLease = $lease.WaitOne(0)
    if (-not $ownsLease) { throw 'A verification run already owns the guest.' }
    [IO.Compression.ZipFile]::CreateFromDirectory($output, $payload)
    if ((Get-Item -LiteralPath $payload).Length -gt 48MB) { throw 'The staged test payload exceeds 48 MiB.' }
    $payloadHash = (Get-FileHash -LiteralPath $payload).Hash.ToLowerInvariant()
    $session = Invoke-DirectCommand 'New-PSSession' @{ VMId = $vm.Id; Credential = $credential } 20
    $guestPath = Invoke-Guest 20 @($profile.computer, $stage) {
        param($computer, $name)
        $system = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
        $nativeArchitecture = Get-ItemPropertyValue -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment' `
            -Name PROCESSOR_ARCHITECTURE
        if ($env:COMPUTERNAME -ine $computer -or $nativeArchitecture -ine 'AMD64' -or
            $env:PROCESSOR_ARCHITECTURE -ine 'AMD64' -or
            $system.InstallationType -ne 'Client' -or [int]$system.CurrentBuildNumber -lt 22000 -or
            $name -cnotmatch '^ThirdLife-TL0510-[0-9a-f]{32}$') { throw 'Guest identity or platform did not qualify.' }
        $path = Join-Path $env:ProgramFiles $name
        $volume = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($path))
        if ($volume.AvailableFreeSpace -lt 2GB) { throw 'The guest volume lacks 2 GiB of staging headroom.' }
        if (Test-Path -LiteralPath $path) { throw 'The isolated guest staging directory is already present.' }
        [IO.Directory]::CreateDirectory($path).FullName
    }
    Invoke-DirectCommand 'Copy-Item' @{ ToSession = $session; LiteralPath = $sdk; Destination = (Join-Path $guestPath 'sdk.zip') } 120
    Invoke-DirectCommand 'Copy-Item' @{ ToSession = $session; LiteralPath = $payload; Destination = (Join-Path $guestPath 'tests.zip') } 120
    $result = Invoke-Guest 180 @($guestPath, $profile.sdkSha512, $payloadHash,
        $profile.sdkVersion, $assembly, $assemblyHash, $filter, $tests.Count) {
        param($path, $sdkHash, $testHash, $version, $assembly, $assemblyHash, $filter, $expected)
        $ErrorActionPreference = 'Stop'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        foreach ($archive in @(@('sdk', 'SHA512', $sdkHash), @('tests', 'SHA256', $testHash))) {
            $zip = Join-Path $path ($archive[0] + '.zip')
            if ((Get-FileHash -LiteralPath $zip -Algorithm $archive[1]).Hash -ine $archive[2]) {
                throw 'Transferred archive digest differs.'
            }
            [IO.Compression.ZipFile]::ExtractToDirectory($zip, (Join-Path $path $archive[0]))
        }
        Get-ChildItem Env: | Where-Object Name -Match '^(DOTNET_|CORECLR_|COR_|COMPLUS_|VSTEST_|MSBUILD)' | ForEach-Object {
            Remove-Item -LiteralPath ('Env:' + $_.Name)
        }
        $env:DOTNET_ROOT = Join-Path $path 'sdk'
        $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
        $env:DOTNET_CLI_HOME = $path
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        $env:DOTNET_NOLOGO = '1'
        $env:TEMP = $env:TMP = [IO.Directory]::CreateDirectory((Join-Path $path 'temp')).FullName
        $dotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
        $tests = Join-Path $path 'tests'
        Set-Location -LiteralPath $tests
        if ((& $dotnet --version) -ne $version -or $LASTEXITCODE -ne 0 -or
            (Get-FileHash -LiteralPath $assembly).Hash -ine $assemblyHash) {
            throw 'The guest test SDK or assembly identity did not match.'
        }
        $testTimer = [Diagnostics.Stopwatch]::StartNew()
        & $dotnet vstest $assembly "--TestCaseFilter:$filter" '--Logger:trx;LogFileName=candidate.trx' `
            "--ResultsDirectory:$path" '--' 'RunConfiguration.TreatNoTestsAsError=true' 'RunConfiguration.TestSessionTimeout=120000' | Out-Null
        $exitCode = $LASTEXITCODE
        [xml] $trx = Get-Content -LiteralPath (Join-Path $path 'candidate.trx') -Raw
        $counts = $trx.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
        $system = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
        $total = [int]$counts.GetAttribute('total')
        $passed = [int]$counts.GetAttribute('passed')
        [pscustomobject]@{
            result = if ($exitCode -eq 0 -and $total -eq $expected -and $passed -eq $expected) {
                'passed'
            } else { 'failed' }
            total = $total
            passed = $passed
            exit_code = $exitCode
            test_ms = $testTimer.ElapsedMilliseconds
            windows_release = $system.DisplayVersion
            windows_edition = $system.EditionID
            windows_build = "$($system.CurrentBuildNumber).$($system.UBR)"
            architecture = $env:PROCESSOR_ARCHITECTURE
        }
    }
}
finally {
    try {
        if ($session -and $guestPath) {
            if ($session.State -ne 'Opened') {
                Remove-PSSession $session
                $session = Invoke-DirectCommand 'New-PSSession' @{ VMId = $vm.Id; Credential = $credential } 20
            }
            Invoke-Guest 30 @($guestPath, $stage) {
                param($path, $name)
                $expected = Join-Path $env:ProgramFiles $name
                if ([IO.Path]::GetFullPath($path) -cne $expected -or $name -cnotmatch '^ThirdLife-TL0510-[0-9a-f]{32}$') {
                    throw 'Guest cleanup target did not match the run directory.'
                }
                Set-Location -LiteralPath $env:ProgramFiles
                if ([IO.Directory]::Exists($path)) { [IO.Directory]::Delete($path, $true) }
                if (Test-Path -LiteralPath $path) { throw 'The isolated guest directory still exists.' }
            }
        }
    }
    finally {
        if ($session) { Remove-PSSession $session }
        if ($ownsLease) { $lease.ReleaseMutex() }
        $lease.Dispose()
        [IO.File]::Delete($payload)
        $scratch.Delete()
    }
}

[ordered]@{
    task = 'TL-0510'
    scenario = 'GuestCandidate'
    filters = $tests
    source_head = $source
    payload_sha256 = $payloadHash
    assembly_sha256 = $assemblyHash
    sdk_sha512 = $profile.sdkSha512
    started_utc = $started.ToString('o')
    elapsed_ms = $timer.ElapsedMilliseconds
    scope = 'Controlled candidate profile and action failure variants on the existing ThirdLife-W11 guest. Live readiness stays held.'
    cleanup = 'host archive and guest staging and test data removed'
    observation = $result | Select-Object result, total, passed, exit_code, test_ms, windows_release, windows_edition, windows_build, architecture
} | ConvertTo-Json -Depth 4
if ($result.result -ne 'passed') { exit 1 }
