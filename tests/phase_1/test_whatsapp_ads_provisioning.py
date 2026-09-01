import uuid

import httpx
import pytest

from ._ads_test_support import BASE_URL, connect_mock_facebook, create_admin


@pytest.mark.asyncio
async def test_creative_sources_keep_stable_page_identity_and_supported_formats_only():
    async with httpx.AsyncClient(timeout=30) as client:
        project_id, headers = await create_admin(client, "wa-provisioning-sources")
        catalog = await connect_mock_facebook(client, project_id, headers)

        response = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/creative-sources", headers=headers)
        response.raise_for_status()
        sources = response.json()
        assert {source["mediaType"] for source in sources} == {"Image", "Video"}
        page_id = catalog["pages"][0]["id"]
        assert all(source["stableSourceId"].startswith(f"meta:{page_id}:") for source in sources)
        assert all(source["eligibility"] == 1 or source["eligibility"] == "Eligible" for source in sources)


@pytest.mark.asyncio
async def test_unknown_plan_cannot_create_any_provider_object_and_legacy_launch_route_is_removed():
    async with httpx.AsyncClient(timeout=30) as client:
        project_id, headers = await create_admin(client, "wa-provisioning-closed")
        await connect_mock_facebook(client, project_id, headers)
        plan_id = str(uuid.uuid4())

        response = await client.post(
            f"{BASE_URL}/projects/{project_id}/ad-manager/plans/{plan_id}/provision",
            headers={**headers, "Idempotency-Key": "missing-plan"},
            json={"creativeId": str(uuid.uuid4())},
        )
        assert response.status_code == 409
        assert response.json()["code"] == "ADS_PLAN_NOT_READY"

        legacy = await client.post(
            f"{BASE_URL}/projects/{project_id}/ad-manager/launch-plans/activate",
            headers={**headers, "Idempotency-Key": "legacy-route"},
            json={"objective": "OUTCOME_TRAFFIC", "optimizationEvent": "LINK_CLICKS"},
        )
        assert legacy.status_code == 404
