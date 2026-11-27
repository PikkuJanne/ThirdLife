# TL-0605 recipient guide

Handover previews a plain-language guide from the saved mode, recipient
guidance choices and current independent software evidence. The assisted and
sealed versions use the same headings, in the same order, in the keyboard
preview and printable HTML. Unverified software shows an open check. Backup
stays unconfirmed until a verified restore can support the claim. Missing
contact details direct the recipient to the workshop.

The operator opens the preview before saving. ThirdLife checks the saved mode
and projected facts again and requires another preview if they changed. The
protected `RecipientGuides` folder receives the exact rendered HTML. The copy
uses a random filename and contains no workshop actor code, device or job
identifier, internal action or policy record, path, credential or recovery
material. The renderer writes static typed sections and escapes the optional
public support fields. The same protected writer that guards workshop copies
enforces local path, link, size, collision and atomic publication checks in
the separate recipient folder.

The engineering base is `f39667f1a94bdeb6871fa758476e368fe7d47ca3` (TL-0603).
Verification used Windows 11 Pro 26H2 native x64, build 26300.9974, .NET SDK
10.0.400 and an isolated local browser on 27 November 2026.

## Focused checks

The Release UI selection `FullyQualifiedName~AuditRecipientGuideTests|
FullyQualifiedName~AuditRecipientHandoverTests` passed both cases in 11 s of
test time. The guide case opened the usual Handover surface, checked named and
keyboard-focusable controls, and confirmed saving was unavailable before
preview. A second SQLite connection changed the saved recipient guidance.
Saving the earlier preview produced no file. The fresh preview saved an exact
synthetic HTML copy with the new choice, no recipient information or protected
workshop identifiers, and no attached workshop report.

`eng/verify.ps1 -Tier Targeted -Restore` passed in 269.973 s. Locked restore,
formatting, warnings-as-errors build, validators and eight work-selection
regressions passed. Quick ran 1,379 deterministic cases. The Reports and UI
selections ran 13 and two more cases respectively. There were zero skips.
The two selected projects used this filter:

```text
FullyQualifiedName~RecipientGuideRendererTests|FullyQualifiedName~WorkshopReportStoreTests|FullyQualifiedName~AuditRecipientGuideTests|FullyQualifiedName~AuditRecipientHandoverTests
```

## Rendered variants

- [Assisted example](guide-assisted.html)
- [Sealed example](guide-sealed.html)
- [Isolated Edge layout receipt](layout-receipt.json)
- [Assisted variant at 200% pixel scale](guide-assisted-200.png)
- [Sealed variant at 200% pixel scale](guide-sealed-200.png)

The examples use public synthetic evidence and a reserved example contact.
Their HTML SHA-256 digests are
`62e83c34c7d91fb87e722d2707f2f2e1597ad5a5ccf2317dd83b6a5a76ea8234` for assisted
and `022d0bc82fd95fc28e8a61649178b96565c7a44b90665d8b760c042598ee38c2` for sealed.
Edge 158.0.4370.45 loaded both documents locally with a device pixel ratio
of 2 and a 320 CSS pixel viewport. The browser check took 2.531 s.
Each returned 305 CSS pixels of available
document width and the same scroll width. All ten headings appeared in
matching order in the DOM and browser accessibility tree. The browser
reported zero subresource requests. The captures showed wrapping at the
section titles and long support address, with the content remaining in
reading order. The assisted example asks for backup guidance. The sealed
example leaves that choice deferred.

The browser emulation and keyboard checks establish document structure,
reflow and accessible control properties in these test environments.
Physical printing, audible screen-reader output, recipient comprehension,
and a real recipient transfer were outside the observed scope. Pilot
content adoption and exposure remain at TL-0508 and TL-0611.

The integration trial used main `94ae1a4`. Targeted passed in 279.977 s
with 1,394 Quick and thirteen selected .NET cases, eight work-selection
regressions, both validators, formatting and a clean Release build.

The final integration trial used main `07ac801`, including the keyboard-focus
repair and restart-recovery hold. Both repository validators passed. The
combined recipient guide, handover mode and workshop report WPF selection
passed three cases in 4 s of test time.
