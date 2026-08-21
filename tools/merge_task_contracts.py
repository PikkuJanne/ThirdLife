#!/usr/bin/env python3
"""Merge ThirdLife 0.3.0 task contracts into an in-progress repository.

The canonical bundle defines task contracts and top-level governance metadata. The live
repository remains authoritative for mutable execution history. This tool preserves:

- status
- evidence
- blocked_reason
- existing notes (while adding canonical notes)

Dry-run is the default. Use --write after reviewing the summary.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import sys
from pathlib import Path
from typing import Any

import yaml

MUTABLE_FIELDS = ("status", "evidence", "blocked_reason")


def load_yaml(path: Path) -> dict[str, Any]:
    try:
        value = yaml.safe_load(path.read_text(encoding="utf-8"))
    except Exception as exc:
        raise SystemExit(f"Cannot read {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise SystemExit(f"{path}: top-level value must be a mapping")
    return value


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def merge_notes(canonical: Any, live: Any) -> list[str] | None:
    result: list[str] = []
    for source in (live, canonical):
        if source is None:
            continue
        if not isinstance(source, list) or any(not isinstance(item, str) for item in source):
            raise SystemExit("Task notes must be a list of strings")
        for item in source:
            if item not in result:
                result.append(item)
    return result or None


def build_merged(
    canonical: dict[str, Any],
    live: dict[str, Any],
    *,
    allow_extra_tasks: bool,
) -> tuple[dict[str, Any], list[str]]:
    canonical_tasks = canonical.get("tasks")
    live_tasks = live.get("tasks")
    if not isinstance(canonical_tasks, list) or not isinstance(live_tasks, list):
        raise SystemExit("Both canonical and live TASKS.yaml files must contain a tasks list")

    c_by = {task.get("id"): task for task in canonical_tasks if isinstance(task, dict)}
    l_by = {task.get("id"): task for task in live_tasks if isinstance(task, dict)}
    if None in c_by or None in l_by:
        raise SystemExit("Every task must have an id")

    missing_live = sorted(set(c_by) - set(l_by))
    extra_live = sorted(set(l_by) - set(c_by))
    if missing_live:
        raise SystemExit(
            "Live TASKS.yaml is missing canonical tasks; stop for review: " + ", ".join(missing_live)
        )
    if extra_live and not allow_extra_tasks:
        raise SystemExit(
            "Live TASKS.yaml contains tasks not present in the canonical bundle: "
            + ", ".join(extra_live)
            + ". Re-run with --allow-extra-tasks only after reviewing them."
        )

    merged = copy.deepcopy(canonical)
    merged_tasks: list[dict[str, Any]] = []
    changes: list[str] = []

    for canonical_task in canonical_tasks:
        task_id = canonical_task["id"]
        live_task = l_by[task_id]
        new_task = copy.deepcopy(canonical_task)

        for field in MUTABLE_FIELDS:
            if field in live_task:
                new_task[field] = copy.deepcopy(live_task[field])
            elif field == "evidence":
                new_task[field] = []
            elif field == "status":
                raise SystemExit(f"{task_id}: live task has no status")
            else:
                new_task.pop(field, None)

        notes = merge_notes(canonical_task.get("notes"), live_task.get("notes"))
        if notes:
            new_task["notes"] = notes
        else:
            new_task.pop("notes", None)

        contract_before = {k: v for k, v in live_task.items() if k not in MUTABLE_FIELDS and k != "notes"}
        contract_after = {k: v for k, v in new_task.items() if k not in MUTABLE_FIELDS and k != "notes"}
        if contract_before != contract_after:
            changes.append(task_id)

        merged_tasks.append(new_task)

    if extra_live:
        merged_tasks.extend(copy.deepcopy(l_by[task_id]) for task_id in extra_live)

    merged["tasks"] = merged_tasks
    return merged, changes


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--canonical",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "TASKS.yaml",
        help="canonical 0.3.0 TASKS.yaml (default: bundle TASKS.yaml)",
    )
    parser.add_argument("--target", type=Path, required=True, help="live repository TASKS.yaml")
    parser.add_argument("--write", action="store_true", help="write the merged file; otherwise dry-run")
    parser.add_argument(
        "--allow-extra-tasks",
        action="store_true",
        help="preserve live tasks absent from canonical bundle after explicit review",
    )
    args = parser.parse_args()

    canonical_path = args.canonical.resolve()
    target_path = args.target.resolve()
    if canonical_path == target_path:
        raise SystemExit("Canonical and target paths must be different")
    if not canonical_path.is_file() or not target_path.is_file():
        raise SystemExit("Canonical and target TASKS.yaml files must exist")

    canonical = load_yaml(canonical_path)
    live = load_yaml(target_path)
    merged, changed_contracts = build_merged(
        canonical, live, allow_extra_tasks=args.allow_extra_tasks
    )

    original_mutable = {
        task["id"]: {field: copy.deepcopy(task.get(field)) for field in MUTABLE_FIELDS}
        for task in live["tasks"]
        if isinstance(task, dict) and "id" in task
    }
    merged_mutable = {
        task["id"]: {field: copy.deepcopy(task.get(field)) for field in MUTABLE_FIELDS}
        for task in merged["tasks"]
        if isinstance(task, dict) and "id" in task
    }
    if any(merged_mutable.get(task_id) != values for task_id, values in original_mutable.items()):
        raise SystemExit("Internal safety check failed: mutable execution state would change")

    output = yaml.safe_dump(merged, sort_keys=False, allow_unicode=True, width=120)
    print(f"Canonical: {canonical_path}")
    print(f"Target:    {target_path}")
    print(f"Target SHA-256 before: {sha256(target_path)}")
    print(f"Task contracts changed: {len(changed_contracts)}")
    if changed_contracts:
        print("Changed task IDs: " + ", ".join(changed_contracts))
    print("Mutable status/evidence/blocked_reason values: PRESERVED")

    if not args.write:
        print("DRY RUN ONLY — re-run with --write after review")
        return 0

    backup = target_path.with_suffix(target_path.suffix + ".pre-v0.3.0.bak")
    if backup.exists():
        raise SystemExit(f"Backup already exists: {backup}; preserve or rename it before writing")
    backup.write_bytes(target_path.read_bytes())
    target_path.write_text(output, encoding="utf-8")
    print(f"Backup: {backup}")
    print(f"Target SHA-256 after:  {sha256(target_path)}")
    print("WROTE MERGED TASK GRAPH — run bundle/repository validation now")
    return 0


if __name__ == "__main__":
    sys.exit(main())
