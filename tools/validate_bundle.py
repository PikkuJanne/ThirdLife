#!/usr/bin/env python3
"""Validate the ThirdLife roadmap bundle.

Run from any directory:
    python tools/validate_bundle.py
"""

from __future__ import annotations

import json
import re
import sys
from collections import defaultdict, deque
from pathlib import Path
from typing import Any

import yaml


ROOT = Path(__file__).resolve().parents[1]
REQUIRED_FILES = (
    "ROADMAP.md",
    "DECISIONS.md",
    "TASKS.yaml",
    "AGENTS.md",
    "CODEX_START_PROMPT.md",
    "README.md",
    "TASKS.schema.json",
    "PROJECT_BOUNDARY.md",
    "SECURITY.md",
    "ACCESSIBILITY.md",
    "LOW_SPEC.md",
    "RELEASE_INTERFACE.md",
    "FUTURE_ASSEMBLY_NOTES.md",
    "CHANGELOG.md",
    "tools/requirements.txt",
)
TASK_ID_RE = re.compile(r"^TL-\d{4}$")
DECISION_ID_RE = re.compile(r"^D-\d{3}$")
MILESTONE_ID_RE = re.compile(r"^M\d+$")
DECISION_HEADING_RE = re.compile(r"^### (D-\d{3})\s+—\s+.+$", re.MULTILINE)
FORBIDDEN_BRAND_RE = re.compile("second" + r"\s*" + "life", re.IGNORECASE)

ALLOWED_STATUS = {"backlog", "ready", "in_progress", "blocked", "review", "done", "cancelled"}
ALLOWED_EXECUTOR = {"codex", "hybrid", "human"}
ALLOWED_ENVIRONMENT = {"any", "windows"}
ALLOWED_SIZE = {"S", "M", "L", "XL"}
ALLOWED_PRIORITY = {"P0", "P1", "P2", "P3"}
ALLOWED_KIND = {"build", "code", "data", "docs", "gate", "release", "security", "spike", "test"}
MUTABLE_FIELDS = ["status", "evidence", "blocked_reason"]


class Validation:
    def __init__(self) -> None:
        self.errors: list[str] = []
        self.warnings: list[str] = []

    def error(self, message: str) -> None:
        self.errors.append(message)

    def warn(self, message: str) -> None:
        self.warnings.append(message)


def load_yaml(path: Path, validation: Validation) -> dict[str, Any]:
    try:
        value = yaml.safe_load(path.read_text(encoding="utf-8"))
    except Exception as exc:  # diagnostic boundary
        validation.error(f"{path.name}: cannot parse YAML: {exc}")
        return {}
    if not isinstance(value, dict):
        validation.error(f"{path.name}: top-level value must be a mapping")
        return {}
    return value


def require_nonempty_string(
    owner: str, mapping: dict[str, Any], field: str, validation: Validation
) -> None:
    value = mapping.get(field)
    if not isinstance(value, str) or not value.strip():
        validation.error(f"{owner}: {field} must be a non-empty string")


def require_nonempty_string_list(
    owner: str,
    mapping: dict[str, Any],
    field: str,
    validation: Validation,
    *,
    allow_empty: bool = False,
) -> None:
    value = mapping.get(field)
    if not isinstance(value, list):
        validation.error(f"{owner}: {field} must be a list")
        return
    if not allow_empty and not value:
        validation.error(f"{owner}: {field} must not be empty")
    for index, item in enumerate(value):
        if not isinstance(item, str) or not item.strip():
            validation.error(f"{owner}: {field}[{index}] must be a non-empty string")


def topological_order(
    task_ids: set[str],
    dependencies: dict[str, list[str]],
    validation: Validation,
) -> list[str]:
    children: dict[str, list[str]] = defaultdict(list)
    indegree = {task_id: 0 for task_id in task_ids}
    for task_id, deps in dependencies.items():
        for dep in deps:
            if dep not in task_ids:
                continue
            children[dep].append(task_id)
            indegree[task_id] += 1

    queue = deque(sorted(task_id for task_id, degree in indegree.items() if degree == 0))
    result: list[str] = []
    while queue:
        current = queue.popleft()
        result.append(current)
        for child in sorted(children[current]):
            indegree[child] -= 1
            if indegree[child] == 0:
                queue.append(child)

    if len(result) != len(task_ids):
        cyclic = sorted(task_id for task_id, degree in indegree.items() if degree > 0)
        validation.error(f"TASKS.yaml: dependency graph contains a cycle involving {cyclic}")
    return result


def ancestors(task_id: str, dependencies: dict[str, list[str]]) -> set[str]:
    found: set[str] = set()
    stack = list(dependencies.get(task_id, []))
    while stack:
        current = stack.pop()
        if current in found:
            continue
        found.add(current)
        stack.extend(dependencies.get(current, []))
    return found


def validate() -> int:
    validation = Validation()

    for relative in REQUIRED_FILES:
        path = ROOT / relative
        if not path.is_file():
            validation.error(f"Missing required file: {relative}")

    if validation.errors:
        return report(validation)

    task_doc = load_yaml(ROOT / "TASKS.yaml", validation)
    try:
        json.loads((ROOT / "TASKS.schema.json").read_text(encoding="utf-8"))
    except Exception as exc:
        validation.error(f"TASKS.schema.json: cannot parse JSON: {exc}")

    # Optional full JSON Schema validation when jsonschema is available.
    try:
        import jsonschema  # type: ignore

        schema = json.loads((ROOT / "TASKS.schema.json").read_text(encoding="utf-8"))
        jsonschema.Draft202012Validator(schema).validate(task_doc)
    except ModuleNotFoundError:
        validation.warn("jsonschema is not installed; custom structural validation was used")
    except Exception as exc:
        validation.error(f"TASKS.yaml does not satisfy TASKS.schema.json: {exc}")

    if task_doc.get("schema_version") != 1:
        validation.error("TASKS.yaml: schema_version must equal 1")
    project = task_doc.get("project", {})
    if project.get("name") != "ThirdLife":
        validation.error("TASKS.yaml: project.name must equal ThirdLife")
    expected_project = {
        "component": "ThirdLife Setup Core",
        "owner_team": "Team B",
        "queue_position": "B1",
        "pilot_gate": "TL-0611",
        "standalone_release_gate": "TL-0710",
        "future_assembly_project": "ThirdLife Deployment and Suite Assembly",
        "future_assembly_queue_position": "B4",
    }
    for field, expected in expected_project.items():
        if project.get(field) != expected:
            validation.error(
                f"TASKS.yaml: project.{field} must equal {expected!r}"
            )

    portfolio = task_doc.get("portfolio", {})
    expected_portfolio = {
        "roadmap_version": "2.0",
        "development_posture": "standalone project vacuum",
        "integration_posture": "late binding against frozen stable releases",
        "active_cross_project_dependencies_allowed": False,
        "sibling_specific_work_authorized": False,
        "next_team_project_after_stable": "Scam Explainer",
    }
    for field, expected in expected_portfolio.items():
        if portfolio.get(field) != expected:
            validation.error(
                f"TASKS.yaml: portfolio.{field} must equal {expected!r}"
            )
    if task_doc.get("workflow", {}).get("mutable_task_fields") != MUTABLE_FIELDS:
        validation.error(
            f"TASKS.yaml: workflow.mutable_task_fields must equal {MUTABLE_FIELDS!r}"
        )

    expected_authority = [
        "DECISIONS.md",
        "ROADMAP.md",
        "PROJECT_BOUNDARY.md",
        "SECURITY.md",
        "ACCESSIBILITY.md",
        "LOW_SPEC.md",
        "AGENTS.md",
        "TASKS.yaml",
        "CODEX_START_PROMPT.md",
        "README.md",
    ]
    if task_doc.get("authority", {}).get("precedence") != expected_authority:
        validation.error(
            f"TASKS.yaml: authority.precedence must equal {expected_authority!r}"
        )

    decisions_text = (ROOT / "DECISIONS.md").read_text(encoding="utf-8")
    decision_ids = DECISION_HEADING_RE.findall(decisions_text)
    decision_set = set(decision_ids)
    if len(decision_ids) != len(decision_set):
        validation.error("DECISIONS.md: duplicate decision headings")
    expected_decision_sequence = [f"D-{index:03d}" for index in range(1, len(decision_ids) + 1)]
    if decision_ids != expected_decision_sequence:
        validation.error(
            "DECISIONS.md: decision headings must be contiguous and ordered from D-001"
        )

    milestones = task_doc.get("milestones")
    if not isinstance(milestones, list) or not milestones:
        validation.error("TASKS.yaml: milestones must be a non-empty list")
        milestones = []

    milestone_ids: list[str] = []
    milestone_by_id: dict[str, dict[str, Any]] = {}
    for index, milestone in enumerate(milestones):
        owner = f"milestones[{index}]"
        if not isinstance(milestone, dict):
            validation.error(f"{owner}: must be a mapping")
            continue
        milestone_id = milestone.get("id")
        if not isinstance(milestone_id, str) or not MILESTONE_ID_RE.fullmatch(milestone_id):
            validation.error(f"{owner}: invalid id {milestone_id!r}")
            continue
        if milestone_id in milestone_by_id:
            validation.error(f"TASKS.yaml: duplicate milestone id {milestone_id}")
        milestone_ids.append(milestone_id)
        milestone_by_id[milestone_id] = milestone
        for field in ("name", "objective", "gate_task"):
            require_nonempty_string(owner, milestone, field, validation)
        require_nonempty_string_list(owner, milestone, "exit_criteria", validation)

    expected_milestones = [f"M{index}" for index in range(len(milestone_ids))]
    if milestone_ids != expected_milestones:
        validation.error(
            f"TASKS.yaml: milestones must be contiguous and ordered: expected {expected_milestones}"
        )
    if milestone_ids != [f"M{index}" for index in range(8)]:
        validation.error("TASKS.yaml: portfolio-aligned bundle must contain M0 through M7")
    elif milestone_by_id.get("M7", {}).get("gate_task") != "TL-0710":
        validation.error("TASKS.yaml: M7 gate_task must equal TL-0710")
    milestone_rank = {milestone_id: index for index, milestone_id in enumerate(milestone_ids)}

    tasks = task_doc.get("tasks")
    if not isinstance(tasks, list) or not tasks:
        validation.error("TASKS.yaml: tasks must be a non-empty list")
        tasks = []

    task_by_id: dict[str, dict[str, Any]] = {}
    dependency_map: dict[str, list[str]] = {}
    task_order: list[str] = []

    required_task_fields = {
        "id",
        "title",
        "milestone",
        "workstream",
        "kind",
        "priority",
        "status",
        "size",
        "executor",
        "environment",
        "depends_on",
        "decision_refs",
        "objective",
        "deliverables",
        "acceptance_criteria",
        "verification",
        "evidence",
    }
    allowed_task_fields = required_task_fields | {
        "human_evidence_required",
        "notes",
        "blocked_reason",
    }

    for index, task in enumerate(tasks):
        owner = f"tasks[{index}]"
        if not isinstance(task, dict):
            validation.error(f"{owner}: must be a mapping")
            continue

        missing = sorted(required_task_fields - set(task))
        unknown = sorted(set(task) - allowed_task_fields)
        if missing:
            validation.error(f"{owner}: missing fields {missing}")
        if unknown:
            validation.error(f"{owner}: unknown fields {unknown}")

        task_id = task.get("id")
        if not isinstance(task_id, str) or not TASK_ID_RE.fullmatch(task_id):
            validation.error(f"{owner}: invalid task id {task_id!r}")
            continue
        if task_id in task_by_id:
            validation.error(f"TASKS.yaml: duplicate task id {task_id}")
        task_by_id[task_id] = task
        task_order.append(task_id)

        for field in ("title", "milestone", "workstream", "objective"):
            require_nonempty_string(task_id, task, field, validation)
        for field in ("deliverables", "acceptance_criteria", "verification"):
            require_nonempty_string_list(task_id, task, field, validation)

        status = task.get("status")
        if status not in ALLOWED_STATUS:
            validation.error(f"{task_id}: invalid status {status!r}")
        executor = task.get("executor")
        if executor not in ALLOWED_EXECUTOR:
            validation.error(f"{task_id}: invalid executor {executor!r}")
        environment = task.get("environment")
        if environment not in ALLOWED_ENVIRONMENT:
            validation.error(f"{task_id}: invalid environment {environment!r}")
        if task.get("size") not in ALLOWED_SIZE:
            validation.error(f"{task_id}: invalid size {task.get('size')!r}")
        if task.get("priority") not in ALLOWED_PRIORITY:
            validation.error(f"{task_id}: invalid priority {task.get('priority')!r}")
        if task.get("kind") not in ALLOWED_KIND:
            validation.error(f"{task_id}: invalid kind {task.get('kind')!r}")

        milestone_id = task.get("milestone")
        if milestone_id not in milestone_by_id:
            validation.error(f"{task_id}: unknown milestone {milestone_id!r}")

        deps = task.get("depends_on")
        if not isinstance(deps, list) or any(not isinstance(dep, str) for dep in deps):
            validation.error(f"{task_id}: depends_on must be a list of task IDs")
            deps = []
        if len(deps) != len(set(deps)):
            validation.error(f"{task_id}: duplicate dependencies")
        if task_id in deps:
            validation.error(f"{task_id}: cannot depend on itself")
        dependency_map[task_id] = list(deps)

        refs = task.get("decision_refs")
        if not isinstance(refs, list) or not refs:
            validation.error(f"{task_id}: decision_refs must be a non-empty list")
            refs = []
        if len(refs) != len(set(refs)):
            validation.error(f"{task_id}: duplicate decision_refs")
        for ref in refs:
            if not isinstance(ref, str) or not DECISION_ID_RE.fullmatch(ref):
                validation.error(f"{task_id}: invalid decision reference {ref!r}")
            elif ref not in decision_set:
                validation.error(f"{task_id}: unknown decision reference {ref}")

        evidence = task.get("evidence")
        if not isinstance(evidence, list):
            validation.error(f"{task_id}: evidence must be a list")

        if executor in {"hybrid", "human"}:
            require_nonempty_string_list(
                task_id, task, "human_evidence_required", validation
            )
        if status == "blocked":
            require_nonempty_string(task_id, task, "blocked_reason", validation)
        elif "blocked_reason" in task and task.get("blocked_reason"):
            validation.warn(f"{task_id}: blocked_reason is present while status is {status}")

    if task_order != sorted(task_order):
        validation.error("TASKS.yaml: tasks must be ordered by task ID")

    task_ids = set(task_by_id)
    for task_id, deps in dependency_map.items():
        for dep in deps:
            if dep not in task_ids:
                validation.error(f"{task_id}: unknown dependency {dep}")
                continue
            own_milestone = task_by_id[task_id].get("milestone")
            dep_milestone = task_by_id[dep].get("milestone")
            if (
                own_milestone in milestone_rank
                and dep_milestone in milestone_rank
                and milestone_rank[dep_milestone] > milestone_rank[own_milestone]
            ):
                validation.error(
                    f"{task_id}: dependency {dep} is in later milestone {dep_milestone}"
                )

    topological_order(task_ids, dependency_map, validation)

    roadmap_text = (ROOT / "ROADMAP.md").read_text(encoding="utf-8")
    for task_id in sorted(task_ids):
        if f"`{task_id}`" not in roadmap_text:
            validation.error(f"ROADMAP.md: missing task reference `{task_id}`")

    # Milestone gate and closure checks.
    for milestone_id, milestone in milestone_by_id.items():
        gate_id = milestone.get("gate_task")
        gate = task_by_id.get(gate_id)
        if gate is None:
            validation.error(f"{milestone_id}: gate task {gate_id!r} does not exist")
            continue
        if gate.get("milestone") != milestone_id:
            validation.error(f"{milestone_id}: gate {gate_id} is assigned to {gate.get('milestone')}")
        if gate.get("kind") != "gate":
            validation.error(f"{milestone_id}: gate {gate_id} must have kind: gate")

        gate_ancestors = ancestors(gate_id, dependency_map)
        milestone_tasks = {
            task_id
            for task_id, task in task_by_id.items()
            if task.get("milestone") == milestone_id and task_id != gate_id
        }
        missing_from_gate = sorted(milestone_tasks - gate_ancestors)
        if missing_from_gate:
            validation.error(
                f"{milestone_id}: gate {gate_id} does not depend transitively on {missing_from_gate}"
            )

        if milestone_rank.get(milestone_id, 0) > 0:
            previous_id = milestone_ids[milestone_rank[milestone_id] - 1]
            previous_gate = milestone_by_id[previous_id].get("gate_task")
            for task_id in sorted(
                task_id
                for task_id, task in task_by_id.items()
                if task.get("milestone") == milestone_id
            ):
                if previous_gate not in ancestors(task_id, dependency_map):
                    validation.error(
                        f"{task_id}: does not depend transitively on prior gate {previous_gate}"
                    )

    # Keep active bundle content on the approved name.
    for relative in (
        "ROADMAP.md",
        "DECISIONS.md",
        "TASKS.yaml",
        "AGENTS.md",
        "CODEX_START_PROMPT.md",
        "README.md",
        "TASKS.schema.json",
        "PROJECT_BOUNDARY.md",
        "SECURITY.md",
        "ACCESSIBILITY.md",
        "LOW_SPEC.md",
        "RELEASE_INTERFACE.md",
        "FUTURE_ASSEMBLY_NOTES.md",
        "CHANGELOG.md",
    ):
        text = (ROOT / relative).read_text(encoding="utf-8")
        if FORBIDDEN_BRAND_RE.search(text):
            validation.error(f"{relative}: contains a forbidden legacy product name")

    boundary_text = (ROOT / "PROJECT_BOUNDARY.md").read_text(encoding="utf-8")
    for required_phrase in (
        "Team B / B1",
        "project vacuum",
        "ThirdLife Deployment and Suite Assembly",
        "Scam Explainer",
        "TL-0710",
    ):
        if required_phrase not in boundary_text:
            validation.error(
                f"PROJECT_BOUNDARY.md: missing required portfolio phrase {required_phrase!r}"
            )

    release_interface_text = (ROOT / "RELEASE_INTERFACE.md").read_text(encoding="utf-8")
    for required_phrase in (
        "Draft placeholder",
        "TL-0706",
        "TL-0710",
        "not a shared application API",
    ):
        if required_phrase not in release_interface_text:
            validation.error(
                f"RELEASE_INTERFACE.md: missing required boundary phrase {required_phrase!r}"
            )

    future_notes_text = (ROOT / "FUTURE_ASSEMBLY_NOTES.md").read_text(encoding="utf-8")
    for required_phrase in (
        "Non-binding deferred backlog",
        "Nothing in this file is an active B1 requirement",
        "Team B / B4",
    ):
        if required_phrase not in future_notes_text:
            validation.error(
                f"FUTURE_ASSEMBLY_NOTES.md: missing required deferral phrase {required_phrase!r}"
            )

    # Initial/ongoing selection diagnostics.
    done = {
        task_id
        for task_id, task in task_by_id.items()
        if task.get("status") == "done"
    }
    dependency_ready = [
        task_id
        for task_id in task_order
        if task_by_id[task_id].get("status") not in {"done", "cancelled"}
        and all(dep in done for dep in dependency_map.get(task_id, []))
    ]
    codex_ready = [
        task_id
        for task_id in dependency_ready
        if task_by_id[task_id].get("executor") in {"codex", "hybrid"}
        and task_by_id[task_id].get("status") in {"ready", "backlog"}
    ]
    if not codex_ready and len(done) < len(task_by_id):
        validation.warn("No Codex-ready task exists; check blockers, review tasks, or human gates")

    return report(
        validation,
        summary=(
            f"{len(task_by_id)} tasks, {len(milestone_by_id)} milestones, "
            f"{len(decision_set)} frozen decisions; DAG valid"
        ),
        codex_ready=codex_ready,
    )


def report(
    validation: Validation,
    *,
    summary: str | None = None,
    codex_ready: list[str] | None = None,
) -> int:
    for warning in validation.warnings:
        print(f"WARNING: {warning}", file=sys.stderr)
    if validation.errors:
        for error in validation.errors:
            print(f"ERROR: {error}", file=sys.stderr)
        print(f"FAILED: {len(validation.errors)} error(s)", file=sys.stderr)
        return 1

    print(f"OK: {summary or 'bundle structure valid'}")
    if codex_ready is not None:
        print("Codex-ready tasks: " + (", ".join(codex_ready) if codex_ready else "none"))
    return 0


if __name__ == "__main__":
    raise SystemExit(validate())
