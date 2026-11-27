# TL-0708 exact private package reproduction

Two clean detached checkouts on the active Windows 11 Pro 26H2 native
x64 host independently produced the private 0.3.4-dev package and the
per-user MSI 0.3.4. Both used source
`71ec3b3bda6229d62b38263cb8422606d954e975`, SDK 10.0.400, Python
3.14.7, locked NuGet restore and the same native packaging tools. The
checkout paths differed in length. Each package build used fresh
intermediates, and each complete build took less than 30 seconds.

The baseline private 0.3.3-dev rebuild at
`ee09076c6cba111546615ae4e06c86ad1aca0aa6` reproduced the SBOM
but differed in five of its 19 managed and runtime entries. The build
now maps checkout paths for the compiler. Native MSI comparison also
found differences in cabinet member times, the compound file timestamp
and the random package code. The new authoring sets a fixed DOS
member date, normalizes the generated storage timestamp through
Windows and derives the package code from the rendered database
and cabinet with the package code field blanked.
[Compiler path mapping](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/advanced)
· [Compound storage timestamps](https://learn.microsoft.com/en-us/windows/win32/api/objidl/nf-objidl-istorage-setelementtimes)
· [MSI package codes](https://learn.microsoft.com/en-us/windows/win32/msi/package-codes)

| Artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| Development ZIP | 1,265,590 | `15ff3d9c03d67aee347d5746a6c2c2a7a559ee64d6509ff45972f45ac2fde793` |
| CycloneDX SBOM | 156,677 | `121dbfe5e10bbe9ac95def5edb5a5c6880a202e7a26e80c28b9005b5d41f706b` |
| Per-user MSI | 1,257,472 | `cce70ae130dbf5eb3bc2f750d7ffd807b057784ff15b21001c334d8269dead55` |
| MSI cabinet | 1,229,728 | `a3215e60bd6b3b3a00c6b30aceff31604d9744595830ce064c98d0b9d496ae63` |

Direct comparison found identical bytes for both sets, including
both receipts. The independent package verifier checked the
rebuild against the first reviewed digests, all 19 entries and
AMD64 apphost admission. Both assemblies passed the native
Windows Installer package verification API. The package code is
`{EAFF06CC-0598-5F44-AD63-E2B7ECE828BA}`. Ten focused authoring
and verifier tests passed in 0.782 s. The new regression test queries
the code through Windows Installer and verifies that a same-size
payload change at the same product version changes the package code.

A read-only Windows Installer database view inspected the exact MSI.
It contained the 15 declared tables and a single fixed property
assignment. All 19 registry rows were per-user under the declared
installer key. Native Windows cabinet extraction from the actual MSI
stream produced the same 19 members and bytes as the reviewed ZIP.

The clean detached Full verification ran on the same source and passed
320 repository checks and 2,688 .NET tests in 529.343 s.

Both artifacts are private and unsigned. The observations establish
same-machine reproducibility with the recorded toolchain. The earlier
guest lifecycle used distinct packages with their own digests. Exact
0.3.4 installed startup, interruption, populated job preservation,
cross-toolchain reproduction, signing and release authorization
remain outside this result.
[Earlier guest lifecycle](../TL-0704/interactive-lifecycle.md)
