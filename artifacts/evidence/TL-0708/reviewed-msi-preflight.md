# TL-0708 exact private 0.3.7 preflight

The existing package verifier accepts the matching private MSI and
installer receipt when all three installer inputs are provided.
The independently reviewed version and digest control the check.
The receipt must link those same inputs to the already verified
source commit, ZIP and SBOM. The preflight reads files only.

Two clean source checkouts produced the private 0.3.7 artifacts at
8440ae0cd7e6797a8c348ce5094396d30e443595 in 31.916 s and 32.144 s.
The ZIP, SBOM, MSI and both receipts matched byte for byte.

| Artifact | SHA-256 |
| --- | --- |
| 0.3.7-dev ZIP | 0e6f5284816e8f5cd9681892dede324386ccad565b83c32fa739a133c4d30eb6 |
| 0.3.7-dev SBOM | ec05116c68d7c3416ab0f288ff399fc419ecf3b0772bc75350d4365fa67d3d78 |
| 0.3.7 MSI | f0c914b5e84746c54fcdc1048e68d081b399a118b7b7aee1d290ad5efe7612f2 |

The command-line verifier with the new preflight accepted the
reviewed 0.3.7 package and installer. The same command with a
substituted digest returned 1. Native authoring and package
verification tests also rejected the wrong version and a receipt
that linked a different desktop archive. Eleven scoped tests passed
in 1.014 s at the new verifier code.

The supply-chain fixture now excludes TestResults and the local
Codex attachments and worktree directories when copying source.
The focused fixture-copy and exact clean-revision tests passed in
66.800 s with an installer receipt present under an ignored
TestResults folder. The generated package and fixture outputs
remain in their local worktrees.

A dedicated clean Full run at
d1b34441f95081e58857703950ec2727c90e088c passed 322 repository checks
and 2,689 .NET tests in 696.369 s.

The exact unpacked desktop opened Inspect under the active host
standard token in 1.404 s and closed normally by 1.601 s. Each of the
19 unpacked files matched the ZIP. This source includes the recorded
report reopening feature. The guest lifecycle and rollback
observations apply to the separately identified 0.3.6 MSI.
The 0.3.7 artifacts remain unsigned and private.
[Guest lifecycle](../TL-0704/populated-job-lifecycle.md)
· [Recorded report](../TL-0703/reopen-archived-report.md)
