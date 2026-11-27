# TL-0706 private black-box interface evidence

On 27 November 2026 the active Windows 11 x64 host built the private
unsigned development ZIP from clean source
`c3284b46dfc94c2b30b2f129a08476b5d7e1d0b0`. The release-package script
used locked dependencies and package metadata version `0.3.2-dev`.
The package verifier checked the independent SHA-256 and AMD64 apphost.
All 19 extracted entries matched the private published payload byte for byte.

| Artifact | Observed identity |
| --- | --- |
| Development ZIP | 1,264,727 bytes, SHA-256 `a21835c7b311e7453abb211eb8c8236cbc3c433e53035fce36c68403bb3eb695` |
| CycloneDX SBOM | SHA-256 `629a9c1e17f4b41573079d00da8702673595064e22b189e03cf88c13232636c4` |
| Apphost | SHA-256 `67439fee0761a7db30459475f05cf746987b4cea40b91f5831fce8654fe02257` |
| Main assembly | SHA-256 `cfd2898374fc0424346a5de6f06c0f33d0a60713e99d6d0c230d4c75b4f17ede` |

The packaged normal desktop entry opened Inspect under an unelevated
token in 1.663 s and closed normally in 1.845 s. The observed process
peak working set was 175,988,736 bytes, and its CPU time through the
Inspect observation was 1,593.75 ms. These are single scope host
measurements from default WPF, with no minimum-hardware claim.

External Windows UI Automation drove **Prepare** from an unpacked
copy without opening a workshop job. It previewed the Basic and Job
Seeker synthetic configuration choices. Both exposed the three
expected members. Through the native folder picker it saved Job
Seeker to a fresh isolated local folder. The observer verified the
exact folder and its owned dialog before confirming. The resulting
ZIP matched the visible SHA-256 and each member matched the pinned
source bytes. The run observed the save in 11.249 s and closed the
app normally after that observation. The earlier preview-only run
completed in 3.080 s. Initial runner attempts exposed incomplete
UI Automation patterns on the owned picker and produced no successful
save evidence. [Job Seeker configuration sample](job-seeker-configuration.zip)

The eight-item sample manifest records hashes and expected results.
The synthetic support archive contains four typed observations and
the two fixed files with the expected field allowlists. The historical
workshop sample remains tied to its original renderer. The public
tree contains synthetic reference files only. The private application
payload, observer output and fixture folders remain outside deliverables.
[Sample manifest](sample-manifest.json)

Clean Full and focused application verification on the same source
are recorded with the resume implementation. The installed MSI
lifecycle, stable signing, freeze and exposure remain separate.
[Resume verification](../TL-0702/reopen-sample.md) · [Guest MSI preflight](../TL-0704/private-msi-preflight.md)
