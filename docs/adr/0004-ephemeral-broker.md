# ADR 0004 — Ephemeral elevated broker

## Status and authority

**Status:** Accepted for `TL-0009`.

**Task:** [`TL-0009`](../../TASKS.yaml)

This ADR records existing binding decisions as planned architecture constraints. It does not amend [`DECISIONS.md`](../../DECISIONS.md) or [`PROJECT_BOUNDARY.md`](../../PROJECT_BOUNDARY.md), and it is not evidence that the planned behavior is implemented or verified.

## Decision IDs

- [D-023](../../DECISIONS.md) — Profiles are non-executable data
- [D-029](../../DECISIONS.md) — Privilege separation
- [D-030](../../DECISIONS.md) — Broker protocol
- [D-031](../../DECISIONS.md) — No always-on privileged service
- [D-032](../../DECISIONS.md) — Action journal and completion
- [D-033](../../DECISIONS.md) — Separate verification and cold boot

## Context

Some approved machine-wide package, update, and system actions require elevation, but the normal operator interface, policy evaluation, persistence, reporting, and verification do not need to remain privileged. Trusting UI validation would create a confused-deputy boundary, while an always-running privileged service would create an unnecessary persistent attack and lifecycle surface.

Approval, dispatch, privileged execution, journaling, and verification must remain distinct even when the UI, broker, or machine is interrupted.

## Decision

The WPF UI remains unelevated. An ephemeral elevated `ThirdLife.Broker` process handles only the exact approved batch and exits when that batch completes, fails, expires, or is abandoned.

- Before dispatch, the unelevated authoritative journal service reloads the current durable approval, validates its exact plan/content digest, and durably records a correlated started/dispatch-intent checkpoint.
- The unelevated client authenticates the expected broker/server identity, and the broker independently validates the initiating caller's user/session. Both rely on restrictive named-pipe ACLs, authenticated framing, and bounded messages. The broker also validates protocol version, schema, nonce, expiry, correlation, replay state, job/action identity, approval digest, action type, and every parameter.
- The broker owns the compiled action allowlist. UI validation, profile data, a plan-service assertion, or a supplied type name cannot introduce an action.
- Requests and actions expose no arbitrary or free-form PowerShell/shell command, executable path, registry path, URL, file operation, or profile-, catalogue-, or UI-supplied installer-argument string. A backend may use only fixed compiled arguments or values derived from bounded typed parameters that the broker validates against the action allowlist. Unknown fields, versions, and actions fail closed.
- The broker has no SQLite database, job, attachment, report, or ordinary-log handle. Its backend returns bounded structured progress and results over the authenticated channel; the unelevated journal service validates correlation/source before persisting a transition.
- UAC decline, broker termination, UI termination, timeout, cancellation, replay, or a missing terminal result leaves a truthful recoverable or requires-review state. Recovery re-observes actual state and never blindly retries a mutation.
- Backend or broker success may record `applied`; only separate fresh postcondition evidence may record `verified`.
- No permanent LocalSystem service, retained administrator token, remote IPC, or background privileged process is introduced for Core 1.0.

## Alternatives considered

- **Run the full WPF application elevated:** rejected because ordinary navigation, reports, imports, and rendering would share unnecessary administrator authority.
- **Install a permanent LocalSystem service:** rejected because the standalone release does not need a long-lived privileged process or its additional installation, update, repair, and attack surface.
- **Use a generic PowerShell or command runner:** rejected because arbitrary execution cannot be safely constrained by profile or UI validation.
- **Trust the unelevated UI's approved flag:** rejected because the broker must independently validate durable approval content and the caller/session.

## Consequences

- Privilege is explicit, attributable, time-bounded, and limited to compiled actions.
- Protocol versioning, authenticated IPC, digest binding, replay control, rate/size bounds, cancellation, process lifetime, and result correlation require focused implementation and adversarial tests.
- UI and broker failures cannot erase the durable dispatch checkpoint or assert a terminal result; ambiguous state requires reconciliation and may need human review.
- The broker cannot directly repair or rewrite job history, which keeps the privileged boundary narrow but requires structured result handoff.
- Accessibility work must provide clear pre-UAC impact, focus restoration, declined-elevation recovery, progress, cancellation, and error state in the unelevated UI.

## References

- [Security policy and broker requirements](../../SECURITY.md#7-privileged-broker-requirements)
- [Security data flow](../security/data-flow.md)
- [Threat model](../security/threat-model.md)
- [Abuse cases](../security/abuse-cases.md)
