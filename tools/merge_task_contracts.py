#!/usr/bin/env python3
"""Reconcile a reviewed task amendment with live work and evidence.

The candidate may revise implementation contracts and add tasks. Keep live execution
state, completed task contracts, and all recorded evidence. Dry-run by default.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import sys
from pathlib import Path
from typing import Any

import yaml

from validate_bundle import PREREQUISITE_FIELDS, TASK_ID_RE, Validation, load_yaml


def read_task_document(path: Path) -> dict[str, Any]:
    validation = Validation()
    document = load_yaml(path, validation)
    if validation.errors:
        raise SystemExit("\n".join(validation.errors))
    return document


def indexed_tasks(document: dict[str, Any], label: str) -> dict[str, dict[str, Any]]:
    tasks = document.get("tasks")
    if not isinstance(tasks, list):
        raise SystemExit(f"{label}: expected a task list")
    index: dict[str, dict[str, Any]] = {}
    for task in tasks:
        task_id = task.get("id") if isinstance(task, dict) else None
        if not isinstance(task_id, str) or not TASK_ID_RE.fullmatch(task_id):
            raise SystemExit(f"{label}: invalid task ID {task_id!r}")
        if task_id in index:
            raise SystemExit(f"{label}: duplicate task ID {task_id}")
        index[task_id] = task
    return index


def append_distinct(live: list[Any], candidate: list[Any]) -> list[Any]:
    result = copy.deepcopy(live)
    for item in candidate:
        if item not in result:
            result.append(copy.deepcopy(item))
    return result


def preserve_prerequisite_evidence(live: dict[str, Any], updated: dict[str, Any]) -> None:
    for field in PREREQUISITE_FIELDS:
        for previous in live.get(field, []):
            proof = previous.get("evidence")
            if not proof:
                continue
            scope = {key: value for key, value in previous.items() if key != "evidence"}
            matching = next(
                (
                    entry for entry in updated.get(field, [])
                    if {key: value for key, value in entry.items() if key != "evidence"} == scope
                ),
                None,
            )
            if matching is not None and not matching.get("evidence"):
                matching["evidence"] = proof
                continue
            if matching is not None and matching["evidence"] == proof:
                continue
            prior_evidence = {
                "summary": f"Earlier {field}: {previous['scope']}",
                "reference": proof,
            }
            updated["evidence"] = append_distinct(updated["evidence"], [prior_evidence])


def preserve_checkpoints(
    live: dict[str, Any], candidate: dict[str, Any], updated: dict[str, Any]
) -> None:
    current = {
        entry["reference"]: copy.deepcopy(entry) for entry in live.get("checkpoints", [])
    }
    for entry in candidate.get("checkpoints", []):
        reference = entry["reference"]
        prior = current.get(reference)
        if prior is None:
            current[reference] = copy.deepcopy(entry)
        elif prior != entry:
            integrate = entry["state"] == "integrated" and prior["state"] == "retained"
            historical = prior if integrate else entry
            updated["evidence"] = append_distinct(updated["evidence"], [{
                "summary": historical["verification"], "reference": reference,
            }])
            if integrate:
                current[reference] = copy.deepcopy(entry)
    if current:
        updated["checkpoints"] = list(current.values())


def merge_task(live: dict[str, Any], candidate: dict[str, Any]) -> dict[str, Any]:
    if "status" not in live:
        raise SystemExit(f"{live['id']}: live task has no status")
    completed = live["status"] in {"done", "cancelled"}
    updated = copy.deepcopy(live if completed else candidate)
    updated["status"] = live["status"]
    if "blocked_reason" in live:
        updated["blocked_reason"] = live["blocked_reason"]
    else:
        updated.pop("blocked_reason", None)

    updated["evidence"] = append_distinct(live.get("evidence", []), candidate.get("evidence", []))
    notes = append_distinct(live.get("notes", []), candidate.get("notes", []))
    if notes:
        updated["notes"] = notes
    preserve_checkpoints(live, candidate, updated)
    preserve_prerequisite_evidence(live, updated)
    return updated


def build_merged(
    candidate: dict[str, Any], live: dict[str, Any]
) -> tuple[dict[str, Any], list[str]]:
    proposed = indexed_tasks(candidate, "Candidate")
    current = indexed_tasks(live, "Live")
    merged = copy.deepcopy(candidate)
    result: list[dict[str, Any]] = []
    changes: list[str] = []
    for task_id in sorted(proposed.keys() | current.keys()):
        if task_id not in proposed:
            result.append(copy.deepcopy(current[task_id]))
            continue
        if task_id not in current:
            updated = copy.deepcopy(proposed[task_id])
        else:
            updated = merge_task(current[task_id], proposed[task_id])
        if updated != current.get(task_id):
            changes.append(task_id)
        result.append(updated)
    merged["tasks"] = result
    return merged, changes


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--canonical", type=Path, required=True, metavar="CANDIDATE",
        help="reviewed candidate task amendment",
    )
    parser.add_argument("--target", type=Path, required=True, help="live repository TASKS.yaml")
    parser.add_argument("--write", action="store_true", help="write the merged file; otherwise dry-run")
    args = parser.parse_args()

    candidate_path = args.canonical.resolve()
    target_path = args.target.resolve()
    if candidate_path == target_path:
        raise SystemExit("Candidate and target paths must be different.")
    if not candidate_path.is_file() or not target_path.is_file():
        raise SystemExit("Candidate and target task files must exist.")
    candidate = read_task_document(candidate_path)
    live = read_task_document(target_path)
    merged, changes = build_merged(candidate, live)

    original = target_path.read_bytes()
    output = yaml.safe_dump(merged, sort_keys=False, allow_unicode=True, width=120)
    print(f"Candidate: {candidate_path}")
    print(f"Target:    {target_path}")
    print(f"Target SHA-256 before: {hashlib.sha256(original).hexdigest()}")
    print("Changed task records: " + (", ".join(changes) if changes else "none"))
    if not args.write:
        print("Dry run. Review the inputs and use --write to record the amendment.")
        return 0

    backup = target_path.with_suffix(target_path.suffix + ".before-contract-merge.bak")
    staged = target_path.with_suffix(target_path.suffix + ".contract-merge.tmp")
    with backup.open("xb") as stream:
        stream.write(original)
    with staged.open("x", encoding="utf-8", newline="\n") as stream:
        stream.write(output)
    staged.replace(target_path)
    print(f"Backup: {backup}")
    print(f"Target SHA-256 after:  {hashlib.sha256(target_path.read_bytes()).hexdigest()}")
    print("Wrote task records. Verify the merged bundle before integration.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
