import pytest
import httpx
import uuid
import time

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_reports_api_endpoints():
    async with httpx.AsyncClient(timeout=20.0) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "ReportsProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]
        headers = {"X-Project-Id": proj_id}

        # 2. Ingest message to create one Customer & Conversation
        phone = f"555{uuid.uuid4().hex[:6]}"
        webhook_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex}",
                "sender": phone,
                "content": "Need pricing report",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert webhook_resp.status_code == 200

        # Fetch customer to get ID
        customers_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/customers", headers=headers)
        assert customers_resp.status_code == 200
        customer = next((c for c in customers_resp.json() if c["phoneNumber"] == phone), None)
        assert customer is not None
        customer_id = customer["id"]

        # Create one follow-up
        followup_resp = await client.post(
            f"{BASE_URL}/customers/{customer_id}/follow-ups",
            headers=headers,
            json={
                "dueDate": "2026-06-01T12:00:00Z",
                "notes": "Testing reports data"
            }
        )
        assert followup_resp.status_code == 201

        # 3. Query Daily Operations Report
        daily_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/reports/daily-operations")
        assert daily_resp.status_code == 200
        daily_data = daily_resp.json()
        assert daily_data["projectId"] == proj_id
        assert "date" in daily_data
        assert daily_data["totalConversations"] >= 1
        assert daily_data["activeConversations"] >= 1
        assert "completedConversations" in daily_data
        assert "missedFollowUps" in daily_data
        assert "aiAutoRepliesSent" in daily_data

        # 4. Query Follow-ups Report
        followup_report_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/reports/follow-ups")
        assert followup_report_resp.status_code == 200
        followup_data = followup_report_resp.json()
        assert followup_data["projectId"] == proj_id
        assert followup_data["pendingCount"] >= 1
        assert "missedCount" in followup_data
        assert "completedCount" in followup_data

        # 5. Query AI Performance Report
        ai_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/reports/ai-performance")
        assert ai_resp.status_code == 200
        ai_data = ai_resp.json()
        assert ai_data["projectId"] == proj_id
        assert "averageResponseTimeMs" in ai_data
        assert "accuracyScore" in ai_data
        assert "totalTokenUsage" in ai_data
        assert ai_data["accuracyScore"] >= 0.0
