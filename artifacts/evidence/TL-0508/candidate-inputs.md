# TL-0508 exact pilot engineering inputs

The four bundled inputs below were reviewed on 27 November 2026. The
application export preserves their exact reviewed bytes and marks the
candidate scope. The software in the public profiles remains synthetic.
The scoped plain-text action has a distinct reviewed package, policy and
licence scope. [Catalogue review](../../../docs/catalog/package-review-matrix.md)

| Input | Version | SHA-256 |
| --- | --- | --- |
| [community-laptop-candidate](../../../fixtures/catalog/catalog.json) | 0.1.0-candidate.1 | `60880321e29f0db5deb77c85bd10d1c5926ca1b068d930445695580891c1bdce` |
| [community-laptop-policy](../../../src/ThirdLife.UI/Features/Disposition/CommunityLaptopCandidate/community-laptop-policy.json) | 0.1.0-candidate.3 | `4406ccac580cd1b52e439ac3aa79b5931eb3fc7372396e3c6beda55e7661a483` |
| [basic](../../../src/ThirdLife.UI/Features/Disposition/CommunityLaptopCandidate/basic.json) | 0.1.0-candidate.3 | `d80c1c029067ebc3f172011cf66a551eff50c7661c6daf8874aced23324130e2` |
| [job-seeker](../../../src/ThirdLife.UI/Features/Disposition/CommunityLaptopCandidate/job-seeker.json) | 0.1.0-candidate.3 | `928e0a6a5072a480d416861fc1051afd5efd2fc32b40459cad4aa4c3b113eb05` |

The active native x64 Windows 11 host ran the built Release WPF
`JobSeekerCandidateConvergesAndReopensWithoutLiveReadiness`,
`OptionalCandidateFailurePreservesEssentialActions`,
`EssentialCandidateFailureBlocksPreparation`, and
`BundledConfigurationExportPreservesValidatedExactBytes` selections.
All six cases passed in three seconds of test time from
`c3284b46dfc94c2b30b2f129a08476b5d7e1d0b0`. The focused fixture uses
synthetic providers and local configuration. It resolved both profiles
without package acquisition and preserved required and optional decisions.
The unpacked application subsequently saved the exact Job Seeker export
through its native folder picker.
[Packaged export](../TL-0706/private-interface.md)

These results prepare the scoped configuration for review. Production
software selection, exact live package metadata, applicable licence
and privacy decisions, and promotion of policy thresholds stay open.
