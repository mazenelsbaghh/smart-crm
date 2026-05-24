import pytest
import httpx
import uuid
import time

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_crm_pipeline_and_deal_management():
    async with httpx.AsyncClient(timeout=15.0) as client:
        # Create Project
        proj_name = f"CRM_Project_{uuid.uuid4().hex[:6]}"
        create_proj = await client.post(f"{BASE_URL}/projects", json={"name": proj_name})
        assert create_proj.status_code == 201
        proj_id = create_proj.json()["id"]

        # Register User
        user_email = f"crm_user_{uuid.uuid4().hex[:6]}@smartcore.com"
        await client.post(f"{BASE_URL}/auth/register", json={
            "email": user_email,
            "password": "Password123",
            "projectId": proj_id,
            "role": "Admin"
        })

        # Login
        login_resp = await client.post(f"{BASE_URL}/auth/login", json={
            "email": user_email,
            "password": "Password123"
        })
        token = login_resp.json()["accessToken"]
        headers = {"Authorization": f"Bearer {token}"}

        # 1. Create a customer
        await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex[:6]}",
                "sender": "201088888888",
                "content": "I am interested in buying",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        get_custs = await client.get(f"{BASE_URL}/projects/{proj_id}/customers", headers=headers)
        customer_id = get_custs.json()[0]["id"]

        # 2. Create pipeline stages
        stage1_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/pipelines/stages",
            headers=headers,
            json={"name": "Proposal", "order": 1}
        )
        assert stage1_resp.status_code == 201
        stage1_id = stage1_resp.json()["id"]

        stage2_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/pipelines/stages",
            headers=headers,
            json={"name": "Negotiation", "order": 2}
        )
        assert stage2_resp.status_code == 201
        stage2_id = stage2_resp.json()["id"]

        # 3. Create a Deal
        deal_payload = {
            "customerId": customer_id,
            "title": "Summer Deal Package",
            "amount": 250000,
            "pipelineStageId": stage1_id
        }
        deal_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/deals",
            headers=headers,
            json=deal_payload
        )
        assert deal_resp.status_code == 201
        deal_id = deal_resp.json()["id"]
        assert deal_resp.json()["status"] == 0 # DealStatus.Open

        # 4. Update Deal Stage to Stage 2
        update_stage_resp = await client.put(
            f"{BASE_URL}/deals/{deal_id}/stage",
            headers=headers,
            json={"pipelineStageId": stage2_id}
        )
        assert update_stage_resp.status_code == 200
        assert update_stage_resp.json()["pipelineStageId"] == stage2_id

        # 5. Mark Deal as Won (status = 1)
        update_status_resp = await client.put(
            f"{BASE_URL}/deals/{deal_id}/status",
            headers=headers,
            json={"status": 1} # Won
        )
        assert update_status_resp.status_code == 200
        assert update_status_resp.json()["status"] == 1
        assert update_status_resp.json()["closedAt"] is not None
