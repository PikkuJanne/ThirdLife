# ThirdLife Setup Core — Future Assembly Notes

**Status:** Non-binding deferred backlog  
**Bundle version:** 0.3.0  
**Portfolio baseline:** 2.1  
**Current project:** Team B / B1 — ThirdLife Setup Core  
**Implementation owner for these ideas:** Team B / B4 — ThirdLife Deployment and Suite Assembly

Nothing in this file is an active B1 requirement or a `TASKS.yaml` dependency. Codex may add a concise note when it discovers a future opportunity, but must not implement it, inspect a sibling repository, or block current work on it.

## Rules for entries

Each note must state:

- target user journey and user group;
- frozen product release/interface sheet required;
- ordinary manual file/launch path available without the adapter;
- proposed shallow action;
- exact data and privileges touched;
- manual fallback;
- why the repeated burden justifies an adapter;
- security/privacy/accessibility/maintenance risks;
- B4 decision status.

Do not propose private database access, hidden content processing, shared content stores, synchronized releases, or a universal integration framework.

## Known portfolio opportunities — deferred to B4

| Stable product | Potential shallow ThirdLife treatment | Explicit no-go |
|---|---|---|
| PaperWorkShell | Install/update/remove; launch; optional file association; open a user-selected document or documented workspace; profile guidance. | Reading/indexing its document database, controlling interactive redaction, changing source files, or developing against its active branch. |
| CaptionKit | Install the app and reviewed model packages; launch user-selected media; explain storage/resource choices; open exported TXT/SRT/VTT files. | Accessing recordings/transcripts, starting hidden transcription, or centrally storing media/model/user content. |
| Scam Explainer | Install; launch; open user-selected text/report input; include in a support profile. | Reading inboxes, automatically submitting messages/URLs to third parties, or presenting results as a suite security guarantee. |
| Job Application Studio | Install; launch; open a documented workspace/export; include in a Job Seeker profile; show optional-tool guidance. | Reading/ranking applications, rewriting content, uploading records, or submitting to employers. |
| Charity Cyber Check | Install the application and frozen optional collectors; include in a Community Organization profile; launch a scoped assessment. | Ingesting assessment evidence, running collectors without authorization, accessing beneficiary data, or claiming certification. |
| Backup Circle | Install; launch; include in a resilience profile; show restore/setup documentation or a future independently useful non-sensitive status. | Storing keys/credentials/repository indexes, creating or changing backup jobs silently, or operating repositories on the user's behalf. |

## Generic future B4 work

- verified offline package cache and deployment media;
- catalogue entries for exact stable versions and hashes;
- Job Seeker, Student, Family, Senior, and Community Organization profiles;
- install/update/remove and partial-profile behavior;
- launch/open actions using normal operating-system behavior;
- compatibility states: supported, supported with limitation, unverified newer version, unsupported;
- adapter disablement and manual fallback;
- black-box install, launch/open, update, rollback, uninstall, offline, and data-preservation tests;
- suite handover and separate support collection.

## Entry template

```markdown
### FA-XXX — Short title

- Status: proposed | deferred | rejected | approved-for-B4
- Target user journey:
- Target frozen release/interface revision:
- Manual standalone path:
- Proposed shallow action:
- Data touched:
- Privilege required:
- Failure/manual fallback:
- User-value evidence needed:
- Security/privacy/accessibility risks:
- Maintenance owner and version boundary:
- Reason this is not B1 work:
```
