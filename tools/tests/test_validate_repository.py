#!/usr/bin/env python3
"""Focused regressions for repository-level supply-chain approval governance."""

from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import yaml


TOOLS_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS_ROOT))

import supply_chain  # noqa: E402
import validate_repository  # noqa: E402


CURRENT_DIGEST = "a" * 64
HISTORICAL_DIGEST = "b" * 64


def approval_evidence(matrix_digest: str) -> dict[str, str]:
    return {
        "summary": (
            "Named human licence and rights reviewer approved the proposal "
            "without granting blanket redistribution rights."
        ),
        "result": "passed",
        "reference": f"matrix sha256:{matrix_digest}",
    }


class SupplyChainApprovalGovernanceTests(unittest.TestCase):
    def validate(
        self,
        *,
        approval_state: str,
        evidence: list[dict[str, str]],
        status: str = "done",
    ) -> validate_repository.Validation:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "TASKS.yaml").write_text(
                yaml.safe_dump(
                    {
                        "tasks": [
                            {
                                "id": "TL-0006",
                                "status": status,
                                "evidence": evidence,
                            }
                        ]
                    },
                    sort_keys=False,
                ),
                encoding="utf-8",
            )
            result = supply_chain.SupplyChainResult(
                root=root,
                inventory=(),
                dependency_graph={},
                lock_digest="c" * 64,
                matrix_digest=CURRENT_DIGEST,
                approval_state=approval_state,
                errors=(),
            )
            validation = validate_repository.Validation()
            with (
                patch.object(validate_repository, "ROOT", root),
                patch.object(
                    validate_repository.supply_chain,
                    "validate_supply_chain",
                    return_value=result,
                ),
            ):
                validate_repository.validate_supply_chain_contract(validation)
            return validation

    def test_pending_current_matrix_allows_digest_bound_historical_approval(self) -> None:
        validation = self.validate(
            approval_state="pending",
            evidence=[approval_evidence(HISTORICAL_DIGEST)],
        )

        self.assertEqual(validation.errors, [])

    def test_pending_current_matrix_rejects_current_digest_approval_claim(self) -> None:
        validation = self.validate(
            approval_state="pending",
            evidence=[
                approval_evidence(HISTORICAL_DIGEST),
                approval_evidence(CURRENT_DIGEST),
            ],
        )

        self.assertTrue(
            any(
                "current matrix while the governed review is Pending" in error
                for error in validation.errors
            ),
            validation.errors,
        )

    def test_pending_done_task_requires_digest_bound_historical_approval(self) -> None:
        validation = self.validate(approval_state="pending", evidence=[])

        self.assertTrue(
            any(
                "requires prior named human licence/rights approval" in error
                for error in validation.errors
            ),
            validation.errors,
        )

    def test_approval_claim_without_matrix_digest_is_rejected(self) -> None:
        evidence = approval_evidence(HISTORICAL_DIGEST)
        evidence["reference"] = "reviewed commit " + "d" * 40

        validation = self.validate(approval_state="pending", evidence=[evidence])

        self.assertTrue(
            any("must bind an exact matrix SHA-256" in error for error in validation.errors),
            validation.errors,
        )

    def test_approved_current_matrix_rejects_historical_only_evidence(self) -> None:
        validation = self.validate(
            approval_state="approved",
            evidence=[approval_evidence(HISTORICAL_DIGEST)],
        )

        self.assertTrue(
            any(
                "approved review evidence must bind" in error
                for error in validation.errors
            ),
            validation.errors,
        )

    def test_approved_current_matrix_accepts_current_digest_evidence(self) -> None:
        validation = self.validate(
            approval_state="approved",
            evidence=[
                approval_evidence(HISTORICAL_DIGEST),
                approval_evidence(CURRENT_DIGEST),
            ],
        )

        self.assertEqual(validation.errors, [])


if __name__ == "__main__":
    unittest.main()
