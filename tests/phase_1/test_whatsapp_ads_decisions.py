import httpx
import pytest

from ._ads_test_support import BASE_URL, create_admin


@pytest.mark.asyncio
async def test_decision_surfaces_start_empty_and_never_invent_a_financial_action():
    async with httpx.AsyncClient(timeout=30) as client:
        project_id, headers = await create_admin(client, "wa-decisions-empty")

        decisions = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/decisions", headers=headers)
        changes = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/changes", headers=headers)
        audit = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/audit", headers=headers)

        decisions.raise_for_status()
        changes.raise_for_status()
        audit.raise_for_status()
        assert decisions.json() == []
        assert changes.json()["items"] == []
        assert audit.json()["items"] == []


@pytest.mark.asyncio
async def test_decision_audit_and_change_streams_are_project_scoped():
    async with httpx.AsyncClient(timeout=30) as client:
        project_a, headers_a = await create_admin(client, "wa-decisions-a")
        project_b, _ = await create_admin(client, "wa-decisions-b")

        for suffix in ("decisions", "changes", "audit"):
            denied = await client.get(f"{BASE_URL}/projects/{project_b}/ad-manager/{suffix}", headers=headers_a)
            assert denied.status_code == 403

        own = await client.get(f"{BASE_URL}/projects/{project_a}/ad-manager/changes", headers=headers_a)
        own.raise_for_status()
