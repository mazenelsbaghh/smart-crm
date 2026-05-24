import pytest
import httpx
import uuid
import time

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_crm_and_dashboard_operations():
    async with httpx.AsyncClient(timeout=20.0) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "FrontendCRMProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]

        headers = {"X-Project-Id": proj_id}

        # 2. Get default pipeline stages (which will auto-seed because of our handler!)
        stages_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/pipelines/stages", headers=headers)
        assert stages_resp.status_code == 200
        stages = stages_resp.json()
        assert len(stages) >= 6
        assert any(s["name"] == "New" for s in stages)
        assert any(s["name"] == "Won" for s in stages)

        new_stage_id = next(s["id"] for s in stages if s["name"] == "New")
        proposal_stage_id = next(s["id"] for s in stages if s["name"] == "Proposal")

        # 3. Create a Customer in the project context
        # We trigger webhook to create customer
        sender_phone = f"555{uuid.uuid4().hex[:6]}"
        webhook_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex}",
                "sender": sender_phone,
                "content": "Hi, I am interested in buying!",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert webhook_resp.status_code == 200

        # Retrieve customers list for the project
        cust_list_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/customers", headers=headers)
        assert cust_list_resp.status_code == 200
        customers = cust_list_resp.json()
        assert len(customers) > 0
        customer = next(c for c in customers if c["phoneNumber"] == sender_phone)
        customer_id = customer["id"]
        assert customer["pipelineStage"] == "New"

        # 4. Update customer profile (budget, name, notes, tags, and change pipeline stage to Proposal)
        update_resp = await client.put(
            f"{BASE_URL}/customers/{customer_id}",
            headers=headers,
            json={
                "name": "Jane Customer",
                "city": "Paris",
                "leadScore": 50,
                "tags": ["Lead", "HighPriority"],
                "notes": "Follow up next week.",
                "budget": 12000.50,
                "pipelineStage": "Proposal"
            }
        )
        assert update_resp.status_code == 200
        updated_customer = update_resp.json()
        assert updated_customer["name"] == "Jane Customer"
        assert updated_customer["city"] == "Paris"
        assert updated_customer["leadScore"] == 50
        assert updated_customer["budget"] == 12000.50
        assert updated_customer["pipelineStage"] == "Proposal"
        assert "Lead" in updated_customer["tags"]

        # 5. Verify Deal was automatically created and updated to Proposal stage
        deals_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/deals", headers=headers)
        assert deals_resp.status_code == 200
        deals = deals_resp.json()
        assert len(deals) > 0
        active_deal = next(d for d in deals if d["customerId"] == customer_id)
        assert active_deal["amount"] == 12000.50
        assert active_deal["pipelineStageId"] == proposal_stage_id

        # 6. Test manual deal stage update endpoint
        stage_update_resp = await client.put(
            f"{BASE_URL}/deals/{active_deal['id']}/stage",
            headers=headers,
            json={"pipelineStageId": new_stage_id}
        )
        assert stage_update_resp.status_code == 200
        assert stage_update_resp.json()["pipelineStageId"] == new_stage_id

        # 7. Test manual deal status update endpoint
        status_update_resp = await client.put(
            f"{BASE_URL}/deals/{active_deal['id']}/status",
            headers=headers,
            json={"status": 1} # Won status is 1
        )
        assert status_update_resp.status_code == 200
        assert status_update_resp.json()["status"] == 1 # Won

        # 8. Test analytics recalculation endpoint
        recalc_resp = await client.post(f"{BASE_URL}/projects/{proj_id}/analytics/recalculate", headers=headers)
        assert recalc_resp.status_code == 200
