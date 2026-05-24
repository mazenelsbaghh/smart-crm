import pytest
import httpx
import uuid
import time
from datetime import datetime, timedelta

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_follow_ups_flow():
    sender_phone = f"555{uuid.uuid4().hex[:6]}"
    message_id = f"msg_{uuid.uuid4().hex}"
    
    async with httpx.AsyncClient(timeout=20.0) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "FollowUpTestProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]

        # 2. Ingest message via webhook (this should auto-create Customer)
        webhook_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": message_id,
                "sender": sender_phone,
                "content": "Hi, I need follow up.",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert webhook_resp.status_code == 200

        # 3. Retrieve customers for this project to get the auto-created customer ID
        headers = {"X-Project-Id": proj_id}
        customers_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/customers", headers=headers)
        assert customers_resp.status_code == 200
        customers = customers_resp.json()
        assert len(customers) == 1
        customer_id = customers[0]["id"]

        # 4. Schedule a future follow-up (e.g. 1 hour from now)
        future_due = (datetime.utcnow() + timedelta(hours=1)).isoformat() + "Z"
        follow_up_resp = await client.post(
            f"{BASE_URL}/customers/{customer_id}/follow-ups",
            headers=headers,
            json={
                "dueDate": future_due,
                "notes": "Call customer back to discuss offer"
            }
        )
        assert follow_up_resp.status_code == 201
        future_followup = follow_up_resp.json()
        assert future_followup["status"] == "Pending"
        assert future_followup["customerId"] == customer_id

        # 5. Schedule an overdue follow-up (e.g. 5 seconds ago)
        past_due = (datetime.utcnow() - timedelta(seconds=5)).isoformat() + "Z"
        overdue_resp = await client.post(
            f"{BASE_URL}/customers/{customer_id}/follow-ups",
            headers=headers,
            json={
                "dueDate": past_due,
                "notes": "Immediate follow-up needed"
            }
        )
        assert overdue_resp.status_code == 201
        overdue_followup = overdue_resp.json()
        assert overdue_followup["status"] == "Pending"

        # 6. List pending follow-ups
        list_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/follow-ups?status=Pending", headers=headers)
        assert list_resp.status_code == 200
        pending_list = list_resp.json()
        assert len(pending_list) >= 1

        # 7. Wait for the background scheduler to run (Hangfire checks every 15 seconds)
        print("Waiting 18 seconds for FollowUpScheduler...")
        await pytest.importorskip("asyncio").sleep(18.0)

        # 8. List follow-ups again
        list_pending = await client.get(f"{BASE_URL}/projects/{proj_id}/follow-ups?status=Pending", headers=headers)
        assert list_pending.status_code == 200
        pendings = list_pending.json()
        assert any(f["id"] == future_followup["id"] for f in pendings)
        assert not any(f["id"] == overdue_followup["id"] for f in pendings)

        # The overdue one should now be Missed
        list_missed = await client.get(f"{BASE_URL}/projects/{proj_id}/follow-ups?status=Missed", headers=headers)
        assert list_missed.status_code == 200
        missed = list_missed.json()
        assert any(f["id"] == overdue_followup["id"] for f in missed)
