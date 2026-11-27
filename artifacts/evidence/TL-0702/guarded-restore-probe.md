# TL-0702 guarded restore probe

The approval model issues an immutable harmless sample only while the
saved assisted handover, backup volume and capacity still match. The
sample's generated filename and SHA-256 cover fixed test text and an
opaque identifier. The files contain no account or recovery material.

The independent probe opens exactly that filename under the registered
original and recovered roots. It requires different protected file
identities and a bounded read of each copy. Missing, shared, inaccessible,
oversized, modified and over-permissive files cannot verify. A Windows
restored file may inherit the protected destination's existing access
rules. The probe still requires that all principals and rights match the
protected scope. Other store entry points continue to require explicit
protected file rules.

On 27 November 2026 the active Windows 11 x64 host ran Targeted with
locked restore. Quick passed 1,446 .NET tests and the exact selected
Core, Reports and Diagnostics cases passed 62, with no skips. The
run took 209.597 s. The initial test fixture exposed the difference
between original and inherited recovery-file permissions. The focused
tests then passed with bounded inherited rules and included rejection
of a copied file with additional access. The application and test source
are composed at `ba46627aeccf466c1e9a0c17ce42b28d7cb16304`.

The clean detached Full run of `ba46627aeccf466c1e9a0c17ce42b28d7cb16304`
passed 319 governance and 2,683 .NET tests in 484.795 s on the active
Windows host. Locked restore, build and all supported deterministic
tests completed without skips.

Controlled test copies establish separate readback and exact bytes.
The model also requires attributable Windows restoration for a verified
result. An actual Windows File History operation and its journal remain
pending. [Readiness and live limits](readiness.md)
