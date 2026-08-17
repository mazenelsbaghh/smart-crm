import httpx
import pytest

from ._ads_test_support import BASE_URL, create_admin


@pytest.mark.asyncio
async def test_emergency_stop_is_idempotent_and_requires_explicit_recovery():
    async with httpx.AsyncClient(timeout=20) as client:
        project_id, headers = await create_admin(client, "ads-stop")
        endpoint = f"{BASE_URL}/projects/{project_id}/ad-manager/emergency-stop"
        first = await client.post(endpoint, headers=headers, json={"reason": "acceptance drill"})
        second = await client.post(endpoint, headers=headers, json={"reason": "replayed drill"})
        assert first.status_code == second.status_code == 200
        overview = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/overview", headers=headers)
        overview.raise_for_status()
        assert overview.json()["emergencyStop"] is True
        assert overview.json()["autopilot"] is False
        resume = await client.post(f"{endpoint}/resume", headers=headers, json={})
        assert resume.status_code == 409  # No healthy Facebook connection/tracking means fail closed.
