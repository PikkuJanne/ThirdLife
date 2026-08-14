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
import unicodedata
from collections import defaultdict, deque
from datetime import date, datetime
from pathlib import Path
from typing import Any
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
    "docs/privacy/logging-standard.md",
    "docs/privacy/privacy-model.md",
    "docs/privacy/redaction-test-cases.yaml",
    "docs/security/abuse-cases.md",
    "docs/security/data-flow.md",
    "docs/security/threat-model.md",
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
PRIVACY_DATA_IDS = tuple(f"PD-{index:02d}" for index in range(1, 23))
PRIVACY_CLASSES = (
    "Prohibited secret or foreign content",
    "Recipient-controlled private choice",
    "Workshop-confidential record",
    "Internal operational or pseudonymous metadata",
    "Support-allowlisted projection",
    "Recipient-facing projection",
    "Public or release metadata",
    "Transient untrusted sensitive input",
)
PRIVACY_SINKS = {
    "ordinary_log",
    "local_crash",
    "ui_error",
    "workshop_record",
    "recipient_guide",
    "support_export",
    "task_evidence",
    "telemetry",
}
PRIVACY_DECISIONS = {"allow", "replace", "drop", "reject"}
PRIVACY_DECISION_IDS = {"D-011", "D-013", "D-014", "D-036", "D-037", "D-053"}
PRIVACY_SYNTHETIC_REQUIREMENTS = [
    "Inputs are invented test values and never copied from a person, device, account, or local machine.",
    "Secret-like inputs use conspicuous DO_NOT_USE markers rather than real credential formats.",
    "External-product cases are generic and contain no sibling product identifier or behavior.",
]
PRIVACY_FIXTURE_ROOT_FIELDS = {
    "schema_version",
    "model_revision",
    "fixture_type",
    "privacy_owner_approval",
    "expectation_output_contract",
    "synthetic_value_policy",
    "decision_vocabulary",
    "sink_vocabulary",
    "category_vocabulary",
    "cases",
}
PRIVACY_EXPECTATION_OUTPUT_CONTRACT = {
    "ordinary_log": (
        "exact registered safe-field fragment before required closed-envelope assembly"
    ),
    "support_export": (
        "exact allowlisted normalized projection fragment before bundle-manifest assembly"
    ),
    "all_other_sinks": "exact post-classification sink decision output",
}
PRIVACY_FIXTURE_MAX_BYTES = 256 * 1024
PRIVACY_LITERAL_MAX_SCALAR_BYTES = 512
PRIVACY_LITERAL_MAX_INPUT_BYTES = 4096
PRIVACY_LITERAL_MAX_OUTPUT_BYTES = 8192
PRIVACY_CATEGORY_VOCABULARY = {
    "authentication_token",
    "bidi_control",
    "clipboard_secret",
    "control_character",
    "correlation_id",
    "destination_category",
    "device_identifier",
    "duplicate_key",
    "email_address",
    "encoded_blob",
    "encryption_key",
    "environment_path",
    "exception_text",
    "excessive_depth",
    "export_destination",
    "external_private_content",
    "external_private_database",
    "external_recovery_credential",
    "file_uri",
    "formula_injection",
    "full_serial",
    "generic_hardware",
    "hashed_identifier",
    "homoglyph",
    "host_identity",
    "ip_address",
    "log_injection",
    "logical_manifest_name",
    "mac_address",
    "markup_injection",
    "native_error_code",
    "nested_sensitive_value",
    "network_path",
    "operator_identity",
    "os_version",
    "oversized_collection",
    "oversized_input",
    "package_download_url",
    "password",
    "personal_path",
    "product_key",
    "public_package_metadata",
    "public_update_identity",
    "raw_output",
    "raw_provider_report",
    "recipient_choice_status",
    "recipient_identity",
    "recipient_identity_absent",
    "recovery_key",
    "security_identifier",
    "stable_result_code",
    "stack_trace",
    "support_id",
    "telemetry",
    "unicode_normalization",
    "unknown_classification",
    "url_credential",
    "url_token",
    "username",
    "wifi_identifier",
    "wifi_secret",
    "zero_width_character",
}
PRIVACY_BOUND_FIELDS = {
    "actual_collection_items",
    "actual_input_utf8_bytes",
    "actual_nesting_depth",
    "actual_scalar_utf8_bytes",
    "max_collection_items",
    "max_input_utf8_bytes",
    "max_nesting_depth",
    "max_output_utf8_bytes",
    "max_scalar_utf8_bytes",
    "max_stack_frames",
}
PRIVACY_SUPPORT_FIELDS = {
    "support_id",
    "product_name",
    "product_version",
    "build_revision",
    "bundle_schema_version",
    "redaction_rules_version",
    "os_edition",
    "os_build",
    "os_architecture",
    "os_support_state",
    "hardware_manufacturer",
    "hardware_model",
    "device_form_factor",
    "cpu_architecture",
    "installed_memory_bucket",
    "storage_media_class",
    "storage_capacity_bucket",
    "check_id",
    "availability",
    "outcome_code",
    "limitation_code",
    "observed_at_utc",
    "action_type",
    "result_code",
    "verification_code",
    "restart_state",
    "duration_bucket",
    "error_code",
    "error_category",
    "recovery_code",
    "source_id",
    "package_id",
    "publisher",
    "resolved_version",
    "architecture",
    "scope",
    "policy_version",
    "profile_id",
    "catalog_version",
    "configuration_digest",
    "started_at_utc",
    "completed_at_utc",
    "attempt_count",
    "item_count",
    "relative_name",
    "content_sha256",
    "byte_count",
    "generated_at_utc",
}
PRIVACY_LOG_FIELDS = {
    "schema_version",
    "event_id",
    "occurred_at_utc",
    "event_code",
    "severity",
    "component",
    "phase",
    "outcome",
    "job_ref",
    "action_ref",
    "correlation_ref",
    "result_code",
    "duration_bucket",
    "item_count",
    "count_capped",
    "redaction_flags",
    "native_error_code",
}
PRIVACY_SUPPORT_INTEGER_FIELDS = {"attempt_count", "item_count", "byte_count"}
PRIVACY_SUPPORT_TIMESTAMP_FIELDS = {
    "observed_at_utc",
    "started_at_utc",
    "completed_at_utc",
    "generated_at_utc",
}
PRIVACY_SUPPORT_DIGEST_FIELDS = {"configuration_digest", "content_sha256"}
PRIVACY_LOG_INTEGER_FIELDS = {"item_count", "native_error_code"}
PRIVACY_LOG_BOOLEAN_FIELDS = {"count_capped"}
PRIVACY_LOG_TIMESTAMP_FIELDS = {"occurred_at_utc"}
PRIVACY_REVIEW_IDS = tuple(f"PR-{index:02d}" for index in range(1, 17))
PRIVACY_SECRET_CATEGORIES = {
    "authentication_token",
    "clipboard_secret",
    "encryption_key",
    "external_recovery_credential",
    "password",
    "product_key",
    "recovery_key",
    "url_credential",
    "url_token",
    "wifi_secret",
}
PRIVACY_SECRET_FIELD_RE = re.compile(
    r"(?i)(?:password|passphrase|pin|token|credential|"
    r"(?:api|private|client|access|recovery|encryption|product)[_-]?key|"
    r"clipboard[_-]?secret|wifi[_-]?secret|secret(?:[_-]?value)?)"
)
PRIVACY_UNMARKED_POSITIVE_CATEGORIES = {
    "destination_category",
    "generic_hardware",
    "logical_manifest_name",
    "native_error_code",
    "os_version",
    "recipient_choice_status",
    "stable_result_code",
}
PRIVACY_REQUIRED_CASE_CATEGORIES = {
    "recipient_identity",
    "full_serial",
    "username",
    "security_identifier",
    "wifi_identifier",
    "ip_address",
    "mac_address",
    "host_identity",
    "personal_path",
    "network_path",
    "package_download_url",
    "authentication_token",
    "password",
    "recovery_key",
    "encryption_key",
    "raw_output",
    "exception_text",
    "nested_sensitive_value",
    "unicode_normalization",
    "homoglyph",
    "zero_width_character",
    "control_character",
    "oversized_input",
    "excessive_depth",
    "external_private_content",
    "external_private_database",
    "external_recovery_credential",
    "telemetry",
    "stable_result_code",
    "support_id",
    "generic_hardware",
}
SECURITY_MACHINE_PATH_RE = re.compile(
    r"(?i)(?:\b[a-z]:[\\/]|\\\\[^\\/\s]+[\\/][^\\/\s]+|"
    r"file:/+(?:[a-z]:|/)|/(?:home|users)/[^/\s]+/)"
)
FIXTURE_MACHINE_PATH_RE = re.compile(
    r"(?i)(?:\b[a-z]:[\\/][^\s\"'<>|]+|"
    r"\\\\[^\\/\s]+[\\/][^\s\"'<>|]+|"
    r"file:/+(?:[a-z]:|/)[^\s\"'<>|]*|"
    r"/(?:home|users)/[^/\s]+/[^\s\"'<>|]*)"
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


class UniqueKeySafeLoader(yaml.SafeLoader):
    """Safe YAML loader that rejects duplicate mapping keys."""


def construct_unique_mapping(
    loader: UniqueKeySafeLoader,
    node: yaml.nodes.MappingNode,
    deep: bool = False,
) -> dict[Any, Any]:
    mapping: dict[Any, Any] = {}
    for key_node, value_node in node.value:
        key = loader.construct_object(key_node, deep=deep)
        try:
            duplicate = key in mapping
        except TypeError as exc:
            raise yaml.constructor.ConstructorError(
                "while constructing a mapping",
                node.start_mark,
                "found an unhashable mapping key",
                key_node.start_mark,
            ) from exc
        if duplicate:
            raise yaml.constructor.ConstructorError(
                "while constructing a mapping",
                node.start_mark,
                f"found duplicate key {key!r}",
                key_node.start_mark,
            )
        mapping[key] = loader.construct_object(value_node, deep=deep)
    return mapping


UniqueKeySafeLoader.add_constructor(
    yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG,
    construct_unique_mapping,
)


def load_strict_yaml(path: Path, validation: Validation) -> dict[str, Any]:
    try:
        data = path.read_bytes()
        if len(data) > PRIVACY_FIXTURE_MAX_BYTES:
            validation.error(
                f"{path.name}: strict YAML exceeds the {PRIVACY_FIXTURE_MAX_BYTES}-byte source limit"
            )
            return {}
        text = data.decode("utf-8")
        value = parse_strict_yaml_text(text)
    except Exception as exc:
        validation.error(f"{path.name}: cannot parse strict YAML: {exc}")
        return {}
    if not isinstance(value, dict):
        validation.error(f"{path.name}: top-level value must be a mapping")
        return {}
    return value


def parse_strict_yaml_text(text: str) -> Any:
    """Parse a bounded governance fixture without YAML graph or merge semantics."""
    for event in yaml.parse(text):
        if isinstance(event, yaml.events.AliasEvent) or getattr(event, "anchor", None):
            raise ValueError("YAML anchors and aliases are not permitted")
    if re.search(r"(?m)^\s*<<\s*:", text):
        raise ValueError("YAML merge keys are not permitted")
    return yaml.load(text, Loader=UniqueKeySafeLoader)


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
        rf"^[ ]{{0,3}}\*\*{re.escape(field)}:\*\*\s*(.+?)\s*$",
        body,
        re.MULTILINE | re.IGNORECASE,
    )
    return match.group(1).strip() if match is not None else ""


def require_unique_markdown_fields(
    relative: str,
    body: str,
    fields: tuple[str, ...],
    validation: Validation,
) -> None:
    for field in fields:
        count = len(
            re.findall(
                rf"^[ ]{{0,3}}\*\*{re.escape(field)}:\*\*",
                body,
                re.MULTILINE | re.IGNORECASE,
            )
        )
        if count != 1:
            validation.error(
                f"{relative}: approval field {field!r} must occur exactly once"
            )


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


def nested_string_values(value: Any) -> list[str]:
    """Return canonical keys and scalar values from a synthetic fixture value."""
    result: list[str] = []
    stack = [value]
    while stack:
        current = stack.pop()
        if isinstance(current, str):
            result.append(current)
        elif isinstance(current, dict):
            for key, nested in current.items():
                result.append(str(key))
                stack.append(nested)
        elif isinstance(current, list):
            stack.extend(current)
        elif current is not None and isinstance(current, (int, float, bool)):
            result.append(str(current))
    return result


def nested_scalar_strings(value: Any) -> list[str]:
    """Return string scalar values without treating field names as safe markers."""
    result: list[str] = []
    stack = [value]
    while stack:
        current = stack.pop()
        if isinstance(current, str):
            result.append(current)
        elif isinstance(current, dict):
            stack.extend(current.values())
        elif isinstance(current, list):
            stack.extend(current)
    return result


def secret_labeled_scalar_values(value: Any) -> list[str]:
    """Return scalars stored beneath fields that explicitly claim secret material."""
    result: list[str] = []
    stack = [value]
    while stack:
        current = stack.pop()
        if isinstance(current, dict):
            declared_field = current.get("field")
            if (
                isinstance(declared_field, str)
                and PRIVACY_SECRET_FIELD_RE.search(
                    unicodedata.normalize("NFKC", declared_field)
                )
                and "value" in current
            ):
                result.extend(nested_scalar_strings(current["value"]))
            for key, nested in current.items():
                if isinstance(key, str) and PRIVACY_SECRET_FIELD_RE.search(
                    unicodedata.normalize("NFKC", key)
                ):
                    result.extend(nested_scalar_strings(nested))
                stack.append(nested)
        elif isinstance(current, list):
            stack.extend(current)
    return result


def secret_labeled_values(value: Any) -> list[Any]:
    """Return raw values whose key or declared field identifies secret material."""
    result: list[Any] = []
    stack = [value]
    while stack:
        current = stack.pop()
        if isinstance(current, dict):
            declared_field = current.get("field")
            if (
                isinstance(declared_field, str)
                and PRIVACY_SECRET_FIELD_RE.search(
                    unicodedata.normalize("NFKC", declared_field)
                )
                and "value" in current
            ):
                result.append(current["value"])
            for key, nested in current.items():
                if isinstance(key, str) and PRIVACY_SECRET_FIELD_RE.search(
                    unicodedata.normalize("NFKC", key)
                ):
                    result.append(nested)
                stack.append(nested)
        elif isinstance(current, list):
            stack.extend(current)
    return result


def privacy_fixture_canonical_text(value: str) -> str:
    """Normalize Unicode and percent encoding before safety classification."""
    normalized = unicodedata.normalize("NFKC", value)
    for _ in range(32):
        decoded = unicodedata.normalize("NFKC", unquote(normalized))
        if decoded == normalized:
            break
        normalized = decoded
    return normalized


def is_conspicuous_synthetic_secret(value: str) -> bool:
    """Recognize a complete, intentionally unusable scalar secret fixture."""
    normalized = privacy_fixture_canonical_text(value)
    return re.fullmatch(
        r"(?i)SYNTHETIC_[A-Z0-9_.:-]{3,220}DO_NOT_USE",
        normalized,
    ) is not None


def embedded_secret_values(value: str) -> list[str]:
    """Extract bounded credential/query values from the synthetic text fixture forms."""
    normalized = privacy_fixture_canonical_text(value)
    results = re.findall(
        r"(?i)(?:password|passphrase|pin|token|credential|"
        r"(?:api|private|client|access|recovery|encryption|product)[_-]?key|secret)="
        r"([^&#\s]+)",
        normalized,
    )
    results.extend(
        re.findall(r"(?i)://[^/@\s:]+:([^@/\s]+)@", normalized)
    )
    return results


def fixture_path_is_synthetic(path_text: str) -> bool:
    """Require the machine-specific identity-bearing path components to be synthetic."""
    normalized = privacy_fixture_canonical_text(path_text)
    normalized = normalized.replace("/", "\\")
    normalized = re.sub(r"(?i)^file:\\+", "", normalized)
    if re.search(r"(?:^|\\)\.\.?($|\\)", normalized):
        return False
    parts = [part for part in normalized.split("\\") if part]
    if parts and re.fullmatch(r"(?i)[a-z]:", parts[0]):
        parts = parts[1:]
    structural_segments = {"users", "home", "documents", "source"}
    if normalized.startswith("\\\\"):
        structural_segments = set()
    return bool(parts) and all(
        part.casefold() in structural_segments
        or privacy_normal_form(part).startswith("synthetic")
        for part in parts
    )


def privacy_payload_utf8_bytes(value: Any) -> int | None:
    if isinstance(value, str):
        return len(value.encode("utf-8"))
    try:
        return len(
            json.dumps(
                value,
                ensure_ascii=False,
                separators=(",", ":"),
            ).encode("utf-8")
        )
    except (TypeError, ValueError):
        return None


def is_json_scalar_tree(value: Any) -> bool:
    if value is None or type(value) in {str, int, bool}:
        return True
    if isinstance(value, list):
        return all(is_json_scalar_tree(item) for item in value)
    if isinstance(value, dict):
        return all(
            isinstance(key, str) and is_json_scalar_tree(nested)
            for key, nested in value.items()
        )
    return False


def privacy_max_collection_items(value: Any) -> int:
    maximum = 0
    stack = [value]
    while stack:
        current = stack.pop()
        if isinstance(current, dict):
            maximum = max(maximum, len(current))
            stack.extend(current.values())
        elif isinstance(current, list):
            maximum = max(maximum, len(current))
            stack.extend(current)
    return maximum


def is_random_uuid4(value: str) -> bool:
    compact = value.replace("-", "")
    return (
        re.fullmatch(
            r"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}",
            value,
        )
        is not None
        or re.fullmatch(r"[0-9a-fA-F]{32}", value) is not None
    ) and compact != "0" * 32 and compact[12].casefold() == "4" and compact[16].casefold() in "89ab"


def nested_mapping_keys(value: Any) -> set[str]:
    result: set[str] = set()
    stack = [value]
    while stack:
        current = stack.pop()
        if isinstance(current, dict):
            for key, nested in current.items():
                result.add(str(key))
                stack.append(nested)
        elif isinstance(current, list):
            stack.extend(current)
    return result


def privacy_normal_form(value: str) -> str:
    normalized = unicodedata.normalize("NFKC", value)
    return "".join(
        character
        for character in normalized
        if not unicodedata.category(character).startswith("C")
    ).casefold()


def privacy_structure_metrics(value: Any) -> tuple[int, int]:
    max_depth = 0
    node_count = 0
    stack: list[tuple[Any, int]] = [(value, 0)]
    while stack:
        current, depth = stack.pop()
        node_count += 1
        max_depth = max(max_depth, depth)
        if isinstance(current, dict):
            stack.extend((nested, depth + 1) for nested in current.values())
        elif isinstance(current, list):
            stack.extend((nested, depth + 1) for nested in current)
    return max_depth, node_count


def privacy_unknown_keys(mapping: dict[Any, Any], allowed: set[str]) -> list[str]:
    """Render unknown or non-string mapping keys without mixed-type sorting failures."""
    return sorted(
        repr(key)
        for key in mapping
        if not isinstance(key, str) or key not in allowed
    )


def is_rfc3339_utc_seconds(value: str) -> bool:
    if re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z", value) is None:
        return False
    try:
        datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ")
    except ValueError:
        return False
    return True


def parse_iso_calendar_date(value: str) -> date | None:
    if re.fullmatch(r"\d{4}-\d{2}-\d{2}", value) is None:
        return None
    try:
        return date.fromisoformat(value)
    except ValueError:
        return None


def validate_privacy_safe_string(
    owner: str,
    field: str,
    value: Any,
    validation: Validation,
    *,
    max_utf8_bytes: int,
    ascii_only: bool = False,
) -> bool:
    if not isinstance(value, str) or not value:
        validation.error(f"{owner}: {field} must be a non-empty string")
        return False
    if len(value.encode("utf-8")) > max_utf8_bytes:
        validation.error(
            f"{owner}: {field} exceeds its {max_utf8_bytes}-byte UTF-8 limit"
        )
    if ascii_only and not value.isascii():
        validation.error(f"{owner}: {field} must use bounded ASCII")
    if any(unicodedata.category(character).startswith("C") for character in value):
        validation.error(f"{owner}: {field} contains control or format characters")
    decoded_value = value
    if re.search(r"%[0-9a-fA-F]{2}", decoded_value):
        validation.error(f"{owner}: {field} cannot contain percent-encoded data")
    decoded_value = privacy_fixture_canonical_text(decoded_value)
    if re.search(
        r"(?i)(?:\b(?:https?|file|data|urn|ftp|mailto):|\bwww\.)",
        decoded_value,
    ):
        validation.error(f"{owner}: {field} cannot contain a URI")
    if re.search(r"(?i)\b[^@\s]+@[^@\s]+\.[^@\s]+\b", decoded_value):
        validation.error(f"{owner}: {field} cannot contain an email address")
    if FIXTURE_MACHINE_PATH_RE.search(decoded_value):
        validation.error(f"{owner}: {field} cannot contain a machine-specific path")
    if "do_not_use" in privacy_normal_form(decoded_value):
        validation.error(f"{owner}: {field} cannot retain synthetic secret material")
    return True


def validate_support_output_fields(
    owner: str, output: dict[Any, Any], validation: Validation
) -> None:
    for field, value in output.items():
        if not isinstance(field, str):
            continue
        if field in PRIVACY_SUPPORT_INTEGER_FIELDS:
            upper_bound = 2 * 1024 * 1024 if field == "byte_count" else 10_000
            if (
                not isinstance(value, int)
                or isinstance(value, bool)
                or not 0 <= value <= upper_bound
            ):
                validation.error(
                    f"{owner}: {field} must be an integer from 0 through {upper_bound}"
                )
            continue
        if field in PRIVACY_SUPPORT_TIMESTAMP_FIELDS:
            if not isinstance(value, str) or not is_rfc3339_utc_seconds(value):
                validation.error(f"{owner}: {field} must be an RFC 3339 UTC-second timestamp")
            continue
        if field in PRIVACY_SUPPORT_DIGEST_FIELDS:
            if not isinstance(value, str) or re.fullmatch(r"[0-9a-f]{64}", value) is None:
                validation.error(f"{owner}: {field} must be a lowercase SHA-256 digest")
            continue
        if validate_privacy_safe_string(
            owner,
            field,
            value,
            validation,
            max_utf8_bytes=512,
        ) and field == "relative_name":
            segments = value.split("/")
            windows_reserved = {
                "CON",
                "PRN",
                "AUX",
                "NUL",
                *(f"COM{index}" for index in range(1, 10)),
                *(f"LPT{index}" for index in range(1, 10)),
            }
            if (
                re.fullmatch(r"[A-Za-z0-9._/-]{1,128}", value) is None
                or value.startswith("/")
                or any(not segment or segment in {".", ".."} for segment in segments)
                or any(segment.endswith((".", " ")) for segment in segments)
                or any(
                    segment.split(".", 1)[0].upper() in windows_reserved
                    for segment in segments
                )
            ):
                validation.error(
                    f"{owner}: relative_name must be a bounded internal relative name"
                )
        if not isinstance(value, str):
            continue
        identifier_fields = {
            "support_id",
            "build_revision",
            "bundle_schema_version",
            "redaction_rules_version",
            "os_build",
            "check_id",
            "outcome_code",
            "limitation_code",
            "action_type",
            "result_code",
            "verification_code",
            "restart_state",
            "duration_bucket",
            "error_code",
            "error_category",
            "recovery_code",
            "source_id",
            "package_id",
            "resolved_version",
            "policy_version",
            "profile_id",
            "catalog_version",
            "device_form_factor",
            "installed_memory_bucket",
            "storage_media_class",
            "storage_capacity_bucket",
        }
        if field in identifier_fields and re.fullmatch(
            r"[A-Za-z0-9][A-Za-z0-9._:-]{0,127}", value
        ) is None:
            validation.error(f"{owner}: {field} must be a normalized identifier")
        if field == "publisher" and re.fullmatch(
            r"[A-Za-z0-9][A-Za-z0-9 ._&'()\-]{0,127}", value
        ) is None:
            validation.error(f"{owner}: publisher must be a reviewed normalized identity")
        support_enums = {
            "availability": {"available", "unavailable", "unknown", "not_applicable"},
            "architecture": {"x86", "x64", "arm64", "neutral", "unknown"},
            "os_architecture": {"x86", "x64", "arm64", "unknown"},
            "cpu_architecture": {"x86", "x64", "arm64", "unknown"},
            "scope": {"user", "machine", "portable", "unknown"},
            "os_support_state": {"supported", "unsupported", "unknown"},
            "duration_bucket": {
                "Under1Second",
                "Under10Seconds",
                "Under1Minute",
                "Under10Minutes",
                "Under1Hour",
                "Under24Hours",
                "Over24Hours",
                "Unknown",
            },
        }
        if field in support_enums and value not in support_enums[field]:
            validation.error(f"{owner}: {field} is not a governed enum value")
        if field == "support_id" and not (
            is_random_uuid4(value)
            or re.fullmatch(r"SYNTHETIC_SUPPORT_[A-Z0-9_]{4,120}", value)
            is not None
        ):
            validation.error(
                f"{owner}: support_id must be a random opaque identifier or conspicuous synthetic fixture ID"
            )
    started = output.get("started_at_utc")
    completed = output.get("completed_at_utc")
    if (
        isinstance(started, str)
        and isinstance(completed, str)
        and is_rfc3339_utc_seconds(started)
        and is_rfc3339_utc_seconds(completed)
    ):
        started_at = datetime.strptime(started, "%Y-%m-%dT%H:%M:%SZ")
        completed_at = datetime.strptime(completed, "%Y-%m-%dT%H:%M:%SZ")
        if started_at > completed_at:
            validation.error(
                f"{owner}: started_at_utc cannot be later than completed_at_utc"
            )
        elif isinstance(output.get("duration_bucket"), str):
            elapsed_seconds = (completed_at - started_at).total_seconds()
            if elapsed_seconds < 1:
                expected_duration = "Under1Second"
            elif elapsed_seconds < 10:
                expected_duration = "Under10Seconds"
            elif elapsed_seconds < 60:
                expected_duration = "Under1Minute"
            elif elapsed_seconds < 600:
                expected_duration = "Under10Minutes"
            elif elapsed_seconds < 3600:
                expected_duration = "Under1Hour"
            elif elapsed_seconds < 86_400:
                expected_duration = "Under24Hours"
            else:
                expected_duration = "Over24Hours"
            if output["duration_bucket"] != expected_duration:
                validation.error(
                    f"{owner}: duration_bucket does not match started_at_utc/completed_at_utc"
                )


def validate_log_output_fields(
    owner: str, output: dict[Any, Any], validation: Validation
) -> None:
    try:
        encoded_output = json.dumps(
            output, ensure_ascii=False, separators=(",", ":")
        ).encode("utf-8")
    except (TypeError, ValueError):
        validation.error(f"{owner}: ordinary-log output contains a non-scalar YAML type")
        encoded_output = b""
    if len(encoded_output) > 8192:
        validation.error(f"{owner}: ordinary-log output exceeds the 8-KiB event limit")
    for field, value in output.items():
        if not isinstance(field, str) or field == "redaction_flags":
            continue
        if field in PRIVACY_LOG_INTEGER_FIELDS:
            upper_bound = 65_535 if field == "item_count" else 2_147_483_647
            lower_bound = 0 if field == "item_count" else -2_147_483_648
            if (
                not isinstance(value, int)
                or isinstance(value, bool)
                or not lower_bound <= value <= upper_bound
            ):
                validation.error(
                    f"{owner}: {field} must be an integer from {lower_bound} through {upper_bound}"
                )
            continue
        if field in PRIVACY_LOG_BOOLEAN_FIELDS:
            if not isinstance(value, bool):
                validation.error(f"{owner}: {field} must be Boolean")
            continue
        if field in PRIVACY_LOG_TIMESTAMP_FIELDS:
            if not isinstance(value, str) or not is_rfc3339_utc_seconds(value):
                validation.error(f"{owner}: {field} must be an RFC 3339 UTC-second timestamp")
            continue
        if not validate_privacy_safe_string(
            owner,
            field,
            value,
            validation,
            max_utf8_bytes=128,
            ascii_only=True,
        ):
            continue
        enum_values = {
            "schema_version": {"tl-log-v1"},
            "severity": {"Information", "Warning", "Error", "Critical"},
            "phase": {
                "Startup",
                "Intake",
                "Inspect",
                "Decide",
                "Prepare",
                "Verify",
                "Handover",
                "Lifecycle",
            },
            "outcome": {
                "Started",
                "Progress",
                "Completed",
                "Failed",
                "Cancelled",
                "TimedOut",
                "Unavailable",
                "RequiresReview",
            },
            "duration_bucket": {
                "Under1Second",
                "Under10Seconds",
                "Under1Minute",
                "Under10Minutes",
                "Under1Hour",
                "Under24Hours",
                "Over24Hours",
                "Unknown",
            },
        }
        if field in enum_values and value not in enum_values[field]:
            validation.error(f"{owner}: {field} is not a governed enum value")
        if field == "event_id" and not is_random_uuid4(value):
            validation.error(
                f"{owner}: event_id must be a non-nil random UUIDv4 identifier"
            )
        if field in {"event_code", "result_code"} and re.fullmatch(
            r"[A-Z][A-Z0-9_]{2,127}", value
        ) is None:
            validation.error(f"{owner}: {field} must be a stable compiled code")
        if field == "component" and re.fullmatch(
            r"[A-Za-z][A-Za-z0-9.]{0,63}", value
        ) is None:
            validation.error(f"{owner}: component must be a compiled component identifier")
        if field in {"job_ref", "action_ref", "correlation_ref"} and re.fullmatch(
            r"[A-Za-z0-9][A-Za-z0-9._:-]{0,127}", value
        ) is None:
            validation.error(f"{owner}: {field} must be a bounded opaque identifier")
        if field in {"job_ref", "action_ref", "correlation_ref"}:
            synthetic_prefix = {
                "job_ref": "JOB",
                "action_ref": "ACTION",
                "correlation_ref": "CORRELATION",
            }[field]
            if not (
                is_random_uuid4(value)
                or re.fullmatch(
                    rf"SYNTHETIC_{synthetic_prefix}_[A-Z0-9_]{{4,120}}",
                    value,
                )
                is not None
            ):
                validation.error(
                    f"{owner}: {field} must be a random opaque identifier or conspicuous synthetic fixture reference"
                )
    if output.get("count_capped") is True and output.get("item_count") != 65_535:
        validation.error(
            f"{owner}: count_capped=true requires item_count at the governed cap of 65535"
        )


def read_git_text_at_commit(commit: str, relative: str) -> str | None:
    """Read one governed UTF-8 blob from an immutable local Git commit."""
    result = subprocess.run(
        ["git", "show", f"{commit}:{relative}"],
        cwd=ROOT,
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        return None
    try:
        return result.stdout.decode("utf-8")
    except UnicodeDecodeError:
        return None


def git_commit_is_reachable(commit: str) -> bool:
    object_type = subprocess.run(
        ["git", "cat-file", "-t", commit],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=False,
    )
    if object_type.returncode != 0 or object_type.stdout.strip() != "commit":
        return False
    ancestor = subprocess.run(
        ["git", "merge-base", "--is-ancestor", commit, "HEAD"],
        cwd=ROOT,
        capture_output=True,
        check=False,
    )
    return ancestor.returncode == 0


def canonical_privacy_markdown(text: str, _review_heading: str) -> str:
    body = text
    metadata = re.compile(
        r"^\*\*(?:Status|Model revision|Privacy-owner approval|Approving owner and role|"
        r"Approval date|Reviewed source commit|Approval reference|Review result):\*\*.*$",
        re.MULTILINE,
    )
    body = metadata.sub("", body)
    approval_sentences = re.compile(
        r"(?:No privacy-owner approval is recorded in this draft\.|"
        r"Named privacy-owner approval is recorded for this exact revision\.|"
        r"No approval is present in this draft\.|"
        r"`TL-0005` must remain in review until a named privacy owner approves the "
        r"classifications and default retention guidance for an exact committed revision\.|"
        r"Named privacy-owner approval covers the classifications and default retention "
        r"guidance for this exact committed revision\.|"
        r"No named privacy owner has approved this exact revision, its classifications, "
        r"prohibited and allowed fields, sink contracts, or proposed default retention guidance\.|"
        r"Named privacy-owner approval covers this exact revision, its classifications, "
        r"prohibited and allowed fields, sink contracts, and proposed default retention guidance\.|"
        r"This pending draft does not satisfy the human evidence required by `TL-0005`\.|"
        r"Approval evidence is recorded for the exact reviewed source and named owner\.|"
        r"Human approval of the classifications and default retention guidance remains pending\.|"
        r"Named privacy-owner approval covers the classifications and default retention guidance\.)\s*"
    )
    body = approval_sentences.sub("", body)
    body = re.sub(
        r"^- \[[ xX]\] (`PR-\d{2}` — .*?)(?: \*\*Disposition:\*\* .+)?$",
        r"- [ ] \1",
        body,
        flags=re.MULTILINE,
    )
    return body.strip()


def canonical_privacy_fixture(document: dict[str, Any]) -> str:
    normalized = dict(document)
    normalized.pop("model_revision", None)
    normalized.pop("privacy_owner_approval", None)

    def canonical_value(value: Any) -> Any:
        if isinstance(value, dict):
            return {
                "mapping": [
                    [f"{type(key).__name__}:{key!r}", canonical_value(nested)]
                    for key, nested in value.items()
                ]
            }
        if isinstance(value, list):
            return {"list": [canonical_value(nested) for nested in value]}
        if value is None or isinstance(value, (str, int, float, bool)):
            return {type(value).__name__: value}
        return {type(value).__name__: repr(value)}

    return json.dumps(
        canonical_value(normalized),
        ensure_ascii=False,
        separators=(",", ":"),
    )


def validate_privacy_documents(
    validation: Validation,
    task_by_id: dict[str, dict[str, Any]],
    decision_ids: set[str],
) -> None:
    task_ids = set(task_by_id)
    model_relative = "docs/privacy/privacy-model.md"
    logging_relative = "docs/privacy/logging-standard.md"
    fixture_relative = "docs/privacy/redaction-test-cases.yaml"

    model_text = require_phrases(
        model_relative,
        (
            "Privacy-owner approval:**",
            "Approving owner and role:",
            "Approval date:",
            "Reviewed source commit:",
            "Approval reference:",
            "## 3. Proposed data classifications",
            "## 4. Complete logical data map",
            "## 5. Planned sink and channel separation",
            "## 6. Proposed support allowlist",
            "## 7. Proposed prohibited diagnostic fields",
            "## 8. Candidate retention and cleanup guidance",
            "## 11. Residual privacy limitations and review triggers",
            "## 12. Privacy-owner review and approval",
            "recipient identity",
            "full serial",
            "raw command",
            "telemetry",
            "sibling workspaces",
            "private databases",
            "assessment evidence",
            "backup keys",
            "Archive is not deletion",
            "Uninstall preserves",
        ),
        validation,
    )
    logging_text = require_phrases(
        logging_relative,
        (
            "## Event envelope",
            "## Prohibited fields",
            "## Sink contracts",
            "## Raw output and exceptions",
            "## Local retention",
            "## Support export",
            "## Telemetry",
            "## Failure and recovery",
            "## Accessibility and low-spec impact",
            "## Privacy-owner review checklist",
            "redaction happens before persistence",
            "static message templates",
            "Exception.ToString()",
            "never copied whole logs",
            "no endpoint",
            "no uploader",
            "exact registered safe-field fragment produced before required closed-envelope assembly",
            "expectation_output_contract",
            "Review result:**",
        ),
        validation,
    )
    require_unique_markdown_fields(
        model_relative,
        model_text,
        (
            "Status",
            "Model revision",
            "Draft date",
            "Privacy-owner approval",
            "Approving owner and role",
            "Approval date",
            "Reviewed source commit",
            "Approval reference",
        ),
        validation,
    )
    require_unique_markdown_fields(
        logging_relative,
        logging_text,
        ("Status", "Model revision", "Draft date", "Review result"),
        validation,
    )

    fixture_path = ROOT / fixture_relative
    fixture_doc = load_strict_yaml(fixture_path, validation)

    if set(fixture_doc) != PRIVACY_FIXTURE_ROOT_FIELDS:
        validation.error(
            f"{fixture_relative}: top-level fields must exactly equal {sorted(PRIVACY_FIXTURE_ROOT_FIELDS)!r}"
        )
    if fixture_doc.get("decision_vocabulary") != ["allow", "replace", "drop", "reject"]:
        validation.error(
            f"{fixture_relative}: decision_vocabulary must exactly match the governed decisions"
        )
    sink_vocabulary = fixture_doc.get("sink_vocabulary")
    if (
        not isinstance(sink_vocabulary, list)
        or any(not isinstance(value, str) for value in sink_vocabulary)
        or len(sink_vocabulary) != len(set(sink_vocabulary))
        or set(sink_vocabulary) != PRIVACY_SINKS
    ):
        validation.error(
            f"{fixture_relative}: sink_vocabulary must exactly match the governed sinks"
        )
    category_vocabulary = fixture_doc.get("category_vocabulary")
    if (
        not isinstance(category_vocabulary, list)
        or any(not isinstance(value, str) for value in category_vocabulary)
        or len(category_vocabulary) != len(set(category_vocabulary))
        or set(category_vocabulary) != PRIVACY_CATEGORY_VOCABULARY
    ):
        validation.error(
            f"{fixture_relative}: category_vocabulary must exactly match the governed categories"
        )
    synthetic_policy = fixture_doc.get("synthetic_value_policy")
    if not isinstance(synthetic_policy, dict):
        validation.error(f"{fixture_relative}: synthetic_value_policy must be a mapping")
    else:
        if synthetic_policy.get("reserved_markers") != ["SYNTHETIC_", "example.invalid"]:
            validation.error(
                f"{fixture_relative}: synthetic_value_policy must retain the exact reserved markers"
            )
        if synthetic_policy.get("documentation_network_ranges") != [
            "192.0.2.0/24",
            "2001:db8::/32",
        ]:
            validation.error(
                f"{fixture_relative}: synthetic_value_policy must retain documentation-only network ranges"
            )
        requirements = synthetic_policy.get("requirements")
        if requirements != PRIVACY_SYNTHETIC_REQUIREMENTS:
            validation.error(
                f"{fixture_relative}: synthetic_value_policy must retain its exact safety requirements"
            )

    for classification in PRIVACY_CLASSES:
        if classification not in model_text:
            validation.error(
                f"{model_relative}: missing classification {classification!r}"
            )
        if classification not in logging_text:
            validation.error(
                f"{logging_relative}: missing classification {classification!r}"
            )

    privacy_rows = tuple(
        re.findall(r"^\|\s*`?(PD-\d{2})`?\s*\|", model_text, re.MULTILINE)
    )
    if privacy_rows != PRIVACY_DATA_IDS:
        validation.error(
            f"{model_relative}: data-map rows must exactly equal {list(PRIVACY_DATA_IDS)!r}"
        )

    combined_text = "\n".join((model_text, logging_text))
    referenced_task_ids = set(re.findall(r"\bTL-\d{4}\b", combined_text))
    unknown_task_ids = sorted(referenced_task_ids - task_ids)
    if unknown_task_ids:
        validation.error(
            f"Privacy documents reference unknown roadmap tasks {unknown_task_ids!r}"
        )
    referenced_decision_ids = set(re.findall(r"\bD-\d{3}\b", combined_text))
    unknown_decision_ids = sorted(referenced_decision_ids - decision_ids)
    if unknown_decision_ids:
        validation.error(
            f"Privacy documents reference unknown decisions {unknown_decision_ids!r}"
        )

    for relative, text in (
        (model_relative, model_text),
        (logging_relative, logging_text),
    ):
        if SECURITY_MACHINE_PATH_RE.search(text):
            validation.error(f"{relative}: contains a machine-specific path")

    if fixture_doc.get("schema_version") != 1:
        validation.error(f"{fixture_relative}: schema_version must equal 1")
    if fixture_doc.get("fixture_type") != "synthetic privacy redaction cases":
        validation.error(
            f"{fixture_relative}: fixture_type must identify synthetic privacy redaction cases"
        )
    if fixture_doc.get("expectation_output_contract") != PRIVACY_EXPECTATION_OUTPUT_CONTRACT:
        validation.error(
            f"{fixture_relative}: expectation_output_contract must retain the exact governed stage semantics"
        )
    fixture_revision = fixture_doc.get("model_revision")
    if not isinstance(fixture_revision, str) or not fixture_revision.strip():
        validation.error(f"{fixture_relative}: model_revision must be a non-empty string")
        fixture_revision = ""
    fixture_approval = fixture_doc.get("privacy_owner_approval")
    if not isinstance(fixture_approval, str) or fixture_approval not in {
        "Pending",
        "Approved",
        "ApprovedWithConditions",
    }:
        validation.error(
            f"{fixture_relative}: privacy_owner_approval must be Pending, Approved, or ApprovedWithConditions"
        )

    cases = fixture_doc.get("cases")
    if not isinstance(cases, list):
        validation.error(f"{fixture_relative}: cases must be a list")
        cases = []
    if len(cases) < 35:
        validation.error(
            f"{fixture_relative}: at least 35 bounded adversarial and positive cases are required"
        )

    expected_case_ids = [f"PRV-{index:03d}" for index in range(1, len(cases) + 1)]
    actual_case_ids: list[str] = []
    category_coverage: set[str] = set()
    sink_coverage: set[str] = set()
    recipientless_normal_case = False
    serial_workshop_allow = False
    support_positive_allow = False
    support_id_positive = False
    fixture_decision_ids: set[str] = set()
    stable_codes: set[str] = set()

    required_case_fields = {
        "id",
        "categories",
        "sink",
        "input",
        "expectation",
        "rationale",
        "decision_refs",
        "tags",
    }
    allowed_case_fields = required_case_fields | {"bounds"}
    allowed_input_kinds = {
        "encoded_blob",
        "exception",
        "json_text",
        "nested_record",
        "path",
        "raw_stderr",
        "raw_stdout",
        "raw_xml",
        "stack_trace",
        "structured_field",
        "structured_record",
        "synthetic_collection",
        "synthetic_nesting",
        "synthetic_raw_stdout_repeat",
        "synthetic_repeat",
        "text",
        "url",
    }
    string_input_kinds = {
        "encoded_blob",
        "json_text",
        "path",
        "raw_stderr",
        "raw_stdout",
        "raw_xml",
        "stack_trace",
        "text",
        "url",
    }
    generator_input_kinds = {
        "synthetic_collection",
        "synthetic_nesting",
        "synthetic_raw_stdout_repeat",
        "synthetic_repeat",
    }
    required_expectation_fields = {
        "decision",
        "output",
        "stable_code",
        "prohibited_literals_absent",
        "required_literals_present",
    }

    for index, case in enumerate(cases):
        owner = f"{fixture_relative}: cases[{index}]"
        if not isinstance(case, dict):
            validation.error(f"{owner} must be a mapping")
            continue
        string_case_keys = {key for key in case if isinstance(key, str)}
        missing = sorted(required_case_fields - string_case_keys)
        unknown = privacy_unknown_keys(case, allowed_case_fields)
        if missing:
            validation.error(f"{owner} is missing fields {missing!r}")
        if unknown:
            validation.error(f"{owner} has unknown fields {unknown!r}")

        case_id = case.get("id")
        if not isinstance(case_id, str) or re.fullmatch(r"PRV-\d{3}", case_id) is None:
            validation.error(f"{owner}: invalid case id {case_id!r}")
            case_id = f"case at index {index}"
        else:
            actual_case_ids.append(case_id)
            owner = f"{fixture_relative}: {case_id}"

        categories = case.get("categories")
        if (
            not isinstance(categories, list)
            or not categories
            or any(not isinstance(value, str) or not value.strip() for value in categories)
        ):
            validation.error(f"{owner}: categories must be a non-empty string list")
            categories = []
        category_set = set(categories)
        category_coverage.update(category_set)
        unknown_categories = sorted(category_set - PRIVACY_CATEGORY_VOCABULARY)
        if unknown_categories:
            validation.error(f"{owner}: unknown categories {unknown_categories!r}")
        if len(categories) != len(category_set):
            validation.error(f"{owner}: categories must not contain duplicates")

        sink = case.get("sink")
        if not isinstance(sink, str) or sink not in PRIVACY_SINKS:
            validation.error(f"{owner}: unknown sink {sink!r}")
        else:
            sink_coverage.add(sink)

        input_value = case.get("input")
        if not isinstance(input_value, dict):
            validation.error(f"{owner}: input must be a mapping")
            input_value = {}
        elif set(input_value) != {"kind", "value"}:
            validation.error(f"{owner}: input must contain exactly kind and value")
        input_kind_value = input_value.get("kind")
        if (
            not isinstance(input_kind_value, str)
            or input_kind_value not in allowed_input_kinds
        ):
            validation.error(f"{owner}: unsupported input kind {input_value.get('kind')!r}")
        input_kind = input_kind_value if isinstance(input_kind_value, str) else ""
        if "value" not in input_value:
            validation.error(f"{owner}: input.value is required")
        input_payload = input_value.get("value")
        if input_kind in string_input_kinds and not isinstance(
            input_payload, str
        ):
            validation.error(
                f"{owner}: input kind {input_kind_value!r} requires one string value"
            )
        elif input_kind == "exception":
            if (
                not isinstance(input_payload, dict)
                or set(input_payload) != {"type", "message"}
                or any(
                    not isinstance(input_payload.get(field), str)
                    or not input_payload[field]
                    for field in ("type", "message")
                )
            ):
                validation.error(
                    f"{owner}: exception input requires exactly non-empty type and message strings"
                )
        elif input_kind == "structured_field":
            structured_field_valid = False
            if isinstance(input_payload, dict):
                if set(input_payload) == {"field", "value"}:
                    structured_field_valid = (
                        isinstance(input_payload.get("field"), str)
                        and bool(input_payload["field"])
                        and type(input_payload.get("value"))
                        in {str, int, bool, type(None)}
                    )
                elif set(input_payload) == {"support_id"}:
                    structured_field_valid = isinstance(
                        input_payload.get("support_id"), str
                    ) and bool(input_payload["support_id"])
            if not structured_field_valid:
                validation.error(
                    f"{owner}: structured_field input requires exactly field/value scalars or one support_id"
                )
        elif input_kind == "structured_record":
            if (
                not isinstance(input_payload, dict)
                or not input_payload
                or any(not isinstance(key, str) or not key for key in input_payload)
                or any(type(value) not in {str, int, bool, type(None)} for value in input_payload.values())
            ):
                validation.error(
                    f"{owner}: structured_record input requires one flat non-empty scalar mapping"
                )
        elif input_kind == "nested_record" and not isinstance(
            input_payload, dict
        ):
            validation.error(f"{owner}: nested_record input requires a mapping")
        elif input_kind in {"synthetic_repeat", "synthetic_raw_stdout_repeat"}:
            if (
                not isinstance(input_payload, dict)
                or set(input_payload) != {"unit", "repeat"}
                or not isinstance(input_payload.get("unit"), str)
                or not input_payload["unit"]
                or not isinstance(input_payload.get("repeat"), int)
                or isinstance(input_payload.get("repeat"), bool)
                or not 1 <= input_payload["repeat"] <= 10_000
            ):
                validation.error(
                    f"{owner}: repeat generator needs exactly a bounded string unit and integer repeat"
                )
        elif input_kind == "synthetic_collection":
            if (
                not isinstance(input_payload, dict)
                or set(input_payload) != {"item_template", "item_count"}
                or not isinstance(input_payload.get("item_template"), str)
                or "{index}" not in input_payload["item_template"]
                or not isinstance(input_payload.get("item_count"), int)
                or isinstance(input_payload.get("item_count"), bool)
                or not 1 <= input_payload["item_count"] <= 10_000
            ):
                validation.error(
                    f"{owner}: collection generator needs exactly item_template and bounded item_count"
                )
        elif input_kind == "synthetic_nesting":
            if (
                not isinstance(input_payload, dict)
                or set(input_payload)
                != {"level_key_template", "nesting_depth", "leaf"}
                or not isinstance(input_payload.get("level_key_template"), str)
                or "{index}" not in input_payload["level_key_template"]
                or not isinstance(input_payload.get("leaf"), str)
                or not isinstance(input_payload.get("nesting_depth"), int)
                or isinstance(input_payload.get("nesting_depth"), bool)
                or not 1 <= input_payload["nesting_depth"] <= 64
            ):
                validation.error(
                    f"{owner}: nesting generator needs exactly a key template, bounded depth, and string leaf"
                )
        if not is_json_scalar_tree(input_payload):
            validation.error(
                f"{owner}: input must contain only bounded JSON-compatible string, integer, Boolean, null, mapping, or list values"
            )
        input_strings = nested_string_values(input_payload)
        input_scalar_strings = nested_scalar_strings(input_payload)
        input_depth, input_nodes = privacy_structure_metrics(input_payload)
        input_payload_bytes = privacy_payload_utf8_bytes(input_payload)
        input_collection_items = privacy_max_collection_items(input_payload)
        if input_payload_bytes is None:
            validation.error(f"{owner}: input cannot be deterministically byte-counted")
        if any(
            len(value.encode("utf-8")) > PRIVACY_LITERAL_MAX_SCALAR_BYTES
            for value in input_scalar_strings
        ):
            validation.error(
                f"{owner}: literal input scalar exceeds the {PRIVACY_LITERAL_MAX_SCALAR_BYTES}-byte fixture limit; use a deterministic generator"
            )
        if (
            input_kind not in generator_input_kinds
            and input_payload_bytes is not None
            and input_payload_bytes > PRIVACY_LITERAL_MAX_INPUT_BYTES
        ):
            validation.error(
                f"{owner}: literal input exceeds the {PRIVACY_LITERAL_MAX_INPUT_BYTES}-byte fixture limit; use a deterministic generator"
            )
        if input_depth > 8 or input_nodes > 256:
            validation.error(
                f"{owner}: parsed input exceeds validator depth/node safety limits"
            )
        preliminary_expectation = case.get("expectation")
        preliminary_decision = (
            preliminary_expectation.get("decision")
            if isinstance(preliminary_expectation, dict)
            else None
        )
        has_reserved_synthetic_marker = any(
            re.search(r"synthetic[_-]", value, re.IGNORECASE) is not None
            or "example.invalid" in value.casefold()
            or value.startswith("192.0.2.")
            or value.casefold().startswith("2001:db8")
            or value.casefold().startswith("02-00-")
            for value in input_scalar_strings
        )
        if (
            not has_reserved_synthetic_marker
            and not (
                preliminary_decision in {"allow", "replace"}
                and category_set
                and category_set <= PRIVACY_UNMARKED_POSITIVE_CATEGORIES
            )
        ):
            validation.error(
                f"{owner}: sensitive/adversarial input must contain a reserved synthetic marker"
            )
        labeled_secret_values = secret_labeled_values(input_payload)
        invalid_labeled_secret_values = [
            value
            for value in labeled_secret_values
            if value is not None
            and (
                not isinstance(value, str)
                or not is_conspicuous_synthetic_secret(value)
            )
        ]
        if invalid_labeled_secret_values:
            validation.error(
                f"{owner}: every declared secret field needs its own complete SYNTHETIC_*_DO_NOT_USE scalar or an explicit null-to-drop case"
            )
        if (
            labeled_secret_values
            and not category_set & PRIVACY_SECRET_CATEGORIES
            and any(value is not None for value in labeled_secret_values)
        ):
            validation.error(
                f"{owner}: declared secret fields require an explicit governed secret category"
            )
        embedded_secrets = [
            secret
            for scalar in input_scalar_strings
            for secret in embedded_secret_values(scalar)
        ]
        if any(
            not is_conspicuous_synthetic_secret(secret)
            for secret in embedded_secrets
        ):
            validation.error(
                f"{owner}: every embedded credential/query value needs its own complete SYNTHETIC_*_DO_NOT_USE marker"
            )
        if embedded_secrets and not category_set & PRIVACY_SECRET_CATEGORIES:
            validation.error(
                f"{owner}: embedded credential/query values require an explicit governed secret category"
            )
        if category_set & PRIVACY_SECRET_CATEGORIES and input_kind not in {
            "nested_record",
            "raw_stderr",
            "raw_stdout",
            "structured_field",
            "text",
            "url",
        }:
            validation.error(
                f"{owner}: secret categories must use a governed inspectable secret input form"
            )
        if category_set & PRIVACY_SECRET_CATEGORIES and not any(
            "do_not_use" in privacy_normal_form(unquote(unquote(value)))
            for value in input_scalar_strings
        ):
            validation.error(
                f"{owner}: secret-like input needs a scalar DO_NOT_USE safety marker"
            )
        if category_set & PRIVACY_SECRET_CATEGORIES:
            if (
                input_kind == "structured_field"
                and isinstance(input_payload, dict)
                and not isinstance(input_payload.get("value"), str)
                or input_kind == "structured_field"
                and isinstance(input_payload, dict)
                and isinstance(input_payload.get("value"), str)
                and not is_conspicuous_synthetic_secret(input_payload["value"])
            ):
                validation.error(
                    f"{owner}: secret-category structured field needs one complete SYNTHETIC_*_DO_NOT_USE value"
                )
            unmarked_secret_fields = [
                value
                for value in secret_labeled_scalar_values(input_payload)
                if not is_conspicuous_synthetic_secret(value)
            ]
            if unmarked_secret_fields:
                validation.error(
                    f"{owner}: every secret-labeled scalar needs its own complete SYNTHETIC_*_DO_NOT_USE safety marker"
                )
            if (
                input_kind in {"url", "raw_stdout", "raw_stderr", "text"}
                and not embedded_secrets
                and not any(
                    is_conspicuous_synthetic_secret(value)
                    for value in input_scalar_strings
                )
            ):
                validation.error(
                    f"{owner}: scalar secret fixture needs a complete synthetic secret value in the governed context"
                )
        for input_string in input_strings:
            canonical_input_string = privacy_fixture_canonical_text(input_string)
            if re.search(r"%[0-9a-fA-F]{2}", canonical_input_string):
                validation.error(
                    f"{owner}: fixture input retains percent encoding after the normalization cap"
                )
            for path_match in FIXTURE_MACHINE_PATH_RE.finditer(
                canonical_input_string
            ):
                if not fixture_path_is_synthetic(path_match.group(0)):
                    validation.error(
                        f"{owner}: fixture contains a non-synthetic machine-specific path"
                    )

        expectation = case.get("expectation")
        if not isinstance(expectation, dict):
            validation.error(f"{owner}: expectation must be a mapping")
            expectation = {}
        else:
            string_expectation_keys = {
                key for key in expectation if isinstance(key, str)
            }
            missing_expectation = sorted(
                required_expectation_fields - string_expectation_keys
            )
            unknown_expectation = privacy_unknown_keys(
                expectation, required_expectation_fields
            )
            if missing_expectation:
                validation.error(
                    f"{owner}: expectation is missing fields {missing_expectation!r}"
                )
            if unknown_expectation:
                validation.error(
                    f"{owner}: expectation has unknown fields {unknown_expectation!r}"
                )

        decision = expectation.get("decision")
        output = expectation.get("output")
        if not isinstance(decision, str) or decision not in PRIVACY_DECISIONS:
            validation.error(f"{owner}: unsupported decision {decision!r}")
            decision = None
        if decision in {"drop", "reject"} and output is not None:
            validation.error(f"{owner}: drop/reject output must be null")
        if decision == "replace" and (output is None or output == input_payload):
            validation.error(
                f"{owner}: replace needs an exact non-null output different from input"
            )
        if decision == "allow" and output is None:
            validation.error(f"{owner}: allow needs an exact non-null output")
        if decision == "allow" and output != input_payload:
            validation.error(f"{owner}: allow output must exactly equal its input")
        stable_code = expectation.get("stable_code")
        if not isinstance(stable_code, str) or not stable_code.strip():
            validation.error(f"{owner}: stable_code must be a non-empty string")
        elif re.fullmatch(r"PRV_[A-Z0-9_]+", stable_code) is None:
            validation.error(f"{owner}: stable_code must use the governed PRV_* format")
        elif stable_code in stable_codes:
            validation.error(f"{owner}: stable_code {stable_code!r} is duplicated")
        else:
            stable_codes.add(stable_code)

        absent_literals = expectation.get("prohibited_literals_absent")
        present_literals = expectation.get("required_literals_present")
        for field, values in (
            ("prohibited_literals_absent", absent_literals),
            ("required_literals_present", present_literals),
        ):
            if not isinstance(values, list) or any(
                not isinstance(value, str) or not value for value in values
            ):
                validation.error(f"{owner}: {field} must be a string list")
        if not isinstance(absent_literals, list) or any(
            not isinstance(value, str) or not value for value in absent_literals
        ):
            absent_literals = []
        if not isinstance(present_literals, list) or any(
            not isinstance(value, str) or not value for value in present_literals
        ):
            present_literals = []
        output_strings = nested_string_values(output)
        output_depth, output_nodes = privacy_structure_metrics(output)
        output_scalar_strings = nested_scalar_strings(output)
        output_payload_bytes = privacy_payload_utf8_bytes(output)
        if not is_json_scalar_tree(output):
            validation.error(
                f"{owner}: expected output must contain only bounded JSON-compatible values"
            )
        if output_payload_bytes is None:
            validation.error(
                f"{owner}: expected output cannot be deterministically byte-counted"
            )
        if secret_labeled_values(output):
            validation.error(
                f"{owner}: expected output cannot retain a secret-labeled field, even as a marker or null"
            )
        if any(
            len(value.encode("utf-8")) > PRIVACY_LITERAL_MAX_SCALAR_BYTES
            for value in output_scalar_strings
        ):
            validation.error(
                f"{owner}: expected output scalar exceeds the {PRIVACY_LITERAL_MAX_SCALAR_BYTES}-byte limit"
            )
        if (
            output_payload_bytes is not None
            and output_payload_bytes > PRIVACY_LITERAL_MAX_OUTPUT_BYTES
        ):
            validation.error(
                f"{owner}: expected output exceeds the {PRIVACY_LITERAL_MAX_OUTPUT_BYTES}-byte limit"
            )
        if output_depth > 8 or output_nodes > 256:
            validation.error(
                f"{owner}: expected output exceeds validator depth/node safety limits"
            )
        for literal in absent_literals:
            if not any(literal in source for source in input_strings):
                validation.error(
                    f"{owner}: prohibited literal {literal!r} is not present in the synthetic input"
                )
            normalized_literal = privacy_normal_form(literal)
            if any(
                normalized_literal
                and normalized_literal in privacy_normal_form(rendered)
                for rendered in output_strings
            ):
                validation.error(
                    f"{owner}: prohibited literal {literal!r} survives expected output"
                )
        for literal in present_literals:
            if not any(literal in rendered for rendered in output_strings):
                validation.error(
                    f"{owner}: required literal {literal!r} is absent from expected output"
                )
        if any(
            FIXTURE_MACHINE_PATH_RE.search(
                privacy_fixture_canonical_text(value)
            )
            for value in output_strings
        ):
            validation.error(f"{owner}: expected output cannot contain a machine-specific path")

        rationale = case.get("rationale")
        if not isinstance(rationale, str) or not rationale.strip():
            validation.error(f"{owner}: rationale must be a non-empty string")
        decision_refs = case.get("decision_refs")
        if (
            not isinstance(decision_refs, list)
            or not decision_refs
            or any(not isinstance(value, str) for value in decision_refs)
        ):
            validation.error(f"{owner}: decision_refs must be a non-empty string list")
            decision_refs = []
        for decision_ref in decision_refs:
            fixture_decision_ids.add(decision_ref)
            if decision_ref not in decision_ids:
                validation.error(f"{owner}: unknown decision reference {decision_ref!r}")
        if len(decision_refs) != len(set(decision_refs)):
            validation.error(f"{owner}: decision_refs must not contain duplicates")
        tags = case.get("tags")
        if not isinstance(tags, list) or any(
            not isinstance(value, str) or not value.strip() for value in tags
        ):
            validation.error(f"{owner}: tags must be a string list")
            tags = []
        elif len(tags) != len(set(tags)):
            validation.error(f"{owner}: tags must not contain duplicates")

        bounds = case.get("bounds")
        if bounds is not None:
            if not isinstance(bounds, dict) or not bounds:
                validation.error(f"{owner}: bounds must be a non-empty mapping")
            else:
                unknown_bounds = privacy_unknown_keys(bounds, PRIVACY_BOUND_FIELDS)
                if unknown_bounds:
                    validation.error(f"{owner}: unknown bounds {unknown_bounds!r}")
                if any(
                    not isinstance(value, int) or isinstance(value, bool) or value <= 0
                    for value in bounds.values()
                ):
                    validation.error(f"{owner}: every bound must be a positive integer")
                declared_input_limit = bounds.get("max_input_utf8_bytes")
                if (
                    input_kind not in generator_input_kinds
                    and isinstance(declared_input_limit, int)
                    and not isinstance(declared_input_limit, bool)
                    and input_payload_bytes is not None
                    and input_payload_bytes > declared_input_limit
                ):
                    validation.error(
                        f"{owner}: literal input exceeds its declared max_input_utf8_bytes"
                    )
                declared_output_limit = bounds.get("max_output_utf8_bytes")
                if (
                    isinstance(declared_output_limit, int)
                    and not isinstance(declared_output_limit, bool)
                    and output_payload_bytes is not None
                    and output_payload_bytes > declared_output_limit
                ):
                    validation.error(
                        f"{owner}: expected output exceeds its declared max_output_utf8_bytes"
                    )
                declared_depth_limit = bounds.get("max_nesting_depth")
                if (
                    input_kind not in generator_input_kinds
                    and isinstance(declared_depth_limit, int)
                    and not isinstance(declared_depth_limit, bool)
                    and input_depth > declared_depth_limit
                ):
                    validation.error(
                        f"{owner}: literal input exceeds its declared max_nesting_depth"
                    )
                declared_collection_limit = bounds.get("max_collection_items")
                if (
                    input_kind not in generator_input_kinds
                    and isinstance(declared_collection_limit, int)
                    and not isinstance(declared_collection_limit, bool)
                    and input_collection_items > declared_collection_limit
                ):
                    validation.error(
                        f"{owner}: literal input exceeds its declared max_collection_items"
                    )
                declared_scalar_limit = bounds.get("max_scalar_utf8_bytes")
                if (
                    input_kind not in generator_input_kinds
                    and isinstance(declared_scalar_limit, int)
                    and not isinstance(declared_scalar_limit, bool)
                    and any(
                        len(value.encode("utf-8")) > declared_scalar_limit
                        for value in input_scalar_strings
                    )
                ):
                    validation.error(
                        f"{owner}: literal input exceeds its declared max_scalar_utf8_bytes"
                    )
                declared_stack_limit = bounds.get("max_stack_frames")
                if (
                    input_kind == "stack_trace"
                    and isinstance(input_payload, str)
                    and isinstance(declared_stack_limit, int)
                    and not isinstance(declared_stack_limit, bool)
                ):
                    nonempty_lines = sum(
                        1 for line in input_payload.splitlines() if line.strip()
                    )
                    explicit_frames = len(
                        re.findall(
                            r"(?i)(?:^|[;\n])\s*at\s+",
                            input_payload,
                        )
                    )
                    if max(nonempty_lines, explicit_frames) > declared_stack_limit:
                        validation.error(
                            f"{owner}: stack trace exceeds its declared max_stack_frames"
                        )
                for actual_field, maximum_field in (
                    ("actual_collection_items", "max_collection_items"),
                    ("actual_input_utf8_bytes", "max_input_utf8_bytes"),
                    ("actual_nesting_depth", "max_nesting_depth"),
                    ("actual_scalar_utf8_bytes", "max_scalar_utf8_bytes"),
                ):
                    if actual_field in bounds and maximum_field not in bounds:
                        validation.error(
                            f"{owner}: {actual_field} needs paired {maximum_field}"
                        )
                    if (
                        actual_field in bounds
                        and maximum_field in bounds
                        and isinstance(bounds[actual_field], int)
                        and not isinstance(bounds[actual_field], bool)
                        and isinstance(bounds[maximum_field], int)
                        and not isinstance(bounds[maximum_field], bool)
                        and bounds[actual_field] > bounds[maximum_field]
                        and decision not in {"reject", "replace"}
                    ):
                        validation.error(
                            f"{owner}: over-bound input must reject or use an exact bounded replacement"
                        )
                if isinstance(input_payload, dict):
                    input_kind = input_value.get("kind")
                    computed_bound: tuple[str, int] | None = None
                    if isinstance(input_kind, str) and input_kind in {
                        "synthetic_repeat",
                        "synthetic_raw_stdout_repeat",
                    }:
                        unit = input_payload.get("unit")
                        repeat = input_payload.get("repeat")
                        if isinstance(unit, str) and isinstance(repeat, int):
                            field = (
                                "actual_scalar_utf8_bytes"
                                if input_kind == "synthetic_repeat"
                                else "actual_input_utf8_bytes"
                            )
                            computed_bound = (field, len(unit.encode("utf-8")) * repeat)
                    elif input_kind == "synthetic_collection" and isinstance(
                        input_payload.get("item_count"), int
                    ):
                        computed_bound = (
                            "actual_collection_items",
                            input_payload["item_count"],
                        )
                    elif input_kind == "synthetic_nesting" and isinstance(
                        input_payload.get("nesting_depth"), int
                    ):
                        computed_bound = (
                            "actual_nesting_depth",
                            input_payload["nesting_depth"],
                        )
                    if computed_bound is not None:
                        actual_field, computed_value = computed_bound
                        if bounds.get(actual_field) != computed_value:
                            validation.error(
                                f"{owner}: {actual_field} does not match the deterministic synthetic generator"
                            )
                category_bound_pairs = {
                    "oversized_input": (
                        ("actual_scalar_utf8_bytes", "max_scalar_utf8_bytes"),
                        ("actual_input_utf8_bytes", "max_input_utf8_bytes"),
                    ),
                    "oversized_collection": (
                        ("actual_collection_items", "max_collection_items"),
                    ),
                    "excessive_depth": (
                        ("actual_nesting_depth", "max_nesting_depth"),
                    ),
                }
                for bound_category, pairs in category_bound_pairs.items():
                    if bound_category in category_set and not any(
                        actual in bounds
                        and maximum in bounds
                        and isinstance(bounds[actual], int)
                        and not isinstance(bounds[actual], bool)
                        and isinstance(bounds[maximum], int)
                        and not isinstance(bounds[maximum], bool)
                        and bounds[actual] > bounds[maximum]
                        for actual, maximum in pairs
                    ):
                        validation.error(
                            f"{owner}: {bound_category} must prove an actual value above its governed maximum"
                        )

        output_keys = nested_mapping_keys(output)
        if sink == "support_export" and decision in {"allow", "replace"}:
            if not isinstance(output, dict):
                validation.error(
                    f"{owner}: non-null support output must be an exact allowlisted mapping"
                )
            unknown_support_fields = sorted(output_keys - PRIVACY_SUPPORT_FIELDS)
            if unknown_support_fields:
                validation.error(
                    f"{owner}: support output contains non-allowlisted fields {unknown_support_fields!r}"
                )
            if isinstance(output, dict) and any(
                isinstance(value, (dict, list)) or value is None
                for value in output.values()
            ):
                validation.error(
                    f"{owner}: support output fields must use bounded scalar values"
                )
            if isinstance(output, dict):
                validate_support_output_fields(owner, output, validation)
        if sink == "ordinary_log" and decision in {"allow", "replace"}:
            if not isinstance(output, dict):
                validation.error(
                    f"{owner}: non-null ordinary-log output must use the closed event envelope"
                )
            unknown_log_fields = sorted(output_keys - PRIVACY_LOG_FIELDS)
            if unknown_log_fields:
                validation.error(
                    f"{owner}: ordinary-log output contains non-envelope fields {unknown_log_fields!r}"
                )
            if isinstance(output, dict):
                for field, value in output.items():
                    if isinstance(value, dict) or (
                        isinstance(value, list) and field != "redaction_flags"
                    ):
                        validation.error(
                            f"{owner}: ordinary-log output contains an invalid structured value"
                        )
                    if field == "redaction_flags" and (
                        not isinstance(value, list)
                        or not 1 <= len(value) <= 8
                        or any(not isinstance(item, str) for item in value)
                        or len(value) != len(set(value))
                        or any(
                            item not in {
                                "ValueDropped",
                                "ValueReplaced",
                                "ValueTruncated",
                                "InputRejected",
                            }
                            for item in value
                        )
                    ):
                        validation.error(
                            f"{owner}: redaction_flags must use one to eight governed values"
                        )
                validate_log_output_fields(owner, output, validation)
        if sink == "local_crash" and decision not in {"drop", "reject"}:
            validation.error(f"{owner}: the production local-crash sink accepts no output")
        recipient_secret_keys = {
            "recipient_identity",
            "recipient_name",
            "secret_value",
            "password",
            "credential",
            "recovery_key",
            "encryption_key",
        }
        if sink == "recipient_guide" and output_keys & recipient_secret_keys:
            validation.error(
                f"{owner}: recipient-guide output contains identity or secret-value fields"
            )

        if "recipientless" in tags and decision == "allow":
            expected_recipientless_fields = {"job_id", "device_state"}
            recipientless_shape_valid = (
                "recipient_identity_absent" in category_set
                and sink == "workshop_record"
                and isinstance(input_payload, dict)
                and set(input_payload) == expected_recipientless_fields
                and isinstance(output, dict)
                and set(output) == expected_recipientless_fields
                and output == input_payload
            )
            if not recipientless_shape_valid:
                validation.error(
                    f"{owner}: recipient-less normal contract must contain exactly job_id and device_state"
                )
            recipientless_normal_case = recipientless_shape_valid
            forbidden_identity_keys = {
                "recipient",
                "recipient_identity",
                "recipient_name",
                "email",
                "phone",
                "address",
            }
            input_keys = nested_mapping_keys(input_payload)
            if (input_keys | output_keys) & forbidden_identity_keys:
                validation.error(
                    f"{owner}: recipient-less normal contract cannot contain identity fields"
                )
        if "recipient_identity" in category_set and decision not in {"drop", "reject"}:
            validation.error(
                f"{owner}: recipient identity is unnecessary and must be dropped or rejected in every sink"
            )
        if "full_serial" in category_set:
            if sink == "workshop_record" and decision == "allow":
                serial_workshop_allow = True
            elif decision == "allow":
                validation.error(
                    f"{owner}: full serial may be allowed only in the workshop record"
                )
        if sink == "support_export" and "positive" in tags and decision == "allow":
            support_positive_allow = True
            if isinstance(output, dict) and "support_id" in output:
                support_id_positive = True
        telemetry_classified = "telemetry" in category_set or "telemetry" in tags
        if telemetry_classified and sink != "telemetry":
            validation.error(
                f"{owner}: telemetry-classified input cannot be laundered through another sink"
            )
        if (sink == "telemetry" or telemetry_classified) and decision not in {"drop", "reject"}:
            validation.error(f"{owner}: telemetry cases must fail closed")
        if category_set & {"raw_output", "raw_provider_report"} and decision == "allow":
            validation.error(f"{owner}: raw output cannot be allowed in a sink")
        if category_set & PRIVACY_SECRET_CATEGORIES and decision == "allow":
            validation.error(f"{owner}: secret material cannot be allowed")
        if (
            "external_private_data" in tags
            or any(value.startswith("external_private_") for value in category_set)
            or "external_recovery_credential" in category_set
        ) and decision != "reject":
            validation.error(f"{owner}: external private data must be rejected")

    if actual_case_ids != expected_case_ids:
        validation.error(
            f"{fixture_relative}: case IDs must be unique, contiguous, and ordered from PRV-001"
        )
    missing_decision_coverage = sorted(PRIVACY_DECISION_IDS - fixture_decision_ids)
    if missing_decision_coverage:
        validation.error(
            f"{fixture_relative}: fixtures do not cover TL-0005 decisions {missing_decision_coverage!r}"
        )
    missing_categories = sorted(PRIVACY_REQUIRED_CASE_CATEGORIES - category_coverage)
    if missing_categories:
        validation.error(
            f"{fixture_relative}: missing required case categories {missing_categories!r}"
        )
    missing_vocabulary_coverage = sorted(PRIVACY_CATEGORY_VOCABULARY - category_coverage)
    if missing_vocabulary_coverage:
        validation.error(
            f"{fixture_relative}: category vocabulary lacks case coverage {missing_vocabulary_coverage!r}"
        )
    missing_sinks = sorted(PRIVACY_SINKS - sink_coverage)
    if missing_sinks:
        validation.error(
            f"{fixture_relative}: missing required sink coverage {missing_sinks!r}"
        )
    if not recipientless_normal_case:
        validation.error(
            f"{fixture_relative}: needs an allowed recipient-less normal-job case"
        )
    if not serial_workshop_allow:
        validation.error(
            f"{fixture_relative}: needs a workshop-only full-serial allow case"
        )
    if not support_positive_allow:
        validation.error(
            f"{fixture_relative}: needs a positive support-allowlist case"
        )
    if not support_id_positive:
        validation.error(
            f"{fixture_relative}: needs a positive support_id case using the exact field name"
        )

    model_revision = security_field(model_text, "Model revision")
    logging_revision = security_field(logging_text, "Model revision")
    if not model_revision or len({model_revision, logging_revision, fixture_revision}) != 1:
        validation.error("Privacy documents and fixtures must share one exact model revision")

    approval_match = re.search(
        r"^\*\*Privacy-owner approval:\*\*\s*\*\*(Pending|Approved|Approved with conditions)\*\*\s*$",
        model_text,
        re.MULTILINE,
    )
    approval_state = approval_match.group(1) if approval_match is not None else ""
    if not approval_state:
        validation.error(
            f"{model_relative}: privacy-owner approval must be Pending, Approved, or Approved with conditions"
        )
    approval_metadata: dict[str, str] = {}
    for field in (
        "Approving owner and role",
        "Approval date",
        "Reviewed source commit",
        "Approval reference",
    ):
        approval_metadata[field] = security_field(model_text, field)
        if not approval_metadata[field]:
            validation.error(f"{model_relative}: missing approval metadata {field!r}")
    logging_review_match = re.search(
        r"^\*\*Review result:\*\*\s*(Pending|Approved|Approved with conditions)\s*$",
        logging_text,
        re.MULTILINE,
    )
    logging_review = logging_review_match.group(1) if logging_review_match is not None else ""
    model_status = security_field(model_text, "Status")
    logging_status = security_field(logging_text, "Status")
    model_draft_date_text = security_field(model_text, "Draft date")
    logging_draft_date_text = security_field(logging_text, "Draft date")
    model_draft_date = parse_iso_calendar_date(model_draft_date_text)
    logging_draft_date = parse_iso_calendar_date(logging_draft_date_text)
    if model_draft_date is None:
        validation.error(
            f"{model_relative}: Draft date must be a real calendar date in exact YYYY-MM-DD form"
        )
    if logging_draft_date is None:
        validation.error(
            f"{logging_relative}: Draft date must be a real calendar date in exact YYYY-MM-DD form"
        )
    if (
        model_draft_date is not None
        and logging_draft_date is not None
        and model_draft_date != logging_draft_date
    ):
        validation.error(
            "Privacy model and logging standard must share one exact Draft date"
        )
    pending_disclaimers = (
        "No privacy-owner approval is recorded in this draft.",
        "No approval is present in this draft.",
        "`TL-0005` must remain in review until a named privacy owner approves",
        "No named privacy owner has approved this exact revision",
        "This pending draft does not satisfy the human evidence required by `TL-0005`",
        "Human approval of the classifications and default retention guidance remains pending.",
    )
    review_rows = re.findall(
        r"^- \[([ xX])\] `(PR-\d{2})` — (.+)$", model_text, re.MULTILINE
    )
    review_ids = tuple(row[1] for row in review_rows)
    if review_ids != PRIVACY_REVIEW_IDS:
        validation.error(
            f"{model_relative}: review checklist must contain exactly PR-01 through PR-16 once and in order"
        )

    if approval_state == "Pending":
        if any(value.casefold() != "pending" for value in approval_metadata.values()):
            validation.error(
                f"{model_relative}: pending approval metadata must remain Pending"
            )
        if fixture_approval != "Pending" or logging_review != "Pending":
            validation.error(
                "Privacy review results must be Pending while owner approval is pending"
            )
        if model_status != "Draft for privacy-owner review" or logging_status != "Draft for privacy-owner review":
            validation.error(
                "Pending privacy documents must have status 'Draft for privacy-owner review'"
            )
        if "draft" not in model_revision.casefold():
            validation.error("Pending privacy model revision must identify a draft")
        if any(
            disclaimer not in "\n".join((model_text, logging_text))
            for disclaimer in pending_disclaimers
        ):
            validation.error(
                "Pending privacy documents must retain the no-approval disclaimers"
            )
        if any(mark.casefold() == "x" for mark, _, _ in review_rows) or any(
            "**Disposition:**" in detail for _, _, detail in review_rows
        ):
            validation.error(
                f"{model_relative}: pending review checklist entries must remain unchecked without dispositions"
            )
    elif approval_state in {"Approved", "Approved with conditions"}:
        if any(value.casefold() == "pending" for value in approval_metadata.values()):
            validation.error(
                f"{model_relative}: approved model needs named owner/role, date, and exact reference"
            )
        owner_parts = re.split(
            r"\s+—\s+", approval_metadata.get("Approving owner and role", ""), maxsplit=1
        )
        placeholder_names = {"example reviewer", "pending", "privacy owner", "tbd", "unknown"}
        if (
            len(owner_parts) != 2
            or len(owner_parts[0].strip()) < 3
            or owner_parts[0].strip().casefold() in placeholder_names
            or owner_parts[1].strip().casefold() != "privacy owner"
        ):
            validation.error(
                f"{model_relative}: approved model needs a non-placeholder named privacy owner and role"
            )
        approval_date = parse_iso_calendar_date(
            approval_metadata.get("Approval date", "")
        )
        if approval_date is None:
            validation.error(
                f"{model_relative}: approved model needs a real approval date in exact YYYY-MM-DD form"
            )
        if approval_date is not None and approval_date > date.today():
            validation.error(
                f"{model_relative}: approval date cannot be in the future"
            )
        if (
            approval_date is not None
            and model_draft_date is not None
            and approval_date < model_draft_date
        ):
            validation.error(
                f"{model_relative}: approval date cannot precede the reviewed draft date"
            )
        approval_commit = re.fullmatch(
            r"[0-9a-fA-F]{40}",
            approval_metadata.get("Reviewed source commit", ""),
        )
        if approval_commit is None:
            validation.error(
                f"{model_relative}: approved model needs an immutable 40-character Git commit"
            )
        approval_reference = approval_metadata.get("Approval reference", "").strip()
        internal_approval_reference = (
            "TASKS.yaml TL-0005 privacy-owner approval evidence"
        )
        github_approval_reference = re.fullmatch(
            r"https://github\.com/PikkuJanne/ThirdLife/(?:"
            r"pull/\d+#(?:issuecomment-\d+|pullrequestreview-\d+)|"
            r"issues/\d+#issuecomment-\d+)",
            approval_reference,
        )
        if (
            approval_reference != internal_approval_reference
            and github_approval_reference is None
        ):
            validation.error(
                f"{model_relative}: approved model needs a durable non-local approval reference"
            )
        expected_fixture_approval = (
            "ApprovedWithConditions"
            if approval_state == "Approved with conditions"
            else "Approved"
        )
        if fixture_approval != expected_fixture_approval or logging_review != approval_state:
            validation.error(
                "Privacy review results must use one coherent approved state"
            )
        if model_status != "Approved privacy model" or logging_status != "Approved privacy model":
            validation.error(
                "Approved privacy documents must have status 'Approved privacy model'"
            )
        if any(
            disclaimer in "\n".join((model_text, logging_text))
            for disclaimer in pending_disclaimers
        ):
            validation.error(
                "Approved privacy documents cannot retain pending/no-approval disclaimers"
            )
        condition_count = 0
        for mark, review_id, detail in review_rows:
            if mark.casefold() != "x":
                validation.error(
                    f"{model_relative}: approved checklist entry {review_id} must be checked"
                )
                continue
            if detail.count(" **Disposition:** ") != 1:
                validation.error(
                    f"{model_relative}: approved checklist entry {review_id} needs exactly one Approve or Condition disposition"
                )
                continue
            _, disposition = detail.rsplit(" **Disposition:** ", 1)
            if disposition == "Approve — reviewed without conditions.":
                continue
            condition_match = re.fullmatch(
                r"Condition — owner=([^;]{3,80}); gate=(TL-\d{4}); condition=(.{3,240})",
                disposition,
            )
            if condition_match is None:
                validation.error(
                    f"{model_relative}: approved checklist entry {review_id} has an invalid disposition grammar"
                )
                continue
            condition_owner, condition_gate, condition_text = condition_match.groups()
            condition_count += 1
            condition_placeholders = {"none", "pending", "tbd", "unknown"}
            condition_owner = condition_owner.strip()
            condition_text = condition_text.strip()
            dependency_map = {
                task_id: [
                    dependency
                    for dependency in task.get("depends_on", [])
                    if isinstance(dependency, str)
                ]
                for task_id, task in task_by_id.items()
            }
            if (
                len(condition_owner) < 3
                or not any(character.isalnum() for character in condition_owner)
                or len(condition_text) < 3
                or not any(character.isalnum() for character in condition_text)
                or condition_owner.casefold() in condition_placeholders
                or condition_text.casefold() in condition_placeholders
                or condition_gate not in task_ids
                or condition_gate == "TL-0005"
                or task_by_id.get(condition_gate, {}).get("status")
                in {"done", "cancelled"}
                or "TL-0005" not in ancestors(condition_gate, dependency_map)
            ):
                validation.error(
                    f"{model_relative}: conditional checklist entry {review_id} needs a named owner, concrete condition, and unfinished downstream task gate"
                )
        if approval_state == "Approved" and condition_count:
            validation.error(
                "Approved privacy review cannot contain conditions without the Approved with conditions state"
            )
        if approval_state == "Approved with conditions" and not condition_count:
            validation.error(
                "Approved with conditions privacy review needs at least one conditional checklist disposition"
            )

        if approval_commit is not None:
            reviewed_commit = approval_commit.group(0).casefold()
            if not git_commit_is_reachable(reviewed_commit):
                validation.error(
                    f"{model_relative}: reviewed source must be an existing reachable Git commit"
                )
            reviewed_sources = {
                relative: read_git_text_at_commit(reviewed_commit, relative)
                for relative in (model_relative, logging_relative, fixture_relative)
            }
            missing_reviewed_sources = sorted(
                relative
                for relative, content in reviewed_sources.items()
                if content is None
            )
            if missing_reviewed_sources:
                validation.error(
                    f"{model_relative}: reviewed commit is missing governed privacy artifacts {missing_reviewed_sources!r}"
                )
            else:
                reviewed_model = reviewed_sources[model_relative]
                reviewed_logging = reviewed_sources[logging_relative]
                reviewed_fixture_text = reviewed_sources[fixture_relative]
                assert reviewed_model is not None
                assert reviewed_logging is not None
                assert reviewed_fixture_text is not None
                try:
                    reviewed_fixture = parse_strict_yaml_text(reviewed_fixture_text)
                except Exception as exc:
                    validation.error(
                        f"{fixture_relative}: reviewed commit fixture cannot be parsed strictly: {exc}"
                    )
                    reviewed_fixture = None
                reviewed_revisions = {
                    security_field(reviewed_model, "Model revision"),
                    security_field(reviewed_logging, "Model revision"),
                    reviewed_fixture.get("model_revision")
                    if isinstance(reviewed_fixture, dict)
                    else None,
                }
                if reviewed_revisions != {model_revision}:
                    validation.error(
                        "Approved privacy artifacts must retain the exact model revision from the reviewed commit"
                    )
                if canonical_privacy_markdown(
                    model_text, "## 12. Privacy-owner review and approval"
                ) != canonical_privacy_markdown(
                    reviewed_model, "## 12. Privacy-owner review and approval"
                ):
                    validation.error(
                        f"{model_relative}: normative content differs from the reviewed commit"
                    )
                if canonical_privacy_markdown(
                    logging_text, "## Privacy-owner review checklist"
                ) != canonical_privacy_markdown(
                    reviewed_logging, "## Privacy-owner review checklist"
                ):
                    validation.error(
                        f"{logging_relative}: normative content differs from the reviewed commit"
                    )
                if isinstance(reviewed_fixture, dict) and canonical_privacy_fixture(
                    fixture_doc
                ) != canonical_privacy_fixture(reviewed_fixture):
                    validation.error(
                        f"{fixture_relative}: normative content differs from the reviewed commit"
                    )

    privacy_task = task_by_id.get("TL-0005", {})
    privacy_task_status = privacy_task.get("status")
    approved_state = approval_state in {"Approved", "Approved with conditions"}
    if privacy_task_status == "done" and not approved_state:
        validation.error("TL-0005 cannot be done while privacy-owner approval is Pending")
    if approved_state:
        if privacy_task_status not in {"review", "done"}:
            validation.error(
                "Approved privacy artifacts require TL-0005 to be in review or done"
            )
        approval_commit = re.search(
            r"\b[0-9a-fA-F]{40}\b",
            approval_metadata.get("Reviewed source commit", ""),
        )
        evidence = privacy_task.get("evidence")
        approval_evidence = False
        evidence_owner_parts = re.split(
            r"\s+—\s+",
            approval_metadata.get("Approving owner and role", ""),
            maxsplit=1,
        )
        owner_name = (
            evidence_owner_parts[0].strip()
            if len(evidence_owner_parts) == 2
            else ""
        )
        approval_verb = (
            "approved with conditions"
            if approval_state == "Approved with conditions"
            else "approved"
        )
        expected_summary = (
            f"{owner_name} — Privacy owner {approval_verb} the TL-0005 "
            "classifications and default retention guidance."
        )
        expected_approval_reference = approval_metadata.get(
            "Approval reference", ""
        ).casefold()
        if approval_commit is not None and isinstance(evidence, list):
            reviewed_commit = approval_commit.group(0).casefold()
            approval_evidence = any(
                isinstance(entry, dict)
                and str(entry.get("summary", "")).strip() == expected_summary
                and str(entry.get("result", "")).casefold() == "passed"
                and reviewed_commit in str(entry.get("reference", "")).casefold()
                and expected_approval_reference
                and expected_approval_reference
                in str(entry.get("reference", "")).casefold()
                and str(entry.get("date", ""))
                == approval_metadata.get("Approval date", "")
                and str(entry.get("environment", "")).strip()
                == f"Human privacy-owner review by {owner_name}"
                for entry in evidence
            )
        if not approval_evidence:
            validation.error(
                "TL-0005 evidence must record the exact named privacy-owner approval, date, reviewed commit, and approval reference"
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
    validate_privacy_documents(validation, task_by_id, decision_set)
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
