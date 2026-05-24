import pytest
import httpx
import uuid
import time
import json
from datetime import datetime, timedelta

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_analytics_calculations_and_snapshots():
    async with httpx.AsyncClient(timeout=15.0) as client:
        # Create Project
        proj_name = f"Analytics_Project_{uuid.uuid4().hex[:6]}"
        create_proj = await client.post(f"{BASE_URL}/projects", json={"name": proj_name})
        assert create_proj.status_code == 201
        proj_id = create_proj.json()["id"]

        # Register User
        user_email = f"analytics_user_{uuid.uuid4().hex[:6]}@smartcore.com"
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

        # 1. Create a customer and deals to calculate Sales metrics
        cust_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex[:6]}",
                "sender": f"555{uuid.uuid4().hex[:6]}",
                "content": "Need pricing info",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert cust_resp.status_code == 200

        get_custs = await client.get(f"{BASE_URL}/projects/{proj_id}/customers", headers=headers)
        customer_id = get_custs.json()[0]["id"]

        # Create pipeline stages and deals
        # Create Pipeline Stage
        stage_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/pipelines/stages",
            headers=headers,
            json={"name": "Qualified", "order": 1}
        )
        assert stage_resp.status_code == 201 or stage_resp.status_code == 200
        stage_id = stage_resp.json()["id"]

        # Create Deal
        deal_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/deals",
            headers=headers,
            json={
                "customerId": customer_id,
                "title": "Summer Deal Package",
                "amount": 250000,
                "pipelineStageId": stage_id,
                "status": 1 # Won
            }
        )
        assert deal_resp.status_code == 201

        # 2. Trigger analytics recalculation manually
        recalc_resp = await client.post(f"{BASE_URL}/projects/{proj_id}/analytics/recalculate", headers=headers)
        assert recalc_resp.status_code == 200

        # 3. Verify snapshot values are saved and correct
        get_sales = await client.get(f"{BASE_URL}/projects/{proj_id}/analytics/Sales", headers=headers)
        assert get_sales.status_code == 200
        sales_data = get_sales.json()
        assert len(sales_data) > 0
        assert sales_data[0]["metricValue"] >= 0
        metadata = json.loads(sales_data[0]["metadataJson"])
        assert "totalDeals" in metadata
