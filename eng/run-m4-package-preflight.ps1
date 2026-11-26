#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][guid] $ExpectedVMId,
    [Parameter(Mandatory)][guid] $ExpectedCheckpointId,
    [pscredential] $Credential,
    [switch] $CheckPackageSource
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$profile = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'thirdlife-hyperv-profile.json') -Raw | ConvertFrom-Json
if ($ExpectedVMId -eq [guid]::Empty -or $ExpectedCheckpointId -eq [guid]::Empty) {
    throw 'Supply the concrete IDs of the dedicated VM and its reviewed clean checkpoint.'
}

function Confirm-Target {
    try { $vm = Get-VM -Name $profile.vm }
    catch { throw 'The dedicated guest could not be queried in this host context.' }
    if ($vm.Id -ne $ExpectedVMId -or $vm.State -ne 'Running' -or $vm.Generation -ne 2) {
        throw 'The running dedicated Generation 2 guest does not match the expected VM ID.'
    }
    try { $checkpoints = @(Get-VMSnapshot -VM $vm -Name $profile.baseline) }
    catch { throw 'The dedicated clean checkpoint could not be queried.' }
    if ($checkpoints.Count -ne 1 -or $checkpoints[0].Id -ne $ExpectedCheckpointId) {
        throw 'The dedicated clean checkpoint does not match the expected checkpoint ID.'
    }
    $vm
}

$null = Confirm-Target
if (-not $Credential) {
    $credentialPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ThirdLife\Credentials\guest.credential.xml'
    try { $Credential = Import-Clixml -LiteralPath $credentialPath }
    catch { throw 'Supply the approved guest credential or recover it in the original local DPAPI context.' }
}
$admission = & (Join-Path $PSScriptRoot 'run-thirdlife-hyperv.ps1') -Scenario Inspect `
    -Credential $Credential -ExpectedVMId $ExpectedVMId 6>$null

$lease = [Threading.Mutex]::new($false, ('ThirdLifeVerification-' + $ExpectedVMId.ToString('N')))
$ownsLease = $false
$job = $null
try {
    try { $ownsLease = $lease.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $ownsLease = $true }
    if (-not $ownsLease) { throw 'A qualification run already owns this guest from this host session.' }

    $vm = Confirm-Target
    $job = Invoke-Command -VMId $vm.Id -Credential $Credential -AsJob -ArgumentList ([bool]$CheckPackageSource) -ScriptBlock {
        param([bool] $CheckPackageSource)
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'

        Add-Type -Namespace ThirdLife.Verification -Name PackagePreflight -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("msi.dll", EntryPoint = "MsiGetProductInfoExW",
    CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
[System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
public static extern uint ReadMachineProduct(string product, System.IntPtr sid, uint context, string property,
    System.Text.StringBuilder value, ref uint length);

[System.Runtime.InteropServices.DllImport("msi.dll", EntryPoint = "MsiEnumRelatedProductsW",
    CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
[System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
public static extern uint ReadRelatedProduct(string upgrade, uint reserved, uint index, System.Text.StringBuilder product);

[System.Runtime.InteropServices.DllImport("api-ms-win-core-realtime-l1-1-1.dll", ExactSpelling = true)]
[System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
public static extern void QueryInterruptTimePrecise(out ulong ticks);
'@

        $installerPath = Join-Path ([Environment]::SystemDirectory) 'msiexec.exe'
        $installerPresent = [IO.File]::Exists($installerPath)
        $registration = 'unavailable'
        if ($installerPresent) {
            $productCode = '{A23B657D-838B-4C9B-8F9D-386E50C1A76B}'
            $upgradeCode = '{CDB13460-04DF-4708-A7FD-4CB4A0684605}'
            $expected = [ordered]@{
                State = '5'
                VersionString = '8.9.9.1'
                Publisher = 'Notepad++ team (MSI installer)'
                PackageCode = '{18CD7780-F2FF-4F13-9070-BBE80D108501}'
                AssignmentType = '1'
            }
            $registration = 'exact_machine_registration'
            foreach ($property in $expected.Keys) {
                $buffer = [Text.StringBuilder]::new(128)
                $length = [uint32]$buffer.Capacity
                $status = [ThirdLife.Verification.PackagePreflight]::ReadMachineProduct(
                    $productCode, [IntPtr]::Zero, 4, $property, $buffer, [ref]$length)
                if ($property -eq 'State' -and $status -eq 1605) {
                    $relatedProduct = [Text.StringBuilder]::new(39)
                    $related = [ThirdLife.Verification.PackagePreflight]::ReadRelatedProduct($upgradeCode, 0, 0, $relatedProduct)
                    $registration = switch ($related) {
                        0 { 'related_registration' }
                        259 { 'absent' }
                        default { 'unavailable' }
                    }
                    break
                }
                if ($status -ne 0) { $registration = 'unavailable'; break }
                $actual = $buffer.ToString()
                if ($property -eq 'PackageCode') { $actual = $actual.ToUpperInvariant() }
                if ($actual -cne $expected[$property]) { $registration = 'conflicting_registration'; break }
            }
        }

        $source = 'not_requested'
        if ($CheckPackageSource) {
            Add-Type -AssemblyName System.Net.Http
            $source = 'unavailable'
            $handler = [Net.Http.HttpClientHandler]::new()
            $handler.AllowAutoRedirect = $false
            $handler.UseCookies = $false
            $client = [Net.Http.HttpClient]::new($handler)
            $client.Timeout = [TimeSpan]::FromSeconds(8)
            try {
                $release = [uri]'https://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/v8.9.9.1/npp.8.9.9.1.Installer.x64.msi'
                for ($hop = 0; $hop -lt 2; $hop++) {
                    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Head, $release)
                    try {
                        $response = $client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
                        try {
                            if ($response.StatusCode -eq [Net.HttpStatusCode]::OK) {
                                $source = if ($response.Content.Headers.ContentLength -eq 7933952) { 'expected_length' }
                                    else { 'unexpected_or_missing_length' }
                                break
                            }
                            $location = $response.Headers.Location
                            if ($hop -ne 0 -or ([int]$response.StatusCode -notin @(302, 307)) -or
                                -not $location -or -not $location.IsAbsoluteUri -or $location.Scheme -cne 'https' -or
                                $location.Host -cne 'release-assets.githubusercontent.com' -or -not $location.IsDefaultPort -or
                                $location.UserInfo -or $location.Fragment -or
                                -not $location.AbsolutePath.StartsWith('/github-production-release-asset', [StringComparison]::Ordinal)) {
                                break
                            }
                            $release = $location
                        }
                        finally { $response.Dispose() }
                    }
                    finally { $request.Dispose() }
                }
            }
            catch { $source = 'unavailable' }
            finally { $client.Dispose() }
        }

        $os = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
        $release = if ($os.DisplayVersion -cmatch '\A[0-9]{2}H[12]\z') { $os.DisplayVersion } else { 'unavailable' }
        $edition = if ($os.EditionID -cmatch '\A[A-Za-z0-9]{1,40}\z') { $os.EditionID } else { 'unavailable' }
        $boot = (Get-CimInstance Win32_OperatingSystem -Property LastBootUpTime -OperationTimeoutSec 5).LastBootUpTime
        $ticks = [uint64]0
        [ThirdLife.Verification.PackagePreflight]::QueryInterruptTimePrecise([ref]$ticks)
        $runtime = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'dotnet\shared\Microsoft.WindowsDesktop.App'
        $desktopRuntime = [IO.Directory]::Exists($runtime) -and
            [bool](Get-ChildItem -LiteralPath $runtime -Directory -Filter '10.*' | Select-Object -First 1)
        $capacity = [IO.DriveInfo]::new([IO.Path]::GetPathRoot([Environment]::SystemDirectory))
        $activeInstallers = @(Get-Process -Name msiexec -ErrorAction SilentlyContinue).Count
        [pscustomobject]@{
            WindowsBuild = '{0}.{1}' -f [int]$os.CurrentBuildNumber, [int]$os.UBR
            WindowsRelease = $release
            WindowsEdition = $edition
            NativeWindows11X64 = $os.InstallationType -eq 'Client' -and [int]$os.CurrentBuildNumber -ge 22000 -and
                [Environment]::Is64BitProcess -and $env:PROCESSOR_ARCHITECTURE -eq 'AMD64'
            BootTimeUtc = $boot.ToUniversalTime().ToString('o')
            InterruptTimeSeconds = [math]::Floor($ticks / 10000000)
            GuestFreeGiB = [math]::Round($capacity.AvailableFreeSpace / 1GB, 1)
            SharedWindowsDesktop10Present = $desktopRuntime
            WindowsInstallerPresent = $installerPresent
            InstallerProcessObserved = $activeInstallers -gt 0
            PackageRegistration = $registration
            PackageSourceHead = $source
        }
    }
    if (-not (Wait-Job -Job $job -Timeout 35) -or $job.State -ne 'Completed') {
        throw 'The bounded guest preflight did not complete.'
    }
    try { $guestFacts = @(Receive-Job -Job $job) }
    catch { throw 'The guest preflight did not return bounded observations.' }
    if ($guestFacts.Count -ne 1 -or -not $guestFacts[0].NativeWindows11X64 -or
        [int]$admission.GuestBuild -ne [int]($guestFacts[0].WindowsBuild.Split('.')[0])) {
        throw 'Guest platform observations do not match the admitted Windows 11 x64 guest.'
    }
    $null = Confirm-Target
    [pscustomobject]@{
        Task = 'TL-0410'
        ObservedAtUtc = [DateTime]::UtcNow.ToString('o')
        DedicatedVM = $true
        CleanCheckpointMatched = $true
        UacPolicy = $admission.UacPolicy
        Guest = $guestFacts[0] | Select-Object * -ExcludeProperty PSComputerName, RunspaceId, PSShowComputerName
    }
}
finally {
    if ($job) { Remove-Job -Job $job -Force -ErrorAction SilentlyContinue }
    if ($ownsLease) { $lease.ReleaseMutex() }
    $lease.Dispose()
}
