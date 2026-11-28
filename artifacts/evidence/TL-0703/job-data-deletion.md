# TL-0703 separate job-data deletion

The application removal contract preserves workshop data. **Jobs** provides
a separate permanent deletion action for a selected archived job. It shows
the job code and its attributed local files, requires an operator code and
confirmation, and records approval before removing files.

Schema 22 binds approval to the exact archive checkpoint and sorted local
file manifest. Before a generated report or guide is written, the SQLite
store commits its filename, byte length, digest and job ownership. A second
guarded transaction verifies the reservation and excludes conflicting
archive deletion approval while the bounded publisher runs. The original
File History sample publisher uses the saved preparation record under the
same writer reservation. The verified file receipt used for handover sign-off
remains separate from the ownership intent.

The delete action checks the job directory, unresolved action recovery and
conflicting claims on the files. It acquires each recorded file under its
registered root, checks ownership, protection, name, final handle, link
count, length and digest, then marks the same handle for removal. An already
absent file can be reconciled after a failed attempt. A refused file leaves
the same job and approval available, with the refused names shown in Jobs.
SQLite commits the job-row removal after the exact file results are reconciled.
Shared policy snapshots survive. A verified derived directory can be
reconciled separately if the post-commit directory step is interrupted.

## Observations

- The real Jobs surface opened the archived fixture and displayed the exact
  review. Confirmation and operator code were needed to enable the deletion
  button. A locked report left the job archived with
  the approved deletion pending. The job reopened with a separate Continue
  action. The first attempt removed the guide and original sample. The retry
  removed the report and selected job. The other job
  reopened. The unrelated report and recovered sample retained their exact bytes.
- The child-process interruption test stopped after handle-based removal of
  a synthetic file and before SQLite committed. The parent independently reopened
  the same archive checkpoint, approval and manifest, reconciled the missing
  file and completed job-row removal. The process test uses a scoped synthetic
  callback. Reports fixtures and the Jobs journey exercise the production
  registered-file adapters.
- Windows path fixtures covered links, junctions, changed bytes, widened access
  rules, missing folders, publication collision, and exact matching versus partial
  staging. A partial or changed stage retains its recorded file for review.
  Existing Diagnostics cleanup kept its previous behavior.
- Schema 21 upgraded to 22 with historical report and guide receipts available
  for the exact job. Tests covered concurrent publication and approval,
  stale previews, other-job claims, unowned directory entries, and preservation of
  a policy snapshot referenced by a surviving job. The original file-and-database
  state can be checked using the protected store after reopening.
- A later evidence checkpoint invalidated an earlier approval preview. The
  normal sanitization input now also asks the store to enforce the active-job
  check. A pending support export held deletion until that exact export recorded
  a terminal receipt, including when another export had already reached its terminal state.

The first WPF integration run exposed two compatibility regressions. The store now
allows report and guide publication for archives without a deletion approval, and
retains the bounded publisher's typed error so the app can show the existing storage
limit message. The unchanged `b2fa3db` deterministic WPF baseline passed 10
checks in nine seconds. An earlier broad filter discovered a separately selected
active-machine File History case that requires an exact
`THIRDLIFE_FILE_HISTORY_TEST_VOLUME`. No native restore is claimed here.

This evidence covers controlled jobs, logical SQLite deletion and attributable
local files. Export destinations, recovered samples, earlier local files without
reliable attribution and Windows backup copies follow their owners' retention.
Timed automatic expiry and the separate recovery-copy workflow retain their
documented scope. [Data map](../../../docs/data-locations.md)
[Uninstall and data preservation](../../../docs/uninstall-and-data-preservation.md)
