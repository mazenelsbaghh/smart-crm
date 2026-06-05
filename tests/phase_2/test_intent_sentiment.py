import pytest
import httpx
import uuid
import time

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_intent_sentiment_lead_scoring_flow():
    sender_phone = f"555{uuid.uuid4().hex[:6]}"
    message_id = f"msg_{uuid.uuid4().hex}"
    
    # An angry complaint message which should decrease lead score and flag conversation as Pending
    mock_gemini_json = """
    {
      "intent": "complaint",
      "sentiment": "angry",
      "replyStyle": "Complaint",
      "entities": {},
      "replyContent": "We are very sorry for the issue. How can we resolve this?",
      "confidence": 0.95
    }
    """

    async with httpx.AsyncClient(timeout=25.0) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "SentimentProj"})
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
                "content": "I am extremely angry with your service! It is terrible!",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert webhook_resp.status_code == 200

        # 6. Wait for aggregation, processing and CRM worker (total 8 seconds)
        print("Waiting 8 seconds for sentiment analysis processing...")
        await pytest.importorskip("asyncio").sleep(8.0)

        # 7. Check if the customer's LeadScore was adjusted (since they started at default, check if score is 0 or decreased)
        customers_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/customers", headers=headers)
        assert customers_resp.status_code == 200
        customers = customers_resp.json()
        
        customer = next((c for c in customers if c["phoneNumber"] == sender_phone), None)
        assert customer is not None, "Customer record was not created"
        
        # Lead score should be 0 because default is 0 and we subtract (Math.Max(0, score))
        assert customer["leadScore"] == 0

        # 8. Check if the conversation status was updated to "Pending" (indicating flagged for human attention)
        convs_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/conversations", headers=headers)
        assert convs_resp.status_code == 200
        conversations = convs_resp.json()
        
        conversation = next((c for c in conversations if c["customer"]["id"] == customer["id"]), None)
        assert conversation is not None, "Conversation record was not created"
        assert conversation["status"] == "Pending", "Conversation was not flagged as Pending human attention"

        # 9. Verify that no pending follow-ups exist for this project (angry customer cancels/deletes pending follow-ups)
        followups_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/reports/follow-ups")
        assert followups_resp.status_code == 200
        followup_data = followups_resp.json()
        assert followup_data["pendingCount"] == 0
