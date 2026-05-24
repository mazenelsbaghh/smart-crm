import pytest
import httpx
import uuid
import time

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_human_messaging_flow():
    sender_phone = f"555{uuid.uuid4().hex[:6]}"
    message_id = f"msg_{uuid.uuid4().hex}"
    
    # AI returns a message with two distinct paragraphs to trigger chunking
    mock_gemini_json = """
    {
      "intent": "inquiry",
      "sentiment": "neutral",
      "replyStyle": "Casual",
      "entities": {},
      "replyContent": "First paragraph response.\\n\\nSecond paragraph response.",
      "confidence": 0.95
    }
    """

    async with httpx.AsyncClient(timeout=25.0) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "HumanMessagingProj"})
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
                "content": "Can you give me information in two parts?",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert webhook_resp.status_code == 200

        # 6. Wait for aggregation, processing, and chunking delays (approx 12 seconds to cover both delays)
        print("Waiting 12 seconds for message chunking and delays...")
        await pytest.importorskip("asyncio").sleep(12.0)

        # 7. Check sent messages
        sent_resp = await client.get(f"{BASE_URL}/whatsapp/mock/sent")
        assert sent_resp.status_code == 200
        sent_messages = [m for m in sent_resp.json() if m["to"] == sender_phone]

        # Verify that we received TWO chunks
        assert len(sent_messages) == 2, f"Expected 2 chunked messages, got {len(sent_messages)}"
        assert sent_messages[0]["message"] == "First paragraph response."
        assert sent_messages[1]["message"] == "Second paragraph response."
