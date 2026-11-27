#Requires -Version 5.1
[CmdletBinding(DefaultParameterSetName = 'Run')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Prepare')][switch] $Prepare,
    [Parameter(ParameterSetName = 'Prepare')][switch] $WindowsUpdateAssessment,
    [Parameter(ParameterSetName = 'Prepare')][switch] $WindowsUpdateBrokerAssessment,
    [Parameter(ParameterSetName = 'Prepare')][switch] $WindowsUpdateBrokerRefusal,
    [Parameter(ParameterSetName = 'Prepare')][string] $BrokerArtifact =
        (Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'ThirdLife\Broker\ThirdLife.Broker.exe'),
    [Parameter(ParameterSetName = 'Prepare')][string] $SdkArchive,
    [Parameter(Mandatory, ParameterSetName = 'Run')]
    [ValidateSet('Inspect','Verify','DistinctAccount','Accept','Decline','Interrupt','Timeout','Disconnect','RetainObservation',
        'ApplicationPreview','ApplicationAccept','ApplicationDecline','ApplicationCancel','WindowsUpdateAssessment','WindowsUpdateBrokerAssessment','WindowsUpdateBrokerRefusal')]
    [string] $Scenario,
    [Parameter(ParameterSetName = 'Run')][pscredential] $Credential,
    [Parameter(ParameterSetName = 'Run')][switch] $PromptForCredential,
    [Parameter(ParameterSetName = 'Run')][guid] $ExpectedVMId = [guid]::Empty,
    [Parameter(ParameterSetName = 'Run')][ValidatePattern('\A[0-9a-fA-F]{64}\z')][string] $PayloadSha256,
    [Parameter(ParameterSetName = 'Run')][ValidatePattern('\A[0-9a-f]{64}\z')][string] $ReviewedWindowsUpdateBrokerSha256,
    [Parameter(ParameterSetName = 'Run')][ValidatePattern('\A[0-9a-f]{40}\z')][string] $ReviewedWindowsUpdateBrokerSource,
    [string] $WorkDirectory = (Join-Path $env:LOCALAPPDATA 'ThirdLife\HyperV')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$profile = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'thirdlife-hyperv-profile.json') -Raw | ConvertFrom-Json
if ($Scenario -and $Scenario -ine 'Inspect') {
    $scenarioEntry = $profile.scenarios.PSObject.Properties[$Scenario]
    if (-not $scenarioEntry) { throw 'The scenario is missing from the pinned profile.' }
    $Scenario = $scenarioEntry.Name
}
$applicationScenario = $Scenario -cin @('ApplicationPreview', 'ApplicationAccept', 'ApplicationDecline', 'ApplicationCancel')
if (@(@($WindowsUpdateAssessment,$WindowsUpdateBrokerAssessment,$WindowsUpdateBrokerRefusal) | Where-Object { $_ }).Count -gt 1) {
    throw 'Select one Windows Update qualification case.'
}
$updateRefusal = $WindowsUpdateBrokerRefusal -or $Scenario -ceq 'WindowsUpdateBrokerRefusal'
if ($updateRefusal -and -not $Prepare -and
    (-not $ReviewedWindowsUpdateBrokerSha256 -or -not $ReviewedWindowsUpdateBrokerSource -or $ReviewedWindowsUpdateBrokerSha256 -ieq $profile.brokerSha256)) {
    throw 'The refusal case requires the reviewed v2 artifact digest and source commit for the existing protected guest installation.'
}
if ((-not $updateRefusal) -and ($ReviewedWindowsUpdateBrokerSha256 -or $ReviewedWindowsUpdateBrokerSource)) {
    throw 'A separate artifact review applies only to the Windows Update refusal case.'
}
$brokerHash = if ($updateRefusal) { $ReviewedWindowsUpdateBrokerSha256 } else { $profile.brokerSha256 }
$brokerSource = if ($updateRefusal) { $ReviewedWindowsUpdateBrokerSource } else { $profile.brokerSource }
$nativeAssessment = $WindowsUpdateBrokerAssessment -or $Scenario -ceq 'WindowsUpdateBrokerAssessment'
$updateScenario = $WindowsUpdateAssessment -or $Scenario -ceq 'WindowsUpdateAssessment' -or $nativeAssessment
$requiresApplicationConsent = $applicationScenario -and $Scenario -cne 'ApplicationPreview'
$evidenceTask = if ($updateScenario -or $updateRefusal) { 'TL-0504' } elseif ($applicationScenario) { 'TL-0313' } else { $profile.task }
if (($applicationScenario -or (($updateScenario -or $updateRefusal) -and -not $Prepare)) -and $ExpectedVMId -eq [guid]::Empty) {
    throw 'Bind the selected case to the exact VMId returned by Inspect.'
}
$repo = Split-Path -Parent $PSScriptRoot
$driverPath = Join-Path $PSScriptRoot 'run-thirdlife-hyperv-guest.ps1'
$work = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WorkDirectory)
if ($work -ieq $repo -or $work.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Store Hyper-V bundles and local run output outside the repository.'
}
$id = [guid]::NewGuid().ToString('N')
$payloadName = if ($updateRefusal) { 'windows-update-broker-refusal.zip' } elseif ($nativeAssessment) { 'windows-update-broker-assessment.zip' }
    elseif ($updateScenario) { 'windows-update-assessment.zip' } else { 'payload.zip' }
$payload = Join-Path $work $payloadName
$sdk = Join-Path $work 'dotnet-sdk-win-x64.zip'

function Assert-Digest([string] $Path, [string] $Algorithm, [string] $Expected) {
    if ((Get-FileHash -LiteralPath $Path -Algorithm $Algorithm).Hash -ine $Expected) {
        throw "An input did not match its pinned $Algorithm digest."
    }
}

function Assert-LocalWorkDirectory {
    if ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($work)).DriveType -ne 'Fixed') {
        throw 'The host work directory must be on a fixed local volume.'
    }
    for ($parent = [IO.DirectoryInfo]::new($work); $parent; $parent = $parent.Parent) {
        if ($parent.Exists -and ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'The host work directory must not be redirected.'
        }
    }
}

Assert-LocalWorkDirectory

if ($Prepare) {
    $null = [IO.Directory]::CreateDirectory($work)
    $provisioner = Join-Path $PSScriptRoot 'install-thirdlife-broker.ps1'
    $projects = if ($updateRefusal) { 'ThirdLife.UI.Tests' } elseif ($nativeAssessment) { 'ThirdLife.Broker.Tests' }
        elseif ($updateScenario) { 'ThirdLife.UI.Tests' } else { 'ThirdLife.UI.Tests', 'ThirdLife.Broker.Tests' }
    if (-not $updateScenario) {
        if (-not $updateRefusal) { Assert-Digest $BrokerArtifact SHA256 $profile.brokerSha256 }
        Assert-Digest $provisioner SHA256 $profile.provisionerSha256
    }
    if (Test-Path -LiteralPath $sdk) {
        Assert-Digest $sdk SHA512 $profile.sdkSha512
    }
    else {
        $download = Join-Path $work "$id.sdk.part"
        try {
            if ($SdkArchive) { Copy-Item -LiteralPath $SdkArchive -Destination $download }
            else { Invoke-WebRequest -Uri $profile.sdkUri -OutFile $download -UseBasicParsing -TimeoutSec 180 }
            Assert-Digest $download SHA512 $profile.sdkSha512
            [IO.File]::Move($download, $sdk)
        }
        finally { if (Test-Path -LiteralPath $download) { [IO.File]::Delete($download) } }
    }
    $scratch = Join-Path $work $id
    $archive = Join-Path $work "$id.payload.part"
    $null = [IO.Directory]::CreateDirectory($scratch)
    Push-Location $repo
    try {
        $source = & git rev-parse HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the test source.' }
        $dirty = & git status --porcelain --untracked-files=normal
        if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Prepare from a committed clean worktree.' }
        if ((& dotnet --version) -ne $profile.sdkVersion -or $LASTEXITCODE -ne 0) { throw 'Wrong host .NET SDK.' }
        foreach ($project in $projects) {
            & dotnet build "tests/$project/$project.csproj" -c Release --artifacts-path (Join-Path $scratch 'build') `
                --nologo -p:RestoreLockedMode=true
            if ($LASTEXITCODE -ne 0) { throw "Could not build $project with locked inputs." }
        }
        $content = Join-Path $scratch 'payload'
        $null = [IO.Directory]::CreateDirectory($content)
        foreach ($project in $projects) {
            $build = Join-Path $scratch "build\bin\$project\release"
            if (-not (Test-Path -LiteralPath (Join-Path $build "$project.dll") -PathType Leaf)) {
                throw 'An expected test assembly was missing from the isolated host build.'
            }
            Copy-Item -LiteralPath $build -Destination (Join-Path $content $project) -Recurse
        }
        Copy-Item -LiteralPath $driverPath -Destination $content
        if (-not $updateScenario) {
            Copy-Item -LiteralPath $provisioner -Destination $content
            if (-not $updateRefusal) {
                Copy-Item -LiteralPath $BrokerArtifact -Destination (Join-Path $content 'ThirdLife.Broker.exe')
                Assert-Digest (Join-Path $content 'ThirdLife.Broker.exe') SHA256 $profile.brokerSha256
            }
        }
        [IO.File]::WriteAllText((Join-Path $content 'source.txt'), $source)
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($content, $archive)
        if (Test-Path -LiteralPath $payload) { [IO.File]::Replace($archive, $payload, [NullString]::Value) }
        else { [IO.File]::Move($archive, $payload) }
        [pscustomobject]@{ TestSource = $source; PayloadSha256 = (Get-FileHash -LiteralPath $payload).Hash.ToLowerInvariant() }
    }
    finally {
        Pop-Location
        if (Test-Path -LiteralPath $archive) { [IO.File]::Delete($archive) }
        if ($scratch -eq [IO.Path]::Combine($work, $id) -and [IO.Directory]::Exists($scratch)) {
            [IO.Directory]::Delete($scratch, $true)
        }
    }
    return
}

try { $vm = Get-VM -Name $profile.vm -ErrorAction Stop }
catch { throw 'Host access failed at Get-VM. Use an authorized Hyper-V host context for this runner.' }
if ($vm.State -ne 'Running' -or $vm.Generation -ne 2 -or $vm.Id -eq [guid]::Empty -or
    ($ExpectedVMId -ne [guid]::Empty -and $vm.Id -ne $ExpectedVMId)) {
    throw 'The configured running Generation 2 VM could not be bound to its expected identity.'
}
if (@(Get-VMSnapshot -VM $vm -Name $profile.baseline).Count -ne 1) {
    throw 'The configured clean Hyper-V checkpoint could not be verified.'
}
if ($Credential -and $PromptForCredential) { throw 'Choose the supplied credential or one attended local prompt.' }
if ($PromptForCredential) {
    $Credential = Get-Credential -UserName "$($profile.computer)\$($profile.account)" -Message 'PowerShell Direct credential for the dedicated ThirdLife guest'
}
elseif (-not $Credential) {
    $credentialPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ThirdLife\Credentials\guest.credential.xml'
    if (Test-Path -LiteralPath $credentialPath -PathType Leaf) {
        try { $Credential = Import-Clixml -LiteralPath $credentialPath }
        catch { throw 'The stored guest credential is unavailable to this Windows user in the local DPAPI context.' }
    }
}
if ($Credential -isnot [pscredential] -or $Credential.UserName -ine "$($profile.computer)\$($profile.account)") {
    throw 'The expected local guest PSCredential is required. Use the stored DPAPI credential, -Credential, or an attended -PromptForCredential session.'
}

$driverBytes = [IO.File]::ReadAllBytes($driverPath)
$hash = [Security.Cryptography.SHA256]::Create()
try { $driverHash = [BitConverter]::ToString($hash.ComputeHash($driverBytes)).Replace('-', '').ToLowerInvariant() }
finally { $hash.Dispose() }
$guest = [scriptblock]::Create([Text.Encoding]::UTF8.GetString($driverBytes).TrimStart([char]0xFEFF))
$direct = [PowerShell]::Create()
$session = $null
$lease = [Threading.Mutex]::new($false, ('ThirdLifeVerification-' + $vm.Id.ToString('N')))
$ownsLease = $false
$staged = $false
$cleaned = $false
$identity = $null
$guestBuild = $null
$uacPolicy = $null
$consoleSession = -1
$testName = ''
$testSource = ''
$provisioned = $null
$observed = $null
$applicationPlan = $null
$announced = $false
$runStarted = [DateTime]::UtcNow
$failedPhase = 'Connection'

function Connect-Guest {
    $direct.Commands.Clear()
    $null = $direct.AddCommand('New-PSSession').AddParameter('VMId', $vm.Id).AddParameter('Credential', $Credential).
        AddParameter('ErrorAction', 'Stop')
    $pending = $direct.BeginInvoke()
    if (-not $pending.AsyncWaitHandle.WaitOne(20000)) {
        $null = $direct.BeginStop($null, $null)
        throw 'The PowerShell Direct connection did not authenticate within 20 seconds.'
    }
    $connections = $direct.EndInvoke($pending)
    if ($connections.Count -ne 1) { throw 'The PowerShell Direct endpoint did not return one authenticated session.' }
    $connections[0].PSObject.BaseObject
}

function Invoke-Guest([string] $Mode, [int] $TimeoutSeconds = 20) {
    $job = Invoke-Command -Session $session -ScriptBlock $guest -AsJob -ArgumentList @(
        $Mode, $id, $profile.computer, $profile.account, $Scenario, $testName, $brokerHash,
        $profile.provisionerSha256, $profile.sdkSha512, $PayloadSha256, $driverHash, $consoleSession, $testSource)
    try {
        if (-not (Wait-Job -Job $job -Timeout $TimeoutSeconds)) { throw "Guest phase $Mode exceeded its time limit." }
        Receive-Job -Job $job -ErrorAction Stop
        if ($job.State -ne 'Completed') { throw "Guest phase $Mode did not complete." }
    }
    finally { Remove-Job -Job $job -Force }
}

function Send-GuestArchive([string] $Source, [string] $Destination) {
    $copy = [PowerShell]::Create()
    try {
        $null = $copy.AddCommand('Copy-Item').AddParameter('ToSession', $session).AddParameter('LiteralPath', $Source).
            AddParameter('Destination', $Destination).AddParameter('ErrorAction', 'Stop')
        $pendingCopy = $copy.BeginInvoke()
        if (-not $pendingCopy.AsyncWaitHandle.WaitOne(120000)) {
            $null = $copy.BeginStop($null, $null)
            throw 'PowerShell Direct archive transfer exceeded 120 seconds.'
        }
        $null = $copy.EndInvoke($pendingCopy)
    }
    finally {
        if ($copy.InvocationStateInfo.State -ne 'Stopping') { $copy.Dispose() }
    }
}

try {
    try { $ownsLease = $lease.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $ownsLease = $true }
    if (-not $ownsLease) { throw 'A qualification run already owns this VM from this host session.' }
    $session = Connect-Guest
    $failedPhase = 'Guest admission'
    $identity = Invoke-Guest Inspect
    $consoleSession = $identity.ConsoleSession
    $guestBuild = $identity.Build
    $uacPolicy = $identity | Select-Object EnableLUA, ConsentPromptBehaviorAdmin, PromptOnSecureDesktop
    Write-Host "Admitted $($identity.Computer), Windows build $($identity.Build), with the expected basic-console account."
    if ($Scenario -eq 'Inspect') {
        [pscustomobject]@{ VMId = $vm.Id; GuestBuild = $identity.Build; CredentialVerified = $true; UacPolicy = $uacPolicy }
        return
    }
    if (($requiresApplicationConsent -or $updateRefusal) -and ($uacPolicy.EnableLUA -ne 1 -or
        $uacPolicy.PromptOnSecureDesktop -ne 1 -or $uacPolicy.ConsentPromptBehaviorAdmin -notin @(2, 5))) {
        throw 'The application case requires Windows consent on the secure desktop.'
    }
    $failedPhase = 'Bundle verification'
    if (-not $PayloadSha256) { throw 'Pass the exact PayloadSha256 printed by the trusted host preparation.' }
    Assert-Digest $payload SHA256 $PayloadSha256
    Assert-Digest $sdk SHA512 $profile.sdkSha512
    $testSource = & git -C $repo rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $testSource -cnotmatch '\A[0-9a-f]{40}\z') { throw 'Cannot identify the test source.' }
    if ($applicationScenario -or $updateScenario -or $updateRefusal) {
        $dirty = & git -C $repo status --porcelain --untracked-files=no
        if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Run the application case from the committed payload checkout.' }
    }
    $testName = [string]$scenarioEntry.Value
    if ($updateRefusal -and $testSource -cne $ReviewedWindowsUpdateBrokerSource) {
        throw 'The reviewed v2 artifact and protected refusal test must come from the same clean commit.'
    }
    if (-not $testName) { throw 'The scenario has no test in the pinned profile.' }

    $failedPhase = 'Guest stage'
    $stage = Invoke-Guest Initialize
    $staged = $true
    if ($stage -notmatch ('\A[A-Za-z]:\\Program Files\\ThirdLifeVerification\\' + $id + '\z')) {
        throw 'Guest staging did not resolve under the expected protected Program Files tree.'
    }
    Send-GuestArchive $payload (Join-Path $stage 'payload.zip')
    Send-GuestArchive $sdk (Join-Path $stage 'sdk.zip')
    $failedPhase = 'Guest provisioning'
    $provisioned = Invoke-Guest Provision 120
    if ((-not $updateScenario -and -not $provisioned.ProtectedBrokerVerified) -or -not $provisioned.PrivateSdkVerified) {
        throw 'Protected guest provisioning did not return its verification result.'
    }
    $failedPhase = 'Interactive scenario'
    Invoke-Guest Start
    $limit = [Diagnostics.Stopwatch]::StartNew()
    $timeLimit = if ($requiresApplicationConsent -or $updateScenario) { 300 } else { 160 }
    $cancelAnnounced = $false
    while ($limit.Elapsed.TotalSeconds -lt $timeLimit) {
        $observed = Invoke-Guest Status 10
        if ($applicationScenario -and -not $applicationPlan -and $observed.ApplicationPlan) {
            $applicationPlan = $observed.ApplicationPlan
            Write-Host "The application displays the exact three-action no-op batch for $Scenario."
            Write-Host ($applicationPlan.Capabilities -join ', ')
            Write-Host "Plan SHA-256 $($applicationPlan.PlanSha256)"
            Write-Host "Impact SHA-256 $($applicationPlan.ImpactSha256)"
            if ($requiresApplicationConsent) {
                Write-Host 'In the Basic guest console, review the batch, enter your operator code and choose Approve, then Run approved no-op.'
            }
        }
        if (-not $announced -and $observed.ConsentProcess) {
            Write-Host "Consent process observed for $Scenario in the admitted console. Verify the actual prompt in that console before acting."
            $announced = $true
            if ($requiresApplicationConsent) {
                $windowsChoice = if ($Scenario -ceq 'ApplicationDecline') { 'No' } else { 'Yes' }
                Write-Host "The operator selects $windowsChoice on the reviewed ThirdLife.Broker.exe prompt for $Scenario."
            }
        }
        if (($Scenario -ceq 'ApplicationPreview' -or $updateScenario) -and $announced) {
            throw 'Unexpected Windows consent during read-only qualification.'
        }
        if ($Scenario -ceq 'ApplicationCancel' -and -not $cancelAnnounced -and
            ($observed.Observations | Where-Object stage -ceq 'AwaitingCancellation')) {
            Write-Host 'The application fixture reached its authenticated pre-dispatch cancellation check.'
            $cancelAnnounced = $true
        }
        if ($observed.HasExit -and $observed.State -notin @('Running', 'Queued') -and
            (-not $applicationScenario -or -not $observed.BrokerRemaining)) { break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $observed.Passed -or $observed.State -in @('Running', 'Queued') -or $observed.BrokerRemaining -or
        ($updateRefusal -and -not $announced) -or
        ($applicationScenario -and (-not $applicationPlan -or $requiresApplicationConsent -ne $announced))) {
        throw 'The expected scenario was not verified with one passing case and no surviving broker.'
    }
    $failedPhase = ''
}
finally {
    try {
        if ($staged) {
            if ($session.State -ne 'Opened') {
                Remove-PSSession -Session $session -ErrorAction SilentlyContinue
                $session = Connect-Guest
            }
            Invoke-Guest Cleanup 45
            $cleaned = $true
        }
    }
    finally {
        if ($session) { Remove-PSSession -Session $session -ErrorAction SilentlyContinue }
        if ($direct.InvocationStateInfo.State -ne 'Stopping') { $direct.Dispose() }
        if ($ownsLease) { $lease.ReleaseMutex() }
        $lease.Dispose()
        if ($Scenario -ne 'Inspect') {
            $null = [IO.Directory]::CreateDirectory($work)
            $testResult = if (-not $failedPhase -and $cleaned) { 'passed' }
                elseif ($observed -and $observed.HasExit) { 'failed' } else { 'unobserved' }
            $consoleObservation = if ($updateScenario -or $Scenario -in @('Verify', 'DistinctAccount', 'ApplicationPreview')) { 'not_required' } else { 'collect_separately' }
            $observations = if ($observed) { @($observed.Observations) } else { @() }
            $testFailure = $testExitCode = $taskLastResult = $testNativeErrorCode = $null
            if ($observed) {
                $testFailure = $observed.TestFailure
                $testExitCode = $observed.TestExitCode
                $taskLastResult = $observed.TaskLastResult
                $testNativeErrorCode = $observed.TestNativeErrorCode
            }
            $record = [ordered]@{
                task = $evidenceTask; scenario = $Scenario; brokerSource = $brokerSource; testSource = $testSource
                brokerSha256 = $brokerHash; payloadSha256 = $PayloadSha256; sdkSha512 = $profile.sdkSha512
                durationSeconds = [Math]::Round(([DateTime]::UtcNow - $runStarted).TotalSeconds, 3)
                startedUtc = $runStarted.ToString('o'); stoppedAt = $failedPhase; cleanupVerified = $cleaned
                possibleGuestResidue = $staged -and -not $cleaned
                protectedBrokerVerified = [bool]($provisioned -and $provisioned.ProtectedBrokerVerified)
                guestBuild = $guestBuild; uacPolicy = $uacPolicy
                testResult = $testResult; consoleObservation = $consoleObservation; observations = @($observations)
                testFailure = $testFailure; testExitCode = $testExitCode; taskLastResult = $taskLastResult
                testNativeErrorCode = $testNativeErrorCode
            }
            if ($applicationScenario) {
                $record.applicationPlan = $applicationPlan
                $record.consentProcessObserved = $announced
                $record.brokerRemainingAtLastStatus = if ($observed) { $observed.BrokerRemaining } else { $null }
            }
            if ($updateRefusal) {
                $record.consentProcessObserved = $announced
                $record.brokerRemainingAtLastStatus = if ($observed) { $observed.BrokerRemaining } else { $null }
            }
            if ($updateScenario) {
                foreach ($field in @('brokerSource', 'brokerSha256', 'protectedBrokerVerified')) { $record.Remove($field) }
                if ($nativeAssessment) { $record.nativeBrokerAssessment = if ($observed) { $observed.UpdateAssessment } else { $null } }
                else { $record.updateAssessment = if ($observed) { $observed.UpdateAssessment } else { $null } }
                if ($provisioned) {
                    if ($nativeAssessment) { $record.managedBrokerAssemblySha256 = $provisioned.adapterSha256 }
                    else { $record.adapterSha256 = $provisioned.adapterSha256 }
                    $record.wuapiVersion = $provisioned.wuapiVersion
                    $record.windowsBuild = $provisioned.windowsBuild
                }
            }
            $outcome = Join-Path $work "$id.$Scenario.json"
            [IO.File]::WriteAllText($outcome, ($record | ConvertTo-Json -Depth 5))
            Write-Host "Local result: $outcome"
        }
    }
}
