import httpx
import pytest

from ._ads_test_support import BASE_URL, create_admin


@pytest.mark.asyncio
async def test_strategy_waits_without_cited_current_offer_and_never_creates_provider_objects():
    async with httpx.AsyncClient(timeout=30) as client:
        project_id, headers = await create_admin(client, "wa-strategy-wait")
        response = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/strategy", headers=headers)
        response.raise_for_status()
        assert response.json()["state"] == "WAIT"
        assert response.json()["blockingReasons"]


@pytest.mark.asyncio
async def test_plan_compile_rejects_unknown_or_unauthorized_offer():
    async with httpx.AsyncClient(timeout=30) as client:
        project_id, headers = await create_admin(client, "wa-strategy-offer")
        response = await client.post(
            f"{BASE_URL}/projects/{project_id}/ad-manager/plans/compile",
            headers={**headers, "Idempotency-Key": "unknown-offer"},
            json={"offerId": "00000000-0000-0000-0000-000000000001"},
        )
        assert response.status_code == 422
        assert response.json()["blockingReasons"] == ["ADS_OFFER_NOT_ELIGIBLE"]
