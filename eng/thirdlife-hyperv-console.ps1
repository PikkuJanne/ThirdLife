#Requires -Version 5.1
<#
.SYNOPSIS
Captures the Basic display of the dedicated ThirdLife qualification VM.

.DESCRIPTION
Run locally with authorized Hyper-V host access. Supply the VM ID from the
runner's admission check. Capture writes a private temporary PNG and returns
its path and a digest bound to the VM and raw display pixels. View the PNG
locally and remove it when finished. Keep the frame out of project evidence.

Use Decline only after visually identifying the reviewed ThirdLife broker's
Windows Yes/No consent prompt for an active decline rehearsal. The helper
rechecks the live frame and sends Escape only if the pixels still match.
The returned input status records delivery. Check the guest client result
and broker process state separately.

.EXAMPLE
$frame = .\eng\thirdlife-hyperv-console.ps1 -VMId $vmId

.EXAMPLE
.\eng\thirdlife-hyperv-console.ps1 -VMId $vmId -Decline `
    -ObservedBrokerConsentPrompt -ObservedFrameSha256 $frame.FrameSha256
#>
[CmdletBinding(DefaultParameterSetName = 'Capture')]
param(
    [Parameter(Mandatory)]
    [guid] $VMId,

    [Parameter(Mandatory, ParameterSetName = 'Decline')]
    [switch] $Decline,

    [Parameter(Mandatory, ParameterSetName = 'Decline')]
    [switch] $ObservedBrokerConsentPrompt,

    [Parameter(Mandatory, ParameterSetName = 'Decline')]
    [ValidatePattern('\A[0-9a-fA-F]{64}\z')]
    [string] $ObservedFrameSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($VMId -eq [guid]::Empty) {
    throw 'Supply the VM ID from the ThirdLife runner admission check.'
}
if ($PSCmdlet.ParameterSetName -eq 'Decline' -and (-not $Decline -or -not $ObservedBrokerConsentPrompt)) {
    throw 'Escape requires explicit confirmation of the visually observed broker consent prompt.'
}

$query = @{ Namespace = 'root/virtualization/v2'; OperationTimeoutSec = 8 }
$vmGuid = $VMId.ToString('D')
$computer = @(Get-CimInstance @query -ClassName Msvm_ComputerSystem -Filter "Name='$vmGuid'")
if ($computer.Count -ne 1 -or [guid]$computer[0].Name -ne $VMId -or
    $computer[0].ElementName -cne 'ThirdLife-W11' -or $computer[0].EnabledState -ne 2) {
    throw 'The supplied VM ID must resolve to the running ThirdLife-W11 qualification VM.'
}

$settings = @(Get-CimAssociatedInstance -InputObject $computer[0] -Association Msvm_SettingsDefineState `
    -ResultClassName Msvm_VirtualSystemSettingData -OperationTimeoutSec 8)
$services = @(Get-CimInstance @query -ClassName Msvm_VirtualSystemManagementService)
if ($settings.Count -ne 1 -or [guid]$settings[0].VirtualSystemIdentifier -ne $VMId -or
    $settings[0].VirtualSystemType -ne 'Microsoft:Hyper-V:System:Realized' -or $services.Count -ne 1) {
    throw 'Hyper-V did not return the dedicated VM''s current display settings.'
}

if ($Decline) {
    $keyboard = @(Get-CimAssociatedInstance -InputObject $computer[0] -Association Msvm_SystemDevice `
        -ResultClassName Msvm_Keyboard -OperationTimeoutSec 8)
    if ($keyboard.Count -ne 1 -or [guid]$keyboard[0].SystemName -ne $VMId) {
        throw 'Hyper-V did not return the dedicated VM''s keyboard.'
    }
}

$width = [uint16]1024
$height = [uint16]768
$frame = Invoke-CimMethod -InputObject $services[0] -MethodName GetVirtualSystemThumbnailImage `
    -Arguments @{ TargetSystem = $settings[0]; WidthPixels = $width; HeightPixels = $height } -OperationTimeoutSec 8
if ($frame.ReturnValue -ne 0) {
    throw "Hyper-V display capture failed with status $($frame.ReturnValue)."
}
$pixels = [byte[]]$frame.ImageData
if ($pixels.Length -ne ($width * $height * 2)) {
    throw 'Hyper-V returned an unexpected display frame size.'
}

$hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
try {
    $hash.AppendData([Text.Encoding]::ASCII.GetBytes("$vmGuid/$width/$height/"))
    $hash.AppendData($pixels)
    $digest = [BitConverter]::ToString($hash.GetHashAndReset()).Replace('-', '').ToLowerInvariant()
} finally {
    $hash.Dispose()
}

if ($Decline) {
    if ($digest -ine $ObservedFrameSha256) {
        throw 'The VM display changed. Capture and visually verify the prompt again before sending Escape.'
    }
    $inputResult = Invoke-CimMethod -InputObject $keyboard[0] -MethodName TypeKey `
        -Arguments @{ keyCode = [uint32]0x1b } -OperationTimeoutSec 8
    if ($inputResult.ReturnValue -ne 0) {
        throw "Hyper-V keyboard input failed with status $($inputResult.ReturnValue)."
    }
    [pscustomobject]@{ Interaction = 'AutomatedEscape'; SentUtc = [DateTime]::UtcNow }
    return
}

Add-Type -AssemblyName PresentationCore
$image = [Windows.Media.Imaging.BitmapSource]::Create($width, $height, 96, 96,
    [Windows.Media.PixelFormats]::Bgr565, $null, $pixels, $width * 2)
$encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($image))
$localAppData = [Environment]::GetFolderPath('LocalApplicationData')
if ([string]::IsNullOrWhiteSpace($localAppData) -or $localAppData.StartsWith('\\')) {
    throw 'A local user temporary directory is required for the raw frame.'
}
$temporaryDirectory = Get-Item -LiteralPath (Join-Path $localAppData 'Temp') -Force
for ($directory = $temporaryDirectory; $null -ne $directory; $directory = $directory.Parent) {
    if ($directory -isnot [IO.DirectoryInfo] -or ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'The raw frame requires a local temporary directory without redirection.'
    }
}
$capturePath = Join-Path $temporaryDirectory.FullName ('thirdlife-uac-' + [guid]::NewGuid().ToString('N') + '.png')
$stream = [IO.File]::Open($capturePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    try {
        $encoder.Save($stream)
    } finally {
        $stream.Dispose()
    }
} catch {
    Remove-Item -LiteralPath $capturePath -ErrorAction SilentlyContinue
    throw
}
[pscustomobject]@{ CapturePath = $capturePath; FrameSha256 = $digest; Width = $width; Height = $height }
