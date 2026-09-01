from datetime import datetime, timedelta, timezone
import uuid

import httpx
import pytest

from ._ads_test_support import BASE_URL, create_admin


@pytest.mark.asyncio
async def test_sparse_performance_returns_wait_and_coherent_empty_window():
    async with httpx.AsyncClient(timeout=30) as client:
        project_id, headers = await create_admin(client, "wa-budget-wait")
        end = datetime.now(timezone.utc)
        start = end - timedelta(days=7)

        response = await client.get(
            f"{BASE_URL}/projects/{project_id}/ad-manager/performance",
            headers=headers,
            params={"startUtc": start.isoformat(), "endUtc": end.isoformat()},
        )
        response.raise_for_status()
        result = response.json()
        assert result["verdict"] == "Wait"
        assert result["spend"] == 0
        assert "ADS_WAIT_INSUFFICIENT_SNAPSHOTS" in result["waitReasons"]


@pytest.mark.asyncio
async def test_experiment_without_exactly_one_control_is_rejected_before_budget_or_spend():
    async with httpx.AsyncClient(timeout=30) as client:
        project_id, headers = await create_admin(client, "wa-experiment-invalid")
        identifier = str(uuid.uuid4())
        response = await client.post(
            f"{BASE_URL}/projects/{project_id}/ad-manager/experiments",
            headers={**headers, "Idempotency-Key": "bad-experiment"},
            json={
                "offerId": identifier,
                "destinationId": identifier,
                "envelopeId": identifier,
                "name": "Invalid",
                "hypothesis": "Creative B wins",
                "primaryVariable": "creativeId",
                "businessOutcome": "QualifiedLead",
                "attributionWindowDays": 7,
                "minimumElapsedHours": 48,
                "minimumSpend": 100,
                "minimumAttributedOutcomes": 5,
                "minimumCoverage": 0.9,
                "correctionLagHours": 24,
                "budgetCap": 100,
                "stopRuleJson": "{}",
                "arms": [{"name": "Variant only", "isControl": False, "planId": identifier,
                          "changedValueJson": '{"creativeId":"new"}', "budget": 50}],
            },
        )
        assert response.status_code == 422
        assert response.json()["code"] == "ADS_EXPERIMENT_CONTROL_REQUIRED"

        ledgers = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/budget/ledgers", headers=headers)
        ledgers.raise_for_status()
        assert ledgers.json() == []
