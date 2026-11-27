# TL-0709 private 0.3.6 boundary evidence

The scoped audit used private source
fa2fa35812a8960a35c3131bc902f2d7eee49b39 and MSI SHA-256
1a4a679db5cdc0cdd88c01277acd5aa9064a40582f05bd44d91c0dcbb3b25c9c.
The native Windows signature query returned NotSigned.
[Exact package and guest lifecycle](../TL-0704/populated-job-lifecycle.md)

| Check | Observed result |
| --- | --- |
| Source dependency graph | All 51 project references across 26 tracked projects resolved inside this repository. The desktop manifest declared 12 ThirdLife project libraries and four SQLite packages. Its runtime manifest selected .NET Core and Windows Desktop 10. |
| Package ownership | Read-only Windows Installer inspection found 15 declared tables, 19 per-user registry rows and four fixed property assignments. The exact installer carried one embedded cabinet with 19 files. Every extracted member matched the independently verified ZIP. |
| User data and recovery | Normal upgrade, cancellation and uninstall preserved the exact schema-21 job database and reopened the archived job. The unrelated synthetic decoy remained under its own directory. |
| Deployment boundary | The draft release interface identifies the local application, package, data and support paths at their current observation scope. Product identity, trust and frozen release fields still await the actual release artifact. [Release interface](../../../RELEASE_INTERFACE.md) |
| Portfolio scope | The reviewed project and package introduced no B4 adapter or cross-project dependency. The existing future ideas remain deferred and the active queue still places Scam Explainer after B1. [Boundary](../../../PROJECT_BOUNDARY.md) · [Deferred notes](../../../FUTURE_ASSEMBLY_NOTES.md) |

This audit establishes the private candidate scope. The frozen stable
artifact, actual installed window, cross-schema recovery, rights and
distribution decisions remain at their respective requirements.
The standalone and Team B transition decision remains at TL-0710.
