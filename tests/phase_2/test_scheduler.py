import pytest
import httpx
import uuid
import time
from datetime import datetime, timedelta, timezone

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_scheduler_overdue_followup_flow():
    async with httpx.AsyncClient(timeout=20.0) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "SchedulerProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]
        headers = {"X-Project-Id": proj_id}

        # 2. Ingest message to create Customer
        phone = f"555{uuid.uuid4().hex[:6]}"
        webhook_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex}",
                "sender": phone,
                "content": "Need follow up",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert webhook_resp.status_code == 200

        # Fetch customer to get ID
        customers_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/customers", headers=headers)
        assert customers_resp.status_code == 200
        customers = customers_resp.json()
        customer = next((c for c in customers if c["phoneNumber"] == phone), None)
        assert customer is not None
        customer_id = customer["id"]

        # 3. Create follow-up with DueDate in the past (e.g., 5 seconds ago)
        past_date = (datetime.now(timezone.utc) - timedelta(seconds=5)).isoformat()
        
        followup_resp = await client.post(
            f"{BASE_URL}/customers/{customer_id}/follow-ups",
            headers=headers,
            json={
                "dueDate": past_date,
                "notes": "Testing Hangfire overdue check"
            }
        )
        assert followup_resp.status_code == 201
        followup_id = followup_resp.json()["id"]

        # 4. Wait for Hangfire job to run (sleep 18 seconds to cross Hangfire 15s polling window)
        print("Waiting for Hangfire scheduler job to run overdue check...")
        await pytest.importorskip("asyncio").sleep(18.0)

        # 5. Fetch the follow-up and assert it is marked as Missed
        check_resp = await client.get(f"{BASE_URL}/follow-ups/{followup_id}", headers=headers)
        assert check_resp.status_code == 200
        followup = check_resp.json()
        assert followup["status"] == "Missed"
