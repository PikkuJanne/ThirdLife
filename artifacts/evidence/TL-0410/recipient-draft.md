# Your plain-text editor setup

Draft for a ThirdLife test job. These examples use simulated package results.
The test leaves device readiness unknown and handover incomplete.

## Example where the editor passed its checks

- **What we saw.** The test job passed its starting storage check. The
  editor was missing at the start of the test.
- **What was approved.** The workshop approved Notepad++ version 8.9.9.1 for
  everyone who uses this computer.
- **What was done.** The test installer reported that it had installed the
  editor.
- **What was checked.** A separate check found the approved version installed
  for all users. The editor passed a brief opening check with no documents.
  The saved job and workshop report showed the same result after reopening.

## Example where the editor could not be verified

- **What we saw.** The editor was missing at the start of this separate test.
- **What was approved.** The workshop approved the same editor and version for
  all users in this test job.
- **What was done.** The test installer reported that it had installed the
  editor.
- **What was checked.** A separate check could not find the editor. ThirdLife
  kept the package setup under review. The workshop still needs to check
  whether the editor is available before relying on it.

## Evidence for this draft

The workshop's Windows application tests supplied these examples. They used
an isolated job store and simulated installer, installed-state and launch
results. This draft covers only the editor setup. The live Windows guest
observations are recorded with their own validation limits.
[Verification results](../TL-0406/report.md) · [Test cases](../../../tests/ThirdLife.UI.Tests/PackageSetupTests.cs) · [Live guest evidence](../../e2e/M4-first-write-slice.md)
