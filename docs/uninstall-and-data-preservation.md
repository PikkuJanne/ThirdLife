# Uninstall and data preservation

The repository has a protected broker provisioner and a compiled desktop
application. The current scope includes direct desktop build/start and reviewed
broker identity verification. The standalone installer, uninstall workflow and
release-package file inventory require TL-0702 and TL-0704 evidence.
[Broker provisioner](../eng/install-thirdlife-broker.ps1)

The standalone uninstall contract has two distinct choices.

- **Remove the application.** Remove only owned application, shortcut, registration
  and broker deployment artifacts confirmed by the actual release package. Preserve
  the workshop job store and job-bound evidence by default.
- **Delete ThirdLife-owned job data.** Require a separate explicit choice that
  identifies the affected records. Verify the registered root, exact job identities
  and final file handles before any deletion. Leave exported copies and unrelated
  directories under their respective owners. A failure must preserve or identify
  any retained data. [Registered data roots](data-locations.md)

The installer/lifecycle gate must check the exact package with a clean copy,
retained ThirdLife jobs and unrelated decoys next to owned roots. It must verify
normal removal, the separate data choice, repair or reinstall with preserved data,
locked and denied paths, link/reparse rejection, interruption and later reopening.
Keep Windows Update, vendor packages, other workspaces and user-owned files
outside those operations. The current broker-only setup is evidence for its own
deployment and ACL checks. The lifecycle outcomes remain open.
[Retention and cleanup](retention-and-cleanup.md)
