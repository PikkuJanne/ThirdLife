#Requires -Version 5.1
param(
    [ValidateSet('Inspect','Initialize','Provision','Start','Status','Cleanup','RunTest')]
    [string] $Mode,
    [string] $RunId,
    [string] $Computer,
    [string] $Account,
    [string] $Scenario,
    [string] $TestName,
    [string] $BrokerHash,
    [string] $ProvisionerHash,
    [string] $SdkHash,
    [string] $PayloadHash,
    [string] $DriverHash,
    [int] $ConsoleSession = -1,
    [string] $TestSource
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$admins = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$users = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    $guestSid = $identity.User
    $guestIdentity = $identity.Name
    $isAdmin = [Security.Principal.WindowsPrincipal]::new($identity).IsInRole($admins)
}
finally { $identity.Dispose() }
$os = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$bios = Get-ItemProperty 'HKLM:\HARDWARE\DESCRIPTION\System\BIOS'
if ($Computer -cne 'ThirdLife-W11' -or $Account -cne 'ThirdLifeAdmin' -or
    $env:COMPUTERNAME -ine $Computer -or $guestIdentity -ine "$Computer\$Account" -or
    $os.InstallationType -ne 'Client' -or [int]$os.CurrentBuildNumber -lt 22000 -or
    $env:PROCESSOR_ARCHITECTURE -ne 'AMD64' -or $bios.SystemManufacturer -ne 'Microsoft Corporation' -or
    $bios.SystemProductName -ne 'Virtual Machine') {
    throw 'Guest identity or Windows 11 native x64 Hyper-V admission failed.'
}

function Get-ConsoleSession {
    if (-not ('ThirdLife.Verification.ConsoleSession' -as [type])) {
        Add-Type -Namespace ThirdLife.Verification -Name ConsoleSession -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll")]
public static extern uint WTSGetActiveConsoleSessionId();
'@
    }
    [ThirdLife.Verification.ConsoleSession]::WTSGetActiveConsoleSessionId()
}

if ($Mode -ne 'RunTest' -and -not $isAdmin) {
    throw 'The authenticated PowerShell Direct session needs the guest administrator token.'
}
if ($Mode -eq 'Inspect') {
    $active = Get-ConsoleSession
    $shells = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" -OperationTimeoutSec 5 |
        Where-Object SessionId -eq $active | ForEach-Object { Invoke-CimMethod -InputObject $_ -MethodName GetOwner })
    if ($active -eq 0 -or $active -eq [uint32]::MaxValue -or
        -not ($shells | Where-Object { $_.ReturnValue -eq 0 -and $_.User -ieq $Account -and $_.Domain -ieq $Computer })) {
        throw 'The expected guest account has no active basic-console desktop.'
    }
    $policyPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
    $enableLua = Get-ItemPropertyValue -LiteralPath $policyPath -Name EnableLUA -ErrorAction SilentlyContinue
    $behavior = Get-ItemPropertyValue -LiteralPath $policyPath -Name ConsentPromptBehaviorAdmin -ErrorAction SilentlyContinue
    $secureDesktop = Get-ItemPropertyValue -LiteralPath $policyPath -Name PromptOnSecureDesktop -ErrorAction SilentlyContinue
    [pscustomobject]@{
        Computer = $env:COMPUTERNAME
        Build = $os.CurrentBuildNumber
        ConsoleSession = $active
        EnableLUA = $(if ($enableLua -is [int] -and $enableLua -in @(0, 1)) { $enableLua })
        ConsentPromptBehaviorAdmin = $(if ($behavior -is [int] -and $behavior -ge 0 -and $behavior -le 5) { $behavior })
        PromptOnSecureDesktop = $(if ($secureDesktop -is [int] -and $secureDesktop -in @(0, 1)) { $secureDesktop })
    }
    return
}

if ($RunId -cnotmatch '\A[0-9a-f]{32}\z') { throw 'Invalid run identifier.' }
if ($Mode -in @('Start','Status','RunTest') -and
    ($Scenario -cnotin @('Verify','DistinctAccount','Accept','Decline','Interrupt','Timeout','Disconnect','RetainObservation',
        'ApplicationPreview','ApplicationAccept','ApplicationDecline','ApplicationCancel') -or
        $TestName -cnotmatch '\AThirdLife\.(?:Broker|UI)\.Tests\.[A-Za-z][A-Za-z0-9.]+\z')) {
    throw 'Invalid qualification scenario or test selection.'
}
$applicationTests = @{
    ApplicationPreview = 'ProtectedNoOpApplicationPreviewsWithoutApproval'
    ApplicationAccept = 'ProtectedNoOpApplicationAcceptsWithSavedApproval'
    ApplicationDecline = 'ProtectedNoOpApplicationDeclinesWithSavedApproval'
    ApplicationCancel = 'ProtectedNoOpApplicationCancelsBeforeDispatch'
}
$applicationScenario = $Scenario -in $applicationTests.Keys
if ($applicationScenario -and $Mode -in @('Start','Status','RunTest') -and
    $TestName -cne ('ThirdLife.UI.Tests.PlanReviewTests.' + $applicationTests[$Scenario])) {
    throw 'The test selection does not match the application case.'
}
$evidenceTask = if ($applicationScenario) { 'TL-0313' } else { 'TL-0317' }
$programFiles = [Environment]::GetFolderPath('ProgramFiles')
$programFilesItem = Get-Item -LiteralPath $programFiles -Force
if (-not $programFilesItem.PSIsContainer -or ($programFilesItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
    [IO.Path]::GetPathRoot($programFiles) -ine [IO.Path]::GetPathRoot([Environment]::SystemDirectory)) {
    throw 'Qualification requires the native Program Files directory on the Windows system drive.'
}
$root = Join-Path $programFiles 'ThirdLifeVerification'
$stage = Join-Path $root $RunId
$results = Join-Path $stage 'result'
$tracePath = Join-Path $results 'trace.jsonl'
$requestPath = Join-Path $stage 'run-request.json'
$taskName = "ThirdLife-$($evidenceTask.Replace('-', ''))-$RunId"
$installedBroker = Join-Path $programFiles 'ThirdLife\Broker\ThirdLife.Broker.exe'
$sdkRoot = Join-Path $stage 'sdk'
$sdk = Join-Path $sdkRoot 'dotnet.exe'
$uiTestHost = Join-Path $stage 'payload\ThirdLife.UI.Tests\testhost.exe'
$powershell = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'

function New-ProtectedDirectory([string] $Path, [bool] $WritableResults = $false) {
    $security = [Security.AccessControl.DirectorySecurity]::new()
    $security.SetOwner($admins)
    $security.SetAccessRuleProtection($true, $false)
    foreach ($sid in @($admins, $system, $users)) {
        $rights = if ($sid -eq $users) { 'ReadAndExecute' } else { 'FullControl' }
        $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $sid, $rights, 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    }
    if ($WritableResults) {
        $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $guestSid, 'ReadAndExecute,CreateFiles,CreateDirectories', 'None', 'None', 'Allow'))
        $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $guestSid, 'Modify', 'ContainerInherit,ObjectInherit', 'InheritOnly', 'Allow'))
    }
    [void][IO.Directory]::CreateDirectory($Path, $security)
}

function Assert-ProtectedDirectory([string] $Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Qualification staging is missing its protected directory boundary.'
    }
    $acl = Get-Acl -LiteralPath $Path
    $descriptor = [Security.AccessControl.RawSecurityDescriptor]::new($acl.GetSecurityDescriptorBinaryForm(), 0)
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]) -notin @($admins, $system) -or
        -not $acl.AreAccessRulesProtected -or -not $acl.AreAccessRulesCanonical -or
        $null -eq $descriptor.DiscretionaryAcl -or $descriptor.DiscretionaryAcl.Count -eq 0) {
        throw 'Qualification staging is missing its protected directory boundary.'
    }
    $writeMask = 0x500D0156
    foreach ($entry in $descriptor.DiscretionaryAcl) {
        if ($entry -isnot [Security.AccessControl.CommonAce] -or $entry.IsCallback -or
            ($entry.AceQualifier -eq 'AccessAllowed' -and $entry.SecurityIdentifier -notin @($admins, $system) -and
                ($entry.AccessMask -band $writeMask))) {
            throw 'Qualification staging is missing its protected directory boundary.'
        }
    }
}

function Read-VerifiedScript([string] $Path, [string] $ExpectedHash) {
    $stream = [IO.File]::Open($Path, 'Open', 'Read', 'Read')
    try {
        if ($stream.Length -lt 1 -or $stream.Length -gt 64KB) { throw 'Unexpected script size.' }
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $sha.Dispose() }
        if ($actual -ine $ExpectedHash) { throw 'Transferred script hash differs from the reviewed input.' }
        $stream.Position = 0
        $reader = [IO.StreamReader]::new($stream)
        try { [scriptblock]::Create($reader.ReadToEnd()) }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Get-ResultFile([string] $Name) {
    $directory = Get-Item -LiteralPath $results -Force -ErrorAction SilentlyContinue
    if (-not $directory) { return }
    if (-not $directory.PSIsContainer -or ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'The result directory was redirected or replaced.'
    }
    $item = Get-Item -LiteralPath (Join-Path $results $Name) -Force -ErrorAction SilentlyContinue
    if (-not $item) { return }
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Length -gt 128KB) {
        throw 'A result file was redirected, has the wrong object type, or exceeded its size limit.'
    }
    $item.FullName
}

function Get-ApplicationObservations([object[]] $Lines) {
    $allowed = @('CandidateReady', 'Result')
    if ($Scenario -cne 'ApplicationPreview') {
        $allowed += @('ApprovalSaved', 'RequestingConsent')
        if ($Scenario -ceq 'ApplicationCancel') { $allowed += 'AwaitingCancellation' }
    }
    foreach ($line in $Lines) {
        if ($line.stage -isnot [string] -or $line.stage -cnotin $allowed -or
            $line.scenario -cne $Scenario.ToLowerInvariant()) {
            throw 'An unexpected application qualification stage was reported.'
        }
        $entry = [ordered]@{ stage = $line.stage; scenario = $Scenario.ToLowerInvariant() }
        $timestamp = $line.PSObject.Properties['timestampUtc']
        if ($timestamp -and $null -ne $timestamp.Value) {
            $utc = [DateTimeOffset]::MinValue
            if ($timestamp.Value -is [DateTime]) { $utc = [DateTimeOffset]$timestamp.Value }
            elseif ($timestamp.Value -isnot [string] -or $timestamp.Value.Length -gt 40 -or
                -not [DateTimeOffset]::TryParse($timestamp.Value, [Globalization.CultureInfo]::InvariantCulture,
                    [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$utc)) {
                throw 'An application qualification timestamp was invalid.'
            }
            $entry.timestampUtc = $utc.UtcDateTime.ToString('o')
        }
        foreach ($field in @('clientUnelevated', 'reopened')) {
            $value = $line.PSObject.Properties[$field]
            if ($value -and $value.Value -is [bool]) { $entry[$field] = $value.Value }
        }
        if ($line.stage -ceq 'Result') {
            if ($line.status -isnot [string] -or
                $line.status -cnotin @('Previewed', 'Completed', 'Declined', 'Cancelled') -or
                ($line.terminalCount -isnot [int] -and $line.terminalCount -isnot [long]) -or
                $line.terminalCount -notin @(0, 3)) {
                throw 'An unexpected application result was reported.'
            }
            $entry.status = $line.status
            $entry.terminalCount = [int]$line.terminalCount
        }
        [pscustomobject]$entry
    }
}

function Initialize-PrivateDotNet([string] $CliHome) {
    Get-ChildItem Env: | Where-Object Name -match '^(DOTNET_|CORECLR_|COR_|COMPLUS_|VSTEST_|MSBUILD)' |
        ForEach-Object { Remove-Item -LiteralPath ('Env:' + $_.Name) }
    $env:DOTNET_CLI_HOME = $CliHome
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_ROLL_FORWARD = 'LatestPatch'
    $env:DOTNET_ROOT = $sdkRoot
    $env:DOTNET_ROOT_X64 = $sdkRoot
}

if ($Mode -eq 'Initialize') {
    if (Test-Path -LiteralPath $stage) { throw 'The selected run directory already exists.' }
    if (-not (Test-Path -LiteralPath $root)) { New-ProtectedDirectory $root }
    Assert-ProtectedDirectory $root
    $disk = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($stage))
    if ($disk.DriveType -ne 'Fixed' -or $disk.AvailableFreeSpace -lt 2GB) {
        throw 'A fixed guest volume with at least 2 GiB free is required.'
    }
    New-ProtectedDirectory $stage
    Assert-ProtectedDirectory $stage
    return $stage
}

Assert-ProtectedDirectory $root
Assert-ProtectedDirectory $stage

switch ($Mode) {
    'Provision' {
        if ($TestSource -cnotmatch '\A[0-9a-f]{40}\z') { throw 'A source commit is required for the staged tests.' }
        foreach ($archive in @(@{ Name = 'payload.zip'; Hash = $PayloadHash; Algorithm = 'SHA256'; Destination = 'payload' },
            @{ Name = 'sdk.zip'; Hash = $SdkHash; Algorithm = 'SHA512'; Destination = 'sdk' })) {
            $file = Join-Path $stage $archive.Name
            if ((Get-FileHash -LiteralPath $file -Algorithm $archive.Algorithm).Hash -ine $archive.Hash) {
                throw 'A transferred archive did not match its expected digest.'
            }
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [IO.Compression.ZipFile]::ExtractToDirectory($file, (Join-Path $stage $archive.Destination))
            [IO.File]::Delete($file)
        }
        if ([IO.File]::ReadAllText((Join-Path $stage 'payload\source.txt')).Trim() -cne $TestSource) {
            throw 'The staged test archive is bound to a different source commit.'
        }
        $provisioner = Read-VerifiedScript (Join-Path $stage 'payload\install-thirdlife-broker.ps1') $ProvisionerHash
        $candidate = Join-Path $stage 'payload\ThirdLife.Broker.exe'
        if ((Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -ine $BrokerHash) {
            throw 'The guest candidate differs from the reviewed Native AOT digest.'
        }
        Push-Location -LiteralPath ([Environment]::SystemDirectory)
        try {
            if (-not $applicationScenario) {
                & $provisioner -BrokerArtifact $candidate -ExpectedSha256 $BrokerHash | Out-Null
            }
            & $provisioner -VerifyOnly -ExpectedSha256 $BrokerHash | Out-Null
        }
        finally { Pop-Location }
        Initialize-PrivateDotNet $stage
        $installedSdks = @(& $sdk --list-sdks)
        if ($LASTEXITCODE -ne 0 -or $installedSdks.Count -ne 1 -or $installedSdks[0] -notmatch '^10\.0\.400 \[') {
            throw 'The private guest SDK failed to start at the pinned version.'
        }
        $runtimes = @(& $sdk --list-runtimes)
        if ($LASTEXITCODE -ne 0 -or -not ($runtimes -match '^Microsoft\.NETCore\.App 10\.0\.[0-9]+ \[') -or
            -not ($runtimes -match '^Microsoft\.WindowsDesktop\.App 10\.0\.[0-9]+ \[')) {
            throw 'The pinned guest SDK does not contain the required .NET and Windows Desktop 10.0 runtimes.'
        }
        [pscustomobject]@{ ProtectedBrokerVerified = $true; PrivateSdkVerified = $true }
    }
    'Start' {
        if (Get-Process -Name 'ThirdLife.Broker' -ErrorAction SilentlyContinue) {
            throw 'A broker process was present before the scenario started.'
        }
        if ($ConsoleSession -le 0 -or $ConsoleSession -ne (Get-ConsoleSession)) { throw 'The basic-console session changed.' }
        if ($Scenario -notin @('Verify','DistinctAccount') -and
            (Get-Process -Name 'consent' -ErrorAction SilentlyContinue | Where-Object SessionId -eq $ConsoleSession)) {
            throw 'A Windows consent process was present before the scenario started.'
        }
        if (Test-Path -LiteralPath $results) { throw 'The qualification scenario already has a result directory.' }
        New-ProtectedDirectory $results $true
        [IO.File]::WriteAllText($requestPath, (@{ task_id = $evidenceTask; scenario = $Scenario; run_id = $RunId } | ConvertTo-Json -Compress))
        $acl = Get-Acl -LiteralPath $requestPath
        $acl.SetOwner($admins)
        Set-Acl -LiteralPath $requestPath -AclObject $acl
        [IO.File]::SetAttributes($requestPath, [IO.FileAttributes]::ReadOnly)
        $driver = Join-Path $stage 'payload\run-thirdlife-hyperv-guest.ps1'
        $null = Read-VerifiedScript $driver $DriverHash
        if ($BrokerHash -cnotmatch '\A[0-9a-f]{64}\z' -or $DriverHash -cnotmatch '\A[0-9a-f]{64}\z') {
            throw 'The reviewed broker and guest driver SHA-256 digests are required.'
        }
        $quotedDriver = $driver.Replace("'", "''")
        # Use the task's interactive token and verify the fixed staged script before running it.
        $command = @"
`$ErrorActionPreference = 'Stop'
`$driver = '$quotedDriver'
if ((Get-FileHash -LiteralPath `$driver -Algorithm SHA256).Hash -ine '$DriverHash') { exit 90 }
& ([scriptblock]::Create([IO.File]::ReadAllText(`$driver))) -Mode RunTest -RunId '$RunId' -Computer '$Computer' -Account '$Account' -Scenario '$Scenario' -TestName '$TestName' -BrokerHash '$BrokerHash' -ConsoleSession $ConsoleSession
exit `$LASTEXITCODE
"@
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
        $action = New-ScheduledTaskAction -Execute $powershell -Argument "-NoProfile -NonInteractive -EncodedCommand $encoded" -WorkingDirectory $stage
        $level = if ($Scenario -eq 'DistinctAccount') { 'Highest' } else { 'Limited' }
        $principal = New-ScheduledTaskPrincipal -UserId "$Computer\$Account" -LogonType Interactive -RunLevel $level
        $taskMinutes = if ($applicationScenario -and $Scenario -cne 'ApplicationPreview') { 6 } else { 3 }
        $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::FromMinutes($taskMinutes)) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
        Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings | Out-Null
        Start-ScheduledTask -TaskName $taskName
    }
    'RunTest' {
        if ($isAdmin -ne ($Scenario -eq 'DistinctAccount') -or
            [Diagnostics.Process]::GetCurrentProcess().SessionId -ne $ConsoleSession -or
            $ConsoleSession -ne (Get-ConsoleSession)) {
            throw 'The scheduled test did not receive its required token and basic-console session.'
        }
        Initialize-PrivateDotNet $results
        $env:THIRDLIFE_TL0311_REVIEWED_BROKER_SHA256 = $BrokerHash
        $env:THIRDLIFE_TL0311_CONSENT_REHEARSAL = $Scenario.ToLowerInvariant()
        $env:THIRDLIFE_TL0317_TRACE_PATH = $tracePath
        if ($Scenario -eq 'DistinctAccount') { $env:THIRDLIFE_HYPERV_BROKER_ACL_RUN_ID = $RunId }
        if ($applicationScenario) { $env:THIRDLIFE_TL0313_HYPERV_RUN_ID = $RunId }
        $assembly = if ($Scenario -eq 'DistinctAccount') { 'ThirdLife.Broker.Tests' } else { 'ThirdLife.UI.Tests' }
        $test = Join-Path $stage "payload\$assembly\$assembly.dll"
        & $sdk vstest $test "/TestCaseFilter:FullyQualifiedName=$TestName" `
            '/Logger:trx;LogFileName=result.trx' "/ResultsDirectory:$results" -- RunConfiguration.TreatNoTestsAsError=true
        [IO.File]::WriteAllText((Join-Path $results 'exit.txt'), [string]$LASTEXITCODE)
    }
    'Status' {
        $state = (Get-ScheduledTask -TaskName $taskName).State
        $trace = Get-ResultFile 'trace.jsonl'
        $lines = @()
        if ($trace) {
            $stream = [IO.File]::Open($trace, 'Open', 'Read', 'ReadWrite')
            try {
                if ($stream.Length -gt 128KB) { throw 'The observation trace exceeded its size limit.' }
                $bytes = [byte[]]::new([int]$stream.Length)
                $read = $stream.Read($bytes, 0, $bytes.Length)
                $content = [Text.Encoding]::UTF8.GetString($bytes, 0, $read)
            }
            finally { $stream.Dispose() }
            $lastLine = $content.LastIndexOf("`n")
            if ($lastLine -ge 0) {
                $rows = $content.Substring(0, $lastLine).Split("`n")
                if ($applicationScenario -and $rows.Count -gt 12) { throw 'Too many application observations.' }
                $lines = @($rows | Select-Object -First 12 | ForEach-Object { ConvertFrom-Json $_ })
            }
        }
        $applicationPlan = $null
        $observations = if ($applicationScenario) { @(Get-ApplicationObservations $lines) }
            else { @($lines | Select-Object stage, scenario, timestampUtc, status, terminalCount, clientUnelevated, exitCode) }
        if ($applicationScenario) {
            $candidates = @($lines | Where-Object stage -ceq 'CandidateReady')
            if ($candidates.Count -gt 1) { throw 'The application reported multiple candidate plans.' }
            if ($candidates.Count -eq 1) {
                $candidate = $candidates[0]
                $capabilities = @('document_editing', 'pdf_reading', 'web_browsing')
                if ($candidate.scenario -cne $Scenario.ToLowerInvariant() -or
                    $candidate.clientUnelevated -isnot [bool] -or -not $candidate.clientUnelevated -or
                    $candidate.actionCount -ne 3 -or $candidate.capabilities -isnot [array] -or
                    $candidate.capabilities.Count -ne 3 -or
                    (($candidate.capabilities -join ',') -cne ($capabilities -join ',')) -or
                    $candidate.planSha256 -isnot [string] -or $candidate.planSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
                    $candidate.impactSha256 -isnot [string] -or $candidate.impactSha256 -cnotmatch '\A[0-9a-f]{64}\z') {
                    throw 'The application plan observation does not match the reviewed qualification format.'
                }
                $applicationPlan = [pscustomobject]@{
                    PlanSha256 = $candidate.planSha256
                    ImpactSha256 = $candidate.impactSha256
                    ActionCount = 3
                    Capabilities = $capabilities
                    ClientUnelevated = $true
                }
            }
            $approvals = @($lines | Where-Object stage -ceq 'ApprovalSaved')
            if ($approvals.Count -gt 1 -or ($approvals.Count -eq 1 -and
                (-not $applicationPlan -or
                    $approvals[0].planSha256 -cne $applicationPlan.PlanSha256 -or
                    $approvals[0].impactSha256 -cne $applicationPlan.ImpactSha256))) {
                throw 'The saved approval does not match the candidate plan and impact digests.'
            }
        }
        if ($Scenario -eq 'Interrupt') {
            $target = $lines | Where-Object { $_.stage -eq 'AwaitingTermination' -and $_.scenario -eq 'interrupt' } | Select-Object -First 1
            if ($target -and $state -eq 'Running' -and $target.sessionId -eq $ConsoleSession) {
                $clientId = [int]$target.clientProcessId
                $candidate = Get-Process -Id ([int]$target.brokerProcessId) -ErrorAction SilentlyContinue
                $client = Get-Process -Id $clientId -ErrorAction SilentlyContinue
                $started = [DateTimeOffset]::Parse([string]$target.brokerStartedUtc, [Globalization.CultureInfo]::InvariantCulture).UtcDateTime
                if ($candidate -and $client -and $candidate.Path -ieq $installedBroker -and
                    $candidate.SessionId -eq $ConsoleSession -and $client.SessionId -eq $ConsoleSession -and
                    $client.Path -iin @($sdk, $uiTestHost) -and $candidate.StartTime.ToUniversalTime() -eq $started -and
                    $candidate.StartTime -ge $client.StartTime -and ([DateTime]::UtcNow - $started).TotalSeconds -lt 90) {
                    $brokerProcess = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId=$($candidate.Id)" -OperationTimeoutSec 3
                    $ticket = '\s--thirdlife-broker-v1\s[0-9a-f-]{36}\s' + $clientId + '\s'
                    if ($brokerProcess -and $brokerProcess.CommandLine -cmatch $ticket -and -not $candidate.HasExited) {
                        $candidate.Kill()
                        if (-not $candidate.WaitForExit(5000)) { throw 'The owned broker did not exit after interruption.' }
                    }
                }
            }
        }
        $exitFile = Get-ResultFile 'exit.txt'
        $trx = Get-ResultFile 'result.trx'
        $testExit = $null
        if ($exitFile) {
            $code = 0
            if ([int]::TryParse([IO.File]::ReadAllText($exitFile), [ref]$code)) { $testExit = $code }
        }
        $taskLastResult = $null
        if ($state -notin @('Running', 'Queued')) {
            $taskLastResult = [uint32](Get-ScheduledTaskInfo -TaskName $taskName).LastTaskResult
        }
        $passed = $false
        $testFailure = $null
        $testNativeErrorCode = $null
        $classifyFailure = $applicationScenario -or $Scenario -cin @('DistinctAccount', 'RetainObservation')
        if ($trx -and $null -ne $testExit) {
            $xmlSettings = [Xml.XmlReaderSettings]::new()
            $xmlSettings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
            $xmlSettings.XmlResolver = $null
            $xmlSettings.MaxCharactersInDocument = 128KB
            $reader = [Xml.XmlReader]::Create($trx, $xmlSettings)
            try {
                $document = [Xml.XmlDocument]::new()
                $document.Load($reader)
                $case = $document.SelectNodes("//*[local-name()='UnitTestResult']")
                if ($case.Count -eq 1 -and $case[0].GetAttribute('testName') -ceq $TestName) {
                    $passed = $testExit -eq 0 -and $case[0].GetAttribute('outcome') -ceq 'Passed'
                    if (-not $passed -and $classifyFailure) {
                        # Only fixed classification tokens leave the guest. TRX can contain local paths and identities.
                        $messageNode = $case[0].SelectSingleNode(".//*[local-name()='ErrorInfo']/*[local-name()='Message']")
                        $message = if ($messageNode) { $messageNode.InnerText } else { '' }
                        if ($applicationScenario) {
                            $testFailure = switch -Regex ($message) {
                                'OperationCanceledException|TaskCanceledException|TimeoutException' { 'CaseDeadlineElapsed'; break }
                                default { 'ApplicationFixtureAssertionFailed' }
                            }
                        }
                        elseif ($Scenario -ceq 'RetainObservation') {
                            $testFailure = switch -Regex ($message) {
                                'Expected:\s*typeof\(System\.Diagnostics\.Process\)' { 'BrokerProcessUnavailable'; break }
                                'could not be authenticated|Elevated process changed the initiating user' { 'BrokerPeerRejected'; break }
                                'No process is associated with this object|Process has exited|Process with an Id of .+ is not running' { 'BrokerExitObservationUnavailable'; break }
                                'System\.ComponentModel\.Win32Exception' { 'BrokerProcessObservationRejected'; break }
                                'System\.TimeoutException' { 'BrokerExitDeadlineElapsed'; break }
                                'OperationCanceledException|TaskCanceledException' { 'CaseDeadlineElapsed'; break }
                                'Expected:\s*RequiresReview' { 'RecoveryStateUnexpected'; break }
                                'Assert\.Single\(\) Failure' { 'RetainedObservationCountUnexpected'; break }
                                'Expected:\s*Applied' { 'RetainedObservationStateUnexpected'; break }
                                default { 'FixtureAssertionFailed' }
                            }
                        }
                        else {
                            $stackNode = $case[0].SelectSingleNode(".//*[local-name()='ErrorInfo']/*[local-name()='StackTrace']")
                            $insidePipeProbe = $stackNode -and $stackNode.InnerText.Contains('CheckHyperVDistinctStandardAccount')
                            $testFailure = switch -Regex ($message) {
                                'administrative ThirdLife Hyper-V guest token is required' { 'GuestTokenRejected'; break }
                                'Expected:\s*Supported' { 'GuestPlatformRejected'; break }
                                'Hyper-V guest firmware identity is required' { 'GuestFirmwareRejected'; break }
                                'scenario inputs must be owned by Administrators or SYSTEM' { 'RequestOwnerRejected'; break }
                                'scenario input ACL grants write access' { 'RequestAclRejected'; break }
                                'protected Hyper-V request must admit' { 'RequestMetadataRejected'; break }
                                'Expected:\s*typeof\(System\.UnauthorizedAccessException\)' {
                                    if (-not $insidePipeProbe) { 'RequestWriteDenialMissing' }
                                    elseif ($message.Contains('No exception was thrown')) { 'UnexpectedProbeConnection' }
                                    else { 'ProbeDidNotReportAccessDenied' }
                                    break
                                }
                                'disposable guest could not create its standard test account' { 'AccountCreationRejected'; break }
                                'owned guest test account was not fully removed' { 'AccountCleanupFailed'; break }
                                'The Hyper-V probe could not authenticate its owned standard account\. NativeErrorCode=([0-9]{1,10})\.' {
                                    $nativeCode = 0
                                    if ([int]::TryParse($Matches[1], [ref]$nativeCode)) { $testNativeErrorCode = $nativeCode }
                                    'ProbeLogonRejected'
                                    break
                                }
                                'The Hyper-V probe did not receive its distinct standard account token' { 'ProbeTokenRejected'; break }
                                'The owned guest probe did not start\. NativeErrorCode=([0-9]{1,10})\.' {
                                    $nativeCode = 0
                                    if ([int]::TryParse($Matches[1], [ref]$nativeCode)) { $testNativeErrorCode = $nativeCode }
                                    'ProbeStartRejected'
                                    break
                                }
                                'owned guest probe did not start|System\.ComponentModel\.Win32Exception' { 'ProbeStartRejected'; break }
                                'Actual:\s*21\b' { 'ProbeTokenRejected'; break }
                                'Actual:\s*22\b|restricted pipe accepted a connection' { 'UnexpectedProbeConnection'; break }
                                'Actual:\s*23\b' { 'ProbeDidNotReportAccessDenied'; break }
                                'Actual:\s*24\b' { 'ProbeSetupFailed'; break }
                                'restricted pipe listener should still be awaiting' { 'ListenerStateUnexpected'; break }
                                'OperationCanceledException|TaskCanceledException' { 'CaseDeadlineElapsed'; break }
                                default { 'FixtureAssertionFailed' }
                            }
                        }
                    }
                }
                elseif ($classifyFailure) { $testFailure = 'TestResultSelectionUnexpected' }
            }
            finally { $reader.Dispose() }
        }
        elseif ($exitFile -and $classifyFailure) { $testFailure = 'TestResultMissing' }
        if ($passed -and $applicationScenario) {
            $expectedStatus = @{
                ApplicationPreview = 'Previewed'; ApplicationAccept = 'Completed'
                ApplicationDecline = 'Declined'; ApplicationCancel = 'Cancelled'
            }[$Scenario]
            $expectedTerminals = if ($Scenario -ceq 'ApplicationPreview') { 0 } else { 3 }
            $expectedStages = @('CandidateReady')
            if ($Scenario -cne 'ApplicationPreview') {
                $expectedStages += @('ApprovalSaved', 'RequestingConsent')
                if ($Scenario -ceq 'ApplicationCancel') { $expectedStages += 'AwaitingCancellation' }
            }
            $expectedStages += 'Result'
            $actualStages = @($observations | ForEach-Object stage)
            $applicationResults = @($observations | Where-Object stage -ceq 'Result')
            if (-not $applicationPlan -or ($actualStages -join ',') -cne ($expectedStages -join ',') -or
                $applicationResults.Count -ne 1 -or
                $applicationResults[0].scenario -cne $Scenario.ToLowerInvariant() -or
                $applicationResults[0].status -cne $expectedStatus -or
                $applicationResults[0].terminalCount -ne $expectedTerminals -or
                $applicationResults[0].clientUnelevated -isnot [bool] -or -not $applicationResults[0].clientUnelevated -or
                $applicationResults[0].reopened -isnot [bool] -or -not $applicationResults[0].reopened) {
                $passed = $false
                $testFailure = 'ApplicationResultUnexpected'
            }
        }
        [pscustomobject]@{
            State = [string]$state
            HasExit = [bool]$exitFile
            Passed = $passed
            TestFailure = $testFailure
            TestNativeErrorCode = $testNativeErrorCode
            TestExitCode = $testExit
            TaskLastResult = $taskLastResult
            BrokerRemaining = [bool](Get-Process -Name 'ThirdLife.Broker' -ErrorAction SilentlyContinue)
            ConsentProcess = [bool](Get-Process -Name 'consent' -ErrorAction SilentlyContinue | Where-Object SessionId -eq $ConsoleSession)
            ApplicationPlan = $applicationPlan
            Observations = @($observations)
        }
    }
    'Cleanup' {
        $scheduled = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        if ($scheduled) {
            $actions = @($scheduled.Actions)
            if ($actions.Count -ne 1 -or $actions[0].WorkingDirectory -ine $stage -or $actions[0].Execute -ine $powershell) {
                throw 'The scheduled task does not belong to the selected run.'
            }
            if ($scheduled.State -eq 'Running') {
                Stop-ScheduledTask -TaskName $taskName
                $wait = [Diagnostics.Stopwatch]::StartNew()
                do {
                    Start-Sleep -Milliseconds 200
                    $scheduled = Get-ScheduledTask -TaskName $taskName
                } while ($scheduled.State -eq 'Running' -and $wait.Elapsed.TotalSeconds -lt 5)
                if ($scheduled.State -eq 'Running') { throw 'The owned scenario task did not stop.' }
            }
            Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        }
        if ($Scenario -eq 'DistinctAccount') {
            $name = 'TLBroker' + $RunId.Substring(0, 12)
            $user = Get-LocalUser -Name $name -ErrorAction SilentlyContinue
            if ($user -and $user.Description -ceq "ThirdLife TL-0317 $RunId") {
                $ownedSid = $user.SID
                Remove-LocalUser -InputObject $user
                if (Get-LocalUser -SID $ownedSid -ErrorAction SilentlyContinue) {
                    throw 'The owned distinct-account fixture could not be removed.'
                }
            }
        }
        # Remove only the GUID-owned tree. Treat junctions and symlinks as leaf objects.
        function Remove-OwnedTree([string] $Path) {
            foreach ($child in [IO.Directory]::EnumerateFileSystemEntries($Path)) {
                $attributes = [IO.File]::GetAttributes($child)
                if ($attributes -band [IO.FileAttributes]::Directory) {
                    if ($attributes -band [IO.FileAttributes]::ReparsePoint) { [IO.Directory]::Delete($child) }
                    else { Remove-OwnedTree $child }
                }
                else {
                    if ($child -ieq $requestPath -and ($attributes -band [IO.FileAttributes]::ReadOnly) -and
                        -not ($attributes -band [IO.FileAttributes]::ReparsePoint)) {
                        [IO.File]::SetAttributes($child, ($attributes -band (-bnot [IO.FileAttributes]::ReadOnly)))
                    }
                    [IO.File]::Delete($child)
                }
            }
            [IO.Directory]::Delete($Path)
        }
        Remove-OwnedTree $stage
        if (Test-Path -LiteralPath $stage) { throw 'Guest run cleanup did not complete.' }
    }
}
