# TL-0701 current-user Toggle Keys candidate

In assisted handover, TRACE reads the current Windows user's Toggle Keys
setting before showing an exact preview. Actual dispatch requires the
recipient's approval and an attributable workshop code. It binds review to
the saved handover revision and a transient user context, appends an approved
intent, then checks the account, data and setting again before calling
Windows. A separate read records the observed result. Restoration is a new
review and approval. Sealed handover offers no recipient adjustment.

The native route uses Windows `TOGGLEKEYS` and `SystemParametersInfoW`.
The in-memory context is scoped to the standard-user process. The journal
stores no Windows account identifier. The migration preserves approved intent
and independently verified, declined, changed or unconfirmed outcomes.
[TOGGLEKEYS](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-togglekeys) · [SystemParametersInfoW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow)

On 27 November 2026, active Windows 11 x64 host and SDK 10.0.400, the
candidate based on `7d08d8d7f52730790bda390f1ac7125a030f268f` passed
Targeted in 238.444 s with 1,433 Quick and seven selected .NET tests, zero
skips. Those checks include exact consent and setting preconditions,
separate reversion consent, persisted interrupted state across reopen,
mode-change rejection, WPF accessible preview and live result, and sealed
handover. The production adapter independently read the host setting twice
under the same unelevated user context in a read-only ActiveMachine test.

The WPF applied, verified and restored states used an injected controlled
adapter. The actual host setting stayed unchanged. A native write and
restoration for an actual recipient require the corresponding runtime
consent.

The exact composition against reviewed main
`7590368dbd293ff017069be9cca4d5148b9bae1b` passed Targeted in 234.073 s.
It exercised all Quick tests plus nine selected Core, SQLite and WPF
checks, including the current handover sign-off controls and their
keyboard regression. All selected cases passed with zero skips.

The clean detached checkout of `dd69090554665a87c7e145b81a3bcf5710e4382a`
passed `eng/verify.ps1 -Tier Full -Restore` on 27 November 2026 in
521.818 s. It ran 310 governance tests and 2,676 .NET tests, all passing
without skips. The integration also includes reviewed main
`318de262c38e5cf8c492e0460c78dd5426b7444a`. The application's source, test
sources and verification runner match the Full-verified checkout.
