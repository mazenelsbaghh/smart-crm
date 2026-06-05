import pytest
import httpx
import uuid
import time
from datetime import datetime, timedelta

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_crm_and_follow_ups_flow():
    sender_phone = f"555{uuid.uuid4().hex[:6]}"
    message_id = f"msg_{uuid.uuid4().hex}"
    
    async with httpx.AsyncClient(timeout=20.0) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "CrmTestProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]

        # 2. Ingest message via webhook (this should auto-create Customer)
        webhook_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": message_id,
                "sender": sender_phone,
                "content": "Hi, I am interested in buying.",
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
        
        customer = customers[0]
        assert customer["phoneNumber"] == sender_phone
        customer_id = customer["id"]

        # 4. Update Customer profile
        update_resp = await client.put(
            f"{BASE_URL}/customers/{customer_id}",
            headers=headers,
            json={
                "name": "Jane Doe",
                "city": "Cairo",
                "leadScore": 85,
                "tags": ["Hot Lead", "WhatsApp"],
                "notes": "Very interested in premium version."
            }
        )
        assert update_resp.status_code == 200
        updated_customer = update_resp.json()
        assert updated_customer["name"] == "Jane Doe"
        assert updated_customer["city"] == "Cairo"
        assert updated_customer["leadScore"] == 85
        assert "Hot Lead" in updated_customer["tags"]
        assert updated_customer["notes"] == "Very interested in premium version."

        # 5. Retrieve Customer by ID
        get_cust_resp = await client.get(f"{BASE_URL}/customers/{customer_id}", headers=headers)
        assert get_cust_resp.status_code == 200
        assert get_cust_resp.json()["name"] == "Jane Doe"

        # 6. Schedule a future follow-up (e.g. 1 hour from now)
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

        # 7. Schedule an overdue follow-up (e.g. 5 seconds ago)
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

        # 8. List pending follow-ups - both should be visible initially (scheduler might not have run yet)
        list_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/follow-ups?status=Pending", headers=headers)
        assert list_resp.status_code == 200
        pending_list = list_resp.json()
        assert len(pending_list) >= 1

        # 9. Wait for the background scheduler to run (Hangfire checks every 15 seconds)
        print("Waiting 18 seconds for FollowUpScheduler to mark past due follow-up as Missed...")
        await pytest.importorskip("asyncio").sleep(18.0)

        # 10. List follow-ups again
        # The future one should still be Pending
        list_pending = await client.get(f"{BASE_URL}/projects/{proj_id}/follow-ups?status=Pending", headers=headers)
        assert list_pending.status_code == 200
        pendings = list_pending.json()
        # Find if our future followup is still in the pending list
        assert any(f["id"] == future_followup["id"] for f in pendings)
        # Verify the overdue one is no longer Pending
        assert not any(f["id"] == overdue_followup["id"] for f in pendings)

        # The overdue one should now be Completed or Missed
        list_missed = await client.get(f"{BASE_URL}/projects/{proj_id}/follow-ups?status=Missed", headers=headers)
        assert list_missed.status_code == 200
        missed = list_missed.json()
        
        list_completed = await client.get(f"{BASE_URL}/projects/{proj_id}/follow-ups?status=Completed", headers=headers)
        assert list_completed.status_code == 200
        completed = list_completed.json()
        
        processed_ids = [f["id"] for f in missed] + [f["id"] for f in completed]
        assert overdue_followup["id"] in processed_ids
