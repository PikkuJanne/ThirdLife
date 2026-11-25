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
- `expected-reports/M1/workshop-audit.txt` pins the synthetic workshop report's
  expected output. The report regression and bundle manifest check it.

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

## Candidate policy, catalog, and profile JSON

The runtime validators read UTF-8 JSON for the candidate policy, catalog, and profiles.
Their JSON fixtures encode the exact data in the approved candidate YAML files.
Bundle validation checks the projections byte for byte. The TL-0007 approval
and fixture-set digest cover the original YAML inputs.

- [Policy schema](../schemas/policy.schema.json), [policy JSON](policies/community-laptop-policy.json), and [policy YAML](policies/community-laptop-policy.yaml).
- [Catalog schema](../schemas/catalog.schema.json), [catalog JSON](catalog/catalog.json), and [catalog YAML](catalog/catalog.yaml).
- [Profile schema](../schemas/profile.schema.json), [Basic JSON](profiles/basic.json), [Basic YAML](profiles/basic.yaml), [Job Seeker JSON](profiles/job-seeker.json), and [Job Seeker YAML](profiles/job-seeker.yaml).

Run this from the repository root with the pinned Python environment to
regenerate the test projections.

```powershell
@'
from tools.validate_bundle import (
    PILOT_JSON_PROJECTIONS,
    ROOT,
    Validation,
    load_unique_key_yaml,
    pilot_fixture_json_bytes,
)

for source, projection in PILOT_JSON_PROJECTIONS:
    validation = Validation()
    fixture = load_unique_key_yaml(ROOT / source, validation)
    if validation.errors:
        raise SystemExit("\n".join(validation.errors))
    (ROOT / projection).write_bytes(pilot_fixture_json_bytes(fixture))
'@ | .\.venv\Scripts\python.exe -
```
