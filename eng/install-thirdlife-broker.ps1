#Requires -Version 5.1
[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Install')][string] $BrokerArtifact,
    [Parameter(Mandatory)][ValidatePattern('\A[0-9a-f]{64}\z', Options = 'CultureInvariant')][string] $ExpectedSha256,
    [Parameter(Mandatory, ParameterSetName = 'Verify')][switch] $VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSModuleAutoLoadingPreference = 'None'

# This body is administrator provisioning code. Run only its independently
# verified bytes in Windows PowerShell started from the Windows system directory.
$Administrators = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$System = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$Users = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')
$TrustedInstaller = [Security.Principal.SecurityIdentifier]::new(
    'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
$AclSections = [Security.AccessControl.AccessControlSections]'Access,Owner'
$BrokerName = 'ThirdLife.Broker.exe'
$PinName = 'broker.sha256'
$ArtifactLimit = 32MB

function Get-BrokerPathAttributes {
    param([string] $Path)

    try { [IO.File]::GetAttributes($Path) }
    catch [IO.FileNotFoundException] { $null }
    catch [IO.DirectoryNotFoundException] { $null }
}

function Assert-BrokerPathType {
    param([string] $Path, [bool] $Directory)

    $attributes = Get-BrokerPathAttributes $Path
    if ($null -eq $attributes -or ($attributes -band [IO.FileAttributes]::ReparsePoint) -or
        ($attributes -band [IO.FileAttributes]::Device) -or
        [bool]($attributes -band [IO.FileAttributes]::Directory) -ne $Directory) {
        throw 'A broker provisioning path is missing, redirected or has the wrong object type.'
    }
}

function Get-BrokerPathComponents {
    param([string] $Path)

    $root = [IO.Path]::GetPathRoot($Path)
    if ($root -cnotmatch '\A[A-Za-z]:\\\z' -or [IO.DriveInfo]::new($root).DriveType -ne [IO.DriveType]::Fixed) {
        throw 'Broker provisioning requires absolute paths on a fixed local drive.'
    }

    $root
    $componentPath = $root
    foreach ($component in $Path.Substring($root.Length).Split([char]'\')) {
        if ([string]::IsNullOrWhiteSpace($component) -or $component.Contains(':') -or
            $component.EndsWith('.') -or $component.EndsWith(' ')) {
            throw 'A broker provisioning path contains an unsuitable Windows path component.'
        }

        $componentPath = [IO.Path]::Combine($componentPath, $component)
        $componentPath
    }
}

function Assert-BrokerAncestorAcl {
    param([Security.AccessControl.DirectorySecurity] $Acl, [bool] $VolumeRoot)

    $owner = $Acl.GetOwner([Security.Principal.SecurityIdentifier])
    $descriptor = [Security.AccessControl.RawSecurityDescriptor]::new($Acl.GetSecurityDescriptorBinaryForm(), 0)
    if ($owner -notin @($Administrators, $System, $TrustedInstaller) -or -not $Acl.AreAccessRulesCanonical -or
        $null -eq $descriptor.DiscretionaryAcl) {
        throw 'The installation ancestor lacks a trustworthy owner or access list.'
    }

    # Includes child deletion, writes to the object, generic write/all and ACL
    # or owner changes. A normal Windows volume root permits creating sibling
    # directories. That right does not permit modifying the fixed protected child.
    $mutationMask = if ($VolumeRoot) { 0x500D0152 } else { 0x500D0156 }
    foreach ($entry in $descriptor.DiscretionaryAcl) {
        if ($entry -isnot [Security.AccessControl.CommonAce] -or $entry.IsCallback) {
            throw 'The installation ancestor has an unsupported access list.'
        }

        if ($entry.AceQualifier -eq [Security.AccessControl.AceQualifier]::AccessAllowed -and
            -not ([int]$entry.AceFlags -band [int][Security.AccessControl.AceFlags]::InheritOnly) -and
            $entry.SecurityIdentifier -notin @($Administrators, $System, $TrustedInstaller) -and
            ($entry.AccessMask -band $mutationMask)) {
            throw 'The installation ancestor allows untrusted modification.'
        }
    }
}

function New-BrokerObjectAcl {
    param([bool] $Directory, [bool] $PublicRead)

    $inheritance = [Security.AccessControl.InheritanceFlags]::None
    if ($Directory) {
        $acl = [Security.AccessControl.DirectorySecurity]::new()
        $inheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
    }
    else {
        $acl = [Security.AccessControl.FileSecurity]::new()
    }

    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner($Administrators)
    foreach ($identity in @($Administrators, $System)) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $identity, [Security.AccessControl.FileSystemRights]::FullControl, $inheritance,
            [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow))
    }
    if ($PublicRead) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $Users, [Security.AccessControl.FileSystemRights]::ReadAndExecute, $inheritance,
            [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow))
    }

    $acl
}

function Assert-BrokerObjectAcl {
    param([string] $Path, [bool] $Directory, [bool] $PublicRead)

    Assert-BrokerPathType $Path $Directory
    $actual = if ($Directory) { [IO.Directory]::GetAccessControl($Path, $AclSections) }
        else { [IO.File]::GetAccessControl($Path, $AclSections) }
    $expected = New-BrokerObjectAcl $Directory $PublicRead
    $actualDescriptor = [Security.AccessControl.RawSecurityDescriptor]::new($actual.GetSecurityDescriptorBinaryForm(), 0)
    $expectedDescriptor = [Security.AccessControl.RawSecurityDescriptor]::new($expected.GetSecurityDescriptorBinaryForm(), 0)
    if (-not $actual.AreAccessRulesProtected -or -not $actual.AreAccessRulesCanonical -or
        -not $actualDescriptor.Owner.Equals($Administrators) -or $null -eq $actualDescriptor.DiscretionaryAcl -or
        $actualDescriptor.DiscretionaryAcl.Count -ne $expectedDescriptor.DiscretionaryAcl.Count) {
        throw 'The broker installation object lacks its required protected owner and access list.'
    }

    # Compare the explicit ACEs without depending on incidental security
    # descriptor flags or ordering among equivalent Allow entries.
    foreach ($entry in $expectedDescriptor.DiscretionaryAcl) {
        $matchCount = 0
        foreach ($possibleMatch in $actualDescriptor.DiscretionaryAcl) {
            if ($possibleMatch.Equals($entry)) { $matchCount++ }
        }
        if ($matchCount -ne 1) { throw 'The broker installation object has an unexpected access rule.' }
    }
}

function Get-BrokerStreamSha256 {
    param([IO.Stream] $Stream)

    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        [BitConverter]::ToString($algorithm.ComputeHash($Stream)).Replace('-', '').ToLowerInvariant()
    }
    finally { $algorithm.Dispose() }
}

function Assert-BrokerNativeStartup {
    param([IO.Stream] $Stream)

    # Inspect the x64 PE32+ and IMAGE_LOAD_CONFIG_DIRECTORY64 fields in the
    # already opened, verified executable. The approved hash asserts AOT
    # provenance. Header checks independently confirm the static-loader policy.
    $reader = [IO.BinaryReader]::new($Stream, [Text.Encoding]::ASCII, $true)
    try {
        $Stream.Position = 0
        if ($Stream.Length -lt 512 -or $reader.ReadUInt16() -ne 0x5A4D) {
            throw 'The broker must be a native Windows x64 executable.'
        }

        $Stream.Position = 0x3C
        $pe = [long]$reader.ReadUInt32()
        if ($pe -lt 64 -or $pe -gt $Stream.Length - 264) {
            throw 'The broker PE header is out of bounds.'
        }

        $Stream.Position = $pe
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) {
            throw 'The broker must be a native Windows x64 executable.'
        }

        $sections = $reader.ReadUInt16()
        $Stream.Position = $pe + 20
        $optionalSize = $reader.ReadUInt16()
        $characteristics = $reader.ReadUInt16()
        $optional = $Stream.Position
        if ($sections -lt 1 -or $sections -gt 96 -or $optionalSize -lt 232 -or
            $optional + $optionalSize + 40 * $sections -gt $Stream.Length -or
            ($characteristics -band 0x2002) -ne 0x0002 -or $reader.ReadUInt16() -ne 0x20B) {
            throw 'The broker PE32+ header is unsuitable.'
        }

        $Stream.Position = $optional + 108
        if ($reader.ReadUInt32() -lt 15) { throw 'The broker PE directory is incomplete.' }
        $Stream.Position = $optional + 192
        $loadConfigRva = [long]$reader.ReadUInt32()
        $loadConfigLength = $reader.ReadUInt32()
        $Stream.Position = $optional + 224
        if ($loadConfigRva -eq 0 -or $loadConfigLength -lt 80 -or $reader.ReadUInt64() -ne 0) {
            throw 'The broker PE startup metadata is unsuitable.'
        }

        $configOffset = 0L
        for ($section = 0; $section -lt $sections; $section++) {
            $Stream.Position = $optional + $optionalSize + 40 * $section + 12
            $delta = $loadConfigRva - [long]$reader.ReadUInt32()
            $rawLength = [long]$reader.ReadUInt32()
            $rawStart = [long]$reader.ReadUInt32()
            if ($delta -ge 0 -and $delta + $loadConfigLength -le $rawLength -and
                $rawStart -gt 0 -and $rawStart + $delta + $loadConfigLength -le $Stream.Length) {
                if ($configOffset -ne 0) { throw 'The broker PE load configuration has conflicting ranges.' }
                $configOffset = $rawStart + $delta
            }
        }

        if ($configOffset -eq 0) { throw 'The broker PE load configuration is out of bounds.' }
        $Stream.Position = $configOffset
        $configLength = $reader.ReadUInt32()
        $Stream.Position = $configOffset + 78
        if ($configLength -lt 80 -or $configLength -gt $loadConfigLength -or $reader.ReadUInt16() -ne 0x800) {
            throw 'The broker must restrict static dependency loading to System32.'
        }
    }
    finally { $reader.Dispose() }
}

function Assert-BrokerInstallation {
    param([string] $Path, [string] $Sha256)

    Assert-BrokerObjectAcl $Path $true $true
    $executablePath = [IO.Path]::Combine($Path, $BrokerName)
    $pinPath = [IO.Path]::Combine($Path, $PinName)
    Assert-BrokerObjectAcl $executablePath $false $true
    Assert-BrokerObjectAcl $pinPath $false $true
    $entries = 0
    foreach ($entry in [IO.Directory]::EnumerateFileSystemEntries($Path)) {
        $entries++
        if ($entries -gt 2 -or [IO.Path]::GetFileName($entry) -cnotin @($BrokerName, $PinName)) {
            throw 'The broker installation contains an unexpected object.'
        }
    }
    if ($entries -ne 2) { throw 'The broker installation is incomplete.' }

    $metadata = [IO.FileStream]::new($pinPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        if ($metadata.Length -ne 64) {
            throw 'The protected broker identity record has the wrong length.'
        }

        $pinBytes = [byte[]]::new(64)
        if ($metadata.Read($pinBytes, 0, $pinBytes.Length) -ne $pinBytes.Length -or
            [Text.Encoding]::ASCII.GetString($pinBytes) -cne $Sha256) {
            throw 'The installed broker identity differs from the reviewed artifact.'
        }
    }
    finally { $metadata.Dispose() }

    $artifact = [IO.FileStream]::new($executablePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        if ($artifact.Length -le 0 -or $artifact.Length -gt $ArtifactLimit -or (Get-BrokerStreamSha256 $artifact) -cne $Sha256) {
            throw 'The installed broker differs from the reviewed artifact.'
        }
        Assert-BrokerNativeStartup $artifact
    }
    finally { $artifact.Dispose() }
}

function Write-StagedBrokerArtifact {
    param([IO.FileStream] $Source, [string] $CandidatePath, [string] $Sha256)

    if ($Source.Length -le 0 -or $Source.Length -gt $ArtifactLimit) {
        throw 'The broker artifact exceeds the supported size bounds.'
    }

    $staged = [IO.FileStream]::new($CandidatePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite,
        [IO.FileShare]::None, 65536, [IO.FileOptions]::WriteThrough)
    try {
        $Source.CopyTo($staged, 65536)
        $staged.Flush($true)
        $staged.Position = 0
        if ((Get-BrokerStreamSha256 $staged) -cne $Sha256) {
            throw 'The staged broker SHA-256 differs from the independently reviewed value.'
        }
        Assert-BrokerNativeStartup $staged
    }
    finally { $staged.Dispose() }
}

function Read-LocalMachineValue {
    param([string] $SubKey, [string] $Name)

    $key = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey($SubKey)
    if ($null -eq $key) { throw 'The Windows provisioning platform could not be verified.' }
    try { $key.GetValue($Name) }
    finally { $key.Dispose() }
}

if ($PSVersionTable.PSEdition -ne 'Desktop' -or -not [Environment]::Is64BitProcess -or
    [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Use 64-bit Windows PowerShell from the Windows system directory.'
}

if (-not $VerifyOnly) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        if (-not [Security.Principal.WindowsPrincipal]::new($identity).IsInRole($Administrators)) {
            throw 'Broker installation requires an elevated administrator provisioning session.'
        }
    }
    finally { $identity.Dispose() }
}

$windowsVersionKey = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$nativeArchitecture = Read-LocalMachineValue 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment' 'PROCESSOR_ARCHITECTURE'
$productType = Read-LocalMachineValue 'SYSTEM\CurrentControlSet\Control\ProductOptions' 'ProductType'
$buildNumber = Read-LocalMachineValue $windowsVersionKey 'CurrentBuildNumber'
$major = Read-LocalMachineValue $windowsVersionKey 'CurrentMajorVersionNumber'
$minor = Read-LocalMachineValue $windowsVersionKey 'CurrentMinorVersionNumber'
if ($nativeArchitecture -cne 'AMD64' -or $productType -cne 'WinNT' -or $major -ne 10 -or $minor -ne 0 -or
    $buildNumber -notmatch '\A[0-9]{5}\z' -or [int]$buildNumber -lt 22000) {
    throw 'Broker provisioning requires Windows 11 client on native x64.'
}

$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
if ([string]::IsNullOrEmpty($programFiles) -or
    [IO.Path]::GetPathRoot($programFiles) -ne [IO.Path]::GetPathRoot([Environment]::SystemDirectory)) {
    throw 'The native Program Files location must be on the Windows system drive.'
}
foreach ($ancestor in @(Get-BrokerPathComponents $programFiles)) {
    Assert-BrokerPathType $ancestor $true
    Assert-BrokerAncestorAcl ([IO.Directory]::GetAccessControl($ancestor, $AclSections)) ($ancestor.EndsWith('\'))
}

$application = [IO.Path]::Combine($programFiles, 'ThirdLife')
$installation = [IO.Path]::Combine($application, 'Broker')
if ($VerifyOnly) {
    Assert-BrokerObjectAcl $application $true $true
    Assert-BrokerInstallation $installation $ExpectedSha256
    [pscustomobject]@{ Status = 'Verified'; Path = $installation; Sha256 = $ExpectedSha256 }
    return
}

if ($null -ne (Get-BrokerPathAttributes $application)) {
    Assert-BrokerObjectAcl $application $true $true
    if ($null -ne (Get-BrokerPathAttributes $installation)) {
        Assert-BrokerInstallation $installation $ExpectedSha256
        [pscustomobject]@{ Status = 'AlreadyInstalled'; Path = $installation; Sha256 = $ExpectedSha256 }
        return
    }
}

if ($BrokerArtifact -cnotmatch '\A[A-Za-z]:\\' -or $BrokerArtifact.Length -gt 240 -or
    [IO.Path]::GetFullPath($BrokerArtifact) -cne $BrokerArtifact -or
    [IO.Path]::GetFileName($BrokerArtifact) -cne $BrokerName) {
    throw 'Supply a canonical absolute path to the published broker executable.'
}

$sourcePathParts = @(Get-BrokerPathComponents $BrokerArtifact)
foreach ($ancestor in $sourcePathParts[0..($sourcePathParts.Count - 2)]) { Assert-BrokerPathType $ancestor $true }
Assert-BrokerPathType $BrokerArtifact $false

# This untrusted source provides bytes only. The exact approved hash comes
# from the independently verified invocation of this deployment code.
$source = [IO.FileStream]::new($BrokerArtifact, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    if ($null -eq (Get-BrokerPathAttributes $application)) {
        [void][IO.Directory]::CreateDirectory($application, (New-BrokerObjectAcl $true $true))
    }
    Assert-BrokerObjectAcl $application $true $true

    $stagePath = [IO.Path]::Combine($application, ('.broker-stage-' + [Guid]::NewGuid().ToString('N')))
    if ($null -ne (Get-BrokerPathAttributes $stagePath)) { throw 'The broker staging directory already exists.' }
    [void][IO.Directory]::CreateDirectory($stagePath, (New-BrokerObjectAcl $true $false))
    Assert-BrokerObjectAcl $stagePath $true $false

    $stageArtifact = [IO.Path]::Combine($stagePath, $BrokerName)
    $stagePin = [IO.Path]::Combine($stagePath, $PinName)
    $published = $false
    try {
        Write-StagedBrokerArtifact $source $stageArtifact $ExpectedSha256
        $metadata = [IO.FileStream]::new($stagePin, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write,
            [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
        try {
            $bytes = [Text.Encoding]::ASCII.GetBytes($ExpectedSha256)
            $metadata.Write($bytes, 0, $bytes.Length)
            $metadata.Flush($true)
        }
        finally { $metadata.Dispose() }

        foreach ($file in @($stageArtifact, $stagePin)) {
            [IO.File]::SetAccessControl($file, (New-BrokerObjectAcl $false $true))
        }
        [IO.Directory]::SetAccessControl($stagePath, (New-BrokerObjectAcl $true $true))
        Assert-BrokerInstallation $stagePath $ExpectedSha256

        # Publish the closed, complete artifact and identity as a single rename.
        # Directory.Move refuses an existing destination. The installed identity
        # cannot be changed by this provisioning operation.
        [IO.Directory]::Move($stagePath, $installation)
        $published = $true
        Assert-BrokerInstallation $installation $ExpectedSha256
    }
    finally {
        if (-not $published) {
            Assert-BrokerPathType $stagePath $true
            foreach ($file in @($stageArtifact, $stagePin)) {
                if ($null -ne (Get-BrokerPathAttributes $file)) {
                    Assert-BrokerPathType $file $false
                    [IO.File]::Delete($file)
                }
            }
            [IO.Directory]::Delete($stagePath, $false)
        }
    }

    [pscustomobject]@{ Status = 'Installed'; Path = $installation; Sha256 = $ExpectedSha256 }
}
finally { $source.Dispose() }
