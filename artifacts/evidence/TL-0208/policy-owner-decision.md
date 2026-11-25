# TL-0208 policy-owner decision

**Decision state** Pending policy-owner decision  
**Candidate source** `bf7076ccee9e5c4c438f4706a68a1021e98316b7` on `main`

Approve the wording and exception governance below for the internal M2 candidate, or identify the changes required before adoption. Record the attributable decision, its date and exact scope before closing TL-0208. [Gate contract](../../../TASKS.yaml) [Representative workshop reports](representative-workshop-reports.txt)

## Candidate disposition wording

| Use | Title | Summary |
| --- | --- | --- |
| Required checks passed | Ready to prepare | The required checks passed for this assessment. |
| Required checks resolved with an exception | Ready to prepare | The required checks are resolved. Review the authorized exceptions before proceeding. |
| Repairable failure | Repair and retest | A required check failed. Record the repair and retest the affected checks. |
| Evidence or decision needs review | Human review required | Review the cited findings before proceeding. |
| Windows preparation requirement failed | Alternative operating system candidate | A Windows preparation requirement failed. Consider a supported alternative operating system. |
| Deployment blocker | Do not deploy | A blocking requirement failed. Stop deployment and review the cited findings. |

The five titles retain the vocabulary in D-017. The engine gives first priority to Do not deploy, followed by Human review required, Alternative operating system candidate, Repair and retest and Ready to prepare. The Core guard requires evidence of supported Windows 11 x64. Missing, stale, conflicting or unrecognized target evidence calls for human review. The report cites the controlling rules and available evidence IDs. [D-017](../../../DECISIONS.md#d-017) [Disposition engine](../../../src/ThirdLife.Policy/Disposition/DispositionEngine.cs) [Explanation wording](../../../src/ThirdLife.Policy/Disposition/DispositionExplanation.cs)

## Candidate exception governance

- An exception requires an authorization ID declared by the exact policy for an unmet blocking requirement with one attributable evidence record. The record binds the job, failed rule and evidence ID, policy ID, version and SHA-256, authorization ID, opaque local operator code, bounded reason and UTC time.
- Unavailable and unresolved evidence, human-confirmed requirements, critical requirements, Do not deploy rules and protected facts cannot qualify. The protected keys are `system.architecture`, `storage.system.health_state` and `battery.swelling_inspection`, plus the sanitization, operating-system, processor, firmware, Secure Boot, TPM, ownership and activation evidence families.
- Replacing an exception appends an attributable record linked to the one it supersedes. Evaluation applies the matching authorization and finding as of the evaluation time. The report displays the underlying finding and a distinct Exception status with its attribution. The operator's private reason stays out of reports and ordinary diagnostics. [Exception history](../../../src/ThirdLife.Policy/Exceptions/PolicyExceptionHistory.cs) [Rule report](../../../src/ThirdLife.Policy/Disposition/PolicyRuleReport.cs)

The tests record reasons in the in-memory exception history. SQLite saves
the captured decision and its rendered attribution. The M2 workspace
reviews saved decisions and has no exception-grant administration.

## Evidence and scope

TL-0007 carries forward the policy owner's 2026-08-21 approval of the exact candidate values and initial capability set, recorded against source `7afc6c7599523fb56a66774a29e9107e6a9a0aac` and fixture-set SHA-256 `1df6bbed058da9397555e747e6d3f4e4443934d261366a54145f2003e5fc8743`. Its candidate YAML and thresholds are unchanged. That policy declares no exception authorization IDs. The representative reports exercise the proposed governance with synthetic data. [TL-0007 record](../../../TASKS.yaml) [Candidate policy](../../../fixtures/policies/community-laptop-policy.yaml)

This decision covers internal M2 wording and governance. Effective policy adoption, pilot exposure and release authorization retain their own gates. The examples establish behavior for represented inputs on the active machine. Live lifecycle coverage and hardware support require their own evidence. [Release decisions](../../../DECISIONS.md#d-006) [Lifecycle source record](../../../docs/inventory/windows-lifecycle-data.md)
