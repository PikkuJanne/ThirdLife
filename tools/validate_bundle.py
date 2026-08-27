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
from typing import Any
from urllib.parse import unquote, urlsplit

import yaml


ROOT = Path(__file__).resolve().parents[1]
ADR_NUMBERING_AMENDMENT_PATH = (
    "docs/amendments/2026-08-22-adr-0009-reservation.md"
)
TL0401_WINGET_ADR_PATH = "docs/adr/0009-winget-backend.md"
TL0401_HUMAN_EVIDENCE = (
    "A maintainer reviews the spike evidence and approves ADR 0009 at "
    "docs/adr/0009-winget-backend.md before production adapter work begins."
)
M0_FOUNDATION_GATE_PATH = "artifacts/gates/M0-foundation.md"
M0_SANDBOX_HOST_PATH = "eng/run-tl0010-sandbox.ps1"
M0_SANDBOX_GUEST_PATH = "eng/run-tl0010-sandbox-guest.ps1"
M0_SANDBOX_CANDIDATE = "17975419badd4154b82895d9d92a4a904790c7c0"
M0_SANDBOX_GATE_DIGEST = (
    "b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153"
)
M0_SANDBOX_RESULT_SCHEMA_VERSION = 2
M0_GATE_PREDECESSORS = tuple(f"TL-{number:04d}" for number in range(1, 10))
M0_TASK_STATUS_TO_RECORD_STATE = {
    "backlog": "Candidate",
    "ready": "Candidate",
    "in_progress": "Candidate",
    "blocked": "Blocked",
    "review": "Review",
    "done": "Approved",
}
M0_RECORD_STATE_TO_DECISION = {
    "Candidate": "Pending",
    "Blocked": "Blocked",
    "Review": "Review",
    "Approved": "Approved",
}
M0_RESPONSIBILITY_INPUTS = (
    "Product contract and non-goals",
    "Threat and security model",
    "Privacy and logging model",
    "Accessibility baseline",
    "Modest-hardware design and limitations",
    "Dependencies, licence, and redistribution rights",
    "Reference-machine profile, test tiers, constraints, and manual tests",
    "Initial ADR inputs",
    "M0 gate decision",
)
M0_REQUIRED_FINAL_CHECKS = (
    "Focused M0 gate-record regressions",
    "Working-tree governed Quick",
    "Exact-commit clean-clone Quick",
    "Exact-commit clean-clone Full",
    "Post-evidence bundle and repository validation",
)
M0_FINAL_APPROVAL_FIELDS = {
    "Project-owner decision": "Signed",
    "Security-owner M0 acknowledgement": "Acknowledged",
    "Privacy-owner M0 acknowledgement": "Acknowledged",
    "Dependency/licence-owner M0 acknowledgement": "Acknowledged",
}
ARCHITECTURE_DECISION_PATHS = (
    "docs/adr/0001-windows-wpf-stack.md",
    "docs/adr/0002-evidence-policy-separation.md",
    "docs/adr/0003-sqlite-job-store.md",
    "docs/adr/0004-ephemeral-broker.md",
    "docs/adr/0005-package-adapter.md",
    "docs/adr/0006-report-privacy-classes.md",
    "docs/adr/0007-standalone-late-binding-boundary.md",
    "docs/adr/0008-minimal-release-interface-envelope.md",
)
REQUIRED_FILES = (
    "ROADMAP.md",
    "DECISIONS.md",
    "TASKS.yaml",
    "AGENTS.md",
    "CODEX_START_PROMPT.md",
    "CODEX_TL0008_TRANSITION_PROMPT.md",
    "README.md",
    "STATUS.md",
    "DEVELOPMENT_WORKFLOW.md",
    "TESTING.md",
    "TASKS.schema.json",
    "PROJECT_BOUNDARY.md",
    "SECURITY.md",
    "ACCESSIBILITY.md",
    "LOW_SPEC.md",
    "RELEASE_INTERFACE.md",
    "FUTURE_ASSEMBLY_NOTES.md",
    "TL-0008_TRANSITION.md",
    "CHANGELOG.md",
    "docs/change-control.md",
    ADR_NUMBERING_AMENDMENT_PATH,
    "docs/glossary.md",
    "docs/non-goals.md",
    "docs/product-contract.md",
    *ARCHITECTURE_DECISION_PATHS,
    "docs/history/TL-0008-draft-1-superseded.md",
    "docs/privacy/logging-standard.md",
    "docs/privacy/privacy-model.md",
    "docs/privacy/redaction-test-cases.yaml",
    "fixtures/README.md",
    "fixtures/catalog/catalog.yaml",
    "fixtures/jobs/assessment-ready.yaml",
    "fixtures/jobs/partial-observations.yaml",
    "fixtures/jobs/sanitization-blocked.yaml",
    "fixtures/policies/community-laptop-policy.yaml",
    "fixtures/profiles/basic.yaml",
    "fixtures/profiles/job-seeker.yaml",
    "docs/security/abuse-cases.md",
    "docs/security/data-flow.md",
    "docs/security/threat-model.md",
    "docs/supply-chain/dependencies.md",
    "docs/supply-chain/license-matrix.csv",
    "docs/testing/accessibility-matrix.md",
    "docs/testing/capability-risk-matrix.md",
    "docs/testing/failure-injection.md",
    "docs/testing/manual-hardware-tests.md",
    "docs/testing/reference-machine-profile.md",
    "docs/testing/same-machine-constraints.md",
    M0_SANDBOX_GUEST_PATH,
    M0_SANDBOX_HOST_PATH,
    M0_FOUNDATION_GATE_PATH,
    "tools/merge_task_contracts.py",
    "tools/requirements.txt",
    "tools/tests/test_pilot_fixtures.py",
    "tools/tests/test_validate_bundle.py",
)
BUNDLE_MANIFEST_FILE = "BUNDLE_MANIFEST.sha256"
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
    "DEVELOPMENT_WORKFLOW.md",
    "TESTING.md",
    "AGENTS.md",
    "TASKS.yaml",
    "STATUS.md",
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
    "TESTING.md",
    "DEVELOPMENT_WORKFLOW.md",
    "STATUS.md",
    "docs/testing/accessibility-matrix.md",
    "docs/testing/capability-risk-matrix.md",
    "docs/testing/failure-injection.md",
    "docs/testing/manual-hardware-tests.md",
    "docs/testing/reference-machine-profile.md",
    "docs/testing/same-machine-constraints.md",
    "docs/history/TL-0008-draft-1-superseded.md",
)
PRIVACY_DOCUMENTS = (
    "docs/privacy/privacy-model.md",
    "docs/privacy/logging-standard.md",
    "docs/privacy/redaction-test-cases.yaml",
)
ARCHITECTURE_DECISION_RECORDS = (
    (
        ARCHITECTURE_DECISION_PATHS[0],
        "# ADR 0001 — Windows and WPF stack",
        frozenset({"D-027", "D-058", "D-059", "D-061", "D-063"}),
    ),
    (
        ARCHITECTURE_DECISION_PATHS[1],
        "# ADR 0002 — Evidence, policy, and decision separation",
        frozenset({"D-018", "D-023", "D-032"}),
    ),
    (
        ARCHITECTURE_DECISION_PATHS[2],
        "# ADR 0003 — SQLite job store and bounded attachments",
        frozenset({"D-018", "D-028", "D-032", "D-053"}),
    ),
    (
        ARCHITECTURE_DECISION_PATHS[3],
        "# ADR 0004 — Ephemeral elevated broker",
        frozenset({"D-023", "D-029", "D-030", "D-031", "D-032"}),
    ),
    (
        ARCHITECTURE_DECISION_PATHS[4],
        "# ADR 0005 — Replaceable structured package adapter",
        frozenset(
            {"D-023", "D-024", "D-025", "D-026", "D-032", "D-043", "D-055"}
        ),
    ),
    (
        ARCHITECTURE_DECISION_PATHS[5],
        "# ADR 0006 — Separate report privacy classes",
        frozenset({"D-011", "D-013", "D-014", "D-036", "D-037", "D-053"}),
    ),
    (
        ARCHITECTURE_DECISION_PATHS[6],
        "# ADR 0007 — Standalone product and late-binding boundary",
        frozenset(
            {
                "D-046",
                "D-047",
                "D-048",
                "D-049",
                "D-050",
                "D-051",
                "D-053",
                "D-054",
                "D-055",
            }
        ),
    ),
    (
        ARCHITECTURE_DECISION_PATHS[7],
        "# ADR 0008 — Minimal release-interface envelope",
        frozenset(
            {
                "D-047",
                "D-048",
                "D-049",
                "D-050",
                "D-052",
                "D-053",
                "D-058",
                "D-059",
                "D-061",
                "D-063",
            }
        ),
    ),
)
ADR_REQUIRED_SECTIONS = (
    "Status and authority",
    "Decision IDs",
    "Context",
    "Decision",
    "Alternatives considered",
    "Consequences",
    "References",
)
ADR_CONTRACT_PHRASES = {
    ARCHITECTURE_DECISION_PATHS[0]: (
        "The dependency direction is inward",
        "`ThirdLife.Core` is the innermost domain boundary",
        "`ThirdLife.UI` is the only WPF production assembly",
        "no assembly is added merely as a future extension point or portfolio integration layer",
    ),
    ARCHITECTURE_DECISION_PATHS[1]: (
        "An evidence record is immutable and attributable",
        "A policy evaluation creates a new reproducible decision record",
        "`Applied` is not completion",
    ),
    ARCHITECTURE_DECISION_PATHS[2]: (
        "uses SQLite for structured local job state",
        "Raw provider, backend, installer, command, and exception input has zero persistent retention by default",
        "The elevated broker and its package/system backend have no database",
    ),
    ARCHITECTURE_DECISION_PATHS[3]: (
        "ephemeral elevated `ThirdLife.Broker` process",
        "broker independently validates the initiating caller's user/session",
        "no arbitrary or free-form PowerShell/shell command",
        "No permanent LocalSystem service",
    ),
    ARCHITECTURE_DECISION_PATHS[4]: (
        "replaceable structured package-backend seam",
        "portable `ThirdLife.Actions` does not reference the Windows package implementation",
        "Backend selection remains open until `TL-0401`",
        "Profiles select reviewed generic capabilities",
        "grants no blanket redistribution right",
    ),
    ARCHITECTURE_DECISION_PATHS[5]: (
        "Technical workshop record (`WORKSHOP_RESTRICTED`)",
        "Plain-language recipient guide (`RECIPIENT_GUIDE`)",
        "Sanitized diagnostic bundle (`SUPPORT_SANITIZED`)",
        "never created by copying and redacting the already-rendered workshop record",
        "there is no telemetry, automatic upload, or background sender",
    ),
    ARCHITECTURE_DECISION_PATHS[6]: (
        "B1 is developed and released in a project vacuum",
        "creates no shared SDK",
        "The note creates no B1 code, dependency, acceptance criterion",
        "Any adapter remains optional, version-bounded, independently disableable",
    ),
    ARCHITECTURE_DECISION_PATHS[7]: (
        "human-readable [`RELEASE_INTERFACE.md`]",
        "only implemented and verified standalone facts",
        "TL-0009 does not populate speculative values",
        "documentation, not an API, SDK, plugin contract",
    ),
}
REDACTION_FIXTURE_SCHEMA_VERSION = "thirdlife.redaction-fixtures.v1"
REDACTION_CASE_ID_RE = re.compile(r"^RDX-[0-9]{3}$")
REDACTION_CASE_IDS = tuple(f"RDX-{index:03d}" for index in range(1, 57))
REDACTION_CLASSIFICATIONS = (
    "direct_personal_identifier",
    "account_identifier",
    "network_identifier",
    "device_identifier",
    "personal_path",
    "sensitive_url",
    "secret",
    "personal_content",
    "raw_untrusted_output",
    "sibling_private_data",
    "pseudonymous_operational_identifier",
    "operational_metadata",
    "unknown_field",
)
REDACTION_CONTEXTS = (
    "ordinary_log",
    "crash_report",
    "workshop_record",
    "support_export",
    "command_ingest",
    "provider_ingest",
    "installer_ingest",
    "external_private_input",
    "telemetry",
)
REDACTION_ACTIONS = (
    "redact",
    "omit",
    "reject_and_do_not_persist",
    "preserve_workshop_only",
    "reject_raw_and_extract_allowlisted_fields",
    "reject_out_of_scope",
    "preserve_allowlisted",
    "suppress_telemetry",
)
REDACTION_PERSISTENCE_VALUES = (
    "redacted_value_only",
    "none",
    "workshop_record_only",
    "none_in_support_export",
    "structured_projection_only",
    "structured_value_only",
    "none_for_telemetry",
)
SUPPORT_EXPORT_OUTCOMES = (
    "omit",
    "omit_by_default_truncation_requires_explicit_review",
    "omit_raw_allow_structured_projection_only",
    "include_unchanged_if_allowlisted_and_previewed",
)
SUPPORT_EXPORT_TABLE_ROWS = (
    ("schema_version", "Exact reviewed support schema version."),
    ("manifest_version", "Exact reviewed manifest version."),
    (
        "internal_support_id",
        "Fresh opaque random export ID with no encoded job/device/user value.",
    ),
    ("application_version", "Installed ThirdLife Setup Core version."),
    ("build_version", "Reviewed source/build revision identifier."),
    ("os_version", "Normalized OS version/build, not a product/device ID."),
    ("hardware_architecture", "Fixed architecture enum."),
    (
        "memory_bucket",
        "Reviewed coarse capacity bucket, not module identifiers.",
    ),
    (
        "storage_class",
        "Reviewed generic storage class, not disk identity or path.",
    ),
    ("event_time_utc", "Offset-aware UTC time only for an included event."),
    ("export_created_at_utc", "Exact preview/export creation time."),
    ("check_id", "Registered check ID."),
    ("check_outcome", "Registered bounded outcome enum."),
    ("action_code", "Registered compiled action code, not a command or argument."),
    ("result_code", "Registered stable result code."),
    ("component_id", "Registered stable component ID."),
    ("operation_type", "Registered bounded operation enum."),
    ("evidence_state", "Registered evidence/availability state."),
    (
        "sanitized_error_category",
        "Registered category; no backend/exception prose.",
    ),
    ("retryable", "Boolean determined by reviewed recovery policy."),
    (
        "duration_ms",
        "Bounded non-negative duration for the included operation.",
    ),
    (
        "bounded_count",
        "Bounded non-negative count whose meaning is fixed by the record schema.",
    ),
    ("limitation_code", "Registered stable limitation code."),
    (
        "preview_content_digest_sha256",
        "Digest of exact preview bytes; never a hash of omitted personal data.",
    ),
    (
        "export_content_digest_sha256",
        "Digest of exact exported bytes for preview/export equality.",
    ),
)
SUPPORT_EXPORT_ALLOWLIST = tuple(
    field for field, _ in SUPPORT_EXPORT_TABLE_ROWS
)
PRIVACY_RESERVED_SYNTHETIC_RAW_OUTPUTS = (
    (
        "raw_command_output",
        "SyntheticPerson 192.0.2.44 SYNTHETIC-NOT-A-REAL-TOKEN",
    ),
    (
        "raw_provider_output",
        r"user=synthetic_user path=C:\Users\SyntheticPerson\synthetic.txt",
    ),
    (
        "raw_installer_output",
        "download=https://packages.example.test/synthetic/app.msix result=0",
    ),
)
REDACTION_REQUIRED_APPROVAL_SCOPE = (
    "field and context classifications",
    "default retention guidance",
    "redaction and omission actions",
    "support-export allowlist",
)
SYNTHETIC_SUPPORT_DIGEST_BYTES = b"thirdlife-synthetic-preview-export-v1\n"
SYNTHETIC_SUPPORT_CONTENT_SHA256 = hashlib.sha256(
    SYNTHETIC_SUPPORT_DIGEST_BYTES
).hexdigest()
PRIVACY_FIXTURE_MAX_BYTES = 256 * 1024
PRIVACY_FIXTURE_MAX_NODES = 8_192
PRIVACY_FIXTURE_MAX_DEPTH = 32
PRIVACY_FIXTURE_MAX_SCALAR_CHARS = 16_384
PRIVACY_FIXTURE_MAX_AGGREGATE_SCALAR_CHARS = 128 * 1024
PILOT_FIXTURE_FILES = (
    "fixtures/catalog/catalog.yaml",
    "fixtures/jobs/assessment-ready.yaml",
    "fixtures/jobs/partial-observations.yaml",
    "fixtures/jobs/sanitization-blocked.yaml",
    "fixtures/policies/community-laptop-policy.yaml",
    "fixtures/profiles/basic.yaml",
    "fixtures/profiles/job-seeker.yaml",
)
PILOT_FIXTURE_README = "fixtures/README.md"
PILOT_FIXTURE_SCHEMA_VERSIONS = {
    "catalog": "thirdlife.catalog.v1",
    "job": "thirdlife.job-fixture.v1",
    "policy": "thirdlife.policy.v1",
    "profile": "thirdlife.profile.v1",
}
PILOT_FIXTURE_CLASSIFICATION = "PUBLIC_REFERENCE"
PILOT_FIXTURE_MAX_OBSERVATIONS = 64
PILOT_FIXTURE_MAX_POLICY_RULES = 64
PILOT_FIXTURE_MAX_CATALOG_APPLICATIONS = 32
PILOT_FIXTURE_MAX_PROFILE_ITEMS = 32
PILOT_FIXTURE_ID_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$")
PILOT_SYNTHETIC_JOB_ID_RE = re.compile(r"^SYNTHETIC-JOB-[A-Z0-9-]+$")
PILOT_SYNTHETIC_OPERATOR_ID_RE = re.compile(r"^SYNTHETIC-OPERATOR-[0-9]{3}$")
PILOT_SYNTHETIC_MEDIA_ID_RE = re.compile(
    r"^SYNTHETIC-(?:MEDIA|REPLACEMENT-STORAGE)-[0-9]{3}$"
)
PILOT_CATALOG_APPLICATIONS = (
    (
        "web_browsing",
        "generic.synthetic.web-browser",
        "generic.synthetic.package.web-browser",
    ),
    (
        "document_editing",
        "generic.synthetic.document-editor",
        "generic.synthetic.package.document-editor",
    ),
    (
        "pdf_reading",
        "generic.synthetic.pdf-reader",
        "generic.synthetic.package.pdf-reader",
    ),
    (
        "video_calling",
        "generic.synthetic.video-calling",
        "generic.synthetic.package.video-calling",
    ),
)
PILOT_CATALOG_CAPABILITIES = tuple(
    capability for capability, _application, _package in PILOT_CATALOG_APPLICATIONS
)
PILOT_RECIPIENT_CHOICES = (
    "browser_preference",
    "accessibility_preferences",
    "cloud_account_sign_in",
    "backup_onboarding",
)
PILOT_PROHIBITED_NORMALIZED_KEYS = {
    "arguments",
    "args",
    "assettag",
    "command",
    "credential",
    "credentials",
    "dataurl",
    "donorname",
    "email",
    "emailaddress",
    "executable",
    "executablepath",
    "hostname",
    "installerarguments",
    "installerargs",
    "messagedetails",
    "operatorname",
    "password",
    "passphrase",
    "personalpath",
    "privatekey",
    "rawmessage",
    "rawoutput",
    "recipientname",
    "recoverykey",
    "registrypath",
    "script",
    "serial",
    "serialnumber",
    "servicetag",
    "ssid",
    "token",
    "uri",
    "url",
    "username",
}
PRIVACY_PENDING_STATUS = "Draft contract complete; named privacy-owner approval pending"
PRIVACY_APPROVED_STATUS = "Approved initial privacy contract"
PRIVACY_APPROVAL_PATHS = (
    "docs/privacy/privacy-model.md",
    "docs/privacy/logging-standard.md",
    "docs/privacy/redaction-test-cases.yaml",
)
PRIVACY_RETENTION_ROWS = (
    (
        "Active or interrupted job and workshop evidence",
        "Retain while the job is active; then 180 days after handover, do-not-deploy closure, or explicit abandonment",
        "Unresolved recovery/blocker state pauses ordinary deletion only under a named review record",
        "Warn before expiry, preserve attributable history until deletion, and make deletion distinct from reversible archive",
    ),
    (
        "Job-bound policy/profile/catalogue snapshots and rendered workshop/recipient copies",
        "Same as the owning job",
        "Delete with the explicit job-data deletion operation",
        "Do not leave orphaned files; exported copies are not silently deleted",
    ),
    (
        "Sanitized operational logs",
        "14 days",
        "Rotate by age and by a separately measured, configured byte ceiling; whichever occurs first",
        "Delete whole records/files safely; report cleanup failure only with a stable sanitized code",
    ),
    (
        "Raw provider/backend/installer/command/exception content",
        "Process lifetime only; zero persistent retention by default",
        "Release immediately after bounded normalization or failure",
        "No database/log/support fallback; an explicitly contracted raw attachment becomes workshop-restricted and follows job retention",
    ),
    (
        "Temporary, preview, and staging files",
        "Operation lifetime; stale owned files no longer than 24 hours",
        "Remove on success, cancellation, and failure; sweep only verified owned stale files at next startup",
        "Never follow links/reparse points or delete outside the registered internal root; preserve truthful cleanup failure",
    ),
    (
        "Support preview and application-owned staged bundle",
        "Preview session only",
        "Remove after export/cancel/failure and on the bounded stale-file sweep",
        "Keep only export audit metadata, not a duplicate archive",
    ),
    (
        "User-exported support bundle",
        "Outside product control; recommend deletion when the support case closes and no later than 30 days absent a documented need",
        "Operator/support recipient owns deletion",
        "Show handling guidance before export; ThirdLife cannot claim deletion of an external copy",
    ),
    (
        "User-exported workshop record or recipient guide",
        "Outside product control",
        "Workshop/recipient policy owns deletion",
        "Explain the audience and sensitivity at export; do not record a personal destination path in ordinary logs",
    ),
    (
        "Support export audit metadata",
        "Same as the owning job",
        "Delete with explicit job-data deletion",
        "Retain only support ID, schema version, content digest, export time, and protected operator attribution",
    ),
    (
        "Migration/recovery copy",
        "Until migration/recovery is verified, then 7 days",
        "Start only for a named operation; a failure retains the original and records a bounded review state",
        "Later persistence work must prove safe cleanup, access denial handling, and no orphan/partial copy before enabling the default",
    ),
    (
        "Unreferenced superseded configuration",
        "90 days after supersession",
        "A version referenced by a retained job follows that job instead",
        "Never rewrite the snapshot of a historical job",
    ),
    (
        "Package/update cache",
        "30 days after last verified use by default; keep longer only while an active/recoverable job explicitly references the exact artifact",
        "Evict by age and by a separately measured byte ceiling after checking active plan, resume, provenance, and recovery references",
        "Evict only cache-owned artifacts; never retain raw provider/output text with the artifact, and let later package work tighten this pending default when supply-chain or rollback evidence requires it",
    ),
    (
        "Secrets, recovery material, personal content, telemetry, and sibling-private data",
        "Zero",
        "Never collect or persist",
        "A discovered attempted value is rejected/redacted; do not retain it for debugging",
    ),
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
ALLOWED_TEST_TIER = {"quick", "targeted", "full", "extended"}
MUTABLE_FIELDS = ["status", "evidence", "blocked_reason"]

CURRENT_BUNDLE_VERSION = "0.3.1"
CURRENT_BUNDLE_GENERATED_ON = "2026-08-22"
CURRENT_DECISION_IDS = tuple(f"D-{index:03d}" for index in range(1, 67))
CURRENT_TASK_COUNT = 91
V030_TL0008_SOURCE_COMMIT = "4fa3ea050fd5e9985fde9cc8218281698d371cc8"
V030_TL0008_PROCEDURE_DIGEST = (
    "ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b"
)
CURRENT_BUNDLE_DOCUMENT_MARKERS = {
    "ACCESSIBILITY.md": f"**Bundle version:** {CURRENT_BUNDLE_VERSION}",
    "AGENTS.md": f"**Bundle version:** {CURRENT_BUNDLE_VERSION}",
    "CHANGELOG.md": "## 0.3.1 — 22 August 2026",
    "CODEX_START_PROMPT.md": f"**Bundle version:** {CURRENT_BUNDLE_VERSION}",
    "DECISIONS.md": f"**Bundle version:** {CURRENT_BUNDLE_VERSION}",
    "DEVELOPMENT_WORKFLOW.md": f"**Bundle version:** {CURRENT_BUNDLE_VERSION}",
    "FUTURE_ASSEMBLY_NOTES.md": f"**Bundle version:** {CURRENT_BUNDLE_VERSION}",
    "LOW_SPEC.md": f"**Bundle version:** {CURRENT_BUNDLE_VERSION}",
    "PROJECT_BOUNDARY.md": f"**Bundle version:** {CURRENT_BUNDLE_VERSION}",
    "README.md": (
        f"**Roadmap bundle:** {CURRENT_BUNDLE_VERSION} / "
        "ThirdLife Software Portfolio v2.1"
    ),
    "RELEASE_INTERFACE.md": f"**Bundle version:** {CURRENT_BUNDLE_VERSION}",
    "ROADMAP.md": f"**Bundle version:** {CURRENT_BUNDLE_VERSION}",
    "SECURITY.md": f"**Bundle version:** {CURRENT_BUNDLE_VERSION}",
    "STATUS.md": f"**Bundle baseline:** {CURRENT_BUNDLE_VERSION}",
    "TESTING.md": f"**Bundle version:** {CURRENT_BUNDLE_VERSION}",
}
CURRENT_BUNDLE_METADATA_LABELS = {
    **{
        relative: "Bundle version"
        for relative in CURRENT_BUNDLE_DOCUMENT_MARKERS
        if relative
        not in {
            "CHANGELOG.md",
            "README.md",
            "STATUS.md",
        }
    },
    "README.md": "Roadmap bundle",
    "STATUS.md": "Bundle baseline",
}

# Exact fragments from the superseded draft that are never valid as live
# instructions. Historical and explicit rejection text remains permitted.
OBSOLETE_ACTIVE_PHRASES = (
    "assign it a sanitized id such as `lab-device-001`",
    "you only need to complete the physical device pool",
    "select a safe reference device",
    "gather approved equipment",
    "send me the results",
    "create explicit pilot blockers for missing classes",
    "record the actual device pool",
    "approved physical lab device",
    "required physical reference device",
    "physical only | required human-confirmed reference",
    "physical-device matrix results",
    "review provider results on the physical device matrix",
)


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
    """SafeLoader variant that rejects ambiguous duplicate mapping keys."""


def construct_unique_yaml_mapping(
    loader: UniqueKeySafeLoader,
    node: Any,
    deep: bool = False,
) -> dict[Any, Any]:
    if any(
        key_node.tag == "tag:yaml.org,2002:merge"
        for key_node, _ in node.value
    ):
        raise yaml.constructor.ConstructorError(
            "while constructing a mapping",
            node.start_mark,
            "YAML merge keys are prohibited",
            node.start_mark,
        )
    loader.flatten_mapping(node)
    mapping: dict[Any, Any] = {}
    for key_node, value_node in node.value:
        key = loader.construct_object(key_node, deep=deep)
        if not isinstance(key, str):
            raise yaml.constructor.ConstructorError(
                "while constructing a mapping",
                node.start_mark,
                "mapping keys must be strings",
                key_node.start_mark,
            )
        try:
            duplicate = key in mapping
        except TypeError as exc:
            raise yaml.constructor.ConstructorError(
                "while constructing a mapping",
                node.start_mark,
                f"unhashable mapping key: {exc}",
                key_node.start_mark,
            ) from exc
        if duplicate:
            raise yaml.constructor.ConstructorError(
                "while constructing a mapping",
                node.start_mark,
                "duplicate mapping key",
                key_node.start_mark,
            )
        mapping[key] = loader.construct_object(value_node, deep=deep)
    return mapping


UniqueKeySafeLoader.add_constructor(
    yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG,
    construct_unique_yaml_mapping,
)


def load_unique_key_yaml(path: Path, validation: Validation) -> dict[str, Any]:
    """Load a bounded fixture while rejecting aliases and key ambiguity."""

    try:
        with path.open("rb") as stream:
            raw = stream.read(PRIVACY_FIXTURE_MAX_BYTES + 1)
    except OSError as exc:
        validation.error(f"{path.name}: cannot read YAML: {exc}")
        return {}
    if len(raw) > PRIVACY_FIXTURE_MAX_BYTES:
        validation.error(
            f"{path.name}: YAML exceeds {PRIVACY_FIXTURE_MAX_BYTES} byte limit"
        )
        return {}
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError as exc:
        validation.error(f"{path.name}: YAML is not UTF-8: {exc}")
        return {}

    collection_starts = (
        yaml.tokens.BlockMappingStartToken,
        yaml.tokens.BlockSequenceStartToken,
        yaml.tokens.FlowMappingStartToken,
        yaml.tokens.FlowSequenceStartToken,
    )
    collection_ends = (
        yaml.tokens.BlockEndToken,
        yaml.tokens.FlowMappingEndToken,
        yaml.tokens.FlowSequenceEndToken,
    )
    depth = 0
    node_count = 0
    aggregate_scalar_chars = 0
    try:
        for token in yaml.scan(text):
            if isinstance(token, (yaml.tokens.AnchorToken, yaml.tokens.AliasToken)):
                validation.error(
                    f"{path.name}: YAML anchors and aliases are prohibited"
                )
                return {}
            if isinstance(token, collection_starts):
                depth += 1
                node_count += 1
                if depth > PRIVACY_FIXTURE_MAX_DEPTH:
                    validation.error(
                        f"{path.name}: YAML nesting exceeds {PRIVACY_FIXTURE_MAX_DEPTH} levels"
                    )
                    return {}
            elif isinstance(token, collection_ends):
                depth = max(0, depth - 1)
            elif isinstance(token, yaml.tokens.ScalarToken):
                node_count += 1
                scalar_chars = len(str(token.value))
                aggregate_scalar_chars += scalar_chars
                if scalar_chars > PRIVACY_FIXTURE_MAX_SCALAR_CHARS:
                    validation.error(
                        f"{path.name}: YAML scalar exceeds "
                        f"{PRIVACY_FIXTURE_MAX_SCALAR_CHARS} character limit"
                    )
                    return {}
                if (
                    aggregate_scalar_chars
                    > PRIVACY_FIXTURE_MAX_AGGREGATE_SCALAR_CHARS
                ):
                    validation.error(
                        f"{path.name}: YAML aggregate scalar content exceeds "
                        f"{PRIVACY_FIXTURE_MAX_AGGREGATE_SCALAR_CHARS} characters"
                    )
                    return {}
            if node_count > PRIVACY_FIXTURE_MAX_NODES:
                validation.error(
                    f"{path.name}: YAML exceeds {PRIVACY_FIXTURE_MAX_NODES} node limit"
                )
                return {}
    except Exception:
        validation.error(f"{path.name}: cannot scan YAML safely")
        return {}

    try:
        value = yaml.load(
            text,
            Loader=UniqueKeySafeLoader,
        )
    except Exception as exc:  # diagnostic boundary
        problem = str(getattr(exc, "problem", ""))
        safe_problems = {
            "duplicate mapping key",
            "mapping keys must be strings",
            "YAML merge keys are prohibited",
        }
        safe_problem = problem if problem in safe_problems else "invalid YAML structure"
        validation.error(f"{path.name}: cannot parse YAML: {safe_problem}")
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


def markdown_table_after_heading(
    text: str,
    heading: str,
    relative: str,
    validation: Validation,
) -> tuple[tuple[str, ...], tuple[tuple[str, ...], ...]] | None:
    """Parse one simple governed Markdown table below an exact heading."""

    heading_match = re.fullmatch(r"(#{1,6})\s+(.+)", heading)
    if heading_match is None:
        raise ValueError(f"invalid governed heading {heading!r}")
    level = len(heading_match.group(1))
    matches = list(
        re.finditer(rf"^{re.escape(heading)}\s*$", text, re.MULTILINE)
    )
    if len(matches) != 1:
        validation.error(
            f"{relative}: expected exactly one heading {heading!r}"
        )
        return None
    start = matches[0].end()
    next_heading = re.search(
        rf"^#{{1,{level}}}\s+",
        text[start:],
        re.MULTILINE,
    )
    end = start + next_heading.start() if next_heading is not None else len(text)
    section_lines = text[start:end].splitlines()

    def cells(line: str) -> tuple[str, ...] | None:
        stripped = line.strip()
        if not stripped.startswith("|") or not stripped.endswith("|"):
            return None
        values: list[str] = []
        current: list[str] = []
        escaped = False
        for character in stripped[1:-1]:
            if escaped:
                current.append(character)
                escaped = False
            elif character == "\\":
                escaped = True
            elif character == "|":
                values.append(re.sub(r"\s+", " ", "".join(current).strip()))
                current = []
            else:
                current.append(character)
        if escaped:
            current.append("\\")
        values.append(re.sub(r"\s+", " ", "".join(current).strip()))
        return tuple(values)

    table_start = next(
        (index for index, line in enumerate(section_lines) if cells(line) is not None),
        None,
    )
    if table_start is None or table_start + 1 >= len(section_lines):
        validation.error(f"{relative}: missing table below {heading!r}")
        return None
    header = cells(section_lines[table_start])
    separator = cells(section_lines[table_start + 1])
    if header is None or separator is None or len(header) != len(separator):
        validation.error(f"{relative}: malformed table below {heading!r}")
        return None
    if any(re.fullmatch(r":?-{3,}:?", cell) is None for cell in separator):
        validation.error(f"{relative}: malformed table separator below {heading!r}")
        return None
    rows: list[tuple[str, ...]] = []
    for line in section_lines[table_start + 2 :]:
        row = cells(line)
        if row is None:
            break
        if len(row) != len(header):
            validation.error(
                f"{relative}: table row below {heading!r} has {len(row)} cells; expected {len(header)}"
            )
            continue
        rows.append(row)
    return header, tuple(rows)


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


def markdown_visible_text(text: str) -> str:
    visible_lines: list[str] = []
    fence_character: str | None = None
    fence_length = 0
    inside_comment = False
    for line in text.splitlines(keepends=True):
        line_without_ending = line.rstrip("\r\n")
        if fence_character is not None:
            closing_fence = re.match(
                rf"^ {{0,3}}{re.escape(fence_character)}{{{fence_length},}}[ \t]*$",
                line_without_ending,
            )
            if closing_fence is not None:
                fence_character = None
                fence_length = 0
            continue

        if not inside_comment:
            opening_fence = re.match(
                r"^ {0,3}(`{3,}|~{3,})(.*)$",
                line_without_ending,
            )
            if opening_fence is not None:
                marker = opening_fence.group(1)
                information = opening_fence.group(2)
                if marker[0] != "`" or "`" not in information:
                    fence_character = marker[0]
                    fence_length = len(marker)
                    continue

        visible_line_parts: list[str] = []
        position = 0
        while position < len(line):
            if inside_comment:
                comment_end = line.find("-->", position)
                if comment_end == -1:
                    position = len(line)
                    break
                inside_comment = False
                position = comment_end + 3
                continue

            comment_start = line.find("<!--", position)
            if comment_start == -1:
                visible_line_parts.append(line[position:])
                break
            visible_line_parts.append(line[position:comment_start])
            inside_comment = True
            position = comment_start + 4

        visible_lines.extend(visible_line_parts)
    return "".join(visible_lines)


def exact_inline_markdown_link_count(text: str, target: str) -> int:
    visible_text = markdown_visible_text(text)
    return len(
        re.findall(
            rf"(?<!!)\[[^\]\r\n]+\]\({re.escape(target)}\)",
            visible_text,
        )
    )


def adr_path_numbers(text: str) -> list[int]:
    normalized_text = unicodedata.normalize("NFKC", text)
    normalized_text = "".join(
        character
        for character in normalized_text
        if unicodedata.category(character) != "Cf"
    ).replace("\\", "/")
    normalized_text = "".join(
        "-" if unicodedata.category(character) == "Pd" else character
        for character in normalized_text
    )
    return [
        int(match.group(1))
        for match in re.finditer(
            r"(?i)(?<![A-Z0-9])(?:\./)?docs/adr/"
            r"(?:ADR[-_\s]*)?(\d{3,4})-[^/\s#?]+\.md"
            r"(?=$|[\s#?.,;:)\]}`'\"])",
            normalized_text,
        )
    ]


def validate_governed_adr_markdown(
    relative: str,
    text: str,
    validation: Validation,
) -> None:
    if "<!--" in text or "-->" in text:
        validation.error(f"{relative}: HTML comments are not permitted in governed ADRs")
    if re.search(r"(?m)^ {0,3}(?:`{3,}|~{3,})", text):
        validation.error(f"{relative}: fenced code blocks are not permitted in governed ADRs")
    if re.search(r"(?m)^(?: {4}| {0,3}\t)", text):
        validation.error(
            f"{relative}: indented Markdown lines are not permitted in governed ADRs"
        )
    if re.search(r"!\[[^\]\r\n]*\]\([^\r\n]*\)", text):
        validation.error(f"{relative}: Markdown images are not permitted in governed ADRs")
    if re.search(r"(?i)</?[a-z][^>]*>", text):
        validation.error(f"{relative}: raw HTML is not permitted in governed ADRs")


def markdown_heading_fragments(text: str) -> set[str]:
    counts: dict[str, int] = defaultdict(int)
    fragments: set[str] = set()
    for match in re.finditer(
        r"^#{1,6}\s+(.+?)\s*#*\s*$",
        markdown_visible_text(text),
        re.MULTILINE,
    ):
        heading = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", match.group(1))
        heading = re.sub(r"[`*_~]", "", heading).strip().casefold()
        base = re.sub(r"[^\w\- ]", "", heading)
        base = re.sub(r"\s", "-", base)
        if not base:
            continue
        duplicate_index = counts[base]
        counts[base] += 1
        fragments.add(base if duplicate_index == 0 else f"{base}-{duplicate_index}")
    return fragments


def validate_local_markdown_links(
    relative: str,
    text: str,
    validation: Validation,
) -> None:
    owner_path = ROOT / relative
    root = ROOT.resolve()
    visible_text = markdown_visible_text(text)
    if re.search(r"(?m)^\s*\[[^\]\r\n]+\]:\s*\S+", visible_text):
        validation.error(
            f"{relative}: ADR references must use inline Markdown links"
        )
    for match in re.finditer(
        r"(?<!!)\[[^\]\r\n]+\]\(([^)\r\n]+)\)",
        visible_text,
    ):
        target = match.group(1).strip()
        if target.startswith("<") and target.endswith(">"):
            target = target[1:-1].strip()
        else:
            target = target.split(maxsplit=1)[0]
        parsed = urlsplit(target)
        if parsed.scheme:
            if parsed.scheme.casefold() not in {"http", "https", "mailto"}:
                validation.error(
                    f"{relative}: Markdown link uses unsupported scheme {parsed.scheme!r}"
                )
            continue
        if parsed.netloc:
            validation.error(f"{relative}: Markdown link has an unsafe network path")
            continue
        if not parsed.path:
            if not parsed.fragment:
                continue
            destination = owner_path.resolve()
        else:
            destination = (owner_path.parent / Path(unquote(parsed.path))).resolve()
        try:
            destination.relative_to(root)
        except ValueError:
            validation.error(
                f"{relative}: Markdown link leaves the repository: {target!r}"
            )
            continue
        if not destination.is_file():
            validation.error(
                f"{relative}: Markdown link target does not exist: {target!r}"
            )
            continue
        if parsed.fragment:
            try:
                destination_text = destination.read_text(encoding="utf-8")
            except (OSError, UnicodeError) as exc:
                validation.error(
                    f"{relative}: cannot validate Markdown fragment in {target!r}: {exc}"
                )
                continue
            fragment = unquote(parsed.fragment).casefold()
            if fragment not in markdown_heading_fragments(destination_text):
                validation.error(
                    f"{relative}: Markdown fragment does not exist: {target!r}"
                )


def validate_architecture_decision_records(
    validation: Validation,
    task_by_id: dict[str, dict[str, Any]],
    decision_set: set[str],
) -> None:
    task = task_by_id.get("TL-0009")
    if task is None:
        validation.error("TASKS.yaml: missing TL-0009")
        return

    expected_paths = list(ARCHITECTURE_DECISION_PATHS)
    record_paths = [relative for relative, _, _ in ARCHITECTURE_DECISION_RECORDS]
    if record_paths != expected_paths:
        validation.error("ADR validator path registry is internally inconsistent")
    if task.get("deliverables") != expected_paths:
        validation.error(
            "TL-0009: deliverables must exactly match the governed initial ADR set"
        )

    try:
        readme_text = (ROOT / "README.md").read_text(encoding="utf-8")
    except OSError as exc:
        validation.error(f"README.md: cannot read for ADR navigation: {exc}")
        readme_text = ""
    readme_visible = markdown_visible_text(readme_text)

    all_cited_decisions: set[str] = set()
    for relative, expected_title, required_decisions in ARCHITECTURE_DECISION_RECORDS:
        required_phrases = (
            "records existing binding decisions as planned architecture constraints",
            "does not amend",
            "not evidence that the planned behavior is implemented or verified",
            *ADR_CONTRACT_PHRASES[relative],
        )
        text = require_phrases(relative, (), validation)
        validate_governed_adr_markdown(relative, text, validation)
        visible_text = markdown_visible_text(text)
        visible_folded = visible_text.casefold()
        for phrase in required_phrases:
            if phrase.casefold() not in visible_folded:
                validation.error(
                    f"{relative}: missing required contract phrase {phrase!r}"
                )
        if not text.startswith(expected_title + "\n"):
            validation.error(
                f"{relative}: first heading must equal {expected_title!r}"
            )

        sections = markdown_level_two_sections(visible_text)
        for heading in ADR_REQUIRED_SECTIONS:
            if not sections.get(heading.casefold(), "").strip():
                validation.error(
                    f"{relative}: missing or empty required section {heading!r}"
                )

        decision_body = sections.get("decision ids", "")
        cited_sequence = re.findall(
            r"(?m)^-\s+\[(D-\d{3})\]\(\.\./\.\./DECISIONS\.md\)"
            r"\s+—\s+\S.*$",
            decision_body,
        )
        cited_decisions = set(cited_sequence)
        if len(cited_sequence) != len(cited_decisions):
            validation.error(f"{relative}: Decision IDs section has duplicate citations")
        all_cited_decisions.update(cited_decisions)
        missing_required = sorted(required_decisions - cited_decisions)
        if missing_required:
            validation.error(
                f"{relative}: Decision IDs section is missing {missing_required}"
            )

        for decision_id in sorted(set(re.findall(r"\bD-\d{3}\b", visible_text))):
            if decision_id not in decision_set:
                validation.error(
                    f"{relative}: references unknown decision {decision_id}"
                )
        task_references = set(re.findall(r"\bTL-\d{4}\b", visible_text))
        if "TL-0009" not in task_references:
            validation.error(f"{relative}: must reference owning task TL-0009")
        for task_id in sorted(task_references):
            if task_id not in task_by_id:
                validation.error(f"{relative}: references unknown task {task_id}")

        validate_local_markdown_links(relative, text, validation)
        navigation_matches = re.findall(
            rf"(?m)^-\s+\[[^\]\r\n]+\]\({re.escape(relative)}\)"
            r"\s+—\s+\S.*$",
            readme_visible,
        )
        if len(navigation_matches) != 1:
            validation.error(f"README.md: missing navigation link to {relative}")

    declared_decisions = task.get("decision_refs")
    if not isinstance(declared_decisions, list):
        declared_decisions = []
    missing_task_decisions = sorted(set(declared_decisions) - all_cited_decisions)
    if missing_task_decisions:
        validation.error(
            "TL-0009: ADR set does not cover declared decision_refs "
            f"{missing_task_decisions}"
        )


def validate_tl0401_adr_reservation(
    validation: Validation,
    task_by_id: dict[str, dict[str, Any]],
) -> None:
    task = task_by_id.get("TL-0401")
    if not isinstance(task, dict):
        validation.error("TASKS.yaml: missing TL-0401")
        return

    deliverables = task.get("deliverables")
    if not isinstance(deliverables, list):
        deliverables = []
    if TL0401_WINGET_ADR_PATH not in deliverables:
        validation.error(
            f"TL-0401: deliverables must reserve {TL0401_WINGET_ADR_PATH}"
        )
    winget_adr_paths = [
        value
        for value in deliverables
        if isinstance(value, str)
        and value.casefold().startswith("docs/adr/")
        and "winget" in value.casefold()
    ]
    if winget_adr_paths != [TL0401_WINGET_ADR_PATH]:
        validation.error(
            "TL-0401: WinGet ADR deliverable must be exactly "
            f"{TL0401_WINGET_ADR_PATH!r}"
        )

    human_evidence = task.get("human_evidence_required")
    if not isinstance(human_evidence, list):
        human_evidence = []
    if human_evidence != [TL0401_HUMAN_EVIDENCE]:
        validation.error(
            "TL-0401: human_evidence_required must remain the one exact approved "
            f"maintainer gate {TL0401_HUMAN_EVIDENCE!r}"
        )
    human_text = "\n".join(
        value for value in human_evidence if isinstance(value, str)
    )
    for phrase in ("ADR 0009", TL0401_WINGET_ADR_PATH):
        if phrase not in human_text:
            validation.error(
                f"TL-0401: human evidence must name {phrase!r}"
            )

    live_contract_text = "\n".join(
        value
        for values in (deliverables, human_evidence)
        for value in values
        if isinstance(value, str)
    )
    normalized_contract = unicodedata.normalize("NFKC", live_contract_text)
    normalized_contract_parts: list[str] = []
    for character in normalized_contract:
        category = unicodedata.category(character)
        if category == "Cf" or character in "*`~":
            continue
        if category == "Pd" or character == "_" or character.isspace():
            normalized_contract_parts.append("-")
        else:
            normalized_contract_parts.append(character)
    normalized_contract = re.sub(
        r"-+",
        "-",
        "".join(normalized_contract_parts),
    )
    legacy_adr_match = re.search(
        r"(?<![A-Z0-9])ADR-?0*4(?![0-9])",
        normalized_contract,
        re.IGNORECASE,
    )
    if legacy_adr_match is not None:
        validation.error(
            "TL-0401: superseded ADR 0004 marker is not permitted in the live contract"
        )

    reservations: dict[int, list[str]] = defaultdict(list)
    for other_task_id, other_task in task_by_id.items():
        other_deliverables = other_task.get("deliverables", [])
        if not isinstance(other_deliverables, list):
            continue
        for value in other_deliverables:
            if not isinstance(value, str):
                continue
            for number in adr_path_numbers(value):
                reservations[number].append(
                    f"{other_task_id}:{value}"
                )
    expected_reservations = {
        4: "TL-0009:docs/adr/0004-ephemeral-broker.md",
        9: f"TL-0401:{TL0401_WINGET_ADR_PATH}",
    }
    for number, expected_owner in expected_reservations.items():
        owners = sorted(reservations.get(number, []))
        if owners != [expected_owner]:
            validation.error(
                f"ADR {number:04d}: reservation must belong only to "
                f"{expected_owner!r}; found {owners}"
            )

    task_status = task.get("status")
    if task_status in {"backlog", "ready"}:
        premature_paths = sorted(
            path.relative_to(ROOT).as_posix()
            for path in (ROOT / "docs/adr").glob("*.md")
            if path.is_file()
            and 9 in adr_path_numbers(path.relative_to(ROOT).as_posix())
        )
        if premature_paths:
            validation.error(
                "TL-0401: ADR 0009 file must not exist before the task executes; "
                f"found {premature_paths}"
            )

    amendment_text = require_phrases(
        ADR_NUMBERING_AMENDMENT_PATH,
        (),
        validation,
    )
    amendment_visible = markdown_visible_text(amendment_text)
    amendment_phrases = (
        "AMD-2026-08-22-ADR-0009",
        "Janne Vuorela — Principal Software Architect & Sole Project Owner",
        "Approval date | 2026-08-22",
        "`0.3.0` → `0.3.1`",
        "docs/adr/0004-ephemeral-broker.md",
        TL0401_WINGET_ADR_PATH,
        "This amendment is numbering approval only; it is not that future backend-selection approval.",
        "The 91-task dependency graph and every milestone gate remain unchanged.",
        "No frozen decision text or meaning changes.",
        "Janne Vuorela owns this numbering amendment.",
    )
    amendment_folded = amendment_visible.casefold()
    for phrase in amendment_phrases:
        if phrase.casefold() not in amendment_folded:
            validation.error(
                f"{ADR_NUMBERING_AMENDMENT_PATH}: missing required amendment phrase {phrase!r}"
            )
    validate_local_markdown_links(
        ADR_NUMBERING_AMENDMENT_PATH,
        amendment_text,
        validation,
    )

    readme_text = require_phrases("README.md", (), validation)
    readme_visible = markdown_visible_text(readme_text)
    expected_navigation = f"({ADR_NUMBERING_AMENDMENT_PATH})"
    if exact_inline_markdown_link_count(readme_text, ADR_NUMBERING_AMENDMENT_PATH) != 1:
        validation.error(
            f"README.md: must link exactly once to {ADR_NUMBERING_AMENDMENT_PATH}"
        )

    authority_contracts = {
        "DECISIONS.md": (
            "AMD-2026-08-22-ADR-0009",
            ADR_NUMBERING_AMENDMENT_PATH,
            "completed ADR 0004 at `docs/adr/0004-ephemeral-broker.md`",
            f"ADR 0009 for TL-0401 at `{TL0401_WINGET_ADR_PATH}`",
        ),
        "ROADMAP.md": (
            "AMD-2026-08-22-ADR-0009",
            ADR_NUMBERING_AMENDMENT_PATH,
            "completed ADR 0004 at `docs/adr/0004-ephemeral-broker.md`",
            f"ADR 0009 for the future TL-0401 backend decision at `{TL0401_WINGET_ADR_PATH}`",
        ),
    }
    for relative, phrases in authority_contracts.items():
        authority_text = require_phrases(relative, (), validation)
        authority_visible = markdown_visible_text(authority_text)
        authority_folded = authority_visible.casefold()
        for phrase in phrases:
            if phrase.casefold() not in authority_folded:
                validation.error(
                    f"{relative}: missing ADR-numbering authority phrase {phrase!r}"
                )
        if exact_inline_markdown_link_count(
            authority_text,
            ADR_NUMBERING_AMENDMENT_PATH,
        ) != 1:
            validation.error(
                f"{relative}: must link exactly once to {ADR_NUMBERING_AMENDMENT_PATH}"
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


def canonical_testing_scan_text(text: str) -> str:
    canonical = unicodedata.normalize("NFKC", text)
    for _ in range(64):
        decoded = unquote(canonical)
        if decoded == canonical:
            break
        canonical = unicodedata.normalize("NFKC", decoded)
    return canonical


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


def privacy_high_risk_secret_match(text: str) -> str | None:
    """Detect secret shapes before any synthetic/example exemption."""

    patterns = (
        ("GitHub token", r"\bgh[pousr]_[A-Za-z0-9]{20,}\b"),
        ("bearer token", r"(?i)\bbearer\s+[A-Za-z0-9._~+/-]{12,}={0,2}\b"),
        ("Slack token", r"\bxox[baprs]-[A-Za-z0-9-]{10,}\b"),
        ("cloud access key", r"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b"),
        ("private-key material", r"-----BEGIN [A-Z ]*PRIVATE KEY-----"),
        ("recovery-key shaped value", r"\b\d{6}(?:-\d{6}){7}\b"),
    )
    canonical = canonical_testing_scan_text(text)
    for label, pattern in patterns:
        if re.search(pattern, canonical):
            return label
    return None


def privacy_yaml_scalar_paths(
    value: Any,
    path: tuple[str | int, ...] = (),
) -> list[tuple[tuple[str | int, ...], Any]]:
    """Return bounded scalar paths without recursive or alias traversal."""

    scalars: list[tuple[tuple[str | int, ...], Any]] = []
    stack: list[tuple[tuple[str | int, ...], Any, int]] = [(path, value, 0)]
    seen_containers: set[int] = set()
    visited = 0
    while stack:
        current_path, current, depth = stack.pop()
        visited += 1
        if visited > PRIVACY_FIXTURE_MAX_NODES:
            raise ValueError("fixture traversal exceeds node limit")
        if depth > PRIVACY_FIXTURE_MAX_DEPTH:
            raise ValueError("fixture traversal exceeds depth limit")
        if isinstance(current, (dict, list)):
            identity = id(current)
            if identity in seen_containers:
                raise ValueError("fixture contains an alias or cycle")
            seen_containers.add(identity)
        if isinstance(current, dict):
            items = list(current.items())
            for key, child in reversed(items):
                if not isinstance(key, str):
                    raise ValueError("fixture mapping keys must be strings")
                stack.append(((*current_path, key), child, depth + 1))
        elif isinstance(current, list):
            for index in range(len(current) - 1, -1, -1):
                stack.append(((*current_path, index), current[index], depth + 1))
        else:
            if isinstance(current, str) and len(current) > PRIVACY_FIXTURE_MAX_SCALAR_CHARS:
                raise ValueError("fixture scalar exceeds character limit")
            scalars.append((current_path, current))
    return scalars


def privacy_path_text(path: tuple[str | int, ...]) -> str:
    rendered = ""
    for component in path:
        if isinstance(component, int):
            rendered += f"[{component}]"
        else:
            rendered += ("." if rendered else "") + component
    return rendered


def reject_unknown_mapping_fields(
    owner: str,
    mapping: dict[str, Any],
    allowed_fields: tuple[str, ...] | set[str],
    validation: Validation,
) -> None:
    non_string_keys = [key for key in mapping if not isinstance(key, str)]
    if non_string_keys:
        validation.error(f"{owner}: mapping keys must be strings")
        return
    unknown = sorted(set(mapping) - set(allowed_fields))
    if unknown:
        validation.error(
            f"{owner}: contains unknown fields (count {len(unknown)})"
        )


def is_explicitly_synthetic_sensitive_value(
    value: Any,
    input_field: Any,
    field_class: Any,
) -> bool:
    """Recognize reserved examples, never arbitrary live-looking identifiers."""

    if (
        not isinstance(value, str)
        or not value.strip()
        or not isinstance(input_field, str)
        or not isinstance(field_class, str)
    ):
        return False
    canonical = canonical_testing_scan_text(value).strip()
    folded = canonical.casefold()

    try:
        address = ipaddress.ip_address(canonical)
    except ValueError:
        address = None
    if address is not None and input_field in {"ipv4_address", "ipv6_address"}:
        documentation_networks = (
            ipaddress.ip_network("192.0.2.0/24"),
            ipaddress.ip_network("198.51.100.0/24"),
            ipaddress.ip_network("203.0.113.0/24"),
            ipaddress.ip_network("2001:db8::/32"),
        )
        return any(address in network for network in documentation_networks)

    if input_field == "mac_address":
        return re.fullmatch(r"02:00:00:00:00:[0-9a-fA-F]{2}", canonical) is not None
    if input_field == "email_address":
        return re.fullmatch(
            r"(?i)synthetic(?:\.[a-z0-9_-]+)*@example\.test",
            canonical,
        ) is not None
    if input_field == "package_download_url":
        parsed = urlsplit(canonical)
        return (
            parsed.scheme == "https"
            and parsed.hostname is not None
            and (
                parsed.hostname == "example.test"
                or parsed.hostname.endswith(".example.test")
            )
            and parsed.username is None
            and parsed.password is None
            and "synthetic" in folded
        )
    if input_field in {"file_path", "network_path"}:
        return (
            "synthetic" in folded
            and SECURITY_MACHINE_PATH_RE.search(canonical) is not None
            and ".." not in canonical
        )
    if input_field == "windows_sid":
        return re.fullmatch(
            r"S-1-[0-9-]*SYNTHETIC-NOT-REAL",
            canonical,
            re.IGNORECASE,
        ) is not None
    if field_class == "raw_untrusted_output":
        return (input_field, canonical) in PRIVACY_RESERVED_SYNTHETIC_RAW_OUTPUTS

    explicit_marker = (
        "synthetic" in folded
        or "not-a-real" in folded
        or "not a real" in folded
    )
    if not explicit_marker:
        return False
    if testing_sensitive_match(canonical) is not None:
        return False
    if SECURITY_MACHINE_PATH_RE.search(canonical) or re.search(r"(?i)https?://", canonical):
        return False
    return True


def pilot_fixture_set_digest(root: Path = ROOT) -> str:
    """Hash the exact, path-sorted TL-0007 YAML fixture set."""

    digest = hashlib.sha256()
    for relative in PILOT_FIXTURE_FILES:
        data = (root / relative).read_bytes()
        digest.update(relative.encode("utf-8"))
        digest.update(b"\0")
        digest.update(hashlib.sha256(data).hexdigest().encode("ascii"))
        digest.update(b"\n")
    return digest.hexdigest()


def _pilot_mapping(
    value: Any, owner: str, validation: Validation
) -> dict[str, Any]:
    if not isinstance(value, dict):
        validation.error(f"{owner}: must be a mapping")
        return {}
    return value


def _pilot_sequence(
    value: Any,
    owner: str,
    validation: Validation,
    *,
    maximum: int,
    allow_empty: bool = False,
) -> list[Any]:
    if not isinstance(value, list):
        validation.error(f"{owner}: must be a list")
        return []
    if not value and not allow_empty:
        validation.error(f"{owner}: must not be empty")
    if len(value) > maximum:
        validation.error(f"{owner}: exceeds the {maximum}-item limit")
        return value[:maximum]
    return value


def _pilot_fields(
    value: dict[str, Any],
    owner: str,
    required: set[str],
    validation: Validation,
    *,
    optional: set[str] | None = None,
) -> None:
    allowed = required | (optional or set())
    reject_unknown_mapping_fields(owner, value, allowed, validation)
    missing = sorted(required - set(value))
    if missing:
        validation.error(f"{owner}: missing required fields {missing}")


def _pilot_token(
    value: Any,
    owner: str,
    validation: Validation,
    *,
    pattern: re.Pattern[str] = PILOT_FIXTURE_ID_RE,
) -> str:
    if not isinstance(value, str) or not value.strip():
        validation.error(f"{owner}: must be a non-empty string")
        return ""
    if value != value.strip() or len(value) > 512 or pattern.fullmatch(value) is None:
        validation.error(f"{owner}: must be a bounded stable token")
    return value


def _pilot_unique(values: list[str], owner: str, validation: Validation) -> None:
    if len(values) != len(set(values)):
        validation.error(f"{owner}: values must be unique")
    if len(values) != len({value.casefold() for value in values}):
        validation.error(f"{owner}: values must not collide by case")


def _pilot_capability_selection_valid(
    declared: tuple[str, ...],
    essential: tuple[str, ...],
    active: tuple[str, ...],
) -> bool:
    """Keep required capabilities active while permitting declared optionals."""

    declared_set = set(declared)
    active_set = set(active)
    return set(essential).issubset(active_set) and active_set.issubset(declared_set)


def _pilot_candidate_condition_matches(
    condition: dict[str, Any],
    evidence: dict[str, Any],
) -> bool:
    """Evaluate only the three bounded condition forms used by TL-0007."""

    data = evidence.get("data")
    condition_type = condition.get("type")
    if condition_type == "equals":
        expected = condition.get("value")
        return type(data) is type(expected) and data == expected
    if condition_type == "one_of":
        values = condition.get("values")
        return isinstance(values, list) and any(
            type(data) is type(expected) and data == expected
            for expected in values
        )
    if condition_type == "minimum_integer":
        minimum = condition.get("value")
        return (
            isinstance(data, int)
            and not isinstance(data, bool)
            and isinstance(minimum, int)
            and not isinstance(minimum, bool)
            and data >= minimum
            and evidence.get("unit") == condition.get("unit")
        )
    return False


def _pilot_safe_path_text(path: tuple[str | int, ...]) -> str:
    """Render only known-safe field-shaped path components in diagnostics."""

    rendered = ""
    for component in path:
        if isinstance(component, int):
            rendered += f"[{component}]"
            continue
        canonical = canonical_testing_scan_text(component)
        normalized = re.sub(r"[^a-z0-9]", "", canonical.casefold())
        unsafe = (
            canonical != component
            or re.fullmatch(r"[a-z][a-z0-9_]{0,63}", component) is None
            or normalized in PILOT_PROHIBITED_NORMALIZED_KEYS
            or privacy_high_risk_secret_match(canonical) is not None
            or testing_sensitive_match(canonical) is not None
            or SECURITY_MACHINE_PATH_RE.search(canonical) is not None
        )
        safe_component = "<redacted-key>" if unsafe else component
        rendered += ("." if rendered else "") + safe_component
    return rendered


def _pilot_timestamp(value: Any, owner: str, validation: Validation) -> None:
    if not isinstance(value, str) or not value.endswith("Z"):
        validation.error(f"{owner}: must be a UTC timestamp ending in Z")
        return
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        parsed = None
    if (
        parsed is None
        or parsed.tzinfo is None
        or parsed.utcoffset() is None
        or parsed.utcoffset().total_seconds() != 0
    ):
        validation.error(f"{owner}: must be an offset-aware UTC timestamp")


def _pilot_scan_fixture(
    relative: str,
    text: str,
    document: dict[str, Any],
    validation: Validation,
) -> None:
    """Reject sensitive/executable content without reflecting it in diagnostics."""

    canonical_text = canonical_testing_scan_text(text)
    secret = privacy_high_risk_secret_match(canonical_text)
    if secret is not None:
        validation.error(f"{relative}: contains prohibited {secret}")
    sensitive = testing_sensitive_match(canonical_text)
    if sensitive is not None:
        validation.error(f"{relative}: contains prohibited {sensitive}")
    if SECURITY_MACHINE_PATH_RE.search(canonical_text):
        validation.error(f"{relative}: contains a prohibited machine-specific path")

    try:
        scalars = privacy_yaml_scalar_paths(document)
    except ValueError as exc:
        validation.error(f"{relative}: unsafe fixture graph: {exc}")
        return
    for path, value in scalars:
        owner = f"{relative}: {_pilot_safe_path_text(path)}"
        normalized_keys = {
            re.sub(
                r"[^a-z0-9]",
                "",
                canonical_testing_scan_text(component).casefold(),
            )
            for component in path
            if isinstance(component, str)
        }
        if normalized_keys & PILOT_PROHIBITED_NORMALIZED_KEYS:
            validation.error(f"{owner}: uses a prohibited data or execution field")
        if not isinstance(value, str):
            continue
        canonical = canonical_testing_scan_text(value)
        folded = canonical.casefold()
        if any(ord(character) < 32 or ord(character) == 127 for character in canonical):
            validation.error(f"{owner}: contains a control character")
        if canonical.lstrip().startswith(("=", "+", "@")):
            validation.error(f"{owner}: starts with a spreadsheet-formula prefix")
        if re.search(r"(?i)(?:https?|ftp|file|mailto|data|javascript):|://", canonical):
            validation.error(f"{owner}: contains a prohibited URL or URI scheme")
        if re.search(r"(?:^|[\\/])\.\.(?:[\\/]|$)", canonical):
            validation.error(f"{owner}: contains a path-traversal segment")
        if re.search(
            r"(?i)(?:^|[\\/])(?:src|tests?|bin|obj|\.git)(?:[\\/]|$)|"
            r"\.(?:sln|csproj|dll|exe|cmd|bat|ps1|vbs|msi|msix)(?:\s|$)",
            canonical,
        ):
            validation.error(f"{owner}: contains a prohibited development artifact")
        if re.search(
            r"(?i)(?:^|\s)(?:powershell(?:\.exe)?|pwsh(?:\.exe)?|cmd(?:\.exe)?|"
            r"wscript(?:\.exe)?|cscript(?:\.exe)?|mshta(?:\.exe)?|"
            r"rundll32(?:\.exe)?)(?:\s|$)|^#!",
            canonical,
        ):
            validation.error(f"{owner}: contains prohibited command content")
        if re.search(r"<\s*/?\s*[A-Za-z][^>]*>", canonical):
            validation.error(f"{owner}: contains prohibited markup")
        if privacy_high_risk_secret_match(canonical) is not None:
            validation.error(f"{owner}: contains a prohibited secret shape")
        if testing_sensitive_match(canonical) is not None:
            validation.error(f"{owner}: contains a prohibited personal or device identifier")
        if SECURITY_MACHINE_PATH_RE.search(canonical):
            validation.error(f"{owner}: contains a prohibited machine-specific path")


def validate_pilot_fixtures(
    validation: Validation,
    task_by_id: dict[str, dict[str, Any]],
    root: Path = ROOT,
) -> str:
    """Validate the bounded TL-0007 candidate contract, not later final schemas."""

    fixture_root = root / "fixtures"
    expected_paths = set(PILOT_FIXTURE_FILES)
    allowed_paths = expected_paths | {PILOT_FIXTURE_README}
    actual_paths: set[str] = set()
    if fixture_root.is_dir():
        try:
            paths = sorted(path for path in fixture_root.rglob("*") if path.is_file())
        except OSError:
            paths = []
            validation.error("fixtures: cannot enumerate fixture inventory")
        for path in paths:
            relative = path.relative_to(root).as_posix()
            actual_paths.add(relative)
            try:
                path.resolve(strict=True).relative_to(root.resolve(strict=True))
            except (OSError, RuntimeError, ValueError):
                validation.error(f"{relative}: resolves outside the repository")
            if path.is_symlink():
                validation.error(f"{relative}: fixture files must not be symbolic links")
    else:
        validation.error("fixtures: required pilot fixture directory is missing")
    missing = sorted(expected_paths - actual_paths)
    unexpected = sorted(actual_paths - allowed_paths)
    if missing:
        validation.error(f"fixtures: missing required pilot fixtures {missing}")
    if unexpected:
        validation.error(f"fixtures: contains unexpected pilot fixture files {unexpected}")

    documents: dict[str, dict[str, Any]] = {}
    for relative in PILOT_FIXTURE_FILES:
        path = root / relative
        if not path.is_file():
            continue
        document = load_unique_key_yaml(path, validation)
        if not document:
            continue
        documents[relative] = document
        try:
            text = path.read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError):
            validation.error(f"{relative}: cannot read fixture text as UTF-8")
        else:
            _pilot_scan_fixture(relative, text, document, validation)
    try:
        digest = pilot_fixture_set_digest(root)
    except OSError:
        digest = ""
        validation.error("fixtures: cannot compute the exact fixture-set digest")

    catalog_path = "fixtures/catalog/catalog.yaml"
    catalog = _pilot_mapping(documents.get(catalog_path), catalog_path, validation)
    _pilot_fields(
        catalog,
        catalog_path,
        {
            "schema_version", "catalog_id", "version", "status",
            "synthetic_data", "classification", "applications",
        },
        validation,
    )
    if (
        catalog.get("schema_version") != PILOT_FIXTURE_SCHEMA_VERSIONS["catalog"]
        or catalog.get("status") != "candidate"
        or catalog.get("synthetic_data") is not True
        or catalog.get("classification") != PILOT_FIXTURE_CLASSIFICATION
    ):
        validation.error(f"{catalog_path}: invalid candidate catalog metadata")
    catalog_id = _pilot_token(catalog.get("catalog_id"), f"{catalog_path}.catalog_id", validation)
    catalog_version = _pilot_token(catalog.get("version"), f"{catalog_path}.version", validation)
    applications = _pilot_sequence(
        catalog.get("applications"),
        f"{catalog_path}: applications",
        validation,
        maximum=PILOT_FIXTURE_MAX_CATALOG_APPLICATIONS,
    )
    application_ids: list[str] = []
    capability_ids: list[str] = []
    catalog_applications: list[tuple[str, str, str]] = []
    application_by_capability: dict[str, str] = {}
    for index, raw in enumerate(applications):
        owner = f"{catalog_path}: applications[{index}]"
        app = _pilot_mapping(raw, owner, validation)
        _pilot_fields(
            app,
            owner,
            {
                "id", "version", "capability_id", "entry_status",
                "production_eligible", "package", "review", "behavior",
                "verification",
            },
            validation,
        )
        app_id = _pilot_token(app.get("id"), f"{owner}.id", validation)
        app_version = _pilot_token(app.get("version"), f"{owner}.version", validation)
        capability = _pilot_token(
            app.get("capability_id"), f"{owner}.capability_id", validation
        )
        application_ids.append(app_id)
        capability_ids.append(capability)
        application_by_capability[capability] = app_id
        if (
            not app_id.startswith("generic.synthetic.")
            or app_version != "0.0.0-fixture.1"
            or app.get("entry_status") != "synthetic_placeholder"
            or app.get("production_eligible") is not False
        ):
            validation.error(f"{owner}: catalog placeholder must remain non-production")

        package = _pilot_mapping(app.get("package"), f"{owner}.package", validation)
        _pilot_fields(
            package,
            f"{owner}.package",
            {
                "source_id", "package_id", "exact_version", "scope",
                "architectures", "minimum_os", "external_artifact",
            },
            validation,
        )
        if (
            package.get("source_id") != "synthetic-fixture-source"
            or not str(package.get("package_id", "")).startswith(
                "generic.synthetic.package."
            )
            or package.get("exact_version") != app_version
            or package.get("scope") != "machine"
            or package.get("architectures") != ["x64"]
            or package.get("minimum_os") != "windows_11"
            or package.get("external_artifact") is not False
        ):
            validation.error(f"{owner}.package: placeholder package contract is invalid")
        catalog_applications.append(
            (capability, app_id, str(package.get("package_id", "")))
        )

        review = _pilot_mapping(app.get("review"), f"{owner}.review", validation)
        _pilot_fields(
            review,
            f"{owner}.review",
            {
                "publisher", "declared_license", "license_review_status",
                "privacy_review_status", "license_reviewed_at_utc",
                "privacy_reviewed_at_utc", "redistribution_status",
            },
            validation,
        )
        if (
            review.get("publisher") != "synthetic-fixture-publisher"
            or review.get("declared_license") != "NOASSERTION"
            or review.get("license_review_status") != "pending"
            or review.get("privacy_review_status") != "pending"
            or review.get("license_reviewed_at_utc") is not None
            or review.get("privacy_reviewed_at_utc") is not None
            or review.get("redistribution_status") != "withheld"
        ):
            validation.error(f"{owner}.review: placeholder review must remain pending/withheld")

        behavior = _pilot_mapping(app.get("behavior"), f"{owner}.behavior", validation)
        _pilot_fields(
            behavior,
            f"{owner}.behavior",
            {
                "reboot", "background_service", "auto_update", "network_use",
                "download_bytes", "installed_bytes",
            },
            validation,
        )
        if behavior != {
            "reboot": "unknown",
            "background_service": "unknown",
            "auto_update": "unknown",
            "network_use": "not_applicable",
            "download_bytes": None,
            "installed_bytes": None,
        }:
            validation.error(f"{owner}.behavior: absent artifact behavior must remain unknown")

        verification = _pilot_mapping(
            app.get("verification"), f"{owner}.verification", validation
        )
        _pilot_fields(
            verification,
            f"{owner}.verification",
            {
                "status", "type", "expected_source_id",
                "expected_package_id", "expected_version",
            },
            validation,
        )
        if verification != {
            "status": "placeholder_unimplemented",
            "type": "exact_package_identity",
            "expected_source_id": package.get("source_id"),
            "expected_package_id": package.get("package_id"),
            "expected_version": package.get("exact_version"),
        }:
            validation.error(f"{owner}.verification: must match the exact placeholder")
    _pilot_unique(application_ids, f"{catalog_path}: application IDs", validation)
    _pilot_unique(capability_ids, f"{catalog_path}: capability IDs", validation)
    if tuple(capability_ids) != PILOT_CATALOG_CAPABILITIES:
        validation.error(f"{catalog_path}: applications must contain the exact ordered generic capability set")
    if tuple(catalog_applications) != PILOT_CATALOG_APPLICATIONS:
        validation.error(
            f"{catalog_path}: applications must match the exact approved synthetic catalog tuples"
        )

    policy_path = "fixtures/policies/community-laptop-policy.yaml"
    policy = _pilot_mapping(documents.get(policy_path), policy_path, validation)
    _pilot_fields(
        policy,
        policy_path,
        {
            "schema_version", "policy_id", "version", "status", "synthetic_data",
            "classification", "claim_scope", "effective", "target", "rules",
        },
        validation,
    )
    if (
        policy.get("schema_version") != PILOT_FIXTURE_SCHEMA_VERSIONS["policy"]
        or policy.get("status") != "candidate"
        or policy.get("synthetic_data") is not True
        or policy.get("classification") != PILOT_FIXTURE_CLASSIFICATION
        or policy.get("claim_scope")
        != "pilot_candidate_not_universal_hardware_requirement"
        or policy.get("effective")
        != {
            "status": "candidate_not_effective",
            "effective_from_utc": None,
            "effective_until_utc": None,
        }
        or policy.get("target")
        != {
            "operating_system": "windows_11",
            "support_state": "supported",
            "architecture": "x64",
            "device_class": "laptop",
        }
    ):
        validation.error(f"{policy_path}: invalid Windows 11 x64 candidate policy metadata")
    policy_id = _pilot_token(policy.get("policy_id"), f"{policy_path}.policy_id", validation)
    policy_version = _pilot_token(policy.get("version"), f"{policy_path}.version", validation)
    rules = _pilot_sequence(
        policy.get("rules"),
        f"{policy_path}: rules",
        validation,
        maximum=PILOT_FIXTURE_MAX_POLICY_RULES,
    )
    rule_ids: list[str] = []
    policy_evidence: list[str] = []
    policy_capabilities: set[str] = set()
    candidate_rules: list[dict[str, Any]] = []
    failure_dispositions = {
        "repair_and_retest", "human_review_required",
        "alternative_operating_system_candidate", "do_not_deploy",
    }
    for index, raw in enumerate(rules):
        owner = f"{policy_path}: rules[{index}]"
        rule = _pilot_mapping(raw, owner, validation)
        _pilot_fields(
            rule,
            owner,
            {
                "id", "requirement_type", "severity", "evidence_key",
                "condition", "decision",
            },
            validation,
            optional={"active_for_capability_ids"},
        )
        rule_ids.append(_pilot_token(rule.get("id"), f"{owner}.id", validation))
        policy_evidence.append(
            _pilot_token(rule.get("evidence_key"), f"{owner}.evidence_key", validation)
        )
        requirement = rule.get("requirement_type")
        active_capabilities_for_rule: tuple[str, ...] = ()
        severities_by_requirement = {
            "blocking": {"critical", "required"},
            "repairable": {"required"},
            "advisory": {"advisory"},
            "profile_dependent": {"required"},
            "human_confirmed": {"critical", "required"},
        }
        if rule.get("severity") not in severities_by_requirement.get(
            requirement, set()
        ):
            validation.error(f"{owner}: invalid requirement semantics")
        active = rule.get("active_for_capability_ids")
        if requirement == "profile_dependent":
            values = _pilot_sequence(
                active,
                f"{owner}.active_for_capability_ids",
                validation,
                maximum=PILOT_FIXTURE_MAX_PROFILE_ITEMS,
            )
            if any(value not in PILOT_CATALOG_CAPABILITIES for value in values):
                validation.error(f"{owner}: references an unknown capability")
            policy_capabilities.update(value for value in values if isinstance(value, str))
            active_capabilities_for_rule = tuple(
                value for value in values if isinstance(value, str)
            )
        elif "active_for_capability_ids" in rule:
            validation.error(f"{owner}: capability activation is profile-dependent only")

        condition = _pilot_mapping(rule.get("condition"), f"{owner}.condition", validation)
        condition_type = condition.get("type")
        condition_fields = {
            "equals": {"type", "value"},
            "one_of": {"type", "values"},
            "minimum_integer": {"type", "value", "unit"},
        }
        if condition_type not in condition_fields:
            validation.error(f"{owner}.condition: has an unapproved value")
        else:
            _pilot_fields(
                condition,
                f"{owner}.condition",
                condition_fields[condition_type],
                validation,
            )
            if condition_type == "equals":
                equals_value = condition.get("value")
                if isinstance(equals_value, str):
                    _pilot_token(
                        equals_value,
                        f"{owner}.condition.value",
                        validation,
                    )
                elif isinstance(equals_value, bool):
                    pass
                elif (
                    not isinstance(equals_value, int)
                    or isinstance(equals_value, bool)
                    or not -(2**63) <= equals_value <= 2**63 - 1
                ):
                    validation.error(
                        f"{owner}.condition.value: equals requires a bounded "
                        "non-null primitive"
                    )
            if condition_type == "one_of":
                values = _pilot_sequence(
                    condition.get("values"),
                    f"{owner}.condition.values",
                    validation,
                    maximum=16,
                )
                if any(not isinstance(value, str) for value in values):
                    validation.error(f"{owner}.condition.values: must contain strings")
                _pilot_unique(
                    [value for value in values if isinstance(value, str)],
                    f"{owner}.condition.values",
                    validation,
                )
            if condition_type == "minimum_integer" and (
                not isinstance(condition.get("value"), int)
                or isinstance(condition.get("value"), bool)
                or condition.get("value", -1) < 0
                or condition.get("unit") not in {"bytes", "percent"}
            ):
                validation.error(f"{owner}.condition: invalid bounded integer threshold")

        decision = _pilot_mapping(rule.get("decision"), f"{owner}.decision", validation)
        _pilot_fields(
            decision,
            f"{owner}.decision",
            {"blocks_ready", "unmet_disposition", "unavailable_disposition"},
            validation,
        )
        if requirement == "advisory":
            valid_decision = decision == {
                "blocks_ready": False,
                "unmet_disposition": None,
                "unavailable_disposition": None,
            }
        else:
            valid_decision = (
                decision.get("blocks_ready") is True
                and decision.get("unmet_disposition") in failure_dispositions
                and decision.get("unavailable_disposition") in failure_dispositions
            )
        if not valid_decision:
            validation.error(f"{owner}.decision: invalid explicit disposition semantics")
        candidate_rules.append(
            {
                "evidence_key": policy_evidence[-1],
                "requirement_type": requirement,
                "active_capabilities": set(active_capabilities_for_rule),
                "condition": condition,
                "decision": decision,
            }
        )
    _pilot_unique(rule_ids, f"{policy_path}: rule IDs", validation)
    _pilot_unique(policy_evidence, f"{policy_path}: evidence keys", validation)

    profiles: list[dict[str, Any]] = []
    for profile_path in (
        "fixtures/profiles/basic.yaml",
        "fixtures/profiles/job-seeker.yaml",
    ):
        profile = _pilot_mapping(documents.get(profile_path), profile_path, validation)
        _pilot_fields(
            profile,
            profile_path,
            {
                "schema_version", "profile_id", "version", "status",
                "synthetic_data", "classification", "policy", "catalog",
                "workshop", "recipient",
            },
            validation,
        )
        profile_id = _pilot_token(
            profile.get("profile_id"), f"{profile_path}.profile_id", validation
        )
        profile_version = _pilot_token(
            profile.get("version"), f"{profile_path}.version", validation
        )
        if (
            profile.get("schema_version") != PILOT_FIXTURE_SCHEMA_VERSIONS["profile"]
            or profile_version != "0.1.0-candidate.1"
            or profile.get("status") != "candidate"
            or profile.get("synthetic_data") is not True
            or profile.get("classification") != PILOT_FIXTURE_CLASSIFICATION
            or profile.get("policy")
            != {"id": policy_id, "version": policy_version}
            or profile.get("catalog")
            != {"id": catalog_id, "version": catalog_version}
        ):
            validation.error(f"{profile_path}: invalid candidate profile references")

        workshop = _pilot_mapping(
            profile.get("workshop"), f"{profile_path}.workshop", validation
        )
        _pilot_fields(
            workshop, f"{profile_path}.workshop", {"capabilities"}, validation
        )
        capabilities = _pilot_sequence(
            workshop.get("capabilities"),
            f"{profile_path}.workshop.capabilities",
            validation,
            maximum=PILOT_FIXTURE_MAX_PROFILE_ITEMS,
        )
        profile_capabilities: list[str] = []
        intents: dict[str, str] = {}
        for index, raw in enumerate(capabilities):
            owner = f"{profile_path}.workshop.capabilities[{index}]"
            capability = _pilot_mapping(raw, owner, validation)
            _pilot_fields(
                capability,
                owner,
                {"capability_id", "intent", "reason_code"},
                validation,
            )
            capability_id = _pilot_token(
                capability.get("capability_id"), f"{owner}.capability_id", validation
            )
            profile_capabilities.append(capability_id)
            intents[capability_id] = str(capability.get("intent", ""))
            _pilot_token(capability.get("reason_code"), f"{owner}.reason_code", validation)
            if capability.get("intent") not in {"essential", "optional"}:
                validation.error(f"{owner}.intent: has an unapproved value")
        _pilot_unique(
            profile_capabilities,
            f"{profile_path}.workshop.capabilities",
            validation,
        )
        if (
            tuple(profile_capabilities) != PILOT_CATALOG_CAPABILITIES
            or any(
                capability not in application_by_capability
                for capability in profile_capabilities
            )
        ):
            validation.error(f"{profile_path}: capabilities do not resolve exactly")
        expected_intents = {
            capability: (
                "optional"
                if profile_id == "basic" and capability == "video_calling"
                else "essential"
            )
            for capability in PILOT_CATALOG_CAPABILITIES
        }
        if intents != expected_intents:
            validation.error(f"{profile_path}: capability intent is invalid")

        recipient = _pilot_mapping(
            profile.get("recipient"), f"{profile_path}.recipient", validation
        )
        _pilot_fields(recipient, f"{profile_path}.recipient", {"choices"}, validation)
        choices = _pilot_sequence(
            recipient.get("choices"),
            f"{profile_path}.recipient.choices",
            validation,
            maximum=PILOT_FIXTURE_MAX_PROFILE_ITEMS,
        )
        choice_ids: list[str] = []
        choice_types: list[str] = []
        for index, raw in enumerate(choices):
            owner = f"{profile_path}.recipient.choices[{index}]"
            choice = _pilot_mapping(raw, owner, validation)
            _pilot_fields(
                choice,
                owner,
                {
                    "choice_id", "choice_type", "state",
                    "requires_recipient_presence", "workshop_action",
                },
                validation,
            )
            choice_ids.append(
                _pilot_token(choice.get("choice_id"), f"{owner}.choice_id", validation)
            )
            choice_types.append(str(choice.get("choice_type", "")))
            if (
                choice.get("state") != "deferred"
                or choice.get("requires_recipient_presence") is not True
                or choice.get("workshop_action") is not False
            ):
                validation.error(f"{owner}: recipient choice must remain deferred")
        _pilot_unique(choice_ids, f"{profile_path}: choice IDs", validation)
        _pilot_unique(choice_types, f"{profile_path}: choice types", validation)
        if tuple(choice_types) != PILOT_RECIPIENT_CHOICES:
            validation.error(f"{profile_path}: recipient choices are incomplete")
        profiles.append(
            {
                "id": profile_id,
                "version": profile_version,
                "capabilities": tuple(profile_capabilities),
                "essential_capabilities": tuple(
                    capability
                    for capability in profile_capabilities
                    if intents.get(capability) == "essential"
                ),
            }
        )
    if [profile["id"] for profile in profiles] != ["basic", "job-seeker"]:
        validation.error("fixtures/profiles: Basic and Job Seeker profiles are required")
    if not policy_capabilities.issubset(set(PILOT_CATALOG_CAPABILITIES)):
        validation.error(f"{policy_path}: active capability references do not resolve")
    profiles_by_reference = {
        (profile["id"], profile["version"]): profile for profile in profiles
    }
    if len(profiles_by_reference) != len(profiles):
        validation.error("fixtures/profiles: profile references must be unique")

    job_contracts = {
        "fixtures/jobs/assessment-ready.yaml": {
            "expected": {
                "sanitization_gate": "allow_assessment",
                "policy_disposition": "ready_to_prepare",
                "limitation_code": "none",
            },
            "sanitization": {
                "sanitization_state": "verified",
                "evidence_state": "observed",
                "verification_state": "verified",
                "method_code": "synthetic_external_sanitization",
                "attributed": True,
            },
            "profile": (
                "job-seeker",
                "0.1.0-candidate.1",
                PILOT_CATALOG_CAPABILITIES,
            ),
        },
        "fixtures/jobs/sanitization-blocked.yaml": {
            "expected": {
                "sanitization_gate": "blocked",
                "policy_evaluation": "not_run",
                "limitation_code": "sanitization_unknown",
            },
            "sanitization": {
                "sanitization_state": "unknown",
                "evidence_state": "not_available",
                "verification_state": "not_available",
                "method_code": "not_available",
                "attributed": False,
            },
            "profile": (
                "basic",
                "0.1.0-candidate.1",
                PILOT_CATALOG_CAPABILITIES[:3],
            ),
        },
        "fixtures/jobs/partial-observations.yaml": {
            "expected": {
                "sanitization_gate": "allow_assessment",
                "policy_disposition": "human_review_required",
                "limitation_code": "required_evidence_not_available",
            },
            "sanitization": {
                "sanitization_state": "replacement_storage",
                "evidence_state": "observed",
                "verification_state": "verified",
                "method_code": "synthetic_storage_replacement",
                "attributed": True,
            },
            "profile": (
                "job-seeker",
                "0.1.0-candidate.1",
                PILOT_CATALOG_CAPABILITIES,
            ),
        },
    }
    job_evidence: dict[str, set[str]] = {}
    job_evidence_values: dict[str, dict[str, dict[str, Any]]] = {}
    job_active_capabilities: dict[str, tuple[str, ...]] = {}
    fixture_ids: list[str] = []
    for job_path, contract in job_contracts.items():
        job_document = _pilot_mapping(
            documents.get(job_path), job_path, validation
        )
        _pilot_fields(
            job_document,
            job_path,
            {
                "schema_version", "fixture_id", "synthetic_data",
                "classification", "expected", "job",
                "profile", "sanitization_evidence", "observations",
            },
            validation,
        )
        fixture_id = _pilot_token(
            job_document.get("fixture_id"), f"{job_path}.fixture_id", validation
        )
        fixture_ids.append(fixture_id)
        if (
            job_document.get("schema_version")
            != PILOT_FIXTURE_SCHEMA_VERSIONS["job"]
            or job_document.get("synthetic_data") is not True
            or job_document.get("classification") != PILOT_FIXTURE_CLASSIFICATION
            or job_document.get("expected") != contract["expected"]
        ):
            validation.error(f"{job_path}: invalid governed scenario metadata")

        job = _pilot_mapping(job_document.get("job"), f"{job_path}.job", validation)
        _pilot_fields(
            job,
            f"{job_path}.job",
            {"job_id", "lifecycle_state", "device_class", "created_at_utc"},
            validation,
        )
        if (
            PILOT_SYNTHETIC_JOB_ID_RE.fullmatch(str(job.get("job_id", ""))) is None
            or job.get("lifecycle_state")
            not in {"intake", "assessment_complete", "assessment_blocked"}
            or job.get("device_class") != "laptop"
        ):
            validation.error(f"{job_path}.job: invalid synthetic job metadata")
        _pilot_timestamp(
            job.get("created_at_utc"), f"{job_path}.job.created_at_utc", validation
        )

        profile = _pilot_mapping(
            job_document.get("profile"), f"{job_path}.profile", validation
        )
        _pilot_fields(
            profile,
            f"{job_path}.profile",
            {"id", "version", "active_capability_ids"},
            validation,
        )
        profile_id = _pilot_token(
            profile.get("id"), f"{job_path}.profile.id", validation
        )
        profile_version = _pilot_token(
            profile.get("version"), f"{job_path}.profile.version", validation
        )
        active_capability_ids = _pilot_sequence(
            profile.get("active_capability_ids"),
            f"{job_path}.profile.active_capability_ids",
            validation,
            maximum=PILOT_FIXTURE_MAX_PROFILE_ITEMS,
        )
        active_capabilities = tuple(
            _pilot_token(
                capability,
                f"{job_path}.profile.active_capability_ids[{index}]",
                validation,
            )
            for index, capability in enumerate(active_capability_ids)
        )
        _pilot_unique(
            list(active_capabilities),
            f"{job_path}.profile.active_capability_ids",
            validation,
        )
        expected_profile_id, expected_profile_version, expected_active = contract[
            "profile"
        ]
        resolved_profile = profiles_by_reference.get((profile_id, profile_version))
        if (
            profile_id != expected_profile_id
            or profile_version != expected_profile_version
            or active_capabilities != expected_active
            or resolved_profile is None
            or not _pilot_capability_selection_valid(
                () if resolved_profile is None else resolved_profile["capabilities"],
                ()
                if resolved_profile is None
                else resolved_profile["essential_capabilities"],
                active_capabilities,
            )
        ):
            validation.error(f"{job_path}.profile: invalid candidate profile binding")
        job_active_capabilities[job_path] = active_capabilities

        sanitization = _pilot_mapping(
            job_document.get("sanitization_evidence"),
            f"{job_path}.sanitization_evidence",
            validation,
        )
        _pilot_fields(
            sanitization,
            f"{job_path}.sanitization_evidence",
            {
                "evidence_id", "classification", "provider_id",
                "collected_at_utc", "provenance", "evidence_state",
                "sanitization_state", "method_code", "operator_id",
                "occurred_at_utc", "media_fixture_id", "verification_state",
                "policy_version",
            },
            validation,
        )
        provenance = sanitization.get("provenance")
        sanitization_contract = contract["sanitization"]
        if (
            sanitization.get("classification") != "WORKSHOP_RESTRICTED"
            or provenance
            != {"kind": "synthetic_fixture", "fixture_id": fixture_id}
            or sanitization.get("policy_version")
            != f"{policy_id}@{policy_version}"
        ):
            validation.error(f"{job_path}.sanitization_evidence: invalid D-007 evidence")
        if any(
            sanitization.get(field) != sanitization_contract[field]
            for field in (
                "sanitization_state",
                "evidence_state",
                "verification_state",
                "method_code",
            )
        ):
            validation.error(
                f"{job_path}.sanitization_evidence: invalid exact D-007 scenario tuple"
            )
        for field in ("evidence_id", "provider_id", "method_code"):
            _pilot_token(
                sanitization.get(field),
                f"{job_path}.sanitization_evidence.{field}",
                validation,
            )
        _pilot_timestamp(
            sanitization.get("collected_at_utc"),
            f"{job_path}.sanitization_evidence.collected_at_utc",
            validation,
        )
        if sanitization_contract["attributed"]:
            if (
                PILOT_SYNTHETIC_OPERATOR_ID_RE.fullmatch(
                    str(sanitization.get("operator_id", ""))
                )
                is None
                or PILOT_SYNTHETIC_MEDIA_ID_RE.fullmatch(
                    str(sanitization.get("media_fixture_id", ""))
                )
                is None
            ):
                validation.error(f"{job_path}.sanitization_evidence: invalid synthetic attribution")
            _pilot_timestamp(
                sanitization.get("occurred_at_utc"),
                f"{job_path}.sanitization_evidence.occurred_at_utc",
                validation,
            )
        elif any(
            sanitization.get(field) is not None
            for field in ("operator_id", "occurred_at_utc", "media_fixture_id")
        ):
            validation.error(f"{job_path}.sanitization_evidence: unavailable attribution must be null")

        observations = _pilot_sequence(
            job_document.get("observations"),
            f"{job_path}.observations",
            validation,
            maximum=PILOT_FIXTURE_MAX_OBSERVATIONS,
            allow_empty=sanitization_contract["sanitization_state"] == "unknown",
        )
        observation_ids: list[str] = []
        evidence_keys: list[str] = []
        evidence_values: dict[str, dict[str, Any]] = {
            "sanitization.state": {
                "state": sanitization.get("evidence_state"),
                "data": sanitization.get("sanitization_state"),
                "unit": None,
            }
        }
        for index, raw in enumerate(observations):
            owner = f"{job_path}.observations[{index}]"
            observation = _pilot_mapping(raw, owner, validation)
            _pilot_fields(
                observation,
                owner,
                {
                    "evidence_id", "evidence_key", "classification",
                    "provider_id", "collected_at_utc", "provenance",
                    "evidence_state", "value",
                },
                validation,
                optional={"unit", "limitation_code"},
            )
            observation_ids.append(
                _pilot_token(
                    observation.get("evidence_id"),
                    f"{owner}.evidence_id",
                    validation,
                )
            )
            evidence_key = _pilot_token(
                observation.get("evidence_key"),
                f"{owner}.evidence_key",
                validation,
            )
            evidence_keys.append(evidence_key)
            _pilot_token(
                observation.get("provider_id"),
                f"{owner}.provider_id",
                validation,
            )
            evidence_state = observation.get("evidence_state")
            if (
                observation.get("classification") != "WORKSHOP_RESTRICTED"
                or observation.get("provenance")
                != {"kind": "synthetic_fixture", "fixture_id": fixture_id}
                or observation.get("evidence_state")
                not in {
                    "observed", "inferred", "human_confirmed",
                    "not_available", "not_applicable",
                }
            ):
                validation.error(f"{owner}: invalid evidence metadata")
            _pilot_timestamp(
                observation.get("collected_at_utc"),
                f"{owner}.collected_at_utc",
                validation,
            )
            value = _pilot_mapping(observation.get("value"), f"{owner}.value", validation)
            _pilot_fields(value, f"{owner}.value", {"type", "data"}, validation)
            value_type = value.get("type")
            if value_type not in {"enum", "string", "integer", "boolean"}:
                validation.error(f"{owner}.value.type: has an unapproved value")
            data = value.get("data")
            evidence_values[evidence_key] = {
                "state": evidence_state,
                "data": data,
                "unit": observation.get("unit"),
            }
            limitation_code = observation.get("limitation_code")
            if evidence_state in {"not_available", "not_applicable"}:
                if data is not None:
                    validation.error(f"{owner}.value: unavailable evidence must carry explicit null")
                _pilot_token(
                    limitation_code,
                    f"{owner}.limitation_code",
                    validation,
                )
            elif data is None:
                validation.error(f"{owner}.value: available evidence must carry typed data")
            elif "limitation_code" in observation:
                validation.error(f"{owner}.limitation_code: available evidence must not carry a limitation")
            elif (
                (value_type == "boolean" and not isinstance(data, bool))
                or (
                    value_type == "integer"
                    and (not isinstance(data, int) or isinstance(data, bool))
                )
                or (value_type in {"enum", "string"} and not isinstance(data, str))
            ):
                validation.error(f"{owner}.value: data does not match its declared type")
        _pilot_unique(observation_ids, f"{job_path}: evidence IDs", validation)
        _pilot_unique(evidence_keys, f"{job_path}: evidence keys", validation)
        job_evidence[job_path] = set(evidence_keys)
        job_evidence_values[job_path] = evidence_values
    _pilot_unique(fixture_ids, "fixtures/jobs: fixture IDs", validation)

    policy_key_set = set(policy_evidence)
    if (
        job_evidence.get("fixtures/jobs/assessment-ready.yaml", set())
        | {"sanitization.state"}
    ) != policy_key_set:
        validation.error("fixtures/jobs/assessment-ready.yaml: evidence keys must exactly cover the candidate policy")
    partial_evidence = (
        job_evidence.get("fixtures/jobs/partial-observations.yaml", set())
        | {"sanitization.state"}
    )
    if partial_evidence != policy_key_set | {"function.touch"}:
        validation.error(
            "fixtures/jobs/partial-observations.yaml: evidence keys must cover "
            "the candidate policy plus only function.touch"
        )
    partial_path = "fixtures/jobs/partial-observations.yaml"
    expected_partial_disposition = job_contracts[partial_path]["expected"][
        "policy_disposition"
    ]

    def active_rule(rule: dict[str, Any], job_path: str) -> bool:
        return rule["requirement_type"] != "profile_dependent" or bool(
            rule["active_capabilities"]
            & set(job_active_capabilities.get(job_path, ()))
        )

    def failure_disposition(
        rule: dict[str, Any], job_path: str
    ) -> str | None:
        evidence = job_evidence_values.get(job_path, {}).get(rule["evidence_key"])
        decision = rule["decision"]
        if evidence is None or evidence.get("state") in {
            "not_available",
            "not_applicable",
        }:
            return decision.get("unavailable_disposition")
        if not _pilot_candidate_condition_matches(rule["condition"], evidence):
            return decision.get("unmet_disposition")
        return None

    ready_path = "fixtures/jobs/assessment-ready.yaml"
    ready_failures = [
        failure_disposition(rule, ready_path)
        for rule in candidate_rules
        if rule["requirement_type"] != "advisory"
        and active_rule(rule, ready_path)
        and failure_disposition(rule, ready_path) is not None
    ]
    if ready_failures:
        validation.error(
            f"{ready_path}: ready claim does not satisfy every active required condition"
        )

    partial_failures = [
        (rule, failure_disposition(rule, partial_path))
        for rule in candidate_rules
        if rule["requirement_type"] != "advisory"
        and active_rule(rule, partial_path)
        and failure_disposition(rule, partial_path) is not None
    ]
    if (
        not partial_failures
        or {disposition for _rule, disposition in partial_failures}
        != {expected_partial_disposition}
        or not any(
            rule["requirement_type"] == "profile_dependent"
            and disposition == expected_partial_disposition
            for rule, disposition in partial_failures
        )
    ):
        validation.error(
            f"{partial_path}: expected profile-dependent disposition is not reproducible"
        )

    task = task_by_id.get("TL-0007", {})
    if task.get("status") == "done":
        evidence = task.get("evidence")
        approved = isinstance(evidence, list) and any(
            isinstance(item, dict)
            and str(item.get("result", "")).casefold() == "passed"
            and digest
            and digest in str(item.get("reference", "")).casefold()
            and re.search(
                r"(?is)pilot owner.{0,100}approv(?:e|ed|al).{0,160}"
                r"candidate policy.{0,160}capability set|"
                r"approv(?:e|ed|al).{0,100}candidate policy.{0,160}"
                r"capability set.{0,160}pilot owner",
                str(item.get("summary", "")),
            )
            is not None
            and bool(str(item.get("environment", "")).strip())
            for item in evidence
        )
        if not approved:
            validation.error(
                "TL-0007 done evidence must bind named pilot-owner approval "
                "of the candidate policy and capability set to the current fixture digest"
            )
    return digest


def privacy_approval_state(value: str) -> str:
    """Return an exact leading approval token, never substring semantics."""

    return value.split("—", 1)[0].strip().casefold()


def canonical_privacy_approval_blob(relative: str, text: str) -> str:
    """Exclude only approval-record state from the reviewed contract bytes.

    A privacy owner reviews a real pre-approval commit. Recording that decision
    necessarily changes status and approval metadata afterward, so those exact
    state-only variants are canonicalized. Contract classifications, retention
    values, allowlists, fixture cases, and runtime-control disclaimers remain
    byte-bound to the reviewed commit.
    """

    def canonical_exact_line(
        value: str, variants: tuple[str, ...], replacement: str
    ) -> str:
        pattern = r"^(?:" + "|".join(re.escape(item) for item in variants) + r")\s*$"
        return re.sub(pattern, replacement, value, flags=re.MULTILINE)

    canonical = text.replace("\r\n", "\n").replace("\r", "\n")
    if relative == "docs/privacy/privacy-model.md":
        canonical = canonical_exact_line(
            canonical,
            (
                f"**Status:** {PRIVACY_PENDING_STATUS}",
                f"**Status:** {PRIVACY_APPROVED_STATUS}",
            ),
            "**Status:** <APPROVAL-STATE>",
        )
        canonical = canonical_exact_line(
            canonical,
            (
                "**Model revision:** TL-0005 review 1",
                "**Model revision:** TL-0005 approved 1",
            ),
            "**Model revision:** <APPROVAL-STATE>",
        )
        canonical = canonical_exact_line(
            canonical,
            (
                "**Approval result:** Pending — this document does not satisfy "
                "the human evidence required by `TL-0005`",
                "**Approval result:** Approved — named privacy-owner review "
                "recorded for the exact commit",
            ),
            "**Approval result:** <APPROVAL-STATE>",
        )
        canonical = canonical_exact_line(
            canonical,
            (
                "The following values are the proposed safe defaults for later "
                "implementation. They are deliberately marked **not approved** "
                "until a named privacy owner reviews the classifications and "
                "durations. Organization policy may shorten them. An extension "
                "must name the data class, reason, accountable owner, review date, "
                "and deletion condition; silent indefinite retention is not permitted.",
                "The following values are the reviewed safe defaults for later "
                "implementation. They are approved by the named privacy owner for "
                "the exact reviewed commit; changes require review of the "
                "classifications and durations. Organization policy may shorten "
                "them. An extension must name the data class, reason, accountable "
                "owner, review date, and deletion condition; silent indefinite "
                "retention is not permitted.",
            ),
            "<APPROVAL-STATE:RETENTION-INTRO>",
        )
        for state in ("pending", "reviewed"):
            canonical = canonical.replace(
                f"this {state} default when supply-chain or rollback evidence requires it",
                "this <APPROVAL-STATE> default when supply-chain or rollback evidence requires it",
            )
        for field in (
            "Current privacy-owner",
            "Current privacy-owner role",
            "Current review date",
            "Reviewed commit/reference",
            "Approval scope",
            "Conditions/residual risks",
            "Current result",
        ):
            canonical = re.sub(
                rf"^\*\*{re.escape(field)}:\*\*.*$",
                f"**{field}:** <APPROVAL-RECORD>",
                canonical,
                flags=re.MULTILINE,
            )
    elif relative == "docs/privacy/logging-standard.md":
        canonical = canonical_exact_line(
            canonical,
            (
                f"**Status:** {PRIVACY_PENDING_STATUS}",
                f"**Status:** {PRIVACY_APPROVED_STATUS}",
            ),
            "**Status:** <APPROVAL-STATE>",
        )
        canonical = canonical_exact_line(
            canonical,
            (
                "**Standard revision:** TL-0005 review 1",
                "**Standard revision:** TL-0005 approved 1",
            ),
            "**Standard revision:** <APPROVAL-STATE>",
        )
        for state_fragment in (
            "the proposed 14-day sanitized-log default from `privacy-model.md` "
            "only after privacy-owner approval, together",
            "the approved 14-day sanitized-log default from `privacy-model.md` together",
        ):
            canonical = canonical.replace(
                state_fragment,
                "the <APPROVAL-STATE> 14-day sanitized-log default from "
                "`privacy-model.md` together",
            )
        for state_sentence in (
            "They do not claim a production redactor, logger, retention job, "
            "support exporter, or human privacy approval exists.",
            "They do not claim a production redactor, logger, retention job, or "
            "support exporter exists; the separate approval record covers only "
            "human contract review.",
        ):
            canonical = canonical.replace(
                state_sentence,
                "<APPROVAL-STATE:HUMAN-REVIEW>; no production redactor, logger, "
                "retention job, or support exporter is claimed.",
            )
        for state_sentence in (
            "The privacy-owner approval record is maintained in `privacy-model.md`; "
            "it is currently pending.",
            "The privacy-owner approval record is maintained in `privacy-model.md`; "
            "it is approved for the exact reviewed commit.",
        ):
            canonical = canonical.replace(
                state_sentence,
                "The privacy-owner approval record is maintained in "
                "`privacy-model.md`; <APPROVAL-STATE>.",
            )
    elif relative == "docs/privacy/redaction-test-cases.yaml":
        canonical = re.sub(
            r"^review:\n(?:^(?:[ \t].*)?\n)*?^support_export_allowlist:",
            "review:\n  <APPROVAL-RECORD>\n\nsupport_export_allowlist:",
            canonical,
            count=1,
            flags=re.MULTILINE,
        )
    return canonical


def validate_privacy_approval_commit(
    reviewed_commit: str,
    validation: Validation,
) -> None:
    """Bind approved current contracts to real blobs in the reviewed commit."""

    def git(arguments: list[str]) -> subprocess.CompletedProcess[bytes] | None:
        try:
            return subprocess.run(
                ["git", "-C", str(ROOT), *arguments],
                check=False,
                stdout=subprocess.PIPE,
                stderr=subprocess.DEVNULL,
                timeout=5,
            )
        except (OSError, subprocess.TimeoutExpired):
            return None

    commit_check = git(["cat-file", "-e", f"{reviewed_commit}^{{commit}}"])
    if commit_check is None or commit_check.returncode != 0:
        validation.error(
            "Privacy approval reference must name an existing reviewed Git commit"
        )
        return

    for relative in PRIVACY_APPROVAL_PATHS:
        object_name = f"{reviewed_commit}:{relative}"
        size_result = git(["cat-file", "-s", object_name])
        if size_result is None or size_result.returncode != 0:
            validation.error(
                f"Privacy approval commit is missing governed blob {relative}"
            )
            continue
        try:
            blob_size = int(size_result.stdout.decode("ascii").strip())
        except (UnicodeDecodeError, ValueError):
            validation.error(
                f"Privacy approval commit has unreadable blob metadata for {relative}"
            )
            continue
        if blob_size > PRIVACY_FIXTURE_MAX_BYTES:
            validation.error(
                f"Privacy approval commit blob {relative} exceeds the review bound"
            )
            continue
        blob_result = git(["show", object_name])
        if blob_result is None or blob_result.returncode != 0:
            validation.error(
                f"Privacy approval commit blob {relative} cannot be read"
            )
            continue
        try:
            reviewed_text = blob_result.stdout.decode("utf-8")
            current_text = (ROOT / relative).read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            validation.error(
                f"Privacy approval contract {relative} must be readable UTF-8"
            )
            continue
        if canonical_privacy_approval_blob(
            relative, reviewed_text
        ) != canonical_privacy_approval_blob(relative, current_text):
            validation.error(
                f"Privacy approval commit does not bind current contract {relative}"
            )


def validate_privacy_documents(
    validation: Validation,
    task_by_id: dict[str, dict[str, Any]],
    decision_ids: set[str],
) -> None:
    """Validate TL-0005's design contracts without claiming runtime controls."""

    model_relative = "docs/privacy/privacy-model.md"
    logging_relative = "docs/privacy/logging-standard.md"
    fixture_relative = "docs/privacy/redaction-test-cases.yaml"

    model_text = require_phrases(
        model_relative,
        (
            "It does not claim that those controls are implemented",
            "## Privacy invariants",
            "Recipient name, email, account, address, phone number, or other identity is unnecessary",
            "Telemetry is off by default",
            "## Classification model",
            "`WORKSHOP_RESTRICTED`",
            "`RECIPIENT_GUIDE`",
            "`SUPPORT_SANITIZED`",
            "`RAW_UNTRUSTED_SENSITIVE`",
            "`SECRET_OR_PERSONAL_CONTENT_EXCLUDED`",
            "`SIBLING_PRIVATE_EXCLUDED`",
            "## Logical data map",
            "## Three output contracts",
            "### Technical workshop record",
            "### Plain-language recipient guide",
            "### Sanitized diagnostic bundle",
            "## Proposed default retention guidance",
            "`TL-0703`",
            "## Access, export, and deletion rules",
            "## Review and implementation gates",
            "Passing a fixture validator proves only that the contract is internally complete",
            "## Privacy-owner approval record",
            "Current privacy-owner:",
            "Current privacy-owner role:",
            "Current review date:",
            "Reviewed commit/reference:",
            "Approval scope:",
            "Conditions/residual risks:",
            "Current result:",
        ),
        validation,
    )
    logging_text = require_phrases(
        logging_relative,
        (
            "It is a design contract, not a claim that a logger or redactor exists",
            "## Required architecture",
            "Redaction and bounds are applied before the first database, file, UI, report, or queue write",
            "There is no telemetry or background upload path",
            "## Ordinary event envelope",
            "No free-form `message`, `details`, `data`, `context`, `payload`, `command`, `arguments`, `environment`, or arbitrary dictionary field is permitted",
            "## Prohibited diagnostic fields and values",
            "## Raw input normalization",
            "Default persistent retention is zero",
            "## Fixed redaction representation",
            "## Sanitized support schema",
            "## Preview-bound export procedure",
            "Require explicit operator approval",
            "There is no send, upload, remote-support, or analytics shortcut",
            "## Retention, bounds, and cleanup",
            "14-day sanitized-log default",
            "## Verification contract for later implementation",
            "Automated fixture/schema checks in `TL-0005` validate the design artifacts only",
            "production redactor",
            "## Change and approval rule",
        ),
        validation,
    )
    model_status = security_field(model_text, "Status")

    support_table = markdown_table_after_heading(
        logging_text,
        "### Default fields",
        logging_relative,
        validation,
    )
    if support_table is not None:
        support_header, support_rows = support_table
        if support_header != ("Field", "Constraint"):
            validation.error(
                f"{logging_relative}: support table header must be Field | Constraint"
            )
        support_fields: list[str] = []
        for row in support_rows:
            field_match = re.fullmatch(r"`([a-z0-9_]+)`", row[0])
            if field_match is None:
                validation.error(
                    f"{logging_relative}: support field names must be backticked snake_case"
                )
                continue
            support_fields.append(field_match.group(1))
            if not row[1]:
                validation.error(
                    f"{logging_relative}: support field {row[0]} needs a constraint"
                )
        if tuple(support_fields) != SUPPORT_EXPORT_ALLOWLIST:
            validation.error(
                f"{logging_relative}: support table fields must exactly equal "
                f"{list(SUPPORT_EXPORT_ALLOWLIST)!r}"
            )
        expected_support_rows = tuple(
            (f"`{field}`", constraint)
            for field, constraint in SUPPORT_EXPORT_TABLE_ROWS
        )
        if support_rows != expected_support_rows:
            validation.error(
                f"{logging_relative}: support table rows and constraints must "
                "exactly match the governed TL-0005 contract"
            )

    retention_table = markdown_table_after_heading(
        model_text,
        "## Proposed default retention guidance",
        model_relative,
        validation,
    )
    if retention_table is not None:
        retention_header, retention_rows = retention_table
        expected_header = (
            "Data",
            "Proposed default",
            "Start/cleanup rule",
            "Required implementation behavior",
        )
        if retention_header != expected_header:
            validation.error(
                f"{model_relative}: retention table header must exactly match the governed contract"
            )
        expected_retention_rows = PRIVACY_RETENTION_ROWS
        if model_status == PRIVACY_APPROVED_STATUS:
            expected_retention_rows = tuple(
                tuple(
                    cell.replace("this pending default", "this reviewed default")
                    for cell in row
                )
                for row in PRIVACY_RETENTION_ROWS
            )
        if retention_rows != expected_retention_rows:
            validation.error(
                f"{model_relative}: retention rows and values must exactly match the governed TL-0005 contract"
            )

    model_revision = security_field(model_text, "Model revision")
    logging_revision = security_field(logging_text, "Standard revision")
    if not model_revision or model_revision != logging_revision:
        validation.error(
            "Privacy documents must share one exact TL-0005 revision"
        )
    logging_status = security_field(logging_text, "Status")
    approval_result = security_field(model_text, "Approval result")
    approval_fields = {
        field: security_field(model_text, field)
        for field in (
            "Current privacy-owner",
            "Current privacy-owner role",
            "Current review date",
            "Reviewed commit/reference",
            "Approval scope",
            "Conditions/residual risks",
            "Current result",
        )
    }
    for field, value in approval_fields.items():
        if not value:
            validation.error(
                f"{model_relative}: missing approval metadata {field!r}"
            )

    for relative, text in (
        (model_relative, model_text),
        (logging_relative, logging_text),
    ):
        if SECURITY_MACHINE_PATH_RE.search(text):
            validation.error(f"{relative}: contains a machine-specific path")

    combined_privacy_text = "\n".join((model_text, logging_text))
    unknown_decisions = sorted(
        set(re.findall(r"\bD-\d{3}\b", combined_privacy_text)) - decision_ids
    )
    if unknown_decisions:
        validation.error(
            f"Privacy documents reference unknown decisions {unknown_decisions!r}"
        )
    unknown_tasks = sorted(
        set(re.findall(r"\bTL-\d{4}\b", combined_privacy_text))
        - set(task_by_id)
    )
    if unknown_tasks:
        validation.error(
            f"Privacy documents reference unknown roadmap tasks {unknown_tasks!r}"
        )

    fixture = load_unique_key_yaml(ROOT / fixture_relative, validation)
    if not fixture:
        return
    reject_unknown_mapping_fields(
        fixture_relative,
        fixture,
        {
            "schema_version",
            "fixture_set_id",
            "synthetic_data",
            "description",
            "policy",
            "review",
            "support_export_allowlist",
            "transform_invariants",
            "schema",
            "cases",
        },
        validation,
    )
    if fixture.get("schema_version") != REDACTION_FIXTURE_SCHEMA_VERSION:
        validation.error(
            f"{fixture_relative}: schema_version must equal "
            f"{REDACTION_FIXTURE_SCHEMA_VERSION!r}"
        )
    if fixture.get("fixture_set_id") != "TL-0005":
        validation.error(f"{fixture_relative}: fixture_set_id must equal 'TL-0005'")
    if fixture.get("synthetic_data") is not True:
        validation.error(f"{fixture_relative}: synthetic_data must be true")
    require_nonempty_string(fixture_relative, fixture, "description", validation)

    policy = fixture.get("policy")
    if not isinstance(policy, dict):
        validation.error(f"{fixture_relative}: policy must be a mapping")
        policy = {}
    policy_contract: dict[str, Any] = {
        "telemetry_default": "off",
        "redaction_stage": "before_persistence",
        "support_export_mode": "allowlist_and_preview_required",
        "unknown_field_action": "omit",
        "raw_output_action": "reject_raw_and_extract_allowlisted_fields",
        "recipient_identity_required": False,
        "full_serial_default_support_action": "omit",
    }
    for field, expected in policy_contract.items():
        if policy.get(field) != expected:
            validation.error(
                f"{fixture_relative}: policy.{field} must equal {expected!r}"
            )
    reject_unknown_mapping_fields(
        f"{fixture_relative}: policy",
        policy,
        set(policy_contract),
        validation,
    )

    transform_invariants = fixture.get("transform_invariants")
    if not isinstance(transform_invariants, dict):
        validation.error(
            f"{fixture_relative}: transform_invariants must be a mapping"
        )
        transform_invariants = {}
    boolean_invariants = (
        "deterministic",
        "idempotent",
        "diagnostic_sensitive_seeds_absent_after_transform",
        "unknown_fields_omitted",
        "raw_output_replaced_by_typed_projection_only",
        "telemetry_emission_disabled",
        "synthetic_inputs_reserved_for_tests_only",
    )
    for field in boolean_invariants:
        if transform_invariants.get(field) is not True:
            validation.error(
                f"{fixture_relative}: transform_invariants.{field} must be true"
            )
    if transform_invariants.get("workshop_only_exception_fields") != [
        "full_serial_number"
    ]:
        validation.error(
            f"{fixture_relative}: the only workshop-only seed exception must be full_serial_number"
        )
    reject_unknown_mapping_fields(
        f"{fixture_relative}: transform_invariants",
        transform_invariants,
        {*boolean_invariants, "workshop_only_exception_fields"},
        validation,
    )

    allowlist = fixture.get("support_export_allowlist")
    allowlist_is_string_list = isinstance(allowlist, list) and all(
        isinstance(field, str) and bool(field.strip()) for field in allowlist
    )
    if not allowlist_is_string_list:
        validation.error(
            f"{fixture_relative}: support_export_allowlist must contain only non-empty strings"
        )
    if not allowlist_is_string_list or tuple(allowlist) != SUPPORT_EXPORT_ALLOWLIST:
        validation.error(
            f"{fixture_relative}: support_export_allowlist must exactly equal "
            f"{list(SUPPORT_EXPORT_ALLOWLIST)!r}"
        )
        allowlist_set = set(SUPPORT_EXPORT_ALLOWLIST)
    else:
        allowlist_set = set(allowlist)
    if allowlist_is_string_list and len(allowlist) != len(set(allowlist)):
        validation.error(
            f"{fixture_relative}: support_export_allowlist values must be unique"
        )

    schema = fixture.get("schema")
    if not isinstance(schema, dict):
        validation.error(f"{fixture_relative}: schema must be a mapping")
        schema = {}
    if schema.get("case_id_pattern") != r"^RDX-[0-9]{3}$":
        validation.error(
            f"{fixture_relative}: schema.case_id_pattern must remain '^RDX-[0-9]{{3}}$'"
        )
    if schema.get("contiguous_ids_required") is not True:
        validation.error(
            f"{fixture_relative}: schema.contiguous_ids_required must be true"
        )
    schema_lists = {
        "required_case_fields": (
            "id",
            "title",
            "classification",
            "input",
            "expected",
            "support_export",
            "rationale",
        ),
        "classification_required_fields": ("field", "context"),
        "input_required_fields": ("field", "value", "synthetic"),
        "expected_required_fields": ("action", "redacted_form", "persistence"),
        "support_export_required_fields": ("outcome", "exported_form"),
        "field_classification_values": REDACTION_CLASSIFICATIONS,
        "context_classification_values": REDACTION_CONTEXTS,
        "expected_action_values": REDACTION_ACTIONS,
        "persistence_values": REDACTION_PERSISTENCE_VALUES,
        "support_export_outcome_values": SUPPORT_EXPORT_OUTCOMES,
    }
    for field, expected in schema_lists.items():
        value = schema.get(field)
        if not isinstance(value, list) or tuple(value) != expected:
            validation.error(
                f"{fixture_relative}: schema.{field} must exactly equal {list(expected)!r}"
            )
    require_nonempty_string(
        f"{fixture_relative}: schema",
        schema,
        "redacted_form_semantics",
        validation,
    )
    reject_unknown_mapping_fields(
        f"{fixture_relative}: schema",
        schema,
        {
            "case_id_pattern",
            "contiguous_ids_required",
            *schema_lists,
            "redacted_form_semantics",
        },
        validation,
    )

    review = fixture.get("review")
    if not isinstance(review, dict):
        validation.error(f"{fixture_relative}: review must be a mapping")
        review = {}
    review_fields = (
        "privacy_owner",
        "owner_role",
        "status",
        "result",
        "approved_at_utc",
        "approval_reference",
        "approval_scope",
        "required_approval_scope",
        "note",
    )
    for field in review_fields:
        if field not in review:
            validation.error(f"{fixture_relative}: review is missing {field!r}")
    reject_unknown_mapping_fields(
        f"{fixture_relative}: review",
        review,
        set(review_fields),
        validation,
    )
    required_scope = review.get("required_approval_scope")
    if (
        not isinstance(required_scope, list)
        or tuple(required_scope) != REDACTION_REQUIRED_APPROVAL_SCOPE
    ):
        validation.error(
            f"{fixture_relative}: review.required_approval_scope must exactly equal "
            f"{list(REDACTION_REQUIRED_APPROVAL_SCOPE)!r}"
        )
    require_nonempty_string(
        f"{fixture_relative}: review", review, "note", validation
    )

    review_status = review.get("status")
    reviewed_commit = ""
    if review_status == "pending":
        pending_fields = (
            "privacy_owner",
            "owner_role",
            "result",
            "approved_at_utc",
            "approval_reference",
            "approval_scope",
        )
        if any(review.get(field) is not None for field in pending_fields):
            validation.error(
                f"{fixture_relative}: pending review metadata must remain null"
            )
        if "named privacy owner" not in str(review.get("note", "")).casefold():
            validation.error(
                f"{fixture_relative}: pending review must name the required human approval"
            )
        if (
            model_status != PRIVACY_PENDING_STATUS
            or logging_status != PRIVACY_PENDING_STATUS
            or privacy_approval_state(approval_result) != "pending"
            or privacy_approval_state(approval_fields["Current result"]) != "pending"
        ):
            validation.error(
                "Pending privacy review requires Pending status in both Markdown contracts"
            )
        if any(
            not value.casefold().startswith("pending")
            for value in approval_fields.values()
            if value
        ):
            validation.error(
                f"{model_relative}: pending approval metadata must remain Pending"
            )
    elif review_status == "approved":
        owner = review.get("privacy_owner")
        role = review.get("owner_role")
        placeholders = {"pending", "privacy owner", "tbd", "unknown", "reviewer"}
        if (
            not isinstance(owner, str)
            or len(owner.strip()) < 3
            or owner.strip().casefold() in placeholders
        ):
            validation.error(
                f"{fixture_relative}: approved review needs a non-placeholder named privacy owner"
            )
        if not isinstance(role, str) or "privacy" not in role.casefold():
            validation.error(
                f"{fixture_relative}: approved review needs the privacy-owner role"
            )
        review_result = review.get("result")
        if not isinstance(review_result, str) or review_result not in (
            "approved",
            "approved_with_conditions",
        ):
            validation.error(
                f"{fixture_relative}: approved review result must be approved or approved_with_conditions"
            )
        if re.search(
            r"^\s*(?:the\s+)?(?:named\s+)?privacy[- ]owner approved\b",
            str(review.get("note", "")),
            re.IGNORECASE,
        ) is None:
            validation.error(
                f"{fixture_relative}: approved review note must record the human approval"
            )
        approved_at = review.get("approved_at_utc")
        approved_date = ""
        if isinstance(approved_at, str) and approved_at.endswith("Z"):
            try:
                approved_date = datetime.fromisoformat(
                    approved_at[:-1] + "+00:00"
                ).date().isoformat()
            except ValueError:
                approved_date = ""
        if not approved_date:
            validation.error(
                f"{fixture_relative}: approved review needs a real UTC approval timestamp"
            )
        reference = review.get("approval_reference")
        commit_match = re.fullmatch(
            r"reviewed commit ([0-9a-f]{40})",
            reference if isinstance(reference, str) else "",
        )
        if commit_match is None:
            validation.error(
                f"{fixture_relative}: approved review needs exact 'reviewed commit <40-lowercase-hex>' metadata"
            )
        else:
            reviewed_commit = commit_match.group(1)
        approved_scope = review.get("approval_scope")
        if (
            not isinstance(approved_scope, list)
            or tuple(approved_scope) != REDACTION_REQUIRED_APPROVAL_SCOPE
        ):
            validation.error(
                f"{fixture_relative}: approved review must cover every required approval scope"
            )
        expected_markdown_result = (
            review_result.replace("_", " ")
            if isinstance(review_result, str)
            else ""
        )
        if (
            model_status != PRIVACY_APPROVED_STATUS
            or logging_status != PRIVACY_APPROVED_STATUS
            or privacy_approval_state(approval_result) != expected_markdown_result
            or privacy_approval_state(approval_fields["Current result"])
            != expected_markdown_result
        ):
            validation.error(
                "Approved privacy review requires Approved status in both Markdown contracts"
            )
        if isinstance(owner, str) and approval_fields[
            "Current privacy-owner"
        ].strip() != owner.strip():
            validation.error(
                f"{model_relative}: approved owner must match fixture review metadata"
            )
        if isinstance(role, str) and approval_fields[
            "Current privacy-owner role"
        ].strip() != role.strip():
            validation.error(
                f"{model_relative}: approved owner role must match fixture review metadata"
            )
        if approved_date and approval_fields["Current review date"].strip() != approved_date:
            validation.error(
                f"{model_relative}: approved date must match fixture review metadata"
            )
        if isinstance(reference, str) and approval_fields[
            "Reviewed commit/reference"
        ].strip() != reference:
            validation.error(
                f"{model_relative}: reviewed commit must match fixture review metadata"
            )
        approval_scope_text = approval_fields["Approval scope"].casefold()
        for required_term in ("classification", "retention", "redaction", "support"):
            if required_term not in approval_scope_text:
                validation.error(
                    f"{model_relative}: approval scope must include {required_term} review"
                )
        pending_claims = (
            "named privacy-owner approval pending",
            "does not satisfy the human evidence required by `TL-0005`",
            "deliberately marked **not approved**",
            "human privacy approval exists",
            "it is currently pending",
        )
        stale_claims = [
            claim
            for claim in pending_claims
            if claim.casefold() in combined_privacy_text.casefold()
        ]
        if stale_claims:
            validation.error(
                "Approved privacy documents cannot retain pending/no-approval claims"
            )
        if reviewed_commit:
            validate_privacy_approval_commit(reviewed_commit, validation)
    else:
        validation.error(
            f"{fixture_relative}: review.status must be 'pending' or 'approved'"
        )

    cases = fixture.get("cases")
    if not isinstance(cases, list) or not cases:
        validation.error(f"{fixture_relative}: cases must be a non-empty list")
        cases = []
    if len(cases) != len(REDACTION_CASE_IDS):
        validation.error(
            f"{fixture_relative}: cases must contain exactly {len(REDACTION_CASE_IDS)} entries"
        )
    bounded_cases = cases[: len(REDACTION_CASE_IDS)]

    case_ids: list[str] = []
    preserve_fields: list[str] = []
    support_digest_values: dict[str, Any] = {}
    sensitive_seeds: list[tuple[str, str]] = []
    workshop_seed_paths: set[tuple[str | int, ...]] = set()
    observed_actions: set[str] = set()
    observed_contexts: set[str] = set()
    observed_classifications: set[str] = set()
    expected_persistence = {
        "redact": "redacted_value_only",
        "omit": "none_in_support_export",
        "reject_and_do_not_persist": "none",
        "preserve_workshop_only": "workshop_record_only",
        "reject_raw_and_extract_allowlisted_fields": "structured_projection_only",
        "reject_out_of_scope": "none",
        "preserve_allowlisted": "structured_value_only",
        "suppress_telemetry": "none_for_telemetry",
    }
    sentinel_prefix = {
        "redact": "REDACTED",
        "omit": "OMITTED",
        "reject_and_do_not_persist": "REDACTED",
        "reject_raw_and_extract_allowlisted_fields": "OMITTED",
        "reject_out_of_scope": "REJECTED",
        "suppress_telemetry": "NOT-EMITTED",
    }
    non_sensitive_classes = {
        "operational_metadata",
        "pseudonymous_operational_identifier",
    }

    for index, case in enumerate(bounded_cases):
        owner = f"{fixture_relative}: cases[{index}]"
        if not isinstance(case, dict):
            validation.error(f"{owner} must be a mapping")
            continue
        for field in schema_lists["required_case_fields"]:
            if field not in case:
                validation.error(f"{owner} is missing {field!r}")
        reject_unknown_mapping_fields(
            owner,
            case,
            set(schema_lists["required_case_fields"]),
            validation,
        )
        case_id = case.get("id")
        if not isinstance(case_id, str) or REDACTION_CASE_ID_RE.fullmatch(case_id) is None:
            validation.error(f"{owner}.id must match RDX-NNN")
            case_id = f"cases[{index}]"
        else:
            case_ids.append(case_id)
            owner = f"{fixture_relative}: {case_id}"
        require_nonempty_string(owner, case, "title", validation)
        require_nonempty_string(owner, case, "rationale", validation)

        classification = case.get("classification")
        if not isinstance(classification, dict):
            validation.error(f"{owner}.classification must be a mapping")
            classification = {}
        reject_unknown_mapping_fields(
            f"{owner}.classification",
            classification,
            set(schema_lists["classification_required_fields"]),
            validation,
        )
        for field in ("field", "context"):
            require_nonempty_string(
                f"{owner}.classification", classification, field, validation
            )
        field_class = classification.get("field")
        context = classification.get("context")
        if field_class not in REDACTION_CLASSIFICATIONS:
            validation.error(
                f"{owner}.classification.field has unapproved value"
            )
        else:
            observed_classifications.add(field_class)
        if context not in REDACTION_CONTEXTS:
            validation.error(
                f"{owner}.classification.context has unapproved value"
            )
        else:
            observed_contexts.add(context)

        input_value = case.get("input")
        if not isinstance(input_value, dict):
            validation.error(f"{owner}.input must be a mapping")
            input_value = {}
        reject_unknown_mapping_fields(
            f"{owner}.input",
            input_value,
            set(schema_lists["input_required_fields"]),
            validation,
        )
        require_nonempty_string(f"{owner}.input", input_value, "field", validation)
        if "value" not in input_value or isinstance(input_value.get("value"), (dict, list)):
            validation.error(f"{owner}.input.value must be a scalar")
        if input_value.get("synthetic") is not True:
            validation.error(f"{owner}.input.synthetic must be true")
        raw_value = input_value.get("value")
        input_field = input_value.get("field")
        if input_field == "retryable":
            if not isinstance(raw_value, bool):
                validation.error(f"{owner}.input.value must be a boolean")
        elif input_field in ("duration_ms", "bounded_count"):
            if (
                not isinstance(raw_value, int)
                or isinstance(raw_value, bool)
                or raw_value < 0
                or raw_value > 86_400_000
            ):
                validation.error(
                    f"{owner}.input.value must be a bounded non-negative integer"
                )
        elif not isinstance(raw_value, str) or not raw_value.strip():
            validation.error(
                f"{owner}.input.value must be a non-empty string for this field"
            )
        if isinstance(raw_value, str):
            high_risk_secret = privacy_high_risk_secret_match(raw_value)
            if high_risk_secret is not None:
                validation.error(
                    f"{owner}.input.value contains prohibited {high_risk_secret}"
                )
        if isinstance(input_field, str) and input_field in {
            "preview_content_digest_sha256",
            "export_content_digest_sha256",
        }:
            support_digest_values[str(input_field)] = raw_value
            if not isinstance(raw_value, str) or re.fullmatch(
                r"[0-9a-f]{64}", raw_value
            ) is None:
                validation.error(
                    f"{owner}.input.value must be a lowercase 64-hex SHA-256 digest"
                )
        is_non_sensitive_class = (
            isinstance(field_class, str) and field_class in non_sensitive_classes
        )
        if not is_non_sensitive_class:
            if not is_explicitly_synthetic_sensitive_value(
                raw_value,
                input_field,
                field_class,
            ):
                validation.error(
                    f"{owner}.input.value is not an explicitly synthetic or reserved test value"
                )
            if isinstance(raw_value, str):
                sensitive_seeds.append((case_id, raw_value))
        elif isinstance(raw_value, str):
            canonical_input = canonical_testing_scan_text(raw_value)
            sensitive_kind = testing_sensitive_match(canonical_input)
            if sensitive_kind or SECURITY_MACHINE_PATH_RE.search(canonical_input) or re.search(
                r"(?i)https?://", canonical_input
            ):
                validation.error(
                    f"{owner}.input.value is sensitive but classified as operational metadata"
                )

        expected = case.get("expected")
        if not isinstance(expected, dict):
            validation.error(f"{owner}.expected must be a mapping")
            expected = {}
        reject_unknown_mapping_fields(
            f"{owner}.expected",
            expected,
            set(schema_lists["expected_required_fields"]),
            validation,
        )
        action = expected.get("action")
        persistence = expected.get("persistence")
        if action not in REDACTION_ACTIONS:
            validation.error(f"{owner}.expected.action has unapproved value")
        else:
            observed_actions.add(action)
            if persistence != expected_persistence[action]:
                validation.error(
                    f"{owner}.expected.persistence must equal "
                    f"{expected_persistence[action]!r} for action {action!r}"
                )
        if "redacted_form" not in expected or expected.get("redacted_form") is None:
            validation.error(f"{owner}.expected.redacted_form must be present")
        redacted_form = expected.get("redacted_form")
        if isinstance(action, str) and action in sentinel_prefix:
            marker = sentinel_prefix[action]
            if not isinstance(redacted_form, str) or re.fullmatch(
                rf"\[{marker}:[a-z0-9-]+\]", redacted_form
            ) is None:
                validation.error(
                    f"{owner}.expected.redacted_form must be an exact {marker} sentinel"
                )
        elif action in ("preserve_allowlisted", "preserve_workshop_only"):
            if redacted_form != raw_value:
                validation.error(
                    f"{owner}.expected.redacted_form must equal the permitted input value"
                )

        support = case.get("support_export")
        if not isinstance(support, dict):
            validation.error(f"{owner}.support_export must be a mapping")
            support = {}
        reject_unknown_mapping_fields(
            f"{owner}.support_export",
            support,
            set(schema_lists["support_export_required_fields"]),
            validation,
        )
        outcome = support.get("outcome")
        if outcome not in SUPPORT_EXPORT_OUTCOMES:
            validation.error(
                f"{owner}.support_export.outcome has unapproved value"
            )
        if "exported_form" not in support:
            validation.error(f"{owner}.support_export.exported_form must be present")
        exported_form = support.get("exported_form")
        if outcome == "include_unchanged_if_allowlisted_and_previewed":
            if not isinstance(input_field, str) or input_field not in allowlist_set:
                validation.error(
                    f"{owner}: included support field is not allowlisted"
                )
            if exported_form != raw_value:
                validation.error(
                    f"{owner}.support_export.exported_form must equal the allowlisted input value"
                )
        elif exported_form is not None:
            validation.error(
                f"{owner}.support_export.exported_form must be null when the field is omitted"
            )

        if action == "preserve_allowlisted":
            preserve_fields.append(str(input_field))
            if not is_non_sensitive_class or context != "support_export":
                validation.error(
                    f"{owner}: preserve_allowlisted requires safe support-export metadata"
                )
            if outcome != "include_unchanged_if_allowlisted_and_previewed":
                validation.error(
                    f"{owner}: preserve_allowlisted requires the reviewed include outcome"
                )
        if action == "preserve_workshop_only":
            if (
                field_class != "device_identifier"
                or context != "workshop_record"
                or input_field != "full_serial_number"
                or exported_form is not None
            ):
                validation.error(
                    f"{owner}: preserve_workshop_only is restricted to the non-exported full serial"
                )
            workshop_seed_paths.add(("cases", index, "expected", "redacted_form"))
        if field_class == "raw_untrusted_output" and action != "reject_raw_and_extract_allowlisted_fields":
            validation.error(f"{owner}: raw untrusted output must use the typed-projection action")
        if field_class == "sibling_private_data" and action != "reject_out_of_scope":
            validation.error(f"{owner}: sibling-private data must be rejected as out of scope")
        if field_class == "unknown_field" and action != "omit":
            validation.error(f"{owner}: unknown fields must be omitted")
        if context == "telemetry" and action != "suppress_telemetry":
            validation.error(f"{owner}: telemetry input must be suppressed")
        if action == "suppress_telemetry" and context != "telemetry":
            validation.error(f"{owner}: suppress_telemetry requires telemetry context")
        if action == "suppress_telemetry" and (
            outcome != "omit" or exported_form is not None
        ):
            validation.error(
                f"{owner}: suppressed telemetry must have no support-export output surface"
            )
        if input_field == "full_serial_number" and exported_form is not None:
            validation.error(f"{owner}: full serial must never have a support exported_form")
        if not is_non_sensitive_class and action != "preserve_workshop_only":
            if exported_form is not None:
                validation.error(f"{owner}: sensitive values must be absent from support export")
            if redacted_form == raw_value:
                validation.error(f"{owner}: sensitive expected output echoes its input")

    if tuple(case_ids) != REDACTION_CASE_IDS:
        validation.error(
            f"{fixture_relative}: case IDs must exactly equal {list(REDACTION_CASE_IDS)!r}"
        )
    if len(case_ids) != len(set(case_ids)):
        validation.error(f"{fixture_relative}: case IDs must be unique")
    if (
        set(preserve_fields) != set(SUPPORT_EXPORT_ALLOWLIST)
        or len(preserve_fields) != len(SUPPORT_EXPORT_ALLOWLIST)
    ):
        validation.error(
            f"{fixture_relative}: preserve_allowlisted cases must cover each support field exactly once"
        )
    preview_digest = support_digest_values.get("preview_content_digest_sha256")
    export_digest = support_digest_values.get("export_content_digest_sha256")
    if preview_digest != export_digest:
        validation.error(
            f"{fixture_relative}: preview and export content digests must be equal"
        )
    if (
        preview_digest != SYNTHETIC_SUPPORT_CONTENT_SHA256
        or export_digest != SYNTHETIC_SUPPORT_CONTENT_SHA256
    ):
        validation.error(
            f"{fixture_relative}: content digests must hash the declared synthetic preview/export bytes"
        )
    if observed_actions != set(REDACTION_ACTIONS):
        validation.error(f"{fixture_relative}: cases must cover every allowed action")
    if observed_contexts != set(REDACTION_CONTEXTS):
        validation.error(f"{fixture_relative}: cases must cover every allowed context")
    if observed_classifications != set(REDACTION_CLASSIFICATIONS):
        validation.error(f"{fixture_relative}: cases must cover every field classification")

    try:
        scalar_paths = privacy_yaml_scalar_paths(fixture)
    except ValueError as exc:
        validation.error(f"{fixture_relative}: unsafe fixture graph: {exc}")
        scalar_paths = []
    input_paths = {
        path
        for path, _ in scalar_paths
        if len(path) >= 2 and path[-2:] == ("input", "value")
    }
    folded_seed_ids: dict[str, str] = {}
    for seed_id, seed in sensitive_seeds:
        folded_seed = canonical_testing_scan_text(seed).casefold()
        if folded_seed:
            folded_seed_ids.setdefault(folded_seed, seed_id)
    seed_pattern = (
        re.compile(
            "|".join(
                re.escape(seed)
                for seed in sorted(folded_seed_ids, key=len, reverse=True)
            )
        )
        if folded_seed_ids
        else None
    )
    for path, value in scalar_paths:
        if path in input_paths or not isinstance(value, str):
            continue
        if path not in workshop_seed_paths:
            canonical_value = canonical_testing_scan_text(value)
            sensitive_kind = testing_sensitive_match(canonical_value)
            if sensitive_kind is not None:
                validation.error(
                    f"{fixture_relative}: prohibited {sensitive_kind} appears outside synthetic input at {privacy_path_text(path)}"
                )
            if SECURITY_MACHINE_PATH_RE.search(canonical_value):
                validation.error(
                    f"{fixture_relative}: prohibited machine path appears outside synthetic input at {privacy_path_text(path)}"
                )
            if re.search(r"(?i)https?://", canonical_value):
                validation.error(
                    f"{fixture_relative}: prohibited URL appears outside synthetic input at {privacy_path_text(path)}"
                )
        if path in workshop_seed_paths or seed_pattern is None:
            continue
        folded_value = canonical_testing_scan_text(value).casefold()
        seed_match = seed_pattern.search(folded_value)
        if seed_match is not None:
            seed_id = folded_seed_ids[seed_match.group(0)]
            validation.error(
                f"{fixture_relative}: sensitive seed from {seed_id} leaks outside synthetic input at {privacy_path_text(path)}"
            )

    privacy_task = task_by_id.get("TL-0005", {})
    evidence = privacy_task.get("evidence", [])
    evidence_claims_approval = isinstance(evidence, list) and any(
        isinstance(entry, dict)
        and re.search(
            r"^\s*(?:named\s+)?privacy[- ]owner approved\b",
            str(entry.get("summary", "")),
            re.IGNORECASE,
        )
        is not None
        and str(entry.get("result", "")).casefold() == "passed"
        for entry in evidence
    )
    if review_status != "approved" and evidence_claims_approval:
        validation.error(
            "TL-0005 evidence cannot claim privacy-owner approval while the review is Pending"
        )
    if privacy_task.get("status") == "done":
        if review_status != "approved":
            validation.error(
                "TL-0005 cannot be done while privacy-owner approval is Pending"
            )
        approval_evidence = (
            bool(reviewed_commit)
            and isinstance(evidence, list)
            and any(
                isinstance(entry, dict)
                and re.search(
                    r"^\s*(?:named\s+)?privacy[- ]owner approved\b",
                    str(entry.get("summary", "")),
                    re.IGNORECASE,
                )
                is not None
                and str(entry.get("result", "")).casefold() == "passed"
                and reviewed_commit in str(entry.get("reference", "")).casefold()
                for entry in evidence
            )
        )
        if not approval_evidence:
            validation.error(
                "TL-0005 done evidence must record named privacy-owner approval for the reviewed commit"
            )


def testing_document_ids(text: str, prefix: str) -> tuple[str, ...]:
    """Return first-column testing IDs, accepting harmless Markdown styling."""

    return tuple(
        re.findall(
            rf"^\|\s*(?:`|\*\*)?({re.escape(prefix)}-[A-Z0-9-]+)(?:`|\*\*)?\s*\|",
            text,
            re.MULTILINE,
        )
    )


def same_machine_constraint_ids(text: str) -> tuple[str, ...]:
    """Return detailed-profile headings, accepting optional backticks."""

    return tuple(
        re.findall(r"^##(?:\s+\d+\.)?\s+`?(SMC-[A-Z0-9-]+)`?\s*$", text, re.MULTILINE)
    )


def has_obsolete_active_hardware_obligation(line: str, heading: str = "") -> bool:
    """Identify a live obligation to obtain external physical test hardware.

    Documents derived from v0.3.0 intentionally discuss the retired lab model in negative,
    historical, search, and supersession contexts. Those references remain valid;
    affirmative requirements do not.
    """

    if any(phrase in line.casefold() for phrase in OBSOLETE_ACTIVE_PHRASES):
        return True
    folded = unicodedata.normalize("NFKC", f"{heading} {line}").casefold()
    folded = re.sub(r"[*_`]+", "", folded)

    hardware = re.search(
        r"\b(?:hardware[- ]lab|device[- ]pool|lab machines?|lab devices?|"
        r"second physical (?:computer|machine|pc)|lower[- ]performance (?:computer|machine|device)|"
        r"external (?:hardware|runtime) matrix|remote runtime (?:runner|matrix)|"
        r"authoritative remote runtime (?:ci|runner)|physical[- ]device matrix)\b",
        folded,
    )
    if hardware is None:
        return False

    safe_context = re.search(
        r"\b(?:obsolete|supersed(?:e|ed|es|ing)|former|historical|retired|"
        r"earlier|previous|would have|requested|"
        r"replace(?:d|s|ment)?|reject(?:ed|s|ion)?|prohibit(?:ed|s|ion)?|"
        r"search(?:ed|es|ing)?|audit(?:ed|s|ing)?|absence|limitation|"
        r"not required|not authoritative|not (?:itself )?a blocker|does not require|"
        r"not (?:a|an|the) (?:hardware[- ]lab|device[- ]pool|physical[- ]device matrix)|"
        r"no [^.\n]{0,120}\b(?:uses?|requires?|depends?)\b|"
        r"do not (?:require|seek|create|assemble|maintain|use|execute|run)|"
        r"must not (?:require|seek|create|assemble|maintain|use|execute|run)|"
        r"no (?:device|hardware|physical|lab|second|external|remote|volunteer)|"
        r"without (?:requiring|using|a)|cannot (?:require|be required))\b",
        folded,
    )
    if safe_context is not None:
        return False

    obligation = re.search(
        r"\b(?:must|required|requires?|shall|need(?:ed)?|mandatory|block(?:er|s|ed|ing)?|"
        r"obtain|acquire|assemble|build|create|select|assign|gather|provide|complete|execute|run)\b",
        folded,
    )
    return obligation is not None


def has_prohibited_hardware_claim(line: str, heading: str = "") -> bool:
    """Reject affirmative hardware claims that exceed same-machine evidence."""

    folded = unicodedata.normalize("NFKC", f"{heading} {line}").casefold()
    prohibited = re.search(
        r"\b(?:certified for (?:low[- ]end|modest) (?:pcs?|hardware)|"
        r"works on all windows 11 devices|hardware independent|"
        r"tested across (?:4|8)\s*gb (?:systems?|devices?)|"
        r"(?:vm|virtual[- ]machine|same[- ]machine|synthetic fixture) evidence proves physical|"
        r"(?:simulation|process limits?|synthetic fixtures?) (?:reproduces?|certifies?) a (?:specific )?real device|"
        r"minimum (?:cpu|ram|memory|storage) (?:is|of)|"
        r"(?:procedure|run|result|thirdlife) (?:certifies|guarantees)\b)",
        folded,
    )
    if prohibited is None:
        return False
    return re.search(
        r"\b(?:not permitted|may not|must not|do not|does not|cannot|never|"
        r"no document|absence|unverified|not a claim|not certified|without separate evidence)\b",
        folded,
    ) is None


def validate_task_test_tier(
    task_id: str,
    task: dict[str, Any],
    validation: Validation,
) -> None:
    tier = task.get("expected_test_tier")
    if tier not in ALLOWED_TEST_TIER:
        validation.error(f"{task_id}: invalid expected_test_tier {tier!r}")

    for field in ("full_test_triggers", "extended_test_triggers"):
        require_nonempty_string_list(
            task_id,
            task,
            field,
            validation,
            allow_empty=True,
        )

    if tier == "extended" and not task.get("extended_test_triggers"):
        validation.error(
            f"{task_id}: extended tier requires at least one extended_test_triggers item"
        )
    if (
        tier == "full"
        and task.get("kind") in {"gate", "release"}
        and not task.get("full_test_triggers")
    ):
        validation.error(
            f"{task_id}: full gate/release task requires full_test_triggers"
        )


def validate_tl0008_contract(
    validation: Validation,
    task_by_id: dict[str, dict[str, Any]],
) -> None:
    task = task_by_id.get("TL-0008")
    if not isinstance(task, dict):
        validation.error("TASKS.yaml: missing TL-0008")
        return

    expected_fields = {
        "title": "Define the same-machine validation system and manual-test specification",
        "executor": "codex",
        "environment": "windows",
        "expected_test_tier": "quick",
    }
    for field, expected in expected_fields.items():
        if task.get(field) != expected:
            validation.error(f"TL-0008: {field} must equal {expected!r}")

    if "human_evidence_required" in task:
        validation.error(
            "TL-0008: revised contract must not require physical-device or walkthrough evidence"
        )
    if task.get("full_test_triggers") != []:
        validation.error("TL-0008: full_test_triggers must be empty")
    if task.get("extended_test_triggers") != []:
        validation.error("TL-0008: extended_test_triggers must be empty")

    required_decisions = {f"D-{index:03d}" for index in range(58, 67)}
    missing_decisions = sorted(required_decisions - set(task.get("decision_refs", [])))
    if missing_decisions:
        validation.error(
            f"TL-0008: missing v0.3.0 decision references {missing_decisions}"
        )

    required_deliverables = {
        "TESTING.md",
        "DEVELOPMENT_WORKFLOW.md",
        "STATUS.md",
        "docs/testing/reference-machine-profile.md",
        "docs/testing/capability-risk-matrix.md",
        "docs/testing/same-machine-constraints.md",
        "docs/testing/manual-hardware-tests.md",
        "docs/testing/failure-injection.md",
        "docs/testing/accessibility-matrix.md",
        "LOW_SPEC.md benchmark and claim-boundary procedure",
        "docs/history/TL-0008-draft-1-superseded.md or an equivalent preserved supersession record",
    }
    deliverables = set(task.get("deliverables", []))
    missing_deliverables = sorted(required_deliverables - deliverables)
    if missing_deliverables:
        validation.error(
            f"TL-0008: missing revised deliverables {missing_deliverables}"
        )
    if "docs/testing/device-matrix.md" in deliverables:
        validation.error(
            "TL-0008: obsolete docs/testing/device-matrix.md must not remain a deliverable"
        )

    task_contract = "\n".join(
        str(value)
        for field in ("objective", "acceptance_criteria", "verification")
        for value in (
            task.get(field, []) if isinstance(task.get(field), list) else [task.get(field, "")]
        )
    ).casefold()
    for marker in (
        "active codex machine",
        "quick, targeted, full, and extended",
        "mht-001–mht-021",
        "cannot claim broad hardware certification",
        "superseded procedure",
        "reference-machine profile",
    ):
        if marker not in task_contract:
            validation.error(f"TL-0008: revised contract is missing {marker!r}")


def validate_no_obsolete_hardware_obligations(
    validation: Validation,
    documents: dict[str, str],
) -> None:
    for relative, body in documents.items():
        current_heading = ""
        for line_number, line in enumerate(body.splitlines(), start=1):
            if re.match(r"^#{1,6}\s+", line):
                current_heading = line
            if has_obsolete_active_hardware_obligation(line, current_heading):
                validation.error(
                    f"{relative}:{line_number}: contains an obsolete active hardware-lab obligation"
                )


def validate_m0_sandbox_scripts(validation: Validation) -> None:
    """Keep the approved TL-0010 hosted rerun bounded and fail closed."""

    host_text = require_phrases(
        M0_SANDBOX_HOST_PATH,
        (
            M0_SANDBOX_CANDIDATE,
            M0_SANDBOX_GATE_DIGEST,
            f'$ResultSchemaVersion = {M0_SANDBOX_RESULT_SCHEMA_VERSION}',
            '$SandboxTimeoutMinutes = 30',
            '$RemoteVerificationTimeoutSeconds = 30',
            '$RemoteVerificationUrl = "https://github.com/PikkuJanne/ThirdLife.git"',
            '$SourceBranch = "codex/tl-0010-m0-foundation-gate"',
            '$HostedConstraintProfile = "TL0010-WSB-2026-08-27.1"',
            '$SandboxMemoryMb = 8192',
            '[switch] $PreflightOnly',
            'ls-files --error-unmatch',
            '"symbolic-ref", "--short", "HEAD"',
            'if ($upstream -ne "origin/$SourceBranch")',
            'rev-list", "--left-right", "--count"',
            'Get-BoundedRemoteBranchHash',
            '$process.WaitForExit($TimeoutSeconds * 1000)',
            '$startInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0"',
            'if ($remoteBranchHash -ne $harnessCommit)',
            '"clone", "--local", "--no-hardlinks", "--no-checkout"',
            'status --porcelain=v1 --untracked-files=all',
            'Get-FileHash -LiteralPath $stagedGatePath -Algorithm SHA256',
            '<VGpu>Disable</VGpu>',
            '<Networking>Enable</Networking>',
            '<ProtectedClient>Enable</ProtectedClient>',
            '<ClipboardRedirection>Disable</ClipboardRedirection>',
            '<AudioInput>Disable</AudioInput>',
            '<VideoInput>Disable</VideoInput>',
            '<PrinterRedirection>Disable</PrinterRedirection>',
            '<SandboxFolder>C:\\TL0010\\Input\\Source</SandboxFolder>',
            '<SandboxFolder>C:\\TL0010\\Output</SandboxFolder>',
            '<ReadOnly>false</ReadOnly>',
            'Read-BoundedResult -Path $pendingPath -Phase "Guest"',
            'Assert-GuestOutputDirectory -Path $resultDirectory',
            'The guest result directory contains unexpected output.',
            'Assert-FinalOutputDirectory -Path $resultDirectory',
            '$liveBindings = [ordered] @{',
            'launcher_sha256 = $launcherDigest',
            'cannot use an all-zero placeholder.',
            'Assert-VerifiedStagingPath',
            '"WindowsSandboxRemoteSession"',
            '"WindowsSandboxServer"',
            'Refusing cleanup outside the operating-system temporary directory.',
            'Refusing recursive cleanup because verified staging contains a reparse point.',
            'One Windows Sandbox session on the active physical Codex machine; '
            'no cross-hardware certification or host-compatibility claim.',
        ),
        validation,
    )
    guest_text = require_phrases(
        M0_SANDBOX_GUEST_PATH,
        (
            M0_SANDBOX_CANDIDATE,
            M0_SANDBOX_GATE_DIGEST,
            f'$ResultSchemaVersion = {M0_SANDBOX_RESULT_SCHEMA_VERSION}',
            '$ResultLimitBytes = 16384',
            'CiTool.exe" -lp -json',
            'VerifiedAndReputablePolicyState',
            '$sacDocument = Get-ItemProperty `\n'
            '                -LiteralPath $sacRegistryPath `\n'
            '                -ErrorAction Stop',
            'registry_and_system_policy_files',
            'System32\\CodeIntegrity\\CiPolicies\\Active',
            '$policyRootDirectory = Join-Path $env:WINDIR "System32\\CodeIntegrity"',
            'Where-Object { $_.Extension -ieq ".p7b" }',
            'EFI-resident policy enumeration is not claimed.',
            'VerifiedAndReputableDesktop',
            'VerifiedAndReputableDesktopEvaluation',
            '$sandboxIdentityVerified = $env:USERNAME -eq "WDAGUtilityAccount"',
            '$sandboxMappedInvocation = [StringComparer]::OrdinalIgnoreCase.Equals(',
            'if (-not $sandboxIdentityVerified)',
            'if (-not $sandboxMappedInvocation)',
            'if ($sandboxMappedInvocation -and $sandboxIdentityVerified)',
            'if ($beforeObservation.query -ne "succeeded")',
            'code_integrity_policy_fingerprint_before',
            'code_integrity_observation_method_before',
            '$policiesProperty = $document.PSObject.Properties["Policies"]',
            '$policyIdProperty.Value -isnot [string]',
            '$friendlyNameProperty.Value -isnot [string]',
            '$isEnforcedProperty.Value -isnot [bool]',
            '$_.IsEnforced -eq $true',
            'candidate_unchanged_after',
            'gate_record_unchanged_after',
            'full_last_completed_stage',
            'not_run_reason',
            '[DateTime]::UtcNow.ToString("o")',
            'Invoke-GovernedTier -Tier "Quick"',
            'if ($result.quick_result -eq "passed")',
            'Invoke-GovernedTier -Tier "Full"',
            'security_mutation_attempted = $false',
            'Move-Item -LiteralPath $temporaryPath -Destination $Path -Force',
            'Write-Utf8Atomic -Path $completionMarkerPath',
            'The disposable Sandbox will close in 15 seconds.',
            'raw guest logs will be discarded.',
            'One Windows Sandbox session on the active physical Codex machine; '
            'no cross-hardware certification or host-compatibility claim.',
        ),
        validation,
    )
    if not host_text or not guest_text:
        return

    read_only_count = host_text.count("<ReadOnly>true</ReadOnly>")
    writable_count = host_text.count("<ReadOnly>false</ReadOnly>")
    if read_only_count != 6 or writable_count != 1:
        validation.error(
            f"{M0_SANDBOX_HOST_PATH}: expected six explicit read-only mappings "
            f"and one writable result mapping; found {read_only_count} and {writable_count}"
        )
    output_mapping = re.search(
        r"<MappedFolder><HostFolder>\$resultXml</HostFolder>"
        r"<SandboxFolder>C:\\TL0010\\Output</SandboxFolder>"
        r"<ReadOnly>false</ReadOnly></MappedFolder>",
        host_text,
    )
    if output_mapping is None:
        validation.error(
            f"{M0_SANDBOX_HOST_PATH}: the sole writable mapping must be the "
            "dedicated result folder"
        )

    close_index = host_text.find('$closeDeadline = [DateTimeOffset]::UtcNow.AddMinutes(5)')
    result_read_index = host_text.find('Read-BoundedResult -Path $pendingPath -Phase "Guest"')
    if not (0 <= close_index < result_read_index):
        validation.error(
            f"{M0_SANDBOX_HOST_PATH}: the Sandbox must close before its result "
            "mapping is validated"
        )

    identity_guard_index = guest_text.find('if (-not $sandboxIdentityVerified)')
    result_initialization_index = guest_text.find('$result = [ordered] @{')
    guest_try_index = guest_text.find('try {', result_initialization_index)
    mapped_guard_index = guest_text.find('if (-not $sandboxMappedInvocation)')
    if not (
        0 <= identity_guard_index < result_initialization_index
        < guest_try_index < mapped_guard_index
    ):
        validation.error(
            f"{M0_SANDBOX_GUEST_PATH}: physical-host identity must fail before "
            "the guest result or cleanup flow begins"
        )

    marker_write_index = guest_text.find(
        'Write-Utf8Atomic -Path $completionMarkerPath'
    )
    shutdown_finally_index = guest_text.rfind('    finally {')
    shutdown_index = guest_text.find('shutdown.exe', shutdown_finally_index)
    if not (
        0 <= marker_write_index < shutdown_finally_index < shutdown_index
    ):
        validation.error(
            f"{M0_SANDBOX_GUEST_PATH}: disposable shutdown must remain in the "
            "outer bounded-result finally path"
        )

    quick_index = guest_text.find('Invoke-GovernedTier -Tier "Quick"')
    guard_index = guest_text.find('if ($result.quick_result -eq "passed")')
    full_index = guest_text.find('Invoke-GovernedTier -Tier "Full"')
    if not (0 <= quick_index < guard_index < full_index):
        validation.error(
            f"{M0_SANDBOX_GUEST_PATH}: Quick must run before, and gate, Full"
        )

    combined = f"{host_text}\n{guest_text}"
    prohibited_patterns = {
        r"(?im)^\s*Set-ExecutionPolicy\b": "persistent execution-policy mutation",
        r"(?i)\bUnblock-File\b": "file trust bypass",
        r"(?im)(?:\bInvoke-Expression\b|^\s*iex\b)": "dynamic command execution",
        r"(?i)\b(?:Set|Add|Remove)-MpPreference\b": "Defender policy mutation",
        r"(?i)\breg(?:\.exe)?\s+(?:add|delete)\b": "registry mutation",
        r"(?i)\bbcdedit(?:\.exe)?\b|\btestsigning\b": "boot trust mutation",
        r"(?i)CiTool(?:\.exe)?[\"']?\s+-(?:rp|up|r)\b": "Code Integrity policy mutation",
        r"(?i)\bZone\.Identifier\b": "alternate-stream trust bypass",
        r"(?i)--(?:filter|trusted-host|ignore-failed-sources|force-evaluate)\b": "test or provenance bypass",
        r"(?i)-Verb\s+RunAs\b": "automatic elevation",
    }
    for pattern, label in prohibited_patterns.items():
        if re.search(pattern, combined):
            validation.error(
                f"TL-0010 Sandbox harness contains prohibited {label}"
            )

    host_parameter_block = host_text.split(")", 1)[0]
    if re.search(
        r"(?i)(?:candidate|repository|command|url|uri|branch)\w*\s*[,\)]",
        host_parameter_block,
    ):
        validation.error(
            f"{M0_SANDBOX_HOST_PATH}: the human entry point must not accept an "
            "arbitrary candidate, repository, command, URL, or branch"
        )


def validate_m0_foundation_gate(
    validation: Validation,
    task_by_id: dict[str, dict[str, Any]],
) -> None:
    """Validate the durable TL-0010 checklist and its truthful lifecycle state."""

    relative = M0_FOUNDATION_GATE_PATH
    text = require_phrases(
        relative,
        (
            "TL-0010",
            "M0",
            "codex/tl-0010-m0-foundation-gate",
            "REF-CODEX-001",
            "Required test tier:** Full",
            "Extended test tier:** Not triggered",
            "Team B/B1",
            "project vacuum",
            "not a shared application API",
            "xunit.abstractions",
            "remains mutable upstream evidence",
            "Redistribution of the .NET SDK and CPython toolchains remains withheld",
            "NOASSERTION",
            "non-installable",
            "no-artifact",
            "not-shipped",
            "separately withheld for installation and redistribution",
            "Approved one-command Windows Sandbox hosted rerun",
            "powershell.exe -NoProfile -ExecutionPolicy Bypass -File "
            ".\\eng\\run-tl0010-sandbox.ps1",
            "the sole writable host mapping",
            "The host's enforced security remains enabled and is not changed.",
            "It prefers read-only `CiTool` enumeration",
            "Registry/provider access errors make the observation unavailable",
            "does not claim to enumerate EFI-resident policies",
            "test execution, not shipment",
            (
                "No predecessor or M0 acknowledgement grants blanket installation, "
                "redistribution, legal, production, release, or final-product-licence rights."
            ),
            "no cross-hardware certification",
            "no Extended-tier trigger",
        ),
        validation,
    )
    if not text:
        return
    if "<!--" in text or "-->" in text:
        validation.error(f"{relative}: HTML comments are not permitted")
    if re.search(r"(?m)^ {0,3}(?:`{3,}|~{3,})", text):
        validation.error(f"{relative}: fenced code blocks are not permitted")

    visible_text = markdown_visible_text(text)
    task = task_by_id.get("TL-0010")
    if task is None:
        validation.error("TL-0010: M0 gate task is missing")
        return
    task_status = task.get("status")
    expected_record_state = M0_TASK_STATUS_TO_RECORD_STATE.get(str(task_status))
    if expected_record_state is None:
        validation.error(f"TL-0010: cannot map task status {task_status!r} to gate state")
        return

    def exact_metadata_value(label: str) -> str | None:
        values = [
            match.group(1).strip()
            for match in re.finditer(
                rf"(?m)^\*\*{re.escape(label)}:\*\*\s+([^\r\n]+?)\s*$",
                visible_text,
            )
        ]
        if len(values) != 1:
            validation.error(
                f"{relative}: expected exactly one {label!r} metadata value; found {values}"
            )
            return None
        return values[0]

    record_status = exact_metadata_value("Record status")
    record_state = record_status.split(" —", 1)[0] if record_status else None
    if record_state != expected_record_state:
        validation.error(
            f"{relative}: record state {record_state!r} must match "
            f"TL-0010 status {task_status!r} as {expected_record_state!r}"
        )
    decision_value = exact_metadata_value("Decision")
    expected_decision = M0_RECORD_STATE_TO_DECISION[expected_record_state]
    if decision_value != expected_decision:
        validation.error(
            f"{relative}: Decision must be {expected_decision!r} while "
            f"TL-0010 is {task_status!r}"
        )

    predecessor_table = markdown_table_after_heading(
        visible_text,
        "## 2. Predecessor closure",
        relative,
        validation,
    )
    if predecessor_table is not None:
        header, rows = predecessor_table
        expected_header = ("Task", "State", "Durable M0 input", "Gate treatment")
        if header != expected_header:
            validation.error(
                f"{relative}: predecessor header must equal {expected_header!r}"
            )
        row_ids = tuple(row[0].strip("`") for row in rows)
        if row_ids != M0_GATE_PREDECESSORS:
            validation.error(
                f"{relative}: predecessor rows must be exactly "
                f"{M0_GATE_PREDECESSORS!r}; found {row_ids!r}"
            )
        for row in rows:
            predecessor_id = row[0].strip("`")
            predecessor = task_by_id.get(predecessor_id)
            if predecessor is None:
                continue
            evidence = predecessor.get("evidence")
            evidence_count = len(evidence) if isinstance(evidence, list) else 0
            if predecessor.get("status") != "done" or evidence_count == 0:
                validation.error(
                    f"{relative}: predecessor {predecessor_id} must be done with evidence"
                )
            expected_state = f"`done`; {evidence_count} evidence entries"
            if row[1] != expected_state:
                validation.error(
                    f"{relative}: predecessor {predecessor_id} state must equal "
                    f"{expected_state!r}; found {row[1]!r}"
                )

    responsibility_table = markdown_table_after_heading(
        visible_text,
        "## 4. Named responsibility and acknowledgement matrix",
        relative,
        validation,
    )
    responsibility_rows: tuple[tuple[str, ...], ...] = ()
    if responsibility_table is not None:
        header, responsibility_rows = responsibility_table
        expected_header = (
            "M0 input",
            "Governed artifacts",
            "Existing approved owner/evidence",
            "Proposed accountable M0 owner",
            "M0 acknowledgement",
        )
        if header != expected_header:
            validation.error(
                f"{relative}: responsibility header must equal {expected_header!r}"
            )
        inputs = tuple(row[0] for row in responsibility_rows)
        if inputs != M0_RESPONSIBILITY_INPUTS:
            validation.error(
                f"{relative}: responsibility rows must be exactly "
                f"{M0_RESPONSIBILITY_INPUTS!r}; found {inputs!r}"
            )
        placeholder_re = re.compile(
            r"(?i)\b(?:pending|tbd|unknown|unassigned|none|codex)\b"
        )
        for row in responsibility_rows:
            proposed_owner = row[3]
            owner_name = proposed_owner.split("—", 1)[0].strip()
            if (
                placeholder_re.search(proposed_owner)
                or len(owner_name.split()) < 2
                or "—" not in proposed_owner
            ):
                validation.error(
                    f"{relative}: {row[0]!r} must have a named human and role"
                )

    verification_table = markdown_table_after_heading(
        visible_text,
        "## 7. Verification evidence",
        relative,
        validation,
    )
    verification_by_check: dict[str, tuple[str, ...]] = {}
    if verification_table is not None:
        header, rows = verification_table
        expected_header = (
            "Check",
            "Source revision",
            "Environment",
            "Result",
            "Duration / durable reference",
        )
        if header != expected_header:
            validation.error(
                f"{relative}: verification header must equal {expected_header!r}"
            )
        for row in rows:
            check = row[0]
            if check in verification_by_check:
                validation.error(f"{relative}: duplicate verification row {check!r}")
            verification_by_check[check] = row
        for check in M0_REQUIRED_FINAL_CHECKS:
            if check not in verification_by_check:
                validation.error(f"{relative}: missing verification row {check!r}")

    approval_table = markdown_table_after_heading(
        visible_text,
        "### Approval target",
        relative,
        validation,
    )
    approval_by_field: dict[str, str] = {}
    if approval_table is not None:
        header, rows = approval_table
        if header != ("Field", "Value"):
            validation.error(
                f"{relative}: approval-target header must equal ('Field', 'Value')"
            )
        for row in rows:
            if row[0] in approval_by_field:
                validation.error(f"{relative}: duplicate approval field {row[0]!r}")
            approval_by_field[row[0]] = row[1]
        expected_approval_fields = (
            "Verification candidate commit",
            "Gate-record candidate SHA-256",
            "Full-tier result",
            "Clean-checkout Quick result",
            "Project-owner decision",
            "Security-owner M0 acknowledgement",
            "Privacy-owner M0 acknowledgement",
            "Dependency/licence-owner M0 acknowledgement",
        )
        if tuple(approval_by_field) != expected_approval_fields:
            validation.error(
                f"{relative}: approval fields must be exactly "
                f"{expected_approval_fields!r}; found {tuple(approval_by_field)!r}"
            )

    try:
        matrix_bytes = (ROOT / "docs/supply-chain/license-matrix.csv").read_bytes()
    except OSError as exc:
        validation.error(
            f"docs/supply-chain/license-matrix.csv: cannot read for M0 binding: {exc}"
        )
    else:
        matrix_digest = hashlib.sha256(matrix_bytes).hexdigest()
        if matrix_digest not in visible_text.casefold():
            validation.error(
                f"{relative}: current licence-matrix SHA-256 {matrix_digest} is missing"
            )
    dependency_text = require_phrases(
        "docs/supply-chain/dependencies.md",
        (),
        validation,
    )
    reviewed_commit_match = re.search(
        r"(?m)^\| Reviewed commit \|\s*`?([0-9a-f]{40})`?\s*\|\s*$",
        dependency_text,
    )
    if reviewed_commit_match is None:
        validation.error(
            "docs/supply-chain/dependencies.md: missing exact reviewed commit row"
        )
    elif reviewed_commit_match.group(1) not in visible_text:
        validation.error(
            f"{relative}: current supply-chain reviewed commit is missing"
        )

    contradiction_patterns = (
        r"(?i)blanket (?:installation or )?redistribution rights? (?:are|is) "
        r"(?:granted|approved|allowed|authorized)",
        r"(?i)(?:\.NET SDK|CPython).{0,80}redistribution.{0,30}"
        r"(?:is|are) (?:approved|allowed|granted|authorized)",
        r"(?i)xunit\.abstractions.{0,80}(?:limitation|evidence).{0,30}"
        r"(?:removed|resolved|waived)",
    )
    for pattern in contradiction_patterns:
        if re.search(pattern, visible_text):
            validation.error(
                f"{relative}: contains an affirmative contradiction to a binding "
                "licence or redistribution limitation"
            )

    if expected_record_state in {"Review", "Approved"}:
        for check in M0_REQUIRED_FINAL_CHECKS:
            row = verification_by_check.get(check)
            if row is not None and re.match(r"(?i)^pass(?:\b|:)", row[3]) is None:
                validation.error(
                    f"{relative}: {check!r} must begin with Pass in "
                    f"{expected_record_state} state"
                )
        for field in ("Full-tier result", "Clean-checkout Quick result"):
            value = approval_by_field.get(field, "")
            if re.match(r"(?i)^pass(?:\b|:)", value) is None:
                validation.error(
                    f"{relative}: approval field {field!r} must begin with Pass in "
                    f"{expected_record_state} state"
                )

    if expected_record_state == "Blocked":
        unresolved_blockers = exact_metadata_value("Unresolved blockers")
        if unresolved_blockers is not None and unresolved_blockers.casefold() in {
            "none",
            "pending",
        }:
            validation.error(
                f"{relative}: Blocked state must name a concrete unresolved blocker"
            )

    if expected_record_state != "Approved":
        return

    candidate_commit = approval_by_field.get("Verification candidate commit", "")
    if re.fullmatch(r"[0-9a-f]{40}", candidate_commit) is None:
        validation.error(
            f"{relative}: approved candidate commit must be a lowercase 40-hex Git ID"
        )
    candidate_digest = approval_by_field.get("Gate-record candidate SHA-256", "")
    if re.fullmatch(r"[0-9a-f]{64}", candidate_digest) is None:
        validation.error(
            f"{relative}: approved gate-record candidate digest must be lowercase SHA-256"
        )
    candidate_blob: bytes | None = None
    if re.fullmatch(r"[0-9a-f]{40}", candidate_commit) is not None:
        try:
            commit_check = subprocess.run(
                [
                    "git",
                    "-C",
                    str(ROOT),
                    "cat-file",
                    "-e",
                    f"{candidate_commit}^{{commit}}",
                ],
                check=False,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                timeout=5,
            )
        except (OSError, subprocess.TimeoutExpired):
            commit_check = None
        if commit_check is None or commit_check.returncode != 0:
            validation.error(
                f"{relative}: approved candidate must name an existing Git commit"
            )
        else:
            object_name = f"{candidate_commit}:{M0_FOUNDATION_GATE_PATH}"
            try:
                size_result = subprocess.run(
                    ["git", "-C", str(ROOT), "cat-file", "-s", object_name],
                    check=False,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.DEVNULL,
                    timeout=5,
                )
            except (OSError, subprocess.TimeoutExpired):
                size_result = None
            try:
                candidate_size = (
                    int(size_result.stdout.decode("ascii").strip())
                    if size_result is not None and size_result.returncode == 0
                    else -1
                )
            except (UnicodeDecodeError, ValueError):
                candidate_size = -1
            if candidate_size < 0 or candidate_size > 512 * 1024:
                validation.error(
                    f"{relative}: approved candidate gate blob is missing or exceeds 512 KiB"
                )
            else:
                try:
                    blob_result = subprocess.run(
                        ["git", "-C", str(ROOT), "show", object_name],
                        check=False,
                        stdout=subprocess.PIPE,
                        stderr=subprocess.DEVNULL,
                        timeout=5,
                    )
                except (OSError, subprocess.TimeoutExpired):
                    blob_result = None
                if blob_result is None or blob_result.returncode != 0:
                    validation.error(
                        f"{relative}: approved candidate gate blob cannot be read"
                    )
                else:
                    candidate_blob = blob_result.stdout
    if candidate_blob is not None and re.fullmatch(r"[0-9a-f]{64}", candidate_digest):
        actual_candidate_digest = hashlib.sha256(candidate_blob).hexdigest()
        if candidate_digest != actual_candidate_digest:
            validation.error(
                f"{relative}: approved gate digest does not match the candidate commit blob"
            )
    for check in M0_REQUIRED_FINAL_CHECKS:
        row = verification_by_check.get(check)
        if row is not None and candidate_commit not in row[1]:
            validation.error(
                f"{relative}: final verification {check!r} must cite the approved "
                "candidate commit"
            )

    responsibility_owner_names = {
        row[0]: row[3].split("—", 1)[0].strip()
        for row in responsibility_rows
    }
    approval_owner_inputs = {
        "Project-owner decision": "M0 gate decision",
        "Security-owner M0 acknowledgement": "Threat and security model",
        "Privacy-owner M0 acknowledgement": "Privacy and logging model",
        "Dependency/licence-owner M0 acknowledgement": (
            "Dependencies, licence, and redistribution rights"
        ),
    }
    for field, prefix in M0_FINAL_APPROVAL_FIELDS.items():
        value = approval_by_field.get(field, "")
        if not value.startswith(f"{prefix} — "):
            validation.error(
                f"{relative}: approved field {field!r} must begin {prefix!r} "
                "and name the human/durable reference"
            )
        elif re.search(r"(?i)\b(?:pending|tbd|unknown|unassigned|none|codex)\b", value):
            validation.error(
                f"{relative}: approved field {field!r} contains a placeholder"
            )
        owner_name = responsibility_owner_names.get(approval_owner_inputs[field], "")
        if owner_name and owner_name not in value:
            validation.error(
                f"{relative}: approved field {field!r} must name declared owner "
                f"{owner_name!r}"
            )
        if candidate_commit not in value or candidate_digest not in value:
            validation.error(
                f"{relative}: approved field {field!r} must bind the candidate "
                "commit and gate digest"
            )
    for row in responsibility_rows:
        acknowledgement = row[4]
        required_prefix = "Signed — " if row[0] == "M0 gate decision" else "Acknowledged — "
        if not acknowledgement.startswith(required_prefix):
            validation.error(
                f"{relative}: approved responsibility {row[0]!r} must begin "
                f"{required_prefix!r}"
            )
        owner_name = responsibility_owner_names.get(row[0], "")
        if owner_name and owner_name not in acknowledgement:
            validation.error(
                f"{relative}: approved responsibility {row[0]!r} must name "
                f"declared owner {owner_name!r}"
            )
    unresolved_blockers = exact_metadata_value("Unresolved blockers")
    if unresolved_blockers != "None":
        validation.error(f"{relative}: approved gate must have no unresolved blockers")

    evidence = task.get("evidence")
    if not isinstance(evidence, list):
        return
    evidence_fields = (
        "task",
        "command_or_review",
        "tier",
        "result",
        "environment",
        "date",
        "duration",
        "reference",
        "limitation",
    )
    complete_entries: list[dict[str, Any]] = []
    for index, entry in enumerate(evidence):
        if not isinstance(entry, dict):
            validation.error(f"TL-0010: evidence[{index}] must be a mapping when done")
            continue
        missing_fields = [
            field
            for field in evidence_fields
            if not isinstance(entry.get(field), str) or not entry[field].strip()
        ]
        if missing_fields:
            validation.error(
                f"TL-0010: evidence[{index}] has empty required fields {missing_fields}"
            )
            continue
        complete_entries.append(entry)

    def entry_text(entry: dict[str, Any]) -> str:
        return re.sub(
            r"\s+",
            " ",
            " ".join(str(entry.get(field, "")) for field in evidence_fields)
            .replace("/", "\\")
            .casefold(),
        )

    def passed(entry: dict[str, Any]) -> bool:
        return re.match(r"(?i)^passed(?:$|[;:])", str(entry.get("result", ""))) is not None

    quick_evidence = any(
        passed(entry)
        and entry.get("task") == "TL-0010"
        and "quick" in str(entry.get("tier", "")).casefold()
        and "active codex machine" in entry_text(entry)
        and ("clean clone" in entry_text(entry) or "clean worktree" in entry_text(entry))
        and "eng\\verify.ps1 -tier quick" in entry_text(entry)
        for entry in complete_entries
    )
    if not quick_evidence:
        validation.error(
            "TL-0010: done evidence must include passed active-machine clean-checkout Quick"
        )
    full_evidence = any(
        passed(entry)
        and entry.get("task") == "TL-0010"
        and "full" in str(entry.get("tier", "")).casefold()
        and "active codex machine" in entry_text(entry)
        and "eng\\verify.ps1 -tier full" in entry_text(entry)
        for entry in complete_entries
    )
    if not full_evidence:
        validation.error(
            "TL-0010: done evidence must include passed active-machine Full verification"
        )
    human_text = " ".join(
        entry_text(entry)
        for entry in complete_entries
        if passed(entry) and "human" in str(entry.get("tier", "")).casefold()
    )
    for phrase in ("project owner", "security", "privacy"):
        if phrase not in human_text:
            validation.error(
                f"TL-0010: done human evidence must include {phrase!r} acknowledgement"
            )
    if "licence" not in human_text and "license" not in human_text:
        validation.error(
            "TL-0010: done human evidence must include licence-owner acknowledgement"
        )
    for owner_name in sorted(set(responsibility_owner_names.values())):
        if owner_name and owner_name.casefold() not in human_text:
            validation.error(
                f"TL-0010: done human evidence must name declared owner {owner_name!r}"
            )
    if candidate_commit not in human_text or candidate_digest not in human_text:
        validation.error(
            "TL-0010: done human evidence must bind the approved candidate commit "
            "and gate digest"
        )


def validate_current_bundle_document_markers(validation: Validation) -> None:
    for relative, marker in CURRENT_BUNDLE_DOCUMENT_MARKERS.items():
        text = require_phrases(relative, (), validation)
        visible_text = markdown_visible_text(text)
        if relative == "CHANGELOG.md":
            version_headings = re.findall(r"(?m)^##\s+[^\r\n]+$", visible_text)
            if not version_headings or version_headings[0] != marker:
                validation.error(
                    f"{relative}: first version heading must be {marker!r}"
                )
            if visible_text.count(marker) != 1:
                validation.error(
                    f"{relative}: must contain exactly one current version heading {marker!r}"
                )
            continue

        label = CURRENT_BUNDLE_METADATA_LABELS[relative]
        metadata_lines = [
            line.rstrip(" \t")
            for line in re.findall(
            rf"(?m)^\*\*{re.escape(label)}:\*\*[^\r\n]*$",
            visible_text,
            )
        ]
        if metadata_lines != [marker]:
            validation.error(
                f"{relative}: {label!r} metadata must be exactly one line {marker!r}; "
                f"found {metadata_lines}"
            )

    additional_markers = {
        "DECISIONS.md": (f"**Generated:** {CURRENT_BUNDLE_GENERATED_ON}",),
        "RELEASE_INTERFACE.md": (
            f"| Interface revision | Draft {CURRENT_BUNDLE_VERSION} |",
        ),
    }
    for relative, markers in additional_markers.items():
        text = require_phrases(relative, (), validation)
        visible_text = markdown_visible_text(text)
        for marker in markers:
            exact_lines = re.findall(
                rf"(?m)^{re.escape(marker)}[ \t]*$",
                visible_text,
            )
            if len(exact_lines) != 1:
                validation.error(
                    f"{relative}: required metadata line must appear exactly once "
                    f"as {marker!r}"
                )

    required_markers: dict[str, tuple[str, ...]] = {
        "ROADMAP.md": (
            CURRENT_BUNDLE_DOCUMENT_MARKERS["ROADMAP.md"],
            "ThirdLife Software Portfolio v2.1",
            "active Codex machine",
            "quick",
            "targeted",
            "full",
            "extended",
            "TL-0008",
        ),
        "DECISIONS.md": ("D-058", "D-059", "D-064", "D-066"),
        "PROJECT_BOUNDARY.md": (
            "Team B / B1",
            "project vacuum",
            "active Codex machine",
            "ThirdLife Deployment and Suite Assembly",
            "Scam Explainer",
            "TL-0710",
        ),
        "TESTING.md": (
            "Quick tier",
            "Targeted tier",
            "Full tier",
            "Extended/stress tier",
            "active Codex machine",
        ),
        "DEVELOPMENT_WORKFLOW.md": (
            "git fetch",
            "STATUS.md",
            "clean clone",
            "active Codex machine",
        ),
        "STATUS.md": (
            "TL-0008",
            V030_TL0008_SOURCE_COMMIT,
            V030_TL0008_PROCEDURE_DIGEST,
            "superseded",
        ),
        "LOW_SPEC.md": (
            "active Codex machine",
            "reference-machine",
            "same-machine",
            "hardware certification",
        ),
        "RELEASE_INTERFACE.md": (
            "Draft placeholder",
            "TL-0706",
            "TL-0710",
            "not a shared application API",
            "Source continuity",
            "Validation evidence",
            "cross-hardware certification",
        ),
        "FUTURE_ASSEMBLY_NOTES.md": (
            "Non-binding deferred backlog",
            "Nothing in this file is an active B1 requirement",
            "Team B / B4",
        ),
        "TL-0008_TRANSITION.md": (
            "TL-0008 draft 1",
            "SUPERSEDED",
            "MHT-001",
            "active Codex machine",
        ),
        "CODEX_TL0008_TRANSITION_PROMPT.md": (
            "TL-0008 draft 1",
            V030_TL0008_SOURCE_COMMIT,
            V030_TL0008_PROCEDURE_DIGEST,
            "Do not",
        ),
        "docs/history/TL-0008-draft-1-superseded.md": (
            "SUPERSEDED",
            "DO NOT EXECUTE",
            V030_TL0008_SOURCE_COMMIT,
            V030_TL0008_PROCEDURE_DIGEST,
        ),
    }
    for relative, markers in required_markers.items():
        require_phrases(relative, markers, validation)


def validate_testing_documents(
    validation: Validation,
    task_by_id: dict[str, dict[str, Any]],
    decision_ids: set[str],
) -> None:
    """Validate the active same-machine testing contract introduced in v0.3.0.

    The superseded draft required commit-bound physical walkthrough evidence;
    the current contract deliberately does not.
    """

    relative_markers: dict[str, tuple[str, ...]] = {
        "TESTING.md": (
            "active Codex machine only",
            "Quick tier",
            "Targeted tier",
            "Full tier",
            "Extended/stress tier",
            "Every extended scenario must be independently invokable",
            "do not execute the old `MHT-001`–`MHT-021`",
            "not hardware certification",
        ),
        "DEVELOPMENT_WORKFLOW.md": (
            "Continuity source of truth",
            "git fetch --all --prune",
            "STATUS.md",
            "clean clone",
            "active Codex machine only",
        ),
        "STATUS.md": (
            "TL-0008",
            V030_TL0008_SOURCE_COMMIT,
            V030_TL0008_PROCEDURE_DIGEST,
            "No physical hardware walkthrough",
        ),
        "LOW_SPEC.md": (
            CURRENT_BUNDLE_DOCUMENT_MARKERS["LOW_SPEC.md"],
            "active Codex machine only",
            "concurrency explicit, configurable, and conservative",
            "working CPU path",
            "same-machine constraint",
            "absence of cross-hardware certification",
        ),
        "docs/testing/reference-machine-profile.md": (
            "Active Codex Reference-Machine Profile",
            "reproducible development",
            "not asset inventory or hardware certification",
            "Sanitization review",
        ),
        "docs/testing/capability-risk-matrix.md": (
            "Capability and Risk Coverage Matrix",
            "physical-device inventory",
            "proves",
            "does not prove",
            "blocker",
        ),
        "docs/testing/same-machine-constraints.md": (
            "active Codex machine",
            "safe setup",
            "host system volume",
            "Claim limit",
        ),
        "docs/testing/manual-hardware-tests.md": (
            "TL-0008",
            "Pass",
            "Fail",
            "Not available",
            "Not run",
            "Human confirmed",
            "Observed by provider",
            "Simulated deterministic test",
            "MHT-001",
            "MHT-021",
        ),
        "docs/testing/failure-injection.md": (
            "Failure-injection",
            "Not run",
            "independently invokable command",
            "Cleanup/recovery",
        ),
        "docs/testing/accessibility-matrix.md": (
            "Accessibility",
            "TL-0008",
            "active Codex machine",
        ),
        "docs/history/TL-0008-draft-1-superseded.md": (
            "SUPERSEDED — DO NOT EXECUTE",
            "TL-0008 draft 1",
            V030_TL0008_SOURCE_COMMIT,
            V030_TL0008_PROCEDURE_DIGEST,
        ),
    }
    texts = {
        relative: require_phrases(relative, markers, validation)
        for relative, markers in relative_markers.items()
    }

    expected_ids = {
        "docs/testing/capability-risk-matrix.md": (
            "CRM",
            tuple(f"CRM-{index:03d}" for index in range(1, 12)),
        ),
        "docs/testing/failure-injection.md": (
            "FI",
            tuple(f"FI-{index:03d}" for index in range(1, 13)),
        ),
        "docs/testing/accessibility-matrix.md": (
            "A11Y",
            tuple(f"A11Y-{index:03d}" for index in range(1, 11)),
        ),
    }
    known_testing_ids: set[str] = {
        f"MHT-{index:03d}" for index in range(1, 22)
    }
    for relative, (prefix, expected) in expected_ids.items():
        found = testing_document_ids(texts[relative], prefix)
        known_testing_ids.update(expected)
        if found != expected:
            validation.error(
                f"{relative}: {prefix} IDs must be contiguous and ordered as {list(expected)}"
            )

    expected_constraints = (
        "SMC-BASELINE",
        "SMC-NO-GPU",
        "SMC-CONSERVATIVE-CONCURRENCY",
        "SMC-LOW-PRIORITY",
        "SMC-LOW-FREE-SPACE",
        "SMC-OFFLINE",
        "SMC-INTERRUPTED-NETWORK",
        "SMC-PROVIDER-UNAVAILABLE",
        "SMC-SLOW-DESTINATION",
        "SMC-LARGE-WORKLOAD",
    )
    found_constraints = same_machine_constraint_ids(
        texts["docs/testing/same-machine-constraints.md"]
    )
    known_testing_ids.update(expected_constraints)
    if found_constraints != expected_constraints:
        validation.error(
            "docs/testing/same-machine-constraints.md: SMC profiles must be complete, unique, and ordered"
        )

    task_ids = set(task_by_id)
    for relative, body in texts.items():
        if relative.startswith("docs/history/"):
            continue
        unknown_tasks = sorted(set(re.findall(r"\bTL-\d{4}\b", body)) - task_ids)
        if unknown_tasks:
            validation.error(f"{relative}: references unknown task IDs {unknown_tasks}")
        unknown_decisions = sorted(set(re.findall(r"\bD-\d{3}\b", body)) - decision_ids)
        if unknown_decisions:
            validation.error(
                f"{relative}: references unknown decision IDs {unknown_decisions}"
            )
        unknown_testing = sorted(
            set(
                re.findall(
                    r"\b(?:CRM|FI|A11Y|MHT)-\d{3}\b|\bSMC-[A-Z0-9-]+\b",
                    body,
                )
            )
            - known_testing_ids
        )
        if unknown_testing:
            validation.error(
                f"{relative}: references unknown testing IDs {unknown_testing}"
            )

        canonical = canonical_testing_scan_text(body)
        if "<!--" in canonical or "-->" in canonical:
            validation.error(
                f"{relative}: HTML comments are not permitted in governed testing documents"
            )
        if SECURITY_MACHINE_PATH_RE.search(canonical):
            validation.error(f"{relative}: contains a machine-specific path")
        if re.search(r"(?i)(?://[^/\s]+/[^/\s]+|%[A-Z][A-Z0-9_]*%[\\/])", canonical):
            validation.error(f"{relative}: contains a machine-specific path")
        if re.search(r"(?:^|[\\/])\.\.(?:[\\/]|$)", canonical):
            validation.error(f"{relative}: contains a path-traversal segment")
        if re.search(r"(?i)%[0-9a-f]{2}", canonical):
            validation.error(f"{relative}: contains unresolved percent-encoded text")
        sensitive_label = testing_sensitive_match(canonical)
        if sensitive_label is not None:
            validation.error(f"{relative}: contains a prohibited {sensitive_label}")

        current_heading = ""
        for line_number, line in enumerate(canonical.splitlines(), start=1):
            if re.match(r"^#{1,6}\s+", line):
                current_heading = line
            elif line.casefold().startswith("not permitted"):
                current_heading = f"{current_heading} {line}"
            if has_prohibited_hardware_claim(line, current_heading):
                validation.error(
                    f"{relative}:{line_number}: contains an unsupported hardware claim"
                )

    validate_no_obsolete_hardware_obligations(
        validation,
        {
            relative: body
            for relative, body in texts.items()
            if not relative.startswith("docs/history/")
        },
    )

    obsolete_matrix = ROOT / "docs/testing/device-matrix.md"
    if obsolete_matrix.exists():
        validation.error(
            "docs/testing/device-matrix.md: obsolete active device inventory must be removed; preserve history under docs/history"
        )

    validate_tl0008_contract(validation, task_by_id)


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

    for relative in (*REQUIRED_FILES, BUNDLE_MANIFEST_FILE):
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
    if task_doc.get("bundle_version") != CURRENT_BUNDLE_VERSION:
        validation.error(
            f"TASKS.yaml: bundle_version must equal {CURRENT_BUNDLE_VERSION!r}"
        )
    if str(task_doc.get("generated_on")) != CURRENT_BUNDLE_GENERATED_ON:
        validation.error(
            f"TASKS.yaml: generated_on must equal {CURRENT_BUNDLE_GENERATED_ON}"
        )
    project = task_doc.get("project", {})
    if not isinstance(project, dict):
        validation.error("TASKS.yaml: project must be a mapping")
        project = {}
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
        "validation_hardware": "active Codex machine only",
        "hardware_lab_required": False,
        "external_runtime_matrix_required": False,
    }
    for field, expected in expected_project.items():
        if project.get(field) != expected:
            validation.error(
                f"TASKS.yaml: project.{field} must equal {expected!r}"
            )

    portfolio = task_doc.get("portfolio", {})
    if not isinstance(portfolio, dict):
        validation.error("TASKS.yaml: portfolio must be a mapping")
        portfolio = {}
    expected_portfolio = {
        "roadmap_version": "2.1",
        "development_posture": "standalone project vacuum",
        "integration_posture": "late binding against frozen stable releases",
        "active_cross_project_dependencies_allowed": False,
        "sibling_specific_work_authorized": False,
        "next_team_project_after_stable": "Scam Explainer",
        "continuity_source": "GitHub",
        "validation_hardware": "active Codex machine only",
        "hardware_lab_required": False,
        "remote_runtime_testing_required": False,
        "test_tiers": ["quick", "targeted", "full", "extended"],
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
    if tuple(decision_ids) != CURRENT_DECISION_IDS:
        validation.error(
            "DECISIONS.md: current bundle must contain exactly D-001 through D-066"
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
        "expected_test_tier",
        "full_test_triggers",
        "extended_test_triggers",
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
        validate_task_test_tier(task_id, task, validation)

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
    if len(task_by_id) != CURRENT_TASK_COUNT:
        validation.error(
            "TASKS.yaml: current bundle must contain "
            f"{CURRENT_TASK_COUNT} tasks, found {len(task_by_id)}"
        )

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
    validate_architecture_decision_records(validation, task_by_id, decision_set)
    validate_tl0401_adr_reservation(validation, task_by_id)
    validate_security_documents(validation, task_by_id, decision_set)
    validate_privacy_documents(validation, task_by_id, decision_set)
    validate_pilot_fixtures(validation, task_by_id)
    validate_testing_documents(validation, task_by_id, decision_set)
    validate_m0_sandbox_scripts(validation)
    validate_m0_foundation_gate(validation, task_by_id)
    validate_current_bundle_document_markers(validation)

    active_hardware_scope_documents = {
        relative: (ROOT / relative).read_text(encoding="utf-8")
        for relative in (
            "ROADMAP.md",
            "DECISIONS.md",
            "TASKS.yaml",
            "AGENTS.md",
            "CODEX_START_PROMPT.md",
            "README.md",
            "STATUS.md",
            "DEVELOPMENT_WORKFLOW.md",
            "TESTING.md",
            "PROJECT_BOUNDARY.md",
            "SECURITY.md",
            "ACCESSIBILITY.md",
            "LOW_SPEC.md",
            "RELEASE_INTERFACE.md",
        )
    }
    validate_no_obsolete_hardware_obligations(
        validation,
        active_hardware_scope_documents,
    )
    validate_tracked_text_positioning(validation)

    boundary_text = (ROOT / "PROJECT_BOUNDARY.md").read_text(encoding="utf-8")
    for required_phrase in (
        "Team B / B1",
        "project vacuum",
        "active Codex machine",
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
        "Source continuity",
        "Validation evidence",
        "cross-hardware certification",
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
