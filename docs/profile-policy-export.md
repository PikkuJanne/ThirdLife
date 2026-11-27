# Profile and policy export

Open **Prepare**, select a configuration in **Configuration export** and choose
**Preview configuration ZIP**. The preview shows the profile version, file list,
byte counts and SHA-256 digests. A synthetic candidate is identified in the
selector. Changing the selection clears its preview.

Choose **Choose folder and save ZIP** to select an existing local Windows folder.
ThirdLife saves the exact reviewed ZIP to a protected `ThirdLifeConfiguration`
subfolder. Publication uses a restricted temporary file and an atomic rename.
An occupied archive name preserves the existing file. A cancelled picker
preserves the preview for another attempt. [Archive store](../src/ThirdLife.Reports/Configuration/ConfigurationArchiveStore.cs)

| File | Validated schema | Validation binding |
| --- | --- | --- |
| `profile.json` | `thirdlife.profile.v1` | Profile ID and version, declared catalogue and policy references, bounded capabilities and recipient-controlled deferrals. Parsing binds the exact validated catalogue digest. |
| `catalog.json` | `thirdlife.catalog.v1` | Exact catalogue ID, version and digest used to validate the profile. |
| `policy.json` | `thirdlife.policy.v2` | Exact validated policy bytes for the profile's policy ID and version. The compiled candidate or reviewed package policy must agree with the profile's synthetic status. |

Catalogue and profile schemas hash and validate one owned byte snapshot and
retain that snapshot. Each ZIP member preserves the original bytes, including
whitespace, version and synthetic markings. The current model covers the
bundled Basic and Job Seeker candidates and the scoped reviewed plain-text
editor profile. The payloads contain only the reviewed configuration metadata.
[Archive preview](../src/ThirdLife.Reports/Configuration/ConfigurationArchivePreview.cs)
[Catalogue schema](../src/ThirdLife.Catalog/Schema/CatalogSchema.cs)
[Profile schema](../src/ThirdLife.Catalog/Profiles/ProfileSchema.cs)
[Policy snapshot](../src/ThirdLife.Policy/Versioning/PolicySnapshot.cs)

An exported configuration preserves the original review scope. A matching digest
checks byte identity. Adoption, effective policy, package authority and permission
to dispatch retain their existing controls. The export is a copy of the bundled
configuration and leaves the preparation plan and saved job history intact.
