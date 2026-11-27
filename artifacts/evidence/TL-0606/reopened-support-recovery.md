# TL-0606 support archive recovery after reopening

On the active Windows 11 native x64 host on 27 November 2026, the ordinary
Handover view reopened a protected job containing an unconfirmed export
intent. The operator selected its opaque code and reselected the local
folder. The application checked the protected `ThirdLifeSupport` child
and matched the exact bounded ZIP bytes to the saved SHA-256 digest.
It preserved the original approval actor and added the verification time
and recovery actor. The original file timestamp and digest were unchanged.

Three selected WPF tests passed in 5 s, including the direct export path
and the reopened case. A wrong or missing destination yielded an explicit
unconfirmed status and created no directory. Eleven selected Reports tests
passed in 0.560 s, including rejection of same-length changed ZIP bytes.
The Release build passed with warnings as errors.

The fixture used a synthetic archive and a protected temporary directory.
A disappeared, inaccessible, different or modified copy leaves that exact
export intent unconfirmed. Recovery leaves the original file permissions
and content in place and retains no selected personal path.

Against `2c8fadd3a6122f53768ef088f97b3681e485a9d2`, governed Targeted passed
in 185.056 s. There were 1,412 Quick .NET tests and 72 selected Diagnostics,
Reports, Persistence and WPF tests with zero skips. The approved privacy
contract remains the original committed contract. The recovery record uses
its protected operator-attribution and time fields without adding any
personal destination path to the audit or archive.
