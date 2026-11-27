# Handover sign-off keyboard and scaling check

On 27 November 2026 on the active Windows 11 host, a focused WPF check
exercised Handover readiness at a 660 by 520 device-independent-pixel
window, with a 2x layout transform, 22-point test text and a long German
button label. The isolated high-contrast resource dictionary used system
colors without changing the desktop preference. The readiness button
exposed its accessible name, remained in the keyboard viewport with no
horizontal scroll, and moved focus to the announced blocker text on use.

The first keyboard observation found that two read-only results lists
accepted tab focus before the next action. The application now retains
those lists and their accessibility names while moving the next tab to the
operator-code field. The blocked completion action remains disabled.

The selected `AuditHandoverCompletionTests` and `ShellAccessibilityTests`
suites passed 6 tests in 4 s on source composed from
`b15fcb617f484751b1fee009bcd28326ff626dc3` plus the two XAML focus changes.
The test exercises WPF automation and focus. Audible screen-reader
perception, physical DPI changes and the complete v0.1 audit are open.
