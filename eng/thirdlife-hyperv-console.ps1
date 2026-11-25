#Requires -Version 5.1
<#
.SYNOPSIS
Captures the Basic display of the dedicated ThirdLife qualification VM.

.DESCRIPTION
Run locally with authorized Hyper-V host access. Supply the VM ID from the
runner's admission check. Capture writes a private temporary PNG and returns
its path and a digest bound to the VM and raw display pixels. View the PNG
locally and remove it when finished. Keep the frame out of project evidence.

Use Decline or Accept only after visually identifying the reviewed ThirdLife
broker's Windows Yes/No prompt for the active rehearsal. Decline sends Escape.
Accept checks that the capture matches the native display resolution and clicks
the observed Yes coordinates. If moving the pointer changes the frame, capture
it again and confirm the button before retrying. The returned interaction status
records input delivery. Check the guest client result and broker process separately.

.EXAMPLE
$frame = .\eng\thirdlife-hyperv-console.ps1 -VMId $vmId

.EXAMPLE
.\eng\thirdlife-hyperv-console.ps1 -VMId $vmId -Decline `
    -ObservedBrokerConsentPrompt -ObservedFrameSha256 $frame.FrameSha256

.EXAMPLE
.\eng\thirdlife-hyperv-console.ps1 -VMId $vmId -Accept `
    -ObservedBrokerConsentPrompt -ObservedFrameSha256 $frame.FrameSha256 `
    -ObservedYesX $yesX -ObservedYesY $yesY
#>
[CmdletBinding(DefaultParameterSetName = 'Capture')]
param(
    [Parameter(Mandatory)]
    [guid] $VMId,

    [Parameter(Mandatory, ParameterSetName = 'Decline')]
    [switch] $Decline,

    [Parameter(Mandatory, ParameterSetName = 'Accept')]
    [switch] $Accept,

    [Parameter(Mandatory, ParameterSetName = 'Decline')]
    [Parameter(Mandatory, ParameterSetName = 'Accept')]
    [switch] $ObservedBrokerConsentPrompt,

    [Parameter(Mandatory, ParameterSetName = 'Decline')]
    [Parameter(Mandatory, ParameterSetName = 'Accept')]
    [ValidatePattern('\A[0-9a-fA-F]{64}\z')]
    [string] $ObservedFrameSha256,

    [Parameter(Mandatory, ParameterSetName = 'Accept')]
    [int] $ObservedYesX,

    [Parameter(Mandatory, ParameterSetName = 'Accept')]
    [int] $ObservedYesY
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($VMId -eq [guid]::Empty) {
    throw 'Supply the VM ID from the ThirdLife runner admission check.'
}
$inputRequested = $PSCmdlet.ParameterSetName -ne 'Capture'
if ($inputRequested -and (-not ($Decline -or $Accept) -or -not $ObservedBrokerConsentPrompt)) {
    throw 'Input requires explicit confirmation of the visually observed broker consent prompt.'
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

if ($Accept) {
    if ($ObservedYesX -lt 0 -or $ObservedYesX -ge $width -or $ObservedYesY -lt 0 -or $ObservedYesY -ge $height) {
        throw 'The observed Yes coordinates must be within the captured frame.'
    }
    $mouse = @(Get-CimAssociatedInstance -InputObject $computer[0] -Association Msvm_SystemDevice `
        -ResultClassName Msvm_SyntheticMouse -OperationTimeoutSec 8)
    if ($mouse.Count -ne 1 -or [guid]$mouse[0].SystemName -ne $VMId -or $mouse[0].EnabledState -ne 2 -or
        -not $mouse[0].AbsoluteCoordinates -or $mouse[0].IsLocked) {
        throw 'Hyper-V did not return one enabled absolute mouse for the dedicated VM.'
    }
    $controller = @(Get-CimAssociatedInstance -InputObject $computer[0] -Association Msvm_SystemDevice `
        -ResultClassName Msvm_SyntheticDisplayController -OperationTimeoutSec 8)
    if ($controller.Count -ne 1) {
        throw 'Hyper-V did not return one synthetic display controller for the dedicated VM.'
    }
    $videoHead = @(Get-CimAssociatedInstance -InputObject $controller[0] -Association Msvm_VideoHeadOnController `
        -ResultClassName Msvm_VideoHead -OperationTimeoutSec 8)
    if ($videoHead.Count -ne 1 -or [guid]$videoHead[0].SystemName -ne $VMId -or $videoHead[0].EnabledState -ne 2 -or
        $videoHead[0].CurrentHorizontalResolution -ne $width -or $videoHead[0].CurrentVerticalResolution -ne $height) {
        throw 'The captured frame must match the active native VM display resolution for a click.'
    }
}

function Get-DisplayFrame {
    $frame = Invoke-CimMethod -InputObject $services[0] -MethodName GetVirtualSystemThumbnailImage `
        -Arguments @{ TargetSystem = $settings[0]; WidthPixels = $width; HeightPixels = $height } -OperationTimeoutSec 8
    if ($frame.ReturnValue -ne 0) {
        throw "Hyper-V display capture failed with status $($frame.ReturnValue)."
    }
    $imageData = [byte[]]$frame.ImageData
    $pixelLength = $width * $height * 2
    if ($imageData.Length -eq $pixelLength) {
        $pixels = $imageData
    } elseif ($imageData.Length -eq ($pixelLength + 4) -and
        [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($imageData, 0)) -eq $imageData.Length) {
        # Some providers prefix the RGB565 data with its total encoded byte count.
        $pixels = [byte[]]::new($pixelLength)
        [Array]::Copy($imageData, 4, $pixels, 0, $pixelLength)
    } else {
        throw 'Hyper-V returned an unexpected display frame size or length prefix.'
    }

    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        $hash.AppendData([Text.Encoding]::ASCII.GetBytes("$vmGuid/$width/$height/"))
        $hash.AppendData($pixels)
        $digest = [BitConverter]::ToString($hash.GetHashAndReset()).Replace('-', '').ToLowerInvariant()
        [pscustomobject]@{ Pixels = $pixels; Sha256 = $digest }
    } finally {
        $hash.Dispose()
    }
}

$displayFrame = Get-DisplayFrame
if ($inputRequested -and $displayFrame.Sha256 -ine $ObservedFrameSha256) {
    throw 'The VM display changed. Capture and visually verify the prompt again before sending input.'
}

if ($Decline) {
    $inputResult = Invoke-CimMethod -InputObject $keyboard[0] -MethodName TypeKey `
        -Arguments @{ keyCode = [uint32]0x1b } -OperationTimeoutSec 8
    if ($inputResult.ReturnValue -ne 0) {
        throw "Hyper-V keyboard input failed with status $($inputResult.ReturnValue)."
    }
    [pscustomobject]@{ Interaction = 'AutomatedEscape'; SentUtc = [DateTime]::UtcNow }
    return
}

if ($Accept) {
    if ($mouse[0].HorizontalPosition -ne $ObservedYesX -or $mouse[0].VerticalPosition -ne $ObservedYesY) {
        $position = Invoke-CimMethod -InputObject $mouse[0] -MethodName SetAbsolutePosition `
            -Arguments @{ horizontalPosition = $ObservedYesX; verticalPosition = $ObservedYesY } -OperationTimeoutSec 8
        if ($position.ReturnValue -ne 0) {
            throw "Hyper-V mouse positioning failed with status $($position.ReturnValue)."
        }
        if ((Get-DisplayFrame).Sha256 -ine $ObservedFrameSha256) {
            throw 'The VM display changed after moving the pointer. Capture and visually verify Yes again before clicking.'
        }
    }
    $currentMouse = Get-CimInstance -InputObject $mouse[0] -OperationTimeoutSec 8
    if ([guid]$currentMouse.SystemName -ne $VMId -or $currentMouse.HorizontalPosition -ne $ObservedYesX -or
        $currentMouse.VerticalPosition -ne $ObservedYesY) {
        throw 'The mouse no longer matches the verified VM and observed Yes coordinates.'
    }
    $inputResult = Invoke-CimMethod -InputObject $currentMouse -MethodName ClickButton `
        -Arguments @{ buttonIndex = [uint32]1 } -OperationTimeoutSec 8
    if ($inputResult.ReturnValue -ne 0) {
        throw "Hyper-V mouse input failed with status $($inputResult.ReturnValue)."
    }
    [pscustomobject]@{ Interaction = 'AutomatedYesClick'; SentUtc = [DateTime]::UtcNow }
    return
}

Add-Type -AssemblyName PresentationCore
$image = [Windows.Media.Imaging.BitmapSource]::Create($width, $height, 96, 96,
    [Windows.Media.PixelFormats]::Bgr565, $null, $displayFrame.Pixels, $width * 2)
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
[pscustomobject]@{ CapturePath = $capturePath; FrameSha256 = $displayFrame.Sha256; Width = $width; Height = $height }
