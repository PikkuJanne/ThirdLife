#Requires -Version 7.2
[CmdletBinding(DefaultParameterSetName = 'Inspect')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Inspect')][switch] $Inspect,
    [Parameter(Mandatory, ParameterSetName = 'Preview')][switch] $Preview,
    [Parameter(Mandatory, ParameterSetName = 'ResumePreview')][switch] $ResumePreview,
    [Parameter(Mandatory, ParameterSetName = 'Install')][switch] $Install,
    [Parameter(Mandatory, ParameterSetName = 'Reopen')][switch] $Reopen,
    [Parameter(Mandatory, ParameterSetName = 'Preview')]
    [Parameter(Mandatory, ParameterSetName = 'Install')]
    [Parameter(ParameterSetName = 'ResumePreview')]
    [ValidatePattern('\A[A-Za-z0-9_.-]{1,64}\z')][string] $OperatorCode,
    [Parameter(Mandatory, ParameterSetName = 'Preview')]
    [Parameter(ParameterSetName = 'ResumePreview')]
    [ValidatePattern('\A[0-9]{4}-[0-9]{2}-[0-9]{2}\z')][string] $VerifiedNoDonorStorageOn,
    [Parameter(Mandatory, ParameterSetName = 'ResumePreview')]
    [Parameter(Mandatory, ParameterSetName = 'Install')]
    [Parameter(Mandatory, ParameterSetName = 'Reopen')]
    [ValidatePattern('\Ajob-[0-9a-f]{32}\z')][string] $Job,
    [Parameter(Mandatory, ParameterSetName = 'Reopen')]
    [Parameter(Mandatory, ParameterSetName = 'Install')]
    [ValidatePattern('\A[0-9a-f]{64}\z')][string] $ExpectedPlanSha256,
    [Parameter(Mandatory, ParameterSetName = 'Reopen')]
    [ValidateSet('Verified', 'RequiresReview')][string] $ExpectedRecovery,
    [Parameter(Mandatory)][guid] $ExpectedVMId,
    [Parameter(Mandatory)][guid] $ExpectedCheckpointId,
    [Parameter(Mandatory)][ValidatePattern('\A[0-9a-f]{64}\z')][string] $AppHostSha256,
    [Parameter(Mandatory)][ValidatePattern('\A[0-9a-f]{64}\z')][string] $UiSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($ResumePreview -and ([bool]$OperatorCode -xor [bool]$VerifiedNoDonorStorageOn)) {
    throw 'Supply both the original operator code and verification date to continue intake.'
}
$profile = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'thirdlife-hyperv-profile.json') -Raw | ConvertFrom-Json
$preflight = & (Join-Path $PSScriptRoot 'run-m4-package-preflight.ps1') -ExpectedVMId $ExpectedVMId -ExpectedCheckpointId $ExpectedCheckpointId
if ($null -eq $preflight) { throw 'The dedicated guest did not pass preflight.' }
if ($Install -and ($preflight.Guest.PackageRegistration -cne 'absent' -or
    -not $preflight.Guest.WindowsInstallerPresent -or $preflight.Guest.InstallerProcessObserved)) {
    throw 'The approved installation requires a confirmed absent package and an idle Windows Installer.'
}
$thirdLifeLocal = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ThirdLife'
$credential = Import-Clixml -LiteralPath (Join-Path $thirdLifeLocal 'Credentials\guest.credential.xml')
$sdk = Join-Path $thirdLifeLocal 'HyperV\dotnet-sdk-win-x64.zip'
if ((Get-FileHash -LiteralPath $sdk -Algorithm SHA512).Hash -ine $profile.sdkSha512) {
    throw 'The pinned x64 .NET input does not match its reviewed digest.'
}
$driver = Join-Path $PSScriptRoot 'run-m4-package-application.ps1'
$driverHash = (Get-FileHash -LiteralPath $driver).Hash
$repo = Split-Path -Parent $PSScriptRoot
$app = Join-Path $repo 'src\ThirdLife.UI\bin\Release\net10.0-windows'
if ((Get-FileHash -LiteralPath (Join-Path $app 'ThirdLife.UI.exe')).Hash -ine $AppHostSha256 -or
    (Get-FileHash -LiteralPath (Join-Path $app 'ThirdLife.UI.dll')).Hash -ine $UiSha256) {
    throw 'Build the reviewed Release application before running the desktop observation.'
}

$arguments = switch ($PSCmdlet.ParameterSetName) {
    'Inspect' { '-Inspect' }
    'Preview' { "-Preview -OperatorCode '$OperatorCode' -VerifiedNoDonorStorageOn '$VerifiedNoDonorStorageOn'" }
    'ResumePreview' {
        if ($OperatorCode) {
            "-ResumePreview -Job '$Job' -OperatorCode '$OperatorCode' -VerifiedNoDonorStorageOn '$VerifiedNoDonorStorageOn'"
        } else { "-ResumePreview -Job '$Job'" }
    }
    'Install' { "-Install -Job '$Job' -ExpectedPlanSha256 '$ExpectedPlanSha256' -OperatorCode '$OperatorCode'" }
    'Reopen' { "-Reopen -Job '$Job' -ExpectedPlanSha256 '$ExpectedPlanSha256' -ExpectedRecovery '$ExpectedRecovery'" }
}
$task = 'ThirdLife-TL0410-' + [guid]::NewGuid().ToString('N')
$temp = Join-Path ([IO.Path]::GetTempPath()) ($task + '.zip')
$lease = [Threading.Mutex]::new($false, ('ThirdLifeVerification-' + $ExpectedVMId.ToString('N')))
$owned = $false
$session = $null
$stage = $null
$stageAttempted = $false
$canCleanStage = -not $Install
$stagingCleaned = $false
$connection = [PowerShell]::Create()
try {
    try { $owned = $lease.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $owned = $true }
    if (-not $owned) { throw 'Another qualification is using the guest.' }

    $bundle = [IO.Compression.ZipFile]::Open($temp, 'Create')
    try {
        $verifiedSdk = [IO.Compression.ZipFile]::OpenRead($sdk)
        try {
            foreach ($item in $verifiedSdk.Entries) {
                if ($item.Name -and $item.FullName -match '\A(dotnet[.]exe\z|host/fxr/|shared/Microsoft[.](NETCore|WindowsDesktop)[.]App/)') {
                    $inputStream = $item.Open()
                    $outputStream = $bundle.CreateEntry('runtime/' + $item.FullName).Open()
                    try { $inputStream.CopyTo($outputStream) }
                    finally { $outputStream.Dispose(); $inputStream.Dispose() }
                }
            }
        }
        finally { $verifiedSdk.Dispose() }
        foreach ($file in Get-ChildItem -LiteralPath $app -File) {
            if ($file.Extension -in @('.exe', '.dll', '.json')) {
                $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($bundle, $file.FullName, ('app/' + $file.Name))
            }
        }
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($bundle, $driver, 'driver.ps1')
    }
    finally { $bundle.Dispose() }
    $bundleHash = (Get-FileHash -LiteralPath $temp).Hash

    $null = $connection.AddCommand('New-PSSession').AddParameter('VMId', $ExpectedVMId).
        AddParameter('Credential', $credential).AddParameter('ErrorAction', 'Stop')
    $pending = $connection.BeginInvoke()
    if (-not $pending.AsyncWaitHandle.WaitOne(20000)) {
        $null = $connection.BeginStop($null, $null)
        throw 'The guest connection exceeded its time limit.'
    }
    $sessions = $connection.EndInvoke($pending)
    if ($sessions.Count -ne 1) { throw 'The expected guest session did not open.' }
    $session = $sessions[0].PSObject.BaseObject
    $stageAttempted = $true
    $stage = Invoke-Command -Session $session -ArgumentList $task, $profile.computer, $profile.account, ([bool]$Install) -ScriptBlock {
        param($name, $computer, $account, $installMode)
        $ErrorActionPreference = 'Stop'
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        if ($env:COMPUTERNAME -ine $computer -or $identity.Name -ine "$computer\$account" -or
            -not [Security.Principal.WindowsPrincipal]::new($identity).IsInRole(
                [Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Expected the dedicated guest staging account.'
        }
        $programFiles = [Environment]::GetFolderPath('ProgramFiles')
        if ((Get-Item -LiteralPath $programFiles).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'The guest Program Files directory is redirected.'
        }
        if ($installMode -and (Get-ChildItem -LiteralPath $programFiles -Directory -Filter 'ThirdLife-TL0410-*' | Select-Object -First 1)) {
            throw 'A prior guest stage requires inspection before another installation.'
        }
        $path = Join-Path $programFiles $name
        if (Test-Path -LiteralPath $path) { throw 'The one-time stage already exists.' }
        $security = [Security.AccessControl.DirectorySecurity]::new()
        $admins = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
        $security.SetOwner($admins)
        $security.SetAccessRuleProtection($true, $false)
        foreach ($sid in @($admins, [Security.Principal.SecurityIdentifier]::new('S-1-5-18'),
            [Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'))) {
            $rights = if ($sid.IsWellKnown([Security.Principal.WellKnownSidType]::BuiltinUsersSid)) {
                'ReadAndExecute'
            } else { 'FullControl' }
            $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
                $sid, $rights, 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
        }
        [IO.Directory]::CreateDirectory($path, $security).FullName
    }
    $transfer = [PowerShell]::Create()
    try {
        $null = $transfer.AddCommand('Copy-Item').AddParameter('ToSession', $session).
            AddParameter('LiteralPath', $temp).AddParameter('Destination', "$stage\input.zip").
            AddParameter('ErrorAction', 'Stop')
        $copy = $transfer.BeginInvoke()
        if (-not $copy.AsyncWaitHandle.WaitOne(120000)) {
            $null = $transfer.BeginStop($null, $null)
            throw 'The guest bundle transfer exceeded its time limit.'
        }
        $null = $transfer.EndInvoke($copy)
    }
    finally {
        if ($transfer.InvocationStateInfo.State -ne 'Stopping') { $transfer.Dispose() }
    }
    $run = Invoke-Command -Session $session -ArgumentList @(
        $stage, $task, $profile.computer, $profile.account, $bundleHash, $driverHash, $AppHostSha256, $UiSha256, $arguments, ([bool]$Install)
    ) -ScriptBlock {
        param($path, $name, $computer, $account, $bundleHash, $driverHash, $exeHash, $dllHash, $driverArgs, $installMode)
        $ErrorActionPreference = 'Stop'
        $archive = Join-Path $path 'input.zip'
        if ((Get-FileHash -LiteralPath $archive).Hash -ine $bundleHash) { throw 'Guest bundle hash mismatch.' }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($archive, (Join-Path $path 'payload'))
        $resultPath = Join-Path $path 'result.json'
        [IO.File]::WriteAllText($resultPath, '')
        $acl = Get-Acl -LiteralPath $resultPath
        $user = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($user, 'Write', 'Allow'))
        Set-Acl -LiteralPath $resultPath -AclObject $acl
        $quotedPath = $path.Replace("'", "''")
        $command = @"
`$ErrorActionPreference = 'Stop'
`$driver = '$quotedPath\payload\driver.ps1'
if ((Get-FileHash -LiteralPath `$driver).Hash -ine '$driverHash') { exit 90 }
`$env:DOTNET_ROOT_X64 = '$quotedPath\payload\runtime'
if ('$installMode' -eq 'True') { `$env:THIRDLIFE_INSTALL_RESULT_PATH = '$quotedPath\result.json' }
try {
    & ([scriptblock]::Create([IO.File]::ReadAllText(`$driver))) $driverArgs -CandidateDirectory '$quotedPath\payload\app' -AppHostSha256 '$exeHash' -UiSha256 '$dllHash' |
        ConvertTo-Json -Compress | Set-Content -LiteralPath '$quotedPath\result.json' -Encoding UTF8
} catch {
    @{ result = 'failed'; last_checkpoint = 'driver'; error_type = `$_.Exception.GetType().Name;
        error_line = `$_.InvocationInfo.ScriptLineNumber } | ConvertTo-Json -Compress |
        Set-Content -LiteralPath '$quotedPath\result.json' -Encoding UTF8
}
"@
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
        $powershell = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
        $action = New-ScheduledTaskAction -Execute $powershell -Argument "-NoProfile -NonInteractive -EncodedCommand $encoded"
        $principal = New-ScheduledTaskPrincipal -UserId "$computer\$account" -LogonType Interactive -RunLevel Limited
        $settings = if ($installMode) {
            New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
        } else { New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::FromMinutes(7)) }
        $null = Register-ScheduledTask -TaskName $name -Action $action -Principal $principal -Settings $settings
        try {
            $previousRun = (Get-ScheduledTaskInfo -TaskName $name).LastRunTime
            Start-ScheduledTask -TaskName $name
            $timer = [Diagnostics.Stopwatch]::StartNew()
            $observationSeconds = if ($installMode) { 660 } else { 400 }
            do {
                Start-Sleep -Milliseconds 500
                $state = (Get-ScheduledTask -TaskName $name).State
                $info = Get-ScheduledTaskInfo -TaskName $name
                if ($state -notin @('Running', 'Queued') -and $info.LastRunTime -gt $previousRun) {
                    if ($info.LastTaskResult -ne 0) { throw "The desktop observation exited with code $($info.LastTaskResult)." }
                    $file = Get-Item -LiteralPath $resultPath
                    if ($file.Length -lt 2 -or $file.Length -gt 8KB) { throw 'Unexpected desktop result size.' }
                    return [pscustomobject]@{ Finished = $true; Json = [IO.File]::ReadAllText($resultPath) }
                }
            } while ($timer.Elapsed.TotalSeconds -lt $observationSeconds)
            if ($installMode) {
                $file = Get-Item -LiteralPath $resultPath
                $partial = if ($file.Length -ge 2 -and $file.Length -le 8KB) { [IO.File]::ReadAllText($resultPath) }
                return [pscustomobject]@{ Finished = $false; Json = $partial }
            }
            throw 'The desktop observation exceeded its time limit.'
        }
        finally {
            if (-not $installMode) {
                if ((Get-ScheduledTask -TaskName $name).State -in @('Running', 'Queued')) { Stop-ScheduledTask -TaskName $name }
                Unregister-ScheduledTask -TaskName $name -Confirm:$false
            }
        }
    }
    $desktop = if ($run.Json) { $run.Json | ConvertFrom-Json }
    if ($Install -and $run.Finished -and $desktop) {
        $normalExit = $desktop.PSObject.Properties['application_closed_normally']
        $closed = $desktop.PSObject.Properties['application_closed']
        $canCleanStage = $desktop.result -ceq 'passed' -and $normalExit -and $closed -and
            $normalExit.Value -is [bool] -and $normalExit.Value -and $closed.Value -is [bool] -and $closed.Value
    }
}
finally {
    try {
        if ($session -and $stage -and $canCleanStage) {
            $stagingCleaned = Invoke-Command -Session $session -ArgumentList $stage, $task, ([bool]$Install) -ScriptBlock {
                param($path, $name, $installMode)
                $expected = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) $name
                if ($path -ine $expected -or $name -cnotmatch '\AThirdLife-TL0410-[0-9a-f]{32}\z' -or
                    ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                    throw 'The one-time guest stage could not be safely identified for cleanup.'
                }
                if ($installMode) {
                    $scheduled = Get-ScheduledTask -TaskName $name
                    if ($scheduled.State -ne 'Ready' -or (Get-Process -Name 'ThirdLife.UI' -ErrorAction SilentlyContinue)) {
                        return $false
                    }
                    Unregister-ScheduledTask -TaskName $name -Confirm:$false
                }
                [IO.Directory]::Delete($expected, $true)
                if (Test-Path -LiteralPath $expected) { throw 'The guest stage remains after cleanup.' }
                $true
            }
        }
    }
    finally {
        if ($Install -and $stageAttempted -and -not $stagingCleaned) {
            Write-Warning "Install stage $task may contain the run result. Preserve it and inspect the saved job before any further action."
        }
        if ($session) { Remove-PSSession -Session $session }
        if ($connection.InvocationStateInfo.State -ne 'Stopping') { $connection.Dispose() }
        if (Test-Path -LiteralPath $temp -PathType Leaf) { [IO.File]::Delete($temp) }
        if ($owned) { $lease.ReleaseMutex() }
        $lease.Dispose()
    }
}
[pscustomobject]@{
    BundleSha256 = $bundleHash.ToLowerInvariant()
    DriverSha256 = $driverHash.ToLowerInvariant()
    Desktop = $desktop
    StagingCleaned = $stagingCleaned
    RetainedStageName = if ($Install -and -not $stagingCleaned) { $task }
}
if (-not $desktop -or $desktop.result -ne 'passed' -or -not $desktop.application_closed_normally -or -not $stagingCleaned) {
    throw 'The guest desktop observation did not complete normally. The bounded result identifies its last checkpoint.'
}
