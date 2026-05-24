import pytest
import httpx
import uuid
import time

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_crm_auto_update_confidence_flow():
    sender_phone = f"555{uuid.uuid4().hex[:6]}"
    message_id = f"msg_{uuid.uuid4().hex}"
    
    # Low confidence (0.5) mock JSON to test the PendingApproval logic
    mock_gemini_json = """
    {
      "intent": "purchase",
      "sentiment": "positive",
      "replyStyle": "Sales",
      "entities": {
        "city": "Alexandria"
      },
      "replyContent": "Sure, tell me more about Alexandria.",
      "confidence": 0.5
    }
    """

    async with httpx.AsyncClient(timeout=25.0) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "CRMUpdateProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]

        # 2. Update Settings
        headers = {"X-Project-Id": proj_id}
        settings_resp = await client.put(f"{BASE_URL}/projects/{proj_id}/settings", headers=headers, json={
            "aiAutoReplyEnabled": True,
            "timezone": "UTC",
            "geminiApiKey": f"mock_json_{mock_gemini_json}"
        })
        assert settings_resp.status_code == 200

        # 3. Setup WhatsApp session
        start_resp = await client.post(f"{BASE_URL}/whatsapp/session/start", json={
            "projectId": proj_id
        })
        assert start_resp.status_code == 200

        await client.post(f"{BASE_URL}/whatsapp/session/mock", json={
            "projectId": proj_id,
            "status": "Connected",
            "phoneNumber": "1234567890"
        })

        # 4. Clear mock sent messages
        await client.post(f"{BASE_URL}/whatsapp/mock/clear")

        # 5. Ingest message
        webhook_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": message_id,
                "sender": sender_phone,
                "content": "Alexandria is a nice city.",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert webhook_resp.status_code == 200

        # 6. Wait for aggregation, processing and CRM worker (total 8 seconds)
        print("Waiting 8 seconds for CRM auto update processing...")
        await pytest.importorskip("asyncio").sleep(8.0)

        # 7. Check if customer record exists but City remains null (due to low confidence)
        customers_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/customers", headers=headers)
        assert customers_resp.status_code == 200
        customers = customers_resp.json()
        
        customer = next((c for c in customers if c["phoneNumber"] == sender_phone), None)
        assert customer is not None, "Customer record was not created"
        
        # City MUST remain null/empty because confidence was 0.5 (< 0.8 threshold)
        assert customer["city"] is None or customer["city"] == ""

        # 8. Check that a CRMUpdateProposal was created with status 'PendingApproval'
        proposals_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/crm-proposals", headers=headers)
        assert proposals_resp.status_code == 200
        proposals = proposals_resp.json()
        
        proposal = next((p for p in proposals if p["customerId"] == customer["id"] and p["fieldName"] == "City"), None)
        assert proposal is not None, "CRMUpdateProposal was not created for low confidence suggestion"
        assert proposal["status"] == "PendingApproval"
        assert proposal["suggestedValue"] == "Alexandria"
        assert float(proposal["confidenceScore"]) == 0.5
