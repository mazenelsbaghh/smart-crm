import uuid

import httpx
import pytest

from ._ads_test_support import BASE_URL, create_admin


@pytest.mark.asyncio
async def test_emergency_stop_is_idempotent_visible_and_project_scoped():
    async with httpx.AsyncClient(timeout=30) as client:
        project_id, headers = await create_admin(client, "wa-safety-stop")
        other_project, _ = await create_admin(client, "wa-safety-other")
        mutation_headers = {**headers, "Idempotency-Key": str(uuid.uuid4())}

        first = await client.post(
            f"{BASE_URL}/projects/{project_id}/ad-manager/emergency-stop",
            headers=mutation_headers,
            json={"reason": "acceptance drill"},
        )
        first.raise_for_status()
        repeated = await client.post(
            f"{BASE_URL}/projects/{project_id}/ad-manager/emergency-stop",
            headers={**headers, "Idempotency-Key": str(uuid.uuid4())},
            json={"reason": "acceptance drill repeated"},
        )
        repeated.raise_for_status()
        assert repeated.json()["operationId"] == first.json()["operationId"]
        assert repeated.json()["alreadyActive"] is True

        state = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/stop-state", headers=headers)
        state.raise_for_status()
        assert state.json()["emergencyStop"]["state"] == "Paused"

        denied = await client.get(f"{BASE_URL}/projects/{other_project}/ad-manager/stop-state", headers=headers)
        assert denied.status_code == 403


@pytest.mark.asyncio
async def test_normal_disable_defaults_to_pause_and_leave_running_needs_explicit_ack():
    async with httpx.AsyncClient(timeout=30) as client:
        project_id, headers = await create_admin(client, "wa-safety-disable")

        missing_ack = await client.post(
            f"{BASE_URL}/projects/{project_id}/ad-manager/autopilot/disable",
            headers={**headers, "Idempotency-Key": str(uuid.uuid4())},
            json={"mode": "LeaveRunning", "reason": "monitor only", "acknowledgeContinuingSpend": False},
        )
        assert missing_ack.status_code == 422
        assert missing_ack.json()["code"] == "ADS_CONTINUING_SPEND_ACK_REQUIRED"

        accepted = await client.post(
            f"{BASE_URL}/projects/{project_id}/ad-manager/autopilot/disable",
            headers={**headers, "Idempotency-Key": str(uuid.uuid4())},
            json={"mode": "LeaveRunning", "reason": "monitor only", "acknowledgeContinuingSpend": True},
        )
        accepted.raise_for_status()
        assert accepted.json()["mode"] == "LeaveRunning"

        default_pause = await client.post(
            f"{BASE_URL}/projects/{project_id}/ad-manager/autopilot/disable",
            headers={**headers, "Idempotency-Key": str(uuid.uuid4())},
            json={"reason": "normal stop"},
        )
        default_pause.raise_for_status()
        assert default_pause.json()["mode"] == "PauseManaged"
