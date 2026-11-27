# Profile and policy export

The current model returns the exact validated UTF-8 JSON bytes for an embedded
workshop profile and its related catalogue and policy. It supplies fixed file
names and the SHA-256 of each payload. The file selection and destination writer
remain pending. [Configuration export model](../src/ThirdLife.UI/Features/PlanReview/WorkshopConfigurationExport.cs)

| File | Validated schema | Validation binding |
| --- | --- | --- |
| `profile.json` | `thirdlife.profile.v1` | Profile ID and version, declared catalogue and policy references, bounded capabilities and recipient-controlled deferrals. Parsing binds the exact validated catalogue digest. |
| `catalog.json` | `thirdlife.catalog.v1` | Exact catalogue ID, version and digest used to validate the profile. |
| `policy.json` | `thirdlife.policy.v2` | Exact validated policy bytes for the profile's policy ID and version. The compiled candidate or reviewed package policy must agree with the profile's synthetic status. |

Catalogue and profile schemas hash and validate one owned byte snapshot and
retain that snapshot. Export preserves whitespace, byte order, version and
synthetic markings. The current model covers the bundled Basic and Job Seeker
candidates and the scoped reviewed plain-text editor profile. The payloads
contain only the reviewed configuration metadata. [Catalogue schema](../src/ThirdLife.Catalog/Schema/CatalogSchema.cs) [Profile schema](../src/ThirdLife.Catalog/Profiles/ProfileSchema.cs) [Policy snapshot](../src/ThirdLife.Policy/Versioning/PolicySnapshot.cs)

An exported configuration preserves the original review scope. A matching digest
checks byte identity. Adoption, effective policy, package authority and permission
to dispatch are established by their existing distinct controls. A later writer
must preserve the validated payloads, confirm the chosen destination and file set,
handle collision and interruption, and avoid importing or adopting any output.
That writer and its file-lifecycle verification remain open for TL-0703.
