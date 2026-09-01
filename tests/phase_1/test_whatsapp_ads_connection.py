import uuid

import httpx
import pytest

from ._ads_test_support import BASE_URL, connect_mock_facebook, create_admin


@pytest.mark.asyncio
async def test_meta_connection_requires_the_complete_mutually_eligible_whatsapp_chain():
    async with httpx.AsyncClient(timeout=30) as client:
        project_id, headers = await create_admin(client, "wa-meta-chain")
        catalog = await connect_mock_facebook(client, project_id, headers)

        assert catalog["adAccounts"]
        assert catalog["pages"]
        assert catalog["datasets"]
        assert catalog["wabas"][0]["phones"]
        destinations = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/destinations", headers=headers)
        destinations.raise_for_status()
        assert destinations.json()[0]["state"] == "Eligible"
        assert destinations.json()[0]["integrationMode"] == "CloudApiCoexistence"


@pytest.mark.asyncio
async def test_connection_mutations_require_idempotency_and_concurrency_headers():
    async with httpx.AsyncClient(timeout=30) as client:
        project_id, headers = await create_admin(client, "wa-meta-contract")
        missing_key = await client.post(f"{BASE_URL}/projects/{project_id}/ad-manager/facebook/oauth/start", headers=headers)
        assert missing_key.status_code == 400

        await connect_mock_facebook(client, project_id, headers)
        connection = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/connection", headers=headers)
        connection.raise_for_status()
        connection_id = connection.json()["id"]
        missing_match = await client.request(
            "DELETE",
            f"{BASE_URL}/projects/{project_id}/ad-manager/connection/{connection_id}",
            headers={**headers, "Idempotency-Key": str(uuid.uuid4())},
            json={"mode": "PauseManaged"},
        )
        assert missing_match.status_code == 428


@pytest.mark.asyncio
async def test_cross_project_resource_access_is_denied():
    async with httpx.AsyncClient(timeout=30) as client:
        project_a, headers_a = await create_admin(client, "wa-meta-a")
        project_b, _ = await create_admin(client, "wa-meta-b")
        await connect_mock_facebook(client, project_a, headers_a)

        denied = await client.get(f"{BASE_URL}/projects/{project_b}/ad-manager/destinations", headers=headers_a)
        assert denied.status_code == 403
