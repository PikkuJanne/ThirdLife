# TL-0610 private development payload

On 27 November 2026, the active Windows 11 Pro 26H2 native x64 host built
the WPF payload from a clean detached checkout at
`244e51dd4e26a10179331877fe5a898fb39f8f4b` using .NET SDK 10.0.400.
The packaging probe used these commands with empty isolated paths.

```powershell
dotnet restore src/ThirdLife.UI/ThirdLife.UI.csproj --locked-mode
dotnet publish src/ThirdLife.UI/ThirdLife.UI.csproj -c Release --no-restore --self-contained false -p:UseAppHost=true -o $payload
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
```

Publishing and packaging took 15.437 s. The executable PE header reported
AMD64. The local archive has 1,488,358 bytes and SHA-256
`c339f2e51a82a3b8ee819bc06c4a9e7d800ea617849935e9b60c312e0f4d651b`.
The packed and unpacked apphost both have SHA-256
`a54c8b99ad99fa58ce02e3893a0a62d2fa127b6823c269caaf0072bdb284187c`.

After unpacking to a separate ignored directory, the packaged app opened
with the unelevated host token. External Windows UI Automation selected
Inspect in 2.909 s. The owned process closed normally in 3.307 s total.
The tracked source remained clean. Archive and extracted files remain
in the ignored `TestResults/development-preview/` directory in this
worktree. The probe made no change to the named guest.

The payload is a private framework-dependent engineering build. It used
the host Windows Desktop Runtime. Debug symbols remain in this private
payload. The pilot candidate still needs the integrated M6 source, exact
runtime and distribution inputs, checksums, notices and lifecycle evidence
on ThirdLife-W11. A ZIP startup observation leaves installation, repair,
removal, rollback and pilot exposure open.

## Source-pinned component inventory

The existing `eng/generate-sbom.ps1` runner generated the CycloneDX 1.6
inventory twice with `-ProductVersion 0.3.0-dev` and the exact payload
source in `-SourceRevision`. Both files had 33 components and SHA-256
`85ade16e81ab766834bcebc923eb672f2320fc4de96e15306d364e8ffe989d2c`.
Inputs were checked against the same commit as the private app package.
Both copies remain in `TestResults/development-preview/`. The generator
reported pending dependency review. Release review and notices remain
attached to the exact distribution candidate.

## Updated private packaging path

`eng/package-development-preview.ps1 -RunName verified-tool` packaged
reviewed main `af8861aafb9ebcca96adb2a5f7d03f8efd8ea8eb`. It used locked
restore and a serial Release publish. The first concurrent publish hit a
shared MSBuild output lock. Serial publishing to a separate output resolved
it. Sorted ZIP entry names and fixed timestamps reproduced the same bytes
as the independent packaging probe. Verification compared the hashes of
all 19 ZIP members against the published files.

The AMD64 archive has 1,196,552 bytes and SHA-256
`006c445217bd61280daae40e1208fbe8d2fbe10dd032007005f043160766ed06`.
The package contains 19 entries and no debug symbols. The exact apphost SHA-256
is `db0da1472f6ffea4820dd81e3970500514d6a2ef956b3061b63db45d1de02071`.
The source-pinned CycloneDX 1.6 SBOM contains 33 components and SHA-256
`8f09912466f50278cfe612c1c6979aa67ce153733c0b221536496a82474df3a3`.

After archive and extracted apphost verification, the packaged application
launched with the unelevated host token. External UI Automation selected
Inspect in 1.782 s. The process closed normally in 2.128 s with exit code 0.
The local package manifest, verification receipt and binaries remain under
ignored `TestResults/development-preview/` in the packaging worktree. An
unsigned private preview leaves the installer lifecycle and exposure gates
open.

The packaging tool and evidence checkpoint passed Quick on the active
host in 223.632 s, with 1,419 .NET tests and no skips. WPF consumed 109 s
of the measured run. The packaging tool itself also produced and verified
the same archive as the manual packer from the same Release source.

The tool also published the handover-composed source
`c1f92cc4dcfd6e27e544dc4aa8965c86e44d05b9`, which has the same application,
test and governed input bytes as reviewed main `7590368`. Its 19-member
private ZIP measured 1,223,748 bytes and SHA-256
`eb00c3c931389dedf07a8109c0b6cfc2f7325ad72851fd65325aaac09647789b`.
Its source-pinned SBOM has SHA-256
`d60435212e6542ea8d0b079a3885bc5c9d5ee3822a0cfb4f68abbc3a8a1b8b98`.
The exact post-handover package remains in the ignored private output.
