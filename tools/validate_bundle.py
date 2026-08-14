#!/usr/bin/env python3
"""Validate the ThirdLife roadmap bundle.

Run from any directory:
    python tools/validate_bundle.py
"""

from __future__ import annotations

import hashlib
import ipaddress
import json
import re
import subprocess
import sys
import unicodedata
from collections import defaultdict, deque
from datetime import date, datetime
from pathlib import Path
from typing import Any, Callable
from urllib.parse import unquote

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
    "docs/change-control.md",
    "docs/glossary.md",
    "docs/non-goals.md",
    "docs/product-contract.md",
    "docs/security/abuse-cases.md",
    "docs/security/data-flow.md",
    "docs/security/threat-model.md",
    "docs/testing/accessibility-matrix.md",
    "docs/testing/device-matrix.md",
    "docs/testing/failure-injection.md",
    "docs/testing/manual-hardware-tests.md",
    "tools/requirements.txt",
    "tools/tests/test_validate_bundle.py",
)
TASK_ID_RE = re.compile(r"^TL-\d{4}$")
DECISION_ID_RE = re.compile(r"^D-\d{3}$")
MILESTONE_ID_RE = re.compile(r"^M\d+$")
DECISION_HEADING_RE = re.compile(r"^### (D-\d{3})\s+—\s+.+$", re.MULTILINE)
FORBIDDEN_BRAND_RE = re.compile(
    "second"
    + "life"
    + r"|\b"
    + "second"
    + r"(?:[\s_-]+|\*{1,3}|`{1,3}|[./\\])"
    + "life"
    + r"(?![a-z0-9_-])",
    re.IGNORECASE,
)
OPTIMIZER_FAMILY_RE = re.compile(
    r"\b(?:cleaner|optim(?:izer|iser|ization|isation)|"
    r"debloat(?:er|ing|ed)?|registry\s+(?:clean(?:er|ing|up)|optim(?:ization|isation))|"
    r"tune[\s-]*up(?:\s+utility)?|general\s+it\s+toolbox|"
    r"speed\s+up(?:\s+any)?\s+pc|boost\s+(?:your\s+|pc\s+)?performance)\b",
    re.IGNORECASE,
)
EXPLICIT_OPTIMIZER_MARKETING_RE = re.compile(
    r"\b(?:pc\s+(?:cleaner|optim(?:izer|iser|ization|isation))|"
    r"debloat(?:er|ing|ed)?|registry\s+(?:clean(?:er|ing|up)|optim(?:ization|isation))|"
    r"tune[\s-]*up(?:\s+utility)?|general\s+it\s+toolbox|"
    r"speed\s+up(?:\s+any)?\s+pc|boost\s+(?:your\s+|pc\s+)?performance)\b",
    re.IGNORECASE,
)
DENIAL_CONTEXT_RE = re.compile(
    r"\b(?:not|never|no|without|cannot|exclude(?:d|s|ing)?|"
    r"prohibit(?:ed|s|ion|ions)?|reject(?:ed|s)?|avoid(?:ed|s)?)\b|"
    r"\b(?:does|do|must)\s+not\b|\bout\s+of\s+scope\b|"
    r"\bnon[\s-]*goals?\b|\bdoes\s+not\s+own\b",
    re.IGNORECASE,
)
PRODUCT_IDENTITY_RE = re.compile(r"\bthirdlife(?:\s+setup\s+core)?\b", re.IGNORECASE)
NEGATION_START_RE = re.compile(
    r"\b(?:anything\s+but|not|never|no|cannot|without|"
    r"exclude(?:d|s|ing)?|prohibit(?:ed|s|ing)?|reject(?:ed|s|ing)?|"
    r"avoid(?:ed|s|ing)?)\b",
    re.IGNORECASE,
)
DENIAL_PREDICATE_RE = re.compile(
    r"\b(?:is|are|remain(?:s)?|must\s+be)\s+"
    r"(?:strictly\s+)?(?:prohibited|excluded|rejected|avoided|"
    r"out\s+of\s+scope|not\s+(?:allowed|supported))\b",
    re.IGNORECASE,
)
OPTIMIZER_LIST_FILLER_RE = re.compile(
    r"\bdriver[\s-]+download\s+utility\b|"
    r"\b(?:a|an|the|any|our|your|pc|generic|general|aggressive|strictly|"
    r"to|be|being|as|act|serve|function|operate|offer|provide|market|"
    r"position|describe|consider|regard|classify|characterize|treat|use|call|see|"
    r"designed|intended|marketed|positioned|described|considered|regarded|"
    r"classified|characterized|treated|used|called|seen|"
    r"and|or|nor|product|positioning|behaviou?r)\b|[\s,()/&]+",
    re.IGNORECASE,
)
NEGATIVE_SCOPE_HEADING_RE = re.compile(
    r"\b(?:does\s+not\s+own|out\s+of\s+scope|non[\s-]*goals?|"
    r"security\s+and\s+safety\s+prohibitions?)\b",
    re.IGNORECASE,
)

AUTHORITY_ORDER = (
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
)

ASSET_IDS = tuple(f"AST-{index:02d}" for index in range(1, 13))
ACTOR_IDS = tuple(f"ACT-{index:02d}" for index in range(1, 11))
THREAT_IDS = tuple(f"THR-{index:03d}" for index in range(1, 15))
ABUSE_CASE_IDS = tuple(f"AC-{index:03d}" for index in range(1, 20))
RESIDUAL_RISK_IDS = tuple(f"RR-{index:03d}" for index in range(1, 9))
ENTITY_IDS = tuple(f"E-{index:02d}" for index in range(1, 9))
PROCESS_IDS = tuple(f"P-{index:02d}" for index in range(1, 10))
STORE_IDS = tuple(f"DS-{index:02d}" for index in range(1, 8))
FLOW_IDS = tuple(f"F-{index:02d}" for index in range(1, 20))
FLOW_REFERENCE_IDS = (*FLOW_IDS, "F-08a", "F-08b")
SECURITY_BOUNDARY_IDS = (
    "TB-UI",
    "TB-PROVIDER",
    "TB-BROKER",
    "TB-SYSTEM",
    "TB-PACKAGE-SOURCE",
    "TB-JOB-STORE",
    "TB-EXPORT",
    "TB-RECIPIENT",
    "TB-RELEASE-SUPPLY",
    "TB-FUTURE-B4",
)
TESTING_DOCUMENTS = (
    "docs/testing/accessibility-matrix.md",
    "docs/testing/device-matrix.md",
    "docs/testing/failure-injection.md",
    "docs/testing/manual-hardware-tests.md",
)
TESTING_STATUS_DRAFT = "Draft procedure; human evidence pending"
TESTING_STATUS_RECORDED = "Procedure recorded; reference-device evidence complete"
TESTING_REVISION = "TL-0008 draft 1"
TEST_RESULT_VALUES = ("Pass", "Fail", "Not available", "Not run")
EVIDENCE_CLASS_VALUES = ("Observed", "Inferred", "Not available", "Human confirmed")
LOW_SPEC_RECORD_FIELDS = (
    "schema_version",
    "record_kind",
    "record_id",
    "benchmark_id",
    "procedure_revision",
    "thirdlife_version",
    "source_revision",
    "recorded_at_with_offset",
    "operator_role",
    "reviewer_role",
    "hardware_environment",
    "execution_context",
    "constraint_applied",
    "device_id",
    "windows_edition",
    "windows_build",
    "windows_architecture",
    "windows_support_state",
    "cpu_model_class",
    "physical_core_count",
    "logical_processor_count",
    "imposed_cpu_or_priority_constraint",
    "installed_memory_mib",
    "available_memory_mib_at_start",
    "imposed_memory_constraint",
    "storage_type",
    "free_space_mib_at_start",
    "free_space_mib_at_end",
    "imposed_storage_condition",
    "network_profile",
    "network_bandwidth_latency_loss_filtering",
    "power_source_and_battery_state",
    "gpu_acceleration_state",
    "cpu_fallback_result",
    "fixture_id",
    "fixture_version",
    "fixture_sha256",
    "workload_variant",
    "workload_protocol_version",
    "measurement_start_trigger",
    "measurement_stop_trigger",
    "reset_and_stabilization_rule",
    "measurement_tool_and_version",
    "instrumentation_overhead",
    "preflight_result_and_rollback_headroom",
    "baseline_background_activity",
    "pending_update_and_restart_state",
    "measured_process_scope",
    "repetition_count",
    "fewer_than_three_repetitions_reason",
    "raw_measurement_artifact_refs",
    "elapsed_time_ms_each",
    "elapsed_time_ms_min_median_max",
    "cpu_time_ms_each",
    "startup_time_ms_each",
    "idle_working_set_mib_each",
    "peak_working_set_mib_each",
    "peak_commit_mib_each",
    "temporary_storage_peak_mib_each",
    "final_output_size_mib_each",
    "database_log_cache_size_before_after_mib",
    "ui_responsiveness_method_and_observation",
    "cancellation_latency_ms_and_result",
    "resume_duration_ms_and_result",
    "completion_state",
    "budget_revision",
    "budget_result",
    "data_integrity_result",
    "cleanup_result_and_residue",
    "accessibility_check_ids_results_environment_evidence",
    "security_check_ids_results_evidence",
    "test_result",
    "evidence_class",
    "provider_or_human_reviewer",
    "provenance",
    "limitations",
    "defect_or_blocker_ids",
    "sanitized_artifact_sha256_refs",
)
SECURITY_MACHINE_PATH_RE = re.compile(
    r"(?i)(?:\b[a-z]:[\\/]|\\\\[^\\/\s]+[\\/][^\\/\s]+|"
    r"file:/+(?:[a-z]:|/)|/(?:home|users)/[^/\s]+/)"
)
BROKER_TASK_IDS = {
    "TL-0303",
    "TL-0307",
    "TL-0309",
    "TL-0310",
    "TL-0311",
    "TL-0312",
    "TL-0313",
}
PACKAGE_TASK_IDS = {
    "TL-0006",
    "TL-0301",
    "TL-0307",
    "TL-0401",
    "TL-0402",
    "TL-0403",
    "TL-0404",
    "TL-0405",
    "TL-0406",
    "TL-0407",
    "TL-0408",
    "TL-0409",
    "TL-0503",
    "TL-0504",
    "TL-0505",
    "TL-0507",
    "TL-0508",
    "TL-0509",
    "TL-0510",
    "TL-0609",
}

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


def require_phrases(
    relative: str,
    phrases: tuple[str, ...],
    validation: Validation,
) -> str:
    path = ROOT / relative
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as exc:
        validation.error(f"{relative}: cannot read: {exc}")
        return ""

    folded = text.casefold()
    for phrase in phrases:
        if phrase.casefold() not in folded:
            validation.error(f"{relative}: missing required contract phrase {phrase!r}")
    return text


def markdown_level_two_sections(text: str) -> dict[str, str]:
    headings = list(re.finditer(r"^##\s+([^#\r\n].*?)\s*$", text, re.MULTILINE))
    sections: dict[str, str] = {}
    for index, heading in enumerate(headings):
        start = heading.end()
        end = headings[index + 1].start() if index + 1 < len(headings) else len(text)
        sections[heading.group(1).strip().casefold()] = text[start:end].strip()
    return sections


def markdown_security_entries(
    text: str,
    prefix: str,
    validation: Validation,
    relative: str,
) -> dict[str, str]:
    heading_re = re.compile(
        rf"^###\s+({re.escape(prefix)}-\d{{3}})\s+—\s+.+$",
        re.MULTILINE,
    )
    headings = list(heading_re.finditer(text))
    entries: dict[str, str] = {}
    for heading in headings:
        entry_id = heading.group(1)
        next_heading = re.search(r"^#{1,3}\s+", text[heading.end() :], re.MULTILINE)
        end = (
            heading.end() + next_heading.start()
            if next_heading is not None
            else len(text)
        )
        if entry_id in entries:
            validation.error(f"{relative}: duplicate entry {entry_id}")
        entries[entry_id] = text[heading.end() : end].strip()
    return entries


def contains_forbidden_legacy_name(text: str) -> bool:
    return FORBIDDEN_BRAND_RE.search(text) is not None


def is_optimizer_list_fragment(fragment: str) -> bool:
    without_terms = OPTIMIZER_FAMILY_RE.sub(" ", fragment)
    without_fillers = OPTIMIZER_LIST_FILLER_RE.sub(" ", without_terms)
    return without_fillers.strip() == ""


def optimizer_term_is_denied(clause: str, match: re.Match[str]) -> bool:
    prefix = clause[: match.start()]
    suffix = clause[match.end() :]
    for negation in reversed(list(NEGATION_START_RE.finditer(prefix))):
        if is_optimizer_list_fragment(prefix[negation.end() :]):
            return True
    for predicate in DENIAL_PREDICATE_RE.finditer(suffix):
        if is_optimizer_list_fragment(suffix[: predicate.start()]):
            return True
    return False


def has_prohibited_optimizer_positioning(line: str, heading: str = "") -> bool:
    plain_line = re.sub(r"[*_`~]", "", line)
    plain_heading = re.sub(r"[*_`~]", "", heading)
    matches = list(OPTIMIZER_FAMILY_RE.finditer(plain_line))
    if not matches:
        return False

    negative_heading = (
        NEGATIVE_SCOPE_HEADING_RE.search(plain_heading) is not None
        or (
            DENIAL_CONTEXT_RE.search(plain_heading) is not None
            and OPTIMIZER_FAMILY_RE.search(plain_heading) is not None
        )
    )
    if negative_heading and PRODUCT_IDENTITY_RE.search(plain_line) is None:
        return False

    product_context = (
        PRODUCT_IDENTITY_RE.search(plain_line) is not None
        or PRODUCT_IDENTITY_RE.search(plain_heading) is not None
    )
    for match in matches:
        is_positioning_claim = (
            product_context
            or EXPLICIT_OPTIMIZER_MARKETING_RE.search(plain_line) is not None
        )
        if is_positioning_claim and not optimizer_term_is_denied(plain_line, match):
            return True
    return False


def is_probably_binary(data: bytes) -> bool:
    if not data:
        return False
    sample = data[:8192]
    if sample.startswith((b"\xff\xfe", b"\xfe\xff", b"\xff\xfe\0\0", b"\0\0\xfe\xff")):
        return False
    if b"\0" in sample:
        return True
    disallowed_controls = sum(
        byte < 32 and byte not in {8, 9, 10, 12, 13}
        for byte in sample
    )
    return disallowed_controls / len(sample) > 0.05


def repository_text_paths() -> list[Path]:
    candidates: set[Path] = {
        ROOT / relative for relative in REQUIRED_FILES
    } | {ROOT / "tools/validate_bundle.py"}

    try:
        result = subprocess.run(
            ["git", "-C", str(ROOT), "ls-files", "-z"],
            check=False,
            capture_output=True,
            timeout=10,
        )
    except (OSError, subprocess.TimeoutExpired):
        result = None

    if result is not None and result.returncode == 0:
        for raw_path in result.stdout.split(b"\0"):
            if raw_path:
                candidates.add(ROOT / raw_path.decode("utf-8"))
    else:
        ignored_parts = {
            ".git",
            ".idea",
            ".venv",
            ".vs",
            ".vscode",
            "__pycache__",
            "bin",
            "coverage",
            "obj",
            "testresults",
        }
        candidates.update(
            path
            for path in ROOT.rglob("*")
            if path.is_file()
            and not any(
                part.casefold() in ignored_parts
                for part in path.relative_to(ROOT).parts
            )
        )

    return sorted(path for path in candidates if path.is_file())


def validate_tracked_text_positioning(validation: Validation) -> None:
    for path in repository_text_paths():
        try:
            data = path.read_bytes()
        except OSError as exc:
            validation.error(
                f"{path.relative_to(ROOT).as_posix()}: cannot scan tracked file: {exc}"
            )
            continue
        if is_probably_binary(data):
            continue
        try:
            text = data.decode("utf-8-sig")
        except UnicodeDecodeError as exc:
            validation.error(
                f"{path.relative_to(ROOT).as_posix()}: tracked text is not UTF-8: {exc}"
            )
            continue

        relative = path.relative_to(ROOT).as_posix()
        current_heading = ""
        skip_optimizer_policy_source = relative in {
            "tools/validate_bundle.py",
            "tools/tests/test_validate_bundle.py",
        }
        for line_number, line in enumerate(text.splitlines(), start=1):
            if re.match(r"^#{1,6}\s+", line):
                current_heading = line
            if contains_forbidden_legacy_name(line):
                validation.error(
                    f"{relative}:{line_number}: contains a forbidden legacy product name"
                )
            if (
                not skip_optimizer_policy_source
                and has_prohibited_optimizer_positioning(line, current_heading)
            ):
                validation.error(
                    f"{relative}:{line_number}: contains prohibited optimizer positioning"
                )


def validate_governance_documents(validation: Validation) -> None:
    require_phrases(
        "docs/product-contract.md",
        (
            "ThirdLife** is the product family",
            "ThirdLife Setup Core** is the active standalone project",
            "Team B / B1",
            "A present recipient participates only in explicitly recipient-controlled setup",
            "M0 through M6",
            "TL-0611",
            "M7 / `TL-0710`",
            "Scam Explainer",
            "Team B / B4",
            "project vacuum",
            "recipient-controlled accessibility setup",
            "basic operating-system backup onboarding",
            "SECURITY.md",
            "ACCESSIBILITY.md",
            "LOW_SPEC.md",
            "RELEASE_INTERFACE.md",
            "does not amend or outrank",
        ),
        validation,
    )

    require_phrases(
        "docs/non-goals.md",
        (
            "existing personal-PC repair",
            "donor-media erasure",
            "unsupported Windows",
            "activation",
            "ownership controls",
            "PC cleaner, optimizer, debloater, registry cleaner",
            "sibling-domain ownership",
            "shared SDK",
            "plugin framework",
            "No B4 work during B1",
            "FUTURE_ASSEMBLY_NOTES.md",
            "manual fallback",
            "ordinary implementation task cannot turn one into scope",
            "already inside the selected task's contract",
            "without silently expanding the task or editing the task graph",
        ),
        validation,
    )

    change_control_text = require_phrases(
        "docs/change-control.md",
        (
            "derived from D-045",
            "status",
            "evidence",
            "blocked_reason",
            "Explicit human approval",
            "approving owner",
            "approval date",
            "bundle version",
            "stop before the conflicting implementation",
            "FUTURE_ASSEMBLY_NOTES.md",
            "creates no B1 requirement",
            "BUNDLE_MANIFEST.sha256",
            "eng/verify.ps1",
        ),
        validation,
    )
    authority_block = "\n".join(
        f"{index}. `{relative}`"
        for index, relative in enumerate(AUTHORITY_ORDER, start=1)
    )
    if authority_block not in change_control_text:
        validation.error(
            "docs/change-control.md: authority order must exactly match D-045"
        )

    glossary_text = require_phrases(
        "docs/glossary.md",
        ("Exact frozen decisions", "prevail over this summary"),
        validation,
    )
    glossary_sections = markdown_level_two_sections(glossary_text)
    glossary_contract: dict[str, tuple[str, ...]] = {
        "Evidence": (
            "observed",
            "inferred",
            "not available",
            "human confirmed",
            "collection time",
            "provenance",
        ),
        "Requirement": ("versioned policy or profile", "not itself an observation"),
        "Blocker": ("prevents a defined transition", "remains visible", "cannot be overridden"),
        "Disposition": (
            "Ready to prepare",
            "Repair and retest",
            "Human review required",
            "Alternative operating system candidate",
            "Do not deploy",
        ),
        "Applied": ("not complete", "verified"),
        "Verified": (
            "separate, bounded verification step",
            "independently observed",
            "Backend or installer success",
        ),
        "Frozen release": (
            "exact, immutable",
            "source revision",
            "dependency lock",
            "cryptographic hashes",
        ),
        "Compatibility cut": (
            "future B4-owned",
            "exact frozen product releases",
            "manual fallback",
            "newer release",
        ),
        "Adapter": (
            "project-local provider",
            "sibling adapter",
            "future Team B / B4",
            "version-bounded",
            "manual fallback",
            "private database access",
        ),
    }
    for term, fragments in glossary_contract.items():
        body = glossary_sections.get(term.casefold())
        if body is None:
            validation.error(f"docs/glossary.md: missing required term heading {term!r}")
            continue
        folded = body.casefold()
        for fragment in fragments:
            if fragment.casefold() not in folded:
                validation.error(
                    f"docs/glossary.md: {term!r} definition is missing {fragment!r}"
                )


def security_field(body: str, field: str) -> str:
    match = re.search(
        rf"^\*\*{re.escape(field)}:\*\*\s*(.+?)\s*$",
        body,
        re.MULTILINE,
    )
    return match.group(1).strip() if match is not None else ""


def validate_security_documents(
    validation: Validation,
    task_by_id: dict[str, dict[str, Any]],
    decision_ids: set[str],
) -> None:
    task_ids = set(task_by_id)
    threat_relative = "docs/security/threat-model.md"
    flow_relative = "docs/security/data-flow.md"
    abuse_relative = "docs/security/abuse-cases.md"

    threat_text = require_phrases(
        threat_relative,
        (
            "Security-owner approval:**",
            "Approving owner and role:",
            "Approval date:",
            "Approval reference:",
            "A task reference is a traceability link, not proof that the mitigation works",
            "Sanitization is an external prerequisite",
            "ownership controls are never bypassed",
            "B1 runs in a project vacuum",
            "does not design an adapter",
            "## Protected assets",
            "## Actors and capabilities",
            "## Trust boundaries",
            "## Threat register",
            "## Control-to-roadmap summary",
            "## Residual-risk register",
            "## Security-owner review and approval",
            "Dependency, build, or release provenance is incomplete or substituted",
            "dependency, build, package/release metadata and artifact provenance",
            "`Accept`, `Mitigate`, `Avoid`, `Transfer`, or `Block`",
        ),
        validation,
    )
    flow_text = require_phrases(
        flow_relative,
        (
            "Sanitization remains an external prerequisite",
            "there is no erase, wipe, imaging, activation, unlock, MDM removal, or ownership-bypass flow",
            "there is no B4 actor in the runtime diagram",
            "not an adapter specification",
            "Inventory provider collection",
            "Policy/profile/catalogue import",
            "Package metadata resolution and execution-time artifact comparison",
            "Broker handshake and request",
            "durably commits a correlated started/dispatch-intent checkpoint before emitting",
            "Action journal and checkpoint",
            "Report/finalization projection",
            "Structured Windows Update lifecycle",
            "Dependency, build, and release metadata/artifact verification",
            "## Interruption and split-state rules",
        ),
        validation,
    )
    abuse_text = require_phrases(
        abuse_relative,
        (
            "Catalogue or profile data injects executable behavior",
            "Package source, publisher, version, architecture, or catalogue changes after approval",
            "Replayed, stale, tampered, or cross-job approval",
            "Another local user connects to or pre-creates the broker pipe",
            "Traversal, junction, symlink, reparse point, or unsafe temporary path",
            "Sensitive or hostile text leaks through logs",
            "UAC decline, network/power loss, reboot, full disk, or process death",
            "B1 accidentally gains a sibling dependency",
            "Future B4 adapter misuses private state",
            "Dependency, build input, or release artifact is substituted or lacks provenance",
            "flows `F-01` through `F-19`",
            "not an adapter design or B1 implementation",
            "## Structured review checklist",
            "Review result:**",
        ),
        validation,
    )

    threat_entries = markdown_security_entries(
        threat_text,
        "THR",
        validation,
        threat_relative,
    )
    if tuple(threat_entries) != THREAT_IDS:
        validation.error(
            f"{threat_relative}: threat IDs must exactly equal {list(THREAT_IDS)!r}"
        )

    asset_rows = tuple(
        re.findall(r"^\|\s*`?(AST-\d{2})`?\s*\|", threat_text, re.MULTILINE)
    )
    if asset_rows != ASSET_IDS:
        validation.error(
            f"{threat_relative}: asset rows must exactly equal {list(ASSET_IDS)!r}"
        )
    actor_rows = tuple(
        re.findall(r"^\|\s*`?(ACT-\d{2})`?\s*\|", threat_text, re.MULTILINE)
    )
    if actor_rows != ACTOR_IDS:
        validation.error(
            f"{threat_relative}: actor rows must exactly equal {list(ACTOR_IDS)!r}"
        )

    threat_fields = (
        "Initial risk",
        "Likelihood",
        "Impact",
        "Boundaries/flows",
        "Abuse cases",
        "Decisions",
        "Planned controls/tasks",
        "Control status",
        "Target residual risk",
        "Review trigger",
    )
    threat_abuse_links: dict[str, set[str]] = {}
    for threat_id, body in threat_entries.items():
        for field in threat_fields:
            if not security_field(body, field):
                validation.error(
                    f"{threat_relative}: {threat_id} is missing field {field!r}"
                )
        risk = security_field(body, "Initial risk").casefold()
        likelihood = security_field(body, "Likelihood").casefold()
        impact = security_field(body, "Impact").casefold()
        for field, rating in (("Initial risk", risk), ("Likelihood", likelihood), ("Impact", impact)):
            if rating not in {"low", "medium", "high"}:
                validation.error(
                    f"{threat_relative}: {threat_id} {field} must be Low, Medium, or High"
                )
        rating_rank = {"low": 0, "medium": 1, "high": 2}
        if (
            risk in rating_rank
            and likelihood in rating_rank
            and impact in rating_rank
            and rating_rank[risk] < max(rating_rank[likelihood], rating_rank[impact])
        ):
            validation.error(
                f"{threat_relative}: {threat_id} Initial risk cannot be below Likelihood or Impact"
            )
        task_mapping = set(
            re.findall(r"\bTL-\d{4}\b", security_field(body, "Planned controls/tasks"))
        )
        if risk.startswith("high") and not task_mapping:
            validation.error(
                f"{threat_relative}: high-risk {threat_id} has no roadmap task mapping"
            )

        boundary_ids = set(
            re.findall(r"\bTB-[A-Z0-9-]+\b", security_field(body, "Boundaries/flows"))
        )
        if not boundary_ids:
            validation.error(
                f"{threat_relative}: {threat_id} needs at least one typed trust-boundary reference"
            )
        if "TB-BROKER" in boundary_ids and not task_mapping & BROKER_TASK_IDS:
            validation.error(
                f"{threat_relative}: broker threat {threat_id} lacks a relevant broker task mapping"
            )
        if "TB-PACKAGE-SOURCE" in boundary_ids and not task_mapping & PACKAGE_TASK_IDS:
            validation.error(
                f"{threat_relative}: package-source threat {threat_id} lacks a relevant package task mapping"
            )
        threat_abuse_links[threat_id] = set(
            re.findall(r"\bAC-\d{3}\b", security_field(body, "Abuse cases"))
        )
        if not threat_abuse_links[threat_id]:
            validation.error(
                f"{threat_relative}: {threat_id} needs at least one typed abuse-case reference"
            )
        decision_references = set(
            re.findall(r"\bD-\d{3}\b", security_field(body, "Decisions"))
        )
        if not decision_references:
            validation.error(
                f"{threat_relative}: {threat_id} needs at least one typed decision reference"
            )
        control_status = security_field(body, "Control status")
        if "verified" in control_status.casefold():
            status_task_ids = set(re.findall(r"\bTL-\d{4}\b", control_status))
            if not status_task_ids or any(
                task_id not in task_mapping
                or task_by_id.get(task_id, {}).get("status") != "done"
                for task_id in status_task_ids
            ):
                validation.error(
                    f"{threat_relative}: {threat_id} verified control status must cite only mapped done tasks"
                )

    residual_row_lines = re.findall(
        r"^\|\s*`RR-\d{3}`\s*\|.*$",
        threat_text,
        re.MULTILINE,
    )
    residual_rows: list[tuple[str, str]] = []
    residual_threat_links: dict[str, set[str]] = {}
    for line in residual_row_lines:
        cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
        residual_id = cells[0].strip("`") if cells else ""
        if len(cells) != 6:
            validation.error(
                f"{threat_relative}: {residual_id or 'residual row'} must have six table cells"
            )
            continue
        residual_rows.append((residual_id, cells[-1]))
        residual_threat_links[residual_id] = set(
            re.findall(r"\bTHR-\d{3}\b", cells[1])
        )
    residual_ids = tuple(residual_id for residual_id, _ in residual_rows)
    if residual_ids != RESIDUAL_RISK_IDS:
        validation.error(
            f"{threat_relative}: residual rows must exactly equal {list(RESIDUAL_RISK_IDS)!r}"
        )

    approval_match = re.search(
        r"^\*\*Security-owner approval:\*\*\s*\*\*(Pending|Approved)\*\*\s*$",
        threat_text,
        re.MULTILINE,
    )
    approval_state = approval_match.group(1) if approval_match is not None else ""
    if not approval_state:
        validation.error(
            f"{threat_relative}: security-owner approval must be Pending or Approved"
        )
    approval_metadata: dict[str, str] = {}
    for field in ("Approving owner and role", "Approval date", "Approval reference"):
        match = re.search(
            rf"^\*\*{re.escape(field)}:\*\*\s*(.+?)\s*$",
            threat_text,
            re.MULTILINE,
        )
        approval_metadata[field] = match.group(1).strip() if match is not None else ""
        if not approval_metadata[field]:
            validation.error(f"{threat_relative}: missing approval metadata {field!r}")

    abuse_review_match = re.search(
        r"^\*\*Review result:\*\*\s*(Pending|Approved)\b",
        abuse_text,
        re.MULTILINE,
    )
    abuse_review_state = abuse_review_match.group(1) if abuse_review_match is not None else ""
    threat_review_match = re.search(
        r"^\*\*Current review result:\*\*\s*(Pending|Approved)\b",
        threat_text,
        re.MULTILINE,
    )
    threat_review_state = threat_review_match.group(1) if threat_review_match is not None else ""
    document_statuses = tuple(
        security_field(text, "Status")
        for text in (threat_text, flow_text, abuse_text)
    )
    model_revisions = tuple(
        security_field(text, "Model revision")
        for text in (threat_text, flow_text, abuse_text)
    )
    if len(set(model_revisions)) != 1:
        validation.error(
            "Security documents must share one exact model revision"
        )
    pending_review_disclaimers = (
        "This draft does not satisfy the human evidence required by `TL-0004`",
        "Human acceptance of residual risks remains pending.",
        "No security-owner approval or residual-risk acceptance is recorded in this draft.",
    )
    if approval_state == "Pending":
        if any(value.casefold() != "pending" for value in approval_metadata.values()):
            validation.error(
                f"{threat_relative}: pending approval metadata must remain Pending"
            )
        if any(state.casefold() != "pending" for _, state in residual_rows):
            validation.error(
                f"{threat_relative}: unapproved residual-risk decisions must remain Pending"
            )
        if "No residual risk is accepted by this draft" not in threat_text:
            validation.error(
                f"{threat_relative}: pending draft must state that no residual risk is accepted"
            )
        if abuse_review_state != "Pending" or threat_review_state != "Pending":
            validation.error(
                "Security-document review results must be Pending while owner approval is pending"
            )
        if any(status != "Draft for security-owner review" for status in document_statuses):
            validation.error(
                "Pending security documents must have status 'Draft for security-owner review'"
            )
        if any("draft" not in revision.casefold() for revision in model_revisions):
            validation.error(
                "Pending security-document model revisions must identify a draft"
            )
        if any(
            disclaimer not in "\n".join((threat_text, abuse_text))
            for disclaimer in pending_review_disclaimers
        ):
            validation.error(
                "Pending security documents must retain the no-approval disclaimers"
            )
    elif approval_state == "Approved":
        if any(value.casefold() == "pending" for value in approval_metadata.values()):
            validation.error(
                f"{threat_relative}: approved model needs named owner/role, date, and exact reference"
            )
        owner_and_role = approval_metadata["Approving owner and role"]
        owner_parts = re.split(r"\s+—\s+", owner_and_role, maxsplit=1)
        placeholder_names = {
            "example reviewer",
            "pending",
            "security owner",
            "tbd",
            "unknown",
        }
        if (
            len(owner_parts) != 2
            or len(owner_parts[0].strip()) < 3
            or owner_parts[0].strip().casefold() in placeholder_names
            or "security owner" not in owner_parts[1].casefold()
        ):
            validation.error(
                f"{threat_relative}: approved model needs a non-placeholder named security owner and role"
            )
        try:
            date.fromisoformat(approval_metadata["Approval date"])
        except ValueError:
            validation.error(
                f"{threat_relative}: approved model needs a real ISO approval date"
            )
        if not re.search(r"\b[0-9a-fA-F]{40}\b", approval_metadata["Approval reference"]):
            validation.error(
                f"{threat_relative}: approved model needs an immutable 40-character Git commit"
            )
        residual_decision_re = re.compile(
            r"(?:Accept|Mitigate|Avoid|Transfer|Block)\s+—\s+\S.+",
            re.IGNORECASE,
        )
        if any(
            residual_decision_re.fullmatch(state) is None
            for _, state in residual_rows
        ):
            validation.error(
                f"{threat_relative}: approved model needs an explicit treatment decision for every residual risk"
            )
        if "No residual risk is accepted by this draft" in threat_text:
            validation.error(
                f"{threat_relative}: approved model cannot retain the pending no-acceptance statement"
            )
        if abuse_review_state != "Approved" or threat_review_state != "Approved":
            validation.error(
                "Security-document review results must be Approved with owner approval"
            )
        if any(status != "Approved initial model" for status in document_statuses):
            validation.error(
                "Approved security documents must have status 'Approved initial model'"
            )
        if any("draft" in revision.casefold() or not revision for revision in model_revisions):
            validation.error(
                "Approved security-document model revisions must be non-draft"
            )
        if any(
            disclaimer in "\n".join((threat_text, abuse_text))
            for disclaimer in pending_review_disclaimers
        ):
            validation.error(
                "Approved security documents cannot retain pending/no-approval disclaimers"
            )

    threat_model_task = task_by_id.get("TL-0004", {})
    if threat_model_task.get("status") == "done":
        if approval_state != "Approved":
            validation.error(
                "TL-0004 cannot be done while security-owner approval is Pending"
            )
        approval_commit = re.search(
            r"\b[0-9a-fA-F]{40}\b",
            approval_metadata.get("Approval reference", ""),
        )
        evidence = threat_model_task.get("evidence", [])
        approval_evidence = False
        if approval_commit is not None and isinstance(evidence, list):
            reviewed_commit = approval_commit.group(0).casefold()
            approval_evidence = any(
                isinstance(entry, dict)
                and re.search(
                    r"\bsecurity owner approved\b",
                    str(entry.get("summary", "")),
                    re.IGNORECASE,
                )
                is not None
                and str(entry.get("result", "")).casefold() == "passed"
                and reviewed_commit in str(entry.get("reference", "")).casefold()
                for entry in evidence
            )
        if not approval_evidence:
            validation.error(
                "TL-0004 done evidence must record named security-owner approval for the reviewed commit"
            )

    boundary_rows = re.findall(
        r"^\|\s*`(TB-[A-Z0-9-]+)`\s*\|",
        flow_text,
        re.MULTILINE,
    )
    if tuple(boundary_rows) != SECURITY_BOUNDARY_IDS:
        validation.error(
            f"{flow_relative}: trust-boundary table must keep distinct rows {list(SECURITY_BOUNDARY_IDS)!r}"
        )
    flow_table_contract = (
        (r"^\|\s*`?(E-\d{2})`?\s*\|", ENTITY_IDS, "entity"),
        (r"^\|\s*`?(P-\d{2})`?\s*\|", PROCESS_IDS, "process"),
        (r"^\|\s*`?(DS-\d{2})`?\s*\|", STORE_IDS, "store"),
        (r"^\|\s*`?(F-\d{2})`?\s*\|", FLOW_IDS, "flow"),
    )
    for pattern, expected_ids, kind in flow_table_contract:
        actual_ids = tuple(re.findall(pattern, flow_text, re.MULTILINE))
        if actual_ids != expected_ids:
            validation.error(
                f"{flow_relative}: {kind} rows must exactly equal {list(expected_ids)!r}"
            )

    abuse_entries = markdown_security_entries(
        abuse_text,
        "AC",
        validation,
        abuse_relative,
    )
    if tuple(abuse_entries) != ABUSE_CASE_IDS:
        validation.error(
            f"{abuse_relative}: abuse-case IDs must exactly equal {list(ABUSE_CASE_IDS)!r}"
        )

    abuse_fields = (
        "Threats",
        "Actor",
        "Preconditions",
        "Attack path",
        "Assets/impact",
        "Detection",
        "Fail-closed response",
        "Planned controls/tasks",
        "Control status",
        "Recovery/manual path",
        "Residual risk",
    )
    abuse_task_mappings: dict[str, set[str]] = {}
    abuse_threat_links: dict[str, set[str]] = {}
    abuse_residual_links: dict[str, set[str]] = {}
    for abuse_id, body in abuse_entries.items():
        for field in abuse_fields:
            if not security_field(body, field):
                validation.error(
                    f"{abuse_relative}: {abuse_id} is missing field {field!r}"
                )
        task_mapping = set(
            re.findall(r"\bTL-\d{4}\b", security_field(body, "Planned controls/tasks"))
        )
        abuse_task_mappings[abuse_id] = task_mapping
        abuse_threat_links[abuse_id] = set(
            re.findall(r"\bTHR-\d{3}\b", security_field(body, "Threats"))
        )
        abuse_residual_links[abuse_id] = set(
            re.findall(r"\bRR-\d{3}\b", security_field(body, "Residual risk"))
        )
        if not task_mapping:
            validation.error(
                f"{abuse_relative}: {abuse_id} has no roadmap task mapping"
            )
        if not abuse_threat_links[abuse_id]:
            validation.error(
                f"{abuse_relative}: {abuse_id} needs at least one typed threat reference"
            )
        if not re.findall(r"\bACT-\d{2}\b", security_field(body, "Actor")):
            validation.error(
                f"{abuse_relative}: {abuse_id} needs at least one typed actor reference"
            )
        if not re.findall(r"\bAST-\d{2}\b", security_field(body, "Assets/impact")):
            validation.error(
                f"{abuse_relative}: {abuse_id} needs at least one typed asset reference"
            )
        if not abuse_residual_links[abuse_id]:
            validation.error(
                f"{abuse_relative}: {abuse_id} needs at least one typed residual-risk reference"
            )
        control_status = security_field(body, "Control status")
        if "verified" in control_status.casefold():
            status_task_ids = set(re.findall(r"\bTL-\d{4}\b", control_status))
            if not status_task_ids or any(
                task_id not in task_mapping
                or task_by_id.get(task_id, {}).get("status") != "done"
                for task_id in status_task_ids
            ):
                validation.error(
                    f"{abuse_relative}: {abuse_id} verified control status must cite only mapped done tasks"
                )

    for abuse_id in ("AC-002", "AC-003", "AC-004"):
        if not abuse_task_mappings.get(abuse_id, set()) & BROKER_TASK_IDS:
            validation.error(
                f"{abuse_relative}: broker abuse case {abuse_id} lacks a relevant broker task mapping"
            )
    for abuse_id in ("AC-005", "AC-006", "AC-012"):
        if not abuse_task_mappings.get(abuse_id, set()) & PACKAGE_TASK_IDS:
            validation.error(
                f"{abuse_relative}: package/update abuse case {abuse_id} lacks a relevant package task mapping"
            )

    for threat_id, abuse_ids in threat_abuse_links.items():
        for abuse_id in abuse_ids:
            if threat_id not in abuse_threat_links.get(abuse_id, set()):
                validation.error(
                    f"Security documents: {threat_id} links {abuse_id}, but the abuse case does not link back"
                )
    for abuse_id, threat_ids in abuse_threat_links.items():
        for threat_id in threat_ids:
            if abuse_id not in threat_abuse_links.get(threat_id, set()):
                validation.error(
                    f"Security documents: {abuse_id} links {threat_id}, but the threat does not link back"
                )
    for abuse_id, residual_ids in abuse_residual_links.items():
        linked_threats = abuse_threat_links.get(abuse_id, set())
        for residual_id in residual_ids:
            if not linked_threats & residual_threat_links.get(residual_id, set()):
                validation.error(
                    f"Security documents: {abuse_id} cites {residual_id}, but they share no linked threat"
                )

    referenced_task_ids = set(
        re.findall(
            r"\bTL-\d{4}\b",
            "\n".join((threat_text, flow_text, abuse_text)),
        )
    )
    unknown_task_ids = sorted(referenced_task_ids - task_ids)
    if unknown_task_ids:
        validation.error(
            f"Security documents reference unknown roadmap tasks {unknown_task_ids!r}"
        )

    combined_security_text = "\n".join((threat_text, flow_text, abuse_text))
    cross_reference_contract = (
        (r"\bD-\d{3}\b", decision_ids, "decision"),
        (r"\bAST-\d{2}\b", set(ASSET_IDS), "asset"),
        (r"\bACT-\d{2}\b", set(ACTOR_IDS), "actor"),
        (r"\bTHR-\d{3}\b", set(THREAT_IDS), "threat"),
        (r"\bAC-\d{3}\b", set(ABUSE_CASE_IDS), "abuse-case"),
        (r"\bRR-\d{3}\b", set(RESIDUAL_RISK_IDS), "residual-risk"),
        (r"\bE-\d{2}\b", set(ENTITY_IDS), "external-entity"),
        (r"\bP-\d{2}\b", set(PROCESS_IDS), "process"),
        (r"\bDS-\d{2}\b", set(STORE_IDS), "store"),
        (r"\bF-\d{2}[a-z]?\b", set(FLOW_REFERENCE_IDS), "flow"),
        (r"\bTB-[A-Z0-9-]+\b", set(SECURITY_BOUNDARY_IDS), "trust-boundary"),
    )
    for pattern, known_ids, kind in cross_reference_contract:
        references = set(re.findall(pattern, combined_security_text))
        unknown_ids = sorted(references - known_ids)
        if unknown_ids:
            validation.error(
                f"Security documents contain unknown {kind} references {unknown_ids!r}"
            )

    for relative, text in (
        (threat_relative, threat_text),
        (flow_relative, flow_text),
        (abuse_relative, abuse_text),
    ):
        if SECURITY_MACHINE_PATH_RE.search(text):
            validation.error(f"{relative}: contains a machine-specific path")


def testing_table_ids(text: str, prefix: str) -> tuple[str, ...]:
    return tuple(
        re.findall(
            rf"^\|\s*`({re.escape(prefix)}-\d{{3}})`\s*\|",
            text,
            re.MULTILINE,
        )
    )


def numbered_markdown_section(text: str, section_number: int) -> str:
    match = re.search(
        rf"^## {section_number}\. .+?(?=^## \d+\. |\Z)",
        text,
        re.MULTILINE | re.DOTALL,
    )
    return match.group(0).rstrip() + "\n" if match is not None else ""


def testing_markdown_table_rows(
    text: str,
    section_number: int,
    table_index: int = 0,
) -> list[list[str]]:
    tables: list[list[str]] = []
    current: list[str] = []
    for line in numbered_markdown_section(text, section_number).splitlines():
        if line.strip().startswith("|") and line.strip().endswith("|"):
            current.append(line)
        elif current:
            tables.append(current)
            current = []
    if current:
        tables.append(current)
    if table_index >= len(tables):
        return []
    table_lines = tables[table_index]
    if len(table_lines) < 2:
        return []
    header_cells = [
        cell.strip() for cell in table_lines[0].strip().strip("|").split("|")
    ]
    separator_cells = [
        cell.strip() for cell in table_lines[1].strip().strip("|").split("|")
    ]
    if len(separator_cells) != len(header_cells) or any(
        re.fullmatch(r":?-{3,}:?", cell) is None for cell in separator_cells
    ):
        return []
    return [
        [cell.strip() for cell in line.strip().strip("|").split("|")]
        for line in table_lines[2:]
    ]


def testing_procedure_digest(texts: dict[str, str], low_spec_text: str) -> str:
    section_contract = (
        ("docs/testing/device-matrix.md", (1, 2, 3, 4, 7, 9)),
        ("docs/testing/manual-hardware-tests.md", tuple(range(1, 10))),
        ("docs/testing/failure-injection.md", tuple(range(1, 9))),
        ("docs/testing/accessibility-matrix.md", tuple(range(1, 11))),
        ("LOW_SPEC.md", tuple(range(1, 13))),
    )
    digest = hashlib.sha256()
    available_texts = {**texts, "LOW_SPEC.md": low_spec_text}
    for relative, section_numbers in section_contract:
        body = available_texts[relative]
        parts: list[str] = []
        for number in section_numbers:
            section = numbered_markdown_section(body, number)
            if relative == "docs/testing/device-matrix.md" and number == 4:
                canonical_lines: list[str] = []
                for line in section.splitlines():
                    if re.match(r"^\|\s*`DMX-\d{3}`\s*\|", line):
                        cells = [
                            cell.strip()
                            for cell in line.strip().strip("|").split("|")
                        ]
                        if len(cells) == 8:
                            cells[5:] = ["<evidence-excluded>"] * 3
                            line = "| " + " | ".join(cells) + " |"
                    canonical_lines.append(line)
                section = "\n".join(canonical_lines) + "\n"
            parts.append(section)
        canonical = "".join(parts).replace("\r\n", "\n").replace("\r", "\n")
        if relative == "LOW_SPEC.md":
            canonical = re.sub(
                r"^\*\*Procedure status:\*\*.*\n",
                "",
                canonical,
                flags=re.MULTILINE,
            )
        path_bytes = relative.encode("utf-8")
        content_bytes = canonical.encode("utf-8")
        digest.update(len(path_bytes).to_bytes(8, "big"))
        digest.update(path_bytes)
        digest.update(len(content_bytes).to_bytes(8, "big"))
        digest.update(content_bytes)
    return digest.hexdigest()


def testing_evidence_digest(device_text: str, manual_text: str) -> str:
    digest = hashlib.sha256()
    for relative, body, section_numbers in (
        ("docs/testing/device-matrix.md", device_text, (4, 5, 6, 8, 10)),
        ("docs/testing/manual-hardware-tests.md", manual_text, (10,)),
    ):
        canonical = "".join(
            numbered_markdown_section(body, number) for number in section_numbers
        ).replace("\r\n", "\n").replace("\r", "\n")
        path_bytes = relative.encode("utf-8")
        content_bytes = canonical.encode("utf-8")
        digest.update(len(path_bytes).to_bytes(8, "big"))
        digest.update(path_bytes)
        digest.update(len(content_bytes).to_bytes(8, "big"))
        digest.update(content_bytes)
    return digest.hexdigest()


def testing_git_commit_is_reachable(commit: str) -> bool:
    try:
        object_result = subprocess.run(
            ["git", "-C", str(ROOT), "cat-file", "-e", f"{commit}^{{commit}}"],
            check=False,
            capture_output=True,
            timeout=10,
        )
        ancestor_result = subprocess.run(
            ["git", "-C", str(ROOT), "merge-base", "--is-ancestor", commit, "HEAD"],
            check=False,
            capture_output=True,
            timeout=10,
        )
    except (OSError, subprocess.TimeoutExpired):
        return False
    return object_result.returncode == 0 and ancestor_result.returncode == 0


def testing_text_at_commit(commit: str, relative: str) -> str | None:
    try:
        result = subprocess.run(
            ["git", "-C", str(ROOT), "show", f"{commit}:{relative}"],
            check=False,
            capture_output=True,
            timeout=10,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None
    if result.returncode != 0:
        return None
    try:
        return result.stdout.decode("utf-8")
    except UnicodeDecodeError:
        return None


def canonical_testing_scan_text(text: str) -> str:
    canonical = unicodedata.normalize("NFKC", text)
    for _ in range(64):
        decoded = unquote(canonical)
        if decoded == canonical:
            break
        canonical = unicodedata.normalize("NFKC", decoded)
    return canonical


def testing_has_meaningful_text(value: str) -> bool:
    return sum(character.isalnum() for character in value) >= 3


def testing_sensitive_match(text: str) -> str | None:
    patterns = (
        ("email address", r"(?i)\b[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}\b"),
        ("IPv4 address", r"(?<![\d.])(?:25[0-5]|2[0-4]\d|1?\d?\d)(?:\.(?:25[0-5]|2[0-4]\d|1?\d?\d)){3}(?![\d.])"),
        ("MAC address", r"(?i)\b(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}\b"),
        ("MAC address", r"(?i)\b(?:[0-9a-f]{4}\.){2}[0-9a-f]{4}\b"),
        ("Windows SID", r"\bS-1-(?:\d+-){1,14}\d+\b"),
        ("UUID", r"(?i)\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b"),
        ("GitHub token", r"\bgh[pousr]_[A-Za-z0-9]{20,}\b"),
        ("bearer token", r"(?i)\bbearer\s+[A-Za-z0-9._~+/-]{12,}={0,2}\b"),
        ("Slack token", r"\bxox[baprs]-[A-Za-z0-9-]{10,}\b"),
        ("cloud access key", r"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b"),
        ("private-key material", r"-----BEGIN [A-Z ]*PRIVATE KEY-----"),
        ("recovery-key shaped value", r"\b\d{6}(?:-\d{6}){7}\b"),
        ("labelled device identifier", r"(?i)\b(?:serial(?:\s+number)?|service[_ -]?tag|hostname|ssid)\s*[:=]\s*[^\s|,;]+"),
        ("labelled device identifier", r"(?i)\b(?:serial\s+number|service[_ -]?tag)\s+[A-Z0-9-]{6,}\b"),
        ("labelled device identifier", r"(?i)\basset[_ -]?tag\s*[:=]\s*[^\s|,;]+"),
    )
    for label, pattern in patterns:
        if re.search(pattern, text):
            return label
    for token in re.findall(
        r"(?i)(?<![0-9a-z])\[?[0-9a-f:]{2,}\]?(?![0-9a-z])",
        text,
    ):
        candidate = token.strip("[]")
        if candidate.count(":") < 2:
            continue
        try:
            if ipaddress.ip_address(candidate).version == 6:
                return "IPv6 address"
        except ValueError:
            continue
    return None


def testing_metadata_value(
    text: str,
    field: str,
    relative: str,
    validation: Validation,
) -> str:
    matches = re.findall(
        rf"^[ ]{{0,3}}\*\*{re.escape(field)}:\*\*\s*(.+?)\s*$",
        text,
        re.MULTILINE | re.IGNORECASE,
    )
    if len(matches) != 1:
        validation.error(f"{relative}: must contain exactly one {field} field")
        return ""
    return matches[0].strip()


def validate_testing_documents(
    validation: Validation,
    task_by_id: dict[str, dict[str, Any]],
    decision_ids: set[str],
    commit_is_reachable: Callable[[str], bool] = testing_git_commit_is_reachable,
    text_at_commit: Callable[[str, str], str | None] = testing_text_at_commit,
) -> None:
    device_relative = "docs/testing/device-matrix.md"
    manual_relative = "docs/testing/manual-hardware-tests.md"
    failure_relative = "docs/testing/failure-injection.md"
    accessibility_relative = "docs/testing/accessibility-matrix.md"

    device_text = require_phrases(
        device_relative,
        (
            "Physical-device matrix",
            "Actual device-pool record:",
            "Physical reference device:",
            "Human recorder and role:",
            "Walkthrough result:",
            "Walkthrough date:",
            "Reviewed source commit:",
            "Procedure digest:",
            "Evidence digest:",
            "supported Windows 11 x64",
            "Windows 10",
            "unsupported processor",
            "4 GB or 8 GB device is a test class",
            "SATA HDD",
            "SATA SSD",
            "NVMe",
            "Battery provider unavailable",
            "| TPM | Present and ready |",
            "| Secure Boot | Enabled |",
            "Wired network",
            "Wi-Fi",
            "Slow or high-latency",
            "Intermittent or network loss",
            "Filtered, proxy, or captive network",
            "Unusual or partially failed hardware",
            "full powered-off cold boot",
            "Actual device pool",
            "Coverage gaps and pilot blockers",
            "| Device ID | Availability | Reference device | Form factor | Windows edition/build/support | Architecture | CPU support state | RAM class | System storage | Battery state | TPM state | Secure Boot state | Network capabilities | Known partial failure | Matrix roles | Owner/date | Limitations |",
            "| Device ID | Requirement ID | Actual state | Hardware environment | Execution context | Constraint profile | Evidence source | Evidence reference | Test result | Evidence class | Limitation |",
            "| Gap ID | Missing requirement | Reason | Pilot impact | Acquisition or safe alternative plan | Owner | Review date | Status |",
            "cannot prove long-term hardware reliability",
            "`Human confirmed` is an evidence class, not a result",
            "Automated presence detection cannot pass a functional hardware test",
            "hardware environment:** `Physical` or `Virtual`",
            "execution context:** `Interactive lab` or `CI`",
            "Coverage availability` is `Covered`, `Missing`, or `Pending`",
            "8-byte",
        ),
        validation,
    )
    manual_text = require_phrases(
        manual_relative,
        (
            "Manual hardware-test procedure",
            "Automation and a single manual run cannot prove long-term hardware reliability",
            "`Human confirmed` is evidence provenance, never a result by itself",
            "Hardware presence, provider output, or an inference alone cannot pass a functional test",
            "Full powered-off cold boot and recheck",
            "Fast Startup",
            "a process restart or ambiguous hybrid shutdown cannot pass",
            "Pause, close, reopen, and resume the manual workflow",
            "Partial failure, repair/retest linkage, and blocker preservation",
            "Human walkthrough and sign-off",
            "maps exactly to that task's `not tested` value",
            "A constrained VM remains virtual evidence",
            "Evidence digest:",
            "| Test ID | Record/run ID | Test result | Evidence class | Hardware/context/source | Timestamp with offset | Observation and criterion | Artifact reference/hash or none | Continuity/checkpoint | Cleanup/recovery | Defect, blocker, or limitation |",
        ),
        validation,
    )
    failure_text = require_phrases(
        failure_relative,
        (
            "Failure-Injection Procedure",
            "not evidence that the application",
            "cannot prove physical-device behavior or long-term hardware reliability",
            "failure-injection scenario",
            "A rejected or failed operation can therefore be a **Pass**",
            "synthetic job and non-sensitive fixtures",
            "filling a physical system volume",
            "last durable checkpoint",
            "required actual-state re-observation",
            "synthetic-only result",
            "recovery and cleanup",
            "Human execution of these future failure scenarios remains pending",
            "A constrained VM remains virtual evidence",
        ),
        validation,
    )
    accessibility_text = require_phrases(
        accessibility_relative,
        (
            "Accessibility Test Matrix",
            "Automated UI checks may find defects but cannot replace human Narrator, NVDA",
            "complete the primary operator journey without a mouse",
            "Windows Narrator",
            "NVDA",
            "200%",
            "large-text",
            "Windows high contrast",
            "reduced resolution",
            "Long and pseudolocalized strings",
            "Safe cancellation is keyboard reachable",
            "After UAC approval or decline",
            "After safe process interruption or workflow resume",
            "full powered-off cold boot",
            "Workshop and recipient outputs",
            "Sanitized support preview",
            "A present recipient or authorized organization can choose, preview, apply, verify, and reverse",
            "Sealed handover applies no recipient-specific preference",
            "slow-storage, no-GPU, or other constrained class",
            "not accessibility conformance",
            "proof of long-term reliability",
            "A constrained VM remains virtual evidence",
        ),
        validation,
    )
    low_spec_text = require_phrases(
        "LOW_SPEC.md",
        (
            "## 8. TL-0008 benchmark contract",
            "Procedure revision:** TL-0008 draft 1",
            "A 4 GB or 8 GB device in the test matrix is a **test class**, not an automatic support promise",
            "`template_not_evidence`",
            "`test_result: Pass` and `budget_result: Not available`",
            "hardware environment (`Physical` or `Virtual`)",
            "execution context (`Interactive lab` or `CI`)",
            "A constrained CI/VM run remains virtual evidence",
            "at least three measured repetitions",
            "never discard an outlier silently",
            "recorded start trigger, stop trigger, reset rule, and stabilization rule",
            "`test_result` records procedure and invariant success",
            "`budget_result` separately records",
            "record_kind` to `benchmark_result`",
            "accessibility_check_ids_results_environment_evidence",
            "security_check_ids_results_evidence",
            "cannot certify a device or prove long-term reliability",
        ),
        validation,
    )

    texts = {
        device_relative: device_text,
        manual_relative: manual_text,
        failure_relative: failure_text,
        accessibility_relative: accessibility_text,
    }
    statuses: list[str] = []
    revisions: list[str] = []
    for relative, body in texts.items():
        status_value = testing_metadata_value(body, "Status", relative, validation)
        revision_value = testing_metadata_value(
            body,
            "Procedure revision",
            relative,
            validation,
        )
        if status_value:
            statuses.append(status_value)
        if revision_value:
            revisions.append(revision_value)

        folded = body.casefold()
        for value in TEST_RESULT_VALUES:
            if f"`{value}`".casefold() not in folded:
                validation.error(f"{relative}: missing test-result value {value!r}")
        for value in EVIDENCE_CLASS_VALUES:
            if f"`{value}`".casefold() not in folded:
                validation.error(f"{relative}: missing evidence-class value {value!r}")

    if len(statuses) == len(texts) and len(set(statuses)) != 1:
        validation.error("TL-0008 testing documents must share one exact Status")
    if statuses and statuses[0] not in {TESTING_STATUS_DRAFT, TESTING_STATUS_RECORDED}:
        validation.error("TL-0008 testing documents have an unsupported Status")
    if len(revisions) == len(texts) and (
        len(set(revisions)) != 1 or revisions[0] != TESTING_REVISION
    ):
        validation.error(
            f"TL-0008 testing documents must share procedure revision {TESTING_REVISION!r}"
        )

    expected_ids = {
        device_relative: ("DMX", tuple(f"DMX-{index:03d}" for index in range(1, 34))),
        manual_relative: ("MHT", tuple(f"MHT-{index:03d}" for index in range(1, 22))),
        failure_relative: ("FINJ", tuple(f"FINJ-{index:03d}" for index in range(1, 27))),
        accessibility_relative: (
            "A11Y",
            tuple(f"A11Y-{index:03d}" for index in range(1, 25)),
        ),
    }
    for relative, (prefix, expected) in expected_ids.items():
        id_source = texts[relative]
        if relative == manual_relative:
            id_source = numbered_markdown_section(id_source, 6)
        actual = testing_table_ids(id_source, prefix)
        if actual != expected:
            validation.error(
                f"{relative}: {prefix} rows must exactly equal {list(expected)!r}"
            )

    device_rows = re.findall(r"^\|\s*`DMX-\d{3}`\s*\|.*$", device_text, re.MULTILINE)
    coverage_by_id: dict[str, tuple[str, str, str]] = {}
    missing_gap_ids: list[str] = []
    for row in device_rows:
        cells = [cell.strip() for cell in row.strip().strip("|").split("|")]
        if len(cells) != 8:
            validation.error(f"{device_relative}: each DMX row must have eight cells")
            continue
        requirement_id = cells[0].strip("`")
        device_refs, availability, gap_id = cells[5], cells[6], cells[7]
        coverage_by_id[requirement_id] = (device_refs, availability, gap_id)
        if availability not in {"Covered", "Missing", "Pending"}:
            validation.error(
                f"{device_relative}: {requirement_id} has unsupported coverage availability {availability!r}"
            )
        if availability == "Missing":
            if not re.fullmatch(r"`?GAP-DMX-\d{3}`?", gap_id):
                validation.error(
                    f"{device_relative}: missing {requirement_id} requires a GAP-DMX blocker"
                )
            else:
                missing_gap_ids.append(gap_id.strip("`"))
        if availability == "Covered" and device_refs.casefold() == "pending":
            validation.error(
                f"{device_relative}: covered {requirement_id} requires a device/run reference"
            )
        if availability == "Covered" and not re.search(
            r"\b(?:LAB-DEVICE|RUN)-\d{3}\b",
            device_refs,
        ):
            validation.error(
                f"{device_relative}: covered {requirement_id} requires a stable device/run ID"
            )
    if len(set(missing_gap_ids)) != len(missing_gap_ids):
        validation.error(
            f"{device_relative}: each missing coverage class requires a unique GAP-DMX blocker"
        )

    result_table_contracts = (
        (failure_relative, failure_text, "FINJ", 8, 6, 7),
        (accessibility_relative, accessibility_text, "A11Y", 6, 4, 5),
    )
    for relative, body, prefix, cell_count, result_index, evidence_index in result_table_contracts:
        rows = re.findall(
            rf"^\|\s*`{prefix}-\d{{3}}`\s*\|.*$",
            body,
            re.MULTILINE,
        )
        for row in rows:
            cells = [cell.strip() for cell in row.strip().strip("|").split("|")]
            if len(cells) != cell_count:
                validation.error(
                    f"{relative}: each {prefix} row must have {cell_count} cells"
                )
                continue
            row_id = cells[0].strip("`")
            result = cells[result_index].strip("`")
            evidence_class = cells[evidence_index].strip("`")
            if result not in TEST_RESULT_VALUES:
                validation.error(f"{relative}: {row_id} has invalid test result {result!r}")
            if evidence_class not in EVIDENCE_CLASS_VALUES:
                validation.error(
                    f"{relative}: {row_id} has invalid evidence class {evidence_class!r}"
                )
            if result in {"Pass", "Fail"} and evidence_class not in {
                "Observed",
                "Human confirmed",
            }:
                validation.error(
                    f"{relative}: {row_id} {result} requires Observed or Human confirmed evidence"
                )
    planned_manual_rows = testing_markdown_table_rows(manual_text, 10, 1)
    planned_manual_ids: list[str] = []
    for cells in planned_manual_rows:
        if len(cells) != 11:
            validation.error(
                f"{manual_relative}: each MHT result row must have eleven cells"
            )
            continue
        test_id = cells[0].strip("`")
        result = cells[2].strip("`")
        evidence_class = cells[3].strip("`")
        planned_manual_ids.append(test_id)
        if result not in TEST_RESULT_VALUES:
            validation.error(
                f"{manual_relative}: {test_id} has invalid test result {result!r}"
            )
        if evidence_class not in EVIDENCE_CLASS_VALUES:
            validation.error(
                f"{manual_relative}: {test_id} has invalid evidence class {evidence_class!r}"
            )
        if result in {"Pass", "Fail"} and evidence_class not in {
            "Observed",
            "Human confirmed",
        }:
            validation.error(
                f"{manual_relative}: {test_id} {result} requires Observed or Human confirmed evidence"
            )
    expected_manual_ids = tuple(f"MHT-{index:03d}" for index in range(1, 22))
    if tuple(planned_manual_ids) != expected_manual_ids:
        validation.error(
            f"{manual_relative}: result rows must exactly equal {list(expected_manual_ids)!r}"
        )
    for relative, body, section_number, table_index, label in (
        (device_relative, device_text, 5, 0, "actual-pool"),
        (device_relative, device_text, 6, 0, "device-coverage"),
        (device_relative, device_text, 8, 0, "equipment-gap"),
        (manual_relative, manual_text, 10, 0, "walkthrough sign-off"),
        (manual_relative, manual_text, 10, 1, "per-test result"),
    ):
        if not testing_markdown_table_rows(body, section_number, table_index):
            validation.error(
                f"{relative}: {label} table must have a valid Markdown separator and data row"
            )
    expected_axe_ids = tuple(f"AXE-{index:03d}" for index in range(1, 9))
    if testing_table_ids(accessibility_text, "AXE") != expected_axe_ids:
        validation.error(
            f"{accessibility_relative}: AXE rows must exactly equal {list(expected_axe_ids)!r}"
        )
    expected_low_spec_ids = tuple(f"LSB-{index:03d}" for index in range(1, 15))
    if testing_table_ids(low_spec_text, "LSB") != expected_low_spec_ids:
        validation.error(
            f"LOW_SPEC.md: LSB rows must exactly equal {list(expected_low_spec_ids)!r}"
        )

    yaml_fences = re.findall(r"```yaml\s*\n(.*?)\n```", low_spec_text, re.DOTALL)
    if len(yaml_fences) != 1:
        validation.error("LOW_SPEC.md: must contain exactly one provisional YAML record template")
    else:
        template_text = yaml_fences[0]
        template_lines = [line for line in template_text.splitlines() if line.strip()]
        if any(
            re.fullmatch(r"[a-z][a-z0-9_]*:\s*(?:[^\r\n]*)", line) is None
            for line in template_lines
        ):
            validation.error(
                "LOW_SPEC.md: provisional resource template permits only plain exact mapping keys"
            )
        if re.search(r"(?m)(?:^|:\s*)[&*]|^\s*<<:", template_text):
            validation.error(
                "LOW_SPEC.md: provisional resource template must not use YAML anchors, aliases, or merges"
            )
        template_keys = tuple(
            re.findall(r"^([a-z][a-z0-9_]*):", template_text, re.MULTILINE)
        )
        if template_keys != LOW_SPEC_RECORD_FIELDS:
            validation.error(
                "LOW_SPEC.md: provisional resource template keys/order must match LSR-1"
            )
        try:
            template = yaml.safe_load(template_text)
        except Exception as exc:
            validation.error(f"LOW_SPEC.md: provisional resource template is invalid YAML: {exc}")
            template = None
        if not isinstance(template, dict):
            validation.error("LOW_SPEC.md: provisional resource template must be a mapping")
        else:
            empty_list_fields = {
                "raw_measurement_artifact_refs",
                "elapsed_time_ms_each",
                "cpu_time_ms_each",
                "startup_time_ms_each",
                "idle_working_set_mib_each",
                "peak_working_set_mib_each",
                "peak_commit_mib_each",
                "temporary_storage_peak_mib_each",
                "final_output_size_mib_each",
                "defect_or_blocker_ids",
                "sanitized_artifact_sha256_refs",
            }
            expected_template_values: dict[str, object] = {
                field: [] if field in empty_list_fields else "Pending"
                for field in LOW_SPEC_RECORD_FIELDS
            }
            expected_template_values.update({
                "schema_version": "LSR-1",
                "record_kind": "template_not_evidence",
                "benchmark_id": "Pending",
                "procedure_revision": TESTING_REVISION,
                "cancellation_latency_ms_and_result": "Not run",
                "resume_duration_ms_and_result": "Not run",
                "test_result": "Not run",
                "evidence_class": "Not available",
                "budget_result": "Not available",
            })
            for field, expected_value in expected_template_values.items():
                if template.get(field) != expected_value:
                    validation.error(
                        f"LOW_SPEC.md: template {field} must equal {expected_value!r}"
                    )

    all_testing_texts = {**texts, "LOW_SPEC.md": low_spec_text}
    task_ids = set(task_by_id)
    known_testing_ids = set().union(
        *(set(expected) for _, expected in expected_ids.values()),
        set(expected_axe_ids),
        set(expected_low_spec_ids),
    )
    affirmative_claim_patterns = (
        r"(?i)\b(?:4|8)\s*GB\b[^.\n]{0,100}\b(?:is|are|defines?|becomes?)\s+(?:the\s+)?(?:supported\s+)?minimum\b",
        r"(?i)\b(?:VM|virtual-machine|virtual machine)\s+evidence\s+proves?\s+physical",
        r"(?i)\bautomation\s+proves?\s+(?:physical|long-term)",
        r"(?i)\bhuman confirmed\s+(?:is|means|counts as)\s+(?:a\s+)?(?:passing|pass)",
        r"(?i)\b(?:warm|process|ordinary)\s+restart\s+(?:satisfies|is equivalent to)\s+(?:a\s+)?cold boot",
        r"(?i)\b(?:the\s+)?(?:procedure|run|result|ThirdLife)\s+(?:certifies|guarantees)\b",
        r"(?i)\baccessibility\b[^.\n]{0,80}\b(?:may|can|must|should)\s+be\s+disabled\b",
    )
    for relative, body in all_testing_texts.items():
        unknown_tasks = sorted(set(re.findall(r"\bTL-\d{4}\b", body)) - task_ids)
        if unknown_tasks:
            validation.error(f"{relative}: references unknown task IDs {unknown_tasks}")
        unknown_decisions = sorted(set(re.findall(r"\bD-\d{3}\b", body)) - decision_ids)
        if unknown_decisions:
            validation.error(
                f"{relative}: references unknown decision IDs {unknown_decisions}"
            )
        unknown_testing_ids = sorted(
            set(
                re.findall(
                    r"\b(?:DMX|MHT|FINJ|A11Y|AXE|LSB)-\d{3}\b",
                    body,
                )
            )
            - known_testing_ids
        )
        if unknown_testing_ids:
            validation.error(
                f"{relative}: references unknown testing IDs {unknown_testing_ids}"
            )
        canonical_body = canonical_testing_scan_text(body)
        if relative != "LOW_SPEC.md" and "```" in canonical_body:
            validation.error(
                f"{relative}: fenced code blocks are not permitted in governed test evidence"
            )
        if "<!--" in canonical_body or "-->" in canonical_body:
            validation.error(f"{relative}: HTML comments are not permitted in governed test evidence")
        if SECURITY_MACHINE_PATH_RE.search(canonical_body):
            validation.error(f"{relative}: contains a machine-specific path")
        if re.search(
            r"(?i)(?://[^/\s]+/[^/\s]+|%[A-Z][A-Z0-9_]*%[\\/])",
            canonical_body,
        ):
            validation.error(f"{relative}: contains a machine-specific path")
        if re.search(r"(?:^|[\\/])\.\.(?:[\\/]|$)", canonical_body):
            validation.error(f"{relative}: contains a path-traversal segment")
        if re.search(r"(?i)%[0-9a-f]{2}", canonical_body):
            validation.error(f"{relative}: contains unresolved percent-encoded text")
        sensitive_label = testing_sensitive_match(canonical_body)
        if sensitive_label is not None:
            validation.error(f"{relative}: contains a prohibited {sensitive_label}")
        for line_number, line in enumerate(canonical_body.splitlines(), start=1):
            if any(re.search(pattern, line) for pattern in affirmative_claim_patterns):
                validation.error(
                    f"{relative}:{line_number}: contains a prohibited support/reliability claim"
                )

    task = task_by_id.get("TL-0008", {})
    task_done = task.get("status") == "done"
    low_spec_status = testing_metadata_value(
        low_spec_text,
        "Procedure status",
        "LOW_SPEC.md",
        validation,
    )

    device_human_fields = (
        "Actual device-pool record",
        "Physical reference device",
        "Human recorder and role",
        "Walkthrough result",
        "Walkthrough date",
        "Reviewed source commit",
        "Procedure digest",
        "Evidence digest",
    )
    device_field_values = {
        field: testing_metadata_value(device_text, field, device_relative, validation)
        for field in device_human_fields
    }
    manual_human_fields = (
        "Human walkthrough result",
        "Walkthrough owner and role",
        "Walkthrough date",
        "Reviewed source commit",
        "Reference device ID",
        "Walkthrough evidence reference",
        "Evidence digest",
    )
    manual_field_values = {
        field: testing_metadata_value(manual_text, field, manual_relative, validation)
        for field in manual_human_fields
    }

    if not task_done:
        if statuses and statuses[0] != TESTING_STATUS_DRAFT:
            validation.error("Pending TL-0008 testing documents must retain draft status")
        if low_spec_status != TESTING_STATUS_DRAFT:
            validation.error("LOW_SPEC.md: pending TL-0008 procedure must retain draft status")
        if any(value != "Pending" for value in device_field_values.values()):
            validation.error("Pending TL-0008 device evidence metadata must remain Pending")
        if any(value != "Pending" for value in manual_field_values.values()):
            validation.error("Pending TL-0008 walkthrough metadata must remain Pending")
        for relative, body, phrase in (
            (
                device_relative,
                device_text,
                "The actual device pool, reference-device availability, equipment gaps, and representative-device walkthrough are all **Pending**",
            ),
            (
                manual_relative,
                manual_text,
                "The actual representative-device walkthrough is **Pending**",
            ),
        ):
            if phrase not in body:
                validation.error(f"{relative}: pending human-evidence disclaimer is missing")
    else:
        if not statuses or statuses[0] != TESTING_STATUS_RECORDED:
            validation.error(
                "TL-0008 done requires recorded-procedure status in every testing document"
            )
        if low_spec_status != TESTING_STATUS_RECORDED:
            validation.error("TL-0008 done requires recorded procedure status in LOW_SPEC.md")
        if any(availability == "Pending" for _, availability, _ in coverage_by_id.values()):
            validation.error(
                "TL-0008 done requires every DMX coverage row to have availability classified"
            )
        if coverage_by_id.get("DMX-001", ("", "", ""))[1] != "Covered":
            validation.error("TL-0008 done requires DMX-001 reference coverage")
        if any(not value or value == "Pending" for value in device_field_values.values()):
            validation.error(
                "TL-0008 done requires completed pool, reference-device, walkthrough, and review metadata"
            )
        if any(not value or value == "Pending" for value in manual_field_values.values()):
            validation.error("TL-0008 done requires completed manual walkthrough metadata")
        if device_field_values["Actual device-pool record"] != (
            "docs/testing/device-matrix.md#actual-device-pool"
        ):
            validation.error("TL-0008 done requires the canonical actual-pool reference")
        reference_device = device_field_values["Physical reference device"]
        if not re.fullmatch(r"LAB-DEVICE-\d{3}", reference_device):
            validation.error(
                "TL-0008 done requires an opaque LAB-DEVICE reference device ID"
            )
        if device_field_values["Walkthrough result"] != "Pass":
            validation.error("TL-0008 done requires a passed human walkthrough")
        owner_and_role = device_field_values["Human recorder and role"]
        owner_parts = re.fullmatch(r"([A-Za-z0-9][A-Za-z0-9._-]{2,63}) — Workshop test owner", owner_and_role)
        if owner_parts is None:
            validation.error("TL-0008 done requires a named Workshop test owner")
            owner_name = ""
        else:
            owner_name = owner_parts.group(1)
        reviewed_commit = device_field_values["Reviewed source commit"]
        reviewed_commit_valid = (
            re.fullmatch(r"[0-9a-fA-F]{40}", reviewed_commit) is not None
            and len(set(reviewed_commit.casefold())) > 1
        )
        if not reviewed_commit_valid:
            validation.error("TL-0008 done requires an exact reviewed source commit")
        elif not commit_is_reachable(reviewed_commit):
            validation.error(
                "TL-0008 done requires the reviewed source commit to be a reachable Git ancestor"
            )
        procedure_digest = device_field_values["Procedure digest"]
        expected_procedure_digest = testing_procedure_digest(texts, low_spec_text)
        if procedure_digest.casefold() != expected_procedure_digest:
            validation.error("TL-0008 done requires an exact procedure SHA-256 digest")
        if reviewed_commit_valid and commit_is_reachable(reviewed_commit):
            reviewed_texts: dict[str, str] = {}
            for relative in (*texts, "LOW_SPEC.md"):
                reviewed_body = text_at_commit(reviewed_commit, relative)
                if reviewed_body is None:
                    validation.error(
                        f"TL-0008 reviewed source commit is missing UTF-8 artifact {relative}"
                    )
                else:
                    reviewed_texts[relative] = reviewed_body
            if len(reviewed_texts) == len(texts) + 1:
                reviewed_procedure_digest = testing_procedure_digest(
                    {
                        relative: reviewed_texts[relative]
                        for relative in texts
                    },
                    reviewed_texts["LOW_SPEC.md"],
                )
                if procedure_digest.casefold() != reviewed_procedure_digest:
                    validation.error(
                        "TL-0008 procedure digest must match the exact reviewed source commit"
                    )
        evidence_digest = device_field_values["Evidence digest"]
        expected_evidence_digest = testing_evidence_digest(device_text, manual_text)
        if evidence_digest.casefold() != expected_evidence_digest:
            validation.error("TL-0008 done requires an exact evidence SHA-256 digest")
        walkthrough_date = device_field_values["Walkthrough date"]
        if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", walkthrough_date):
            validation.error("TL-0008 done requires a real YYYY-MM-DD walkthrough date")
        else:
            try:
                review_date = date.fromisoformat(walkthrough_date)
                if review_date > date.today():
                    validation.error("TL-0008 walkthrough date cannot be in the future")
            except ValueError:
                validation.error("TL-0008 done requires a real YYYY-MM-DD walkthrough date")

        pool_table_rows = testing_markdown_table_rows(device_text, 5)
        pool_rows: dict[str, list[str]] = {}
        reference_pool_ids: list[str] = []
        for cells in pool_table_rows:
            if len(cells) != 17:
                validation.error(
                    "TL-0008 done requires every actual-pool row to have seventeen cells"
                )
                continue
            pool_device_id = cells[0].strip("`")
            if not re.fullmatch(r"LAB-DEVICE-\d{3}", pool_device_id):
                validation.error(
                    "TL-0008 done requires every actual-pool row to use an opaque LAB-DEVICE ID"
                )
                continue
            if pool_device_id in pool_rows:
                validation.error("TL-0008 actual-pool device IDs must be unique")
                continue
            pool_rows[pool_device_id] = cells
            if cells[1] not in {"Available", "Planned", "Missing"}:
                validation.error(
                    f"TL-0008 actual-pool row {pool_device_id} has invalid availability"
                )
            if cells[2] not in {"Yes", "No"}:
                validation.error(
                    f"TL-0008 actual-pool row {pool_device_id} has invalid reference-device value"
                )
            if cells[2] == "Yes":
                reference_pool_ids.append(pool_device_id)
            if any(cell == "Pending" for cell in cells):
                validation.error(
                    f"TL-0008 actual-pool row {pool_device_id} cannot retain Pending values"
                )
            if cells[15] != f"{owner_name} / {walkthrough_date}":
                validation.error(
                    f"TL-0008 actual-pool row {pool_device_id} requires the named recorder and date"
                )
            for detail in (*cells[3:5], *cells[7:15], cells[16]):
                if detail not in {"No", "None"} and not testing_has_meaningful_text(detail):
                    validation.error(
                        f"TL-0008 actual-pool row {pool_device_id} contains a non-meaningful fact"
                    )
                    break
        if not pool_rows:
            validation.error("TL-0008 done requires at least one actual device-pool row")
        if reference_pool_ids != [reference_device]:
            validation.error(
                "TL-0008 done requires exactly one reference pool row matching the selected device"
            )

        matching_pool_cells: list[str] | None = None
        if reference_device in pool_rows:
            matching_pool_cells = pool_rows[reference_device]
        if matching_pool_cells is None or len(matching_pool_cells) != 17:
            validation.error(
                "TL-0008 done requires an available supported Windows 11 x64 physical reference row"
            )
        else:
            if any(cell == "Pending" for cell in matching_pool_cells):
                validation.error("TL-0008 reference pool row cannot retain Pending values")
            windows_state = matching_pool_cells[4].casefold()
            form_factor = matching_pool_cells[3].casefold()
            if (
                matching_pool_cells[1] != "Available"
                or matching_pool_cells[2] != "Yes"
                or re.search(r"\b(?:virtual|vm|synthetic|simulat|emulat)", form_factor)
                or "windows 11" not in windows_state
                or "supported" not in windows_state
                or "unsupported" in windows_state
                or "not supported" in windows_state
                or matching_pool_cells[5] != "x64"
                or matching_pool_cells[6] != "Supported"
                or any(
                    value.casefold() in {"pending", "unknown", "not available"}
                    for value in (
                        matching_pool_cells[9],
                        matching_pool_cells[10],
                        matching_pool_cells[11],
                    )
                )
                or "DMX-001" not in matching_pool_cells[14]
                or matching_pool_cells[15]
                != f"{owner_name} / {walkthrough_date}"
                or re.search(
                    r"(?i)\b(?:battery swelling|smoke|unusual heat|liquid damage|exposed conductor|sparking|burning smell|unsafe condition unresolved)\b",
                    " ".join((matching_pool_cells[13], matching_pool_cells[16])),
                )
            ):
                validation.error(
                    "TL-0008 reference pool row must identify an available supported Windows 11 x64 device"
                )
        if reference_device not in coverage_by_id.get("DMX-001", ("", "", ""))[0]:
            validation.error("TL-0008 DMX-001 coverage must reference the selected device")
        reference_roles = set(re.findall(r"DMX-\d{3}", matching_pool_cells[14] if matching_pool_cells else ""))
        covered_for_reference = {
            requirement_id
            for requirement_id, (device_refs, availability, _) in coverage_by_id.items()
            if availability == "Covered" and reference_device in device_refs
        }
        if reference_roles != covered_for_reference:
            validation.error(
                "TL-0008 reference pool Matrix roles must exactly match its Covered DMX rows"
            )
        required_reference_roles = {"DMX-001", "DMX-031"}
        if matching_pool_cells is not None:
            form_factor_state = matching_pool_cells[3].casefold()
            memory_state = matching_pool_cells[7].casefold()
            storage_state = matching_pool_cells[8].casefold()
            battery_state = matching_pool_cells[9].casefold()
            tpm_state = matching_pool_cells[10].casefold()
            secure_boot_state = matching_pool_cells[11].casefold()
            network_state = matching_pool_cells[12].casefold()
            partial_failure_state = matching_pool_cells[13].casefold()
            if "laptop" in form_factor_state:
                required_reference_roles.add("DMX-004")
            elif "desktop" in form_factor_state:
                required_reference_roles.add("DMX-005")
            if re.search(r"\b4\s*GB\b", memory_state, re.IGNORECASE):
                required_reference_roles.add("DMX-007")
            if re.search(r"\b8\s*GB\b", memory_state, re.IGNORECASE):
                required_reference_roles.add("DMX-008")
            if "sata hdd" in storage_state:
                required_reference_roles.add("DMX-009")
            elif "sata ssd" in storage_state:
                required_reference_roles.add("DMX-010")
            elif "nvme" in storage_state:
                required_reference_roles.add("DMX-011")
            if "immediate charging indication" in battery_state:
                required_reference_roles.add("DMX-013")
            if "present and ready" in tpm_state:
                required_reference_roles.add("DMX-017")
            if secure_boot_state == "enabled":
                required_reference_roles.add("DMX-020")
            if "wired" in network_state:
                required_reference_roles.add("DMX-023")
            if "wi-fi" in network_state or "wifi" in network_state:
                required_reference_roles.add("DMX-024")
            if partial_failure_state not in {"none", "none known"}:
                required_reference_roles.add("DMX-029")
        if not required_reference_roles.issubset(reference_roles):
            validation.error(
                "TL-0008 reference pool states and cold-boot requirement must map to Covered DMX roles"
            )

        coverage_result_rows = testing_markdown_table_rows(device_text, 6)
        coverage_pairs: set[tuple[str, str]] = set()
        coverage_result_requirements: set[str] = set()
        for cells in coverage_result_rows:
            if len(cells) != 11:
                validation.error(
                    "TL-0008 done requires every device-coverage row to have eleven cells"
                )
                continue
            coverage_device = cells[0].strip("`")
            coverage_requirement = cells[1].strip("`")
            pair = (coverage_device, coverage_requirement)
            if pair in coverage_pairs:
                validation.error("TL-0008 device-coverage pairs must be unique")
            coverage_pairs.add(pair)
            if coverage_device not in pool_rows:
                validation.error(
                    f"TL-0008 coverage row references unknown pool device {coverage_device!r}"
                )
            if coverage_requirement not in coverage_by_id:
                validation.error(
                    f"TL-0008 coverage row references unknown requirement {coverage_requirement!r}"
                )
            coverage_result_requirements.add(coverage_requirement)
            if not testing_has_meaningful_text(cells[2]):
                validation.error(
                    f"TL-0008 coverage row {coverage_device}/{coverage_requirement} requires a meaningful actual state"
                )
            if cells[3] not in {"Physical", "Virtual"}:
                validation.error(
                    f"TL-0008 coverage row {coverage_device}/{coverage_requirement} has invalid hardware environment"
                )
            if cells[4] not in {"Interactive lab", "CI"}:
                validation.error(
                    f"TL-0008 coverage row {coverage_device}/{coverage_requirement} has invalid execution context"
                )
            if not cells[5] or cells[5] == "Pending":
                validation.error(
                    f"TL-0008 coverage row {coverage_device}/{coverage_requirement} requires a constraint profile"
                )
            if cells[6] not in {
                "Direct physical observation",
                "Named provider",
                "Synthetic",
            }:
                validation.error(
                    f"TL-0008 coverage row {coverage_device}/{coverage_requirement} has invalid evidence source"
                )
            if (
                not testing_has_meaningful_text(cells[7])
                or not testing_has_meaningful_text(cells[10])
            ):
                validation.error(
                    f"TL-0008 coverage row {coverage_device}/{coverage_requirement} requires meaningful evidence and limitation text"
                )
            result = cells[8].strip("`")
            evidence_class = cells[9].strip("`")
            if result not in TEST_RESULT_VALUES:
                validation.error(
                    f"TL-0008 coverage row {coverage_device}/{coverage_requirement} has invalid test result"
                )
            if evidence_class not in EVIDENCE_CLASS_VALUES:
                validation.error(
                    f"TL-0008 coverage row {coverage_device}/{coverage_requirement} has invalid evidence class"
                )
            if result in {"Pass", "Fail"} and evidence_class not in {
                "Observed",
                "Human confirmed",
            }:
                validation.error(
                    f"TL-0008 coverage row {coverage_device}/{coverage_requirement} requires observed or human evidence"
                )
            if coverage_requirement == "DMX-001" and (
                coverage_device != reference_device
                or cells[3] != "Physical"
                or cells[4] != "Interactive lab"
                or cells[6] != "Direct physical observation"
                or cells[7]
                != "docs/testing/manual-hardware-tests.md#human-walkthrough-and-sign-off"
                or result != "Pass"
                or evidence_class != "Human confirmed"
            ):
                validation.error(
                    "TL-0008 DMX-001 result must be the passed human-confirmed physical walkthrough"
                )
        covered_requirements = {
            requirement_id
            for requirement_id, (_, availability, _) in coverage_by_id.items()
            if availability == "Covered"
        }
        if coverage_result_requirements != covered_requirements:
            validation.error(
                "TL-0008 device-coverage rows must exactly match the Covered DMX requirements"
            )

        gap_table_rows = testing_markdown_table_rows(device_text, 8)
        gap_rows_by_id: dict[str, list[str]] = {}
        for cells in gap_table_rows:
            if len(cells) != 8:
                validation.error(
                    "TL-0008 done requires every equipment-gap row to have eight cells"
                )
                continue
            gap_id = cells[0].strip("`")
            if gap_id in gap_rows_by_id:
                validation.error("TL-0008 equipment-gap IDs must be unique")
                continue
            gap_rows_by_id[gap_id] = cells
            if any(cell == "Pending" for cell in cells):
                validation.error(
                    f"TL-0008 equipment-gap row {gap_id!r} cannot retain Pending values"
                )
        expected_gap_ids = set(missing_gap_ids)
        if expected_gap_ids:
            if set(gap_rows_by_id) != expected_gap_ids:
                validation.error(
                    "TL-0008 equipment-gap rows must exactly match Missing DMX gap IDs"
                )
            for gap_id, cells in gap_rows_by_id.items():
                requirement_id = cells[1].strip("`")
                if coverage_by_id.get(requirement_id, ("", "", ""))[2].strip("`") != gap_id:
                    validation.error(
                        f"TL-0008 equipment-gap row {gap_id} must name its mapped DMX requirement"
                    )
                if (
                    any(
                        not testing_has_meaningful_text(value)
                        or value.casefold()
                        in {"none", "no missing equipment", "no pilot impact", "no plan"}
                        for value in cells[2:5]
                    )
                    or cells[5] != owner_and_role
                    or cells[6] != walkthrough_date
                    or cells[7] not in {"Open", "Blocked"}
                ):
                    validation.error(
                        f"TL-0008 equipment-gap row {gap_id} requires an attributable open pilot blocker"
                    )
        elif set(gap_rows_by_id) != {"None"}:
            validation.error(
                "TL-0008 done with no missing equipment requires one explicit None gap row"
            )

        manual_expected = {
            "Human walkthrough result": "Pass",
            "Walkthrough owner and role": owner_and_role,
            "Walkthrough date": walkthrough_date,
            "Reviewed source commit": reviewed_commit,
            "Reference device ID": reference_device,
            "Walkthrough evidence reference": "docs/testing/manual-hardware-tests.md#human-walkthrough-and-sign-off",
            "Evidence digest": evidence_digest,
        }
        if any(manual_field_values[field] != expected for field, expected in manual_expected.items()):
            validation.error("TL-0008 manual walkthrough metadata must match the device record")

        signoff_field_order = (
            "Walkthrough result",
            "Human recorder and role",
            "Date and timestamp with offset",
            "Procedure revision",
            "Reviewed source commit",
            "Physical reference device ID",
            "Windows edition/build/architecture",
            "Tests physically executed",
            "Tests reviewed only or not run",
            "Cold-boot result and evidence class",
            "Interruption/resume result and evidence class",
            "Missing equipment and pilot blockers",
            "Limitations and defects",
            "Sanitized evidence reference/hash",
            "Cleanup/recovery result",
        )
        signoff_rows = testing_markdown_table_rows(manual_text, 10, 0)
        if any(len(cells) != 2 for cells in signoff_rows):
            validation.error("TL-0008 manual sign-off rows must each have two cells")
            signoff_values: dict[str, str] = {}
        else:
            signoff_keys = tuple(cells[0] for cells in signoff_rows)
            if signoff_keys != signoff_field_order:
                validation.error(
                    "TL-0008 manual sign-off fields must use the exact governed order"
                )
            signoff_values = {cells[0]: cells[1] for cells in signoff_rows}
        exact_signoff_values = {
            "Walkthrough result": "Pass",
            "Human recorder and role": owner_and_role,
            "Procedure revision": f"`{TESTING_REVISION}`",
            "Reviewed source commit": reviewed_commit,
            "Physical reference device ID": reference_device,
            "Tests physically executed": "MHT-001 through MHT-021",
            "Tests reviewed only or not run": "None",
            "Cold-boot result and evidence class": "Pass / Human confirmed",
            "Interruption/resume result and evidence class": "Pass / Human confirmed",
            "Sanitized evidence reference/hash": "docs/testing/manual-hardware-tests.md#human-walkthrough-and-sign-off",
        }
        if any(
            signoff_values.get(field) != expected
            for field, expected in exact_signoff_values.items()
        ):
            validation.error(
                "TL-0008 manual sign-off must record the exact passed physical walkthrough"
            )
        signoff_timestamp = signoff_values.get("Date and timestamp with offset", "")
        try:
            parsed_signoff_timestamp = datetime.fromisoformat(
                signoff_timestamp.replace("Z", "+00:00")
            )
            signoff_timestamp_valid = (
                re.fullmatch(
                    rf"{re.escape(walkthrough_date)}T\d{{2}}:\d{{2}}:\d{{2}}(?:Z|[+-]\d{{2}}:\d{{2}})",
                    signoff_timestamp,
                )
                is not None
                and parsed_signoff_timestamp.utcoffset() is not None
            )
        except ValueError:
            signoff_timestamp_valid = False
        if not signoff_timestamp_valid:
            validation.error(
                "TL-0008 manual sign-off requires a timestamp with UTC offset on the walkthrough date"
            )
        windows_signoff = signoff_values.get(
            "Windows edition/build/architecture",
            "",
        ).casefold()
        if (
            "windows 11" not in windows_signoff
            or "x64" not in windows_signoff
            or "supported" not in windows_signoff
            or "unsupported" in windows_signoff
            or "not supported" in windows_signoff
        ):
            validation.error(
                "TL-0008 manual sign-off requires supported Windows 11 x64"
            )
        cleanup_value = signoff_values.get("Cleanup/recovery result", "")
        if not cleanup_value.startswith("Pass") or re.search(
            r"(?i)\b(?:fail|unsafe|unresolved|residue remains)\b",
            cleanup_value,
        ):
            validation.error("TL-0008 manual sign-off requires successful safe cleanup")
        limitation_value = signoff_values.get("Limitations and defects", "")
        if not limitation_value or re.search(
            r"(?i)\b(?:essential failure unresolved|unsafe residue|not performed)\b",
            limitation_value,
        ):
            validation.error(
                "TL-0008 manual sign-off cannot retain an unresolved essential failure"
            )

        missing_equipment_value = signoff_values.get(
            "Missing equipment and pilot blockers",
            "",
        )
        signoff_gap_ids = set(re.findall(r"GAP-DMX-\d{3}", missing_equipment_value))
        for start, end in re.findall(
            r"GAP-DMX-(\d{3})\s+through\s+GAP-DMX-(\d{3})",
            missing_equipment_value,
        ):
            start_number = int(start)
            end_number = int(end)
            if start_number <= end_number:
                signoff_gap_ids.update(
                    f"GAP-DMX-{number:03d}"
                    for number in range(start_number, end_number + 1)
                )
        if expected_gap_ids:
            if signoff_gap_ids != expected_gap_ids:
                validation.error(
                    "TL-0008 manual sign-off gap references must match Missing DMX rows"
                )
        elif missing_equipment_value != "None":
            validation.error(
                "TL-0008 manual sign-off must explicitly record no missing equipment"
            )

        manual_result_rows = testing_markdown_table_rows(manual_text, 10, 1)
        expected_manual_result_ids = tuple(
            f"MHT-{index:03d}" for index in range(1, 22)
        )
        actual_manual_result_ids: list[str] = []
        manual_results: dict[str, tuple[str, str, str]] = {}
        for cells in manual_result_rows:
            if len(cells) != 11:
                validation.error(
                    "TL-0008 manual result rows must each have eleven cells"
                )
                continue
            test_id = cells[0].strip("`")
            record_id = cells[1]
            result = cells[2].strip("`")
            evidence_class = cells[3].strip("`")
            environment_source = cells[4]
            result_timestamp = cells[5]
            observation = cells[6]
            artifact = cells[7]
            continuity = cells[8]
            cleanup = cells[9]
            limitation = cells[10]
            actual_manual_result_ids.append(test_id)
            manual_results[test_id] = (result, evidence_class, limitation)
            if record_id != f"RUN-001-{test_id}":
                validation.error(
                    f"TL-0008 {test_id} requires its exact bounded record/run ID"
                )
            if result not in TEST_RESULT_VALUES or result == "Not run":
                validation.error(
                    f"TL-0008 completed walkthrough has invalid or unrun result for {test_id}"
                )
            if evidence_class not in EVIDENCE_CLASS_VALUES:
                validation.error(
                    f"TL-0008 completed walkthrough has invalid evidence for {test_id}"
                )
            if environment_source != (
                "Physical / Interactive lab / Direct physical observation"
            ):
                validation.error(
                    f"TL-0008 {test_id} requires the physical interactive-lab evidence source"
                )
            try:
                parsed_timestamp = datetime.fromisoformat(
                    result_timestamp.replace("Z", "+00:00")
                )
                if (
                    parsed_timestamp.utcoffset() is None
                    or parsed_timestamp.date().isoformat() != walkthrough_date
                    or not re.fullmatch(
                        r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:Z|[+-]\d{2}:\d{2})",
                        result_timestamp,
                    )
                ):
                    raise ValueError
            except ValueError:
                validation.error(
                    f"TL-0008 {test_id} requires a real offset timestamp on the walkthrough date"
                )
            if not testing_has_meaningful_text(observation) or observation == "Pending":
                validation.error(
                    f"TL-0008 {test_id} requires a bounded observation and criterion result"
                )
            if artifact != "none" and not re.fullmatch(
                r"docs/testing/[a-z0-9._/-]+; sha256:[0-9a-f]{64}",
                artifact,
            ):
                validation.error(
                    f"TL-0008 {test_id} requires artifact none or a sanitized hashed repository reference"
                )
            if not testing_has_meaningful_text(continuity) or continuity == "Pending":
                validation.error(
                    f"TL-0008 {test_id} requires a continuity/checkpoint record"
                )
            if test_id == "MHT-019" and "cold-boot" not in continuity.casefold():
                validation.error("TL-0008 MHT-019 requires cold-boot checkpoint linkage")
            if test_id == "MHT-020" and "resume" not in continuity.casefold():
                validation.error("TL-0008 MHT-020 requires resume checkpoint linkage")
            if not cleanup.startswith("Pass") or re.search(
                r"(?i)\b(?:fail|unsafe|unresolved|residue remains)\b",
                cleanup,
            ):
                validation.error(
                    f"TL-0008 {test_id} requires successful safe cleanup/recovery"
                )
            if result in {"Pass", "Fail"} and evidence_class not in {
                "Observed",
                "Human confirmed",
            }:
                validation.error(
                    f"TL-0008 {test_id} {result} requires observed or human evidence"
                )
            if result == "Not available" and (
                evidence_class != "Not available"
                or not re.search(
                    r"\b(?:capability_absent|equipment_missing|provider_unavailable|unsafe_to_run|permission_denied)\b",
                    limitation,
                )
            ):
                validation.error(
                    f"TL-0008 {test_id} Not available requires a governed reason"
                )
            if result == "Fail" and not re.search(
                r"\b(?:DEFECT|GAP-DMX)-\d{3}\b",
                limitation,
            ):
                validation.error(
                    f"TL-0008 {test_id} Fail requires a defect or pilot blocker"
                )
            if not testing_has_meaningful_text(limitation) or limitation == "Pending":
                validation.error(
                    f"TL-0008 completed walkthrough requires a limitation note for {test_id}"
                )
        if tuple(actual_manual_result_ids) != expected_manual_result_ids:
            validation.error(
                "TL-0008 manual result rows must exactly cover MHT-001 through MHT-021"
            )
        for required_test_id in ("MHT-001", "MHT-019", "MHT-020"):
            if manual_results.get(required_test_id) != (
                "Pass",
                "Human confirmed",
                "Point-in-time physical walkthrough",
            ):
                validation.error(
                    f"TL-0008 {required_test_id} requires a physical human-confirmed Pass"
                )
        manual_pass_to_matrix = {
            "MHT-010": "DMX-023",
            "MHT-011": "DMX-024",
            "MHT-013": "DMX-013",
            "MHT-014": "DMX-014",
            "MHT-019": "DMX-031",
        }
        for test_id, requirement_id in manual_pass_to_matrix.items():
            if manual_results.get(test_id, ("", "", ""))[0] == "Pass" and (
                requirement_id not in covered_for_reference
            ):
                validation.error(
                    f"TL-0008 {test_id} Pass must reconcile to Covered {requirement_id}"
                )
        partial_failure_result = manual_results.get("MHT-021", ("", "", ""))[0]
        if matching_pool_cells is not None:
            pool_has_partial_failure = matching_pool_cells[13].casefold() not in {
                "none",
                "none known",
            }
            if not pool_has_partial_failure and partial_failure_result != "Not available":
                validation.error(
                    "TL-0008 MHT-021 must be Not available when no partial failure exists"
                )
            if partial_failure_result == "Pass" and "DMX-029" not in covered_for_reference:
                validation.error(
                    "TL-0008 MHT-021 Pass requires a recorded partial failure and Covered DMX-029"
                )

        mutable_sections = (
            numbered_markdown_section(device_text, 5),
            numbered_markdown_section(device_text, 6),
            numbered_markdown_section(device_text, 8),
            numbered_markdown_section(device_text, 10),
            numbered_markdown_section(manual_text, 10),
        )
        if any(re.search(r"\bPending\b", section) for section in mutable_sections):
            validation.error("TL-0008 done records cannot retain Pending human/device evidence")

        expected_summary = (
            f"{owner_and_role} confirmed the sanitized actual device pool and completed "
            f"the representative-device walkthrough with result Pass on {reference_device}."
        )
        expected_environment = f"Physical Windows 11 x64 reference device {reference_device}"
        expected_reference = (
            "docs/testing/manual-hardware-tests.md#human-walkthrough-and-sign-off; "
            f"reviewed commit {reviewed_commit}; procedure sha256:{procedure_digest}; "
            f"evidence sha256:{evidence_digest}"
        )
        evidence_matches = []
        for item in task.get("evidence", []):
            if not isinstance(item, dict):
                continue
            if (
                item.get("summary") == expected_summary
                and item.get("result") == "passed"
                and item.get("environment") == expected_environment
                and str(item.get("date", "")) == walkthrough_date
                and item.get("reference") == expected_reference
            ):
                evidence_matches.append(item)
        if len(evidence_matches) != 1:
            validation.error(
                "TL-0008 done evidence must record a passed physical representative-device walkthrough"
            )


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

    expected_authority = list(AUTHORITY_ORDER)
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

    validate_governance_documents(validation)
    validate_security_documents(validation, task_by_id, decision_set)
    validate_testing_documents(validation, task_by_id, decision_set)
    validate_tracked_text_positioning(validation)

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
