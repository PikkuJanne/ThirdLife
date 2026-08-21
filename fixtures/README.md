# ThirdLife synthetic pilot fixtures

These fixtures are deterministic, non-personal reference inputs for the candidate
community-laptop pilot contract. They are data only. They do not authorize a
package, an action, system mutation, or a production deployment.

## Fixture groups

- `jobs` contains an assessment-ready case, a sanitization-blocked case, and a
  partial-observation case.
- `policies` contains candidate Windows 11 x64 laptop requirements and explicit
  missing-evidence behavior.
- `profiles` separates workshop capabilities from recipient-controlled choices.
- `catalog` maps generic capabilities to non-installable synthetic placeholders.

Each job snapshots an exact candidate profile version and the capabilities active
for that scenario, so profile-dependent policy rules can be evaluated without an
unstated input. The sanitization-blocked Basic case leaves its optional video
capability inactive; the Job Seeker ready and partial cases activate all four
declared capabilities.

Profiles name generic capability outcomes only. The separate catalog maps each
capability to its current synthetic placeholder, so changing a reviewed package
choice does not couple or rewrite the profile.

Every value is synthetic. Reserved identifiers begin with `SYNTHETIC-` or use
the `generic.synthetic` namespace and are not derived from a person, account,
device, network, or external package. Fixed timestamps are fixture values, not
observations from a real machine.

The catalog placeholders have no external artifact, are not production-eligible,
and retain pending licence and privacy review plus withheld redistribution. A
future reviewed package choice must replace a placeholder through the governed
catalog, supply-chain, and human-review process.

The candidate policy thresholds are pilot proposals, not universal hardware
requirements or cross-hardware certification. Missing required evidence remains
unknown and cannot satisfy a requirement.
