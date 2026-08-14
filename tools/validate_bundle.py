#!/usr/bin/env python3
"""Validate the ThirdLife roadmap bundle.

Run from any directory:
    python tools/validate_bundle.py
"""

from __future__ import annotations

import json
import re
import subprocess
import sys
from collections import defaultdict, deque
from datetime import date
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
    "docs/change-control.md",
    "docs/glossary.md",
    "docs/non-goals.md",
    "docs/product-contract.md",
    "docs/security/abuse-cases.md",
    "docs/security/data-flow.md",
    "docs/security/threat-model.md",
    "docs/supply-chain/dependencies.md",
    "docs/supply-chain/license-matrix.csv",
    "eng/generate-sbom.ps1",
    "tools/generate_sbom.py",
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
