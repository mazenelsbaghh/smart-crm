import pytest
import httpx
import uuid
import time

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_ai_gemini_auto_reply_flow():
    sender_phone = f"555{str(uuid.uuid4().int)[:6]}"
    message_id = f"msg_{uuid.uuid4().hex}"
    incoming_content = "Hello, I want to know about your services."

    # We send structured JSON as the mock Gemini response
    mock_gemini_json = """
    {
      "intent": "inquiry",
      "sentiment": "neutral",
      "replyStyle": "Casual",
      "entities": {
        "city": null,
        "budget": null,
        "interests": [],
        "timeline": null
      },
      "replyContent": "[Mock Gemini Reply] Re: Hello, I want to know about your services.",
      "confidence": 0.95
    }
    """

    async with httpx.AsyncClient(timeout=20.0) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "AiTestProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]

        # 2. Update Settings to ENABLE AI auto-reply
        headers = {"X-Project-Id": proj_id}
        settings_resp = await client.put(f"{BASE_URL}/projects/{proj_id}/settings", headers=headers, json={
            "aiAutoReplyEnabled": True,
            "timezone": "UTC",
            "geminiApiKey": f"mock_json_{mock_gemini_json}"
        })
        assert settings_resp.status_code == 200

        # 3. Setup mock WhatsApp session and connect it
        start_resp = await client.post(f"{BASE_URL}/whatsapp/session/start", json={
            "projectId": proj_id
        })
        assert start_resp.status_code == 200

        mock_resp = await client.post(f"{BASE_URL}/whatsapp/session/mock", json={
            "projectId": proj_id,
            "status": "Connected",
            "phoneNumber": "1234567890"
        })
        assert mock_resp.status_code == 200

        # 4. Clear any existing mock sent messages
        clear_resp = await client.post(f"{BASE_URL}/whatsapp/mock/clear")
        assert clear_resp.status_code == 200

        # 5. Ingest incoming message via webhook
        webhook_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": message_id,
                "sender": sender_phone,
                "content": incoming_content,
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert webhook_resp.status_code == 200

        # 6. Wait for the 5-second silence window + processing buffer + typing delay (total 10 seconds)
        print("Waiting 10 seconds for message aggregation and AI reply generation...")
        await pytest.importorskip("asyncio").sleep(10.0)

        # 7. Check if the gateway received the AI reply
        sent_resp = await client.get(f"{BASE_URL}/whatsapp/mock/sent")
        assert sent_resp.status_code == 200
        sent_messages = sent_resp.json()

        # Find the message sent to our sender_phone
        reply_message = next((m for m in sent_messages if m["to"] == sender_phone), None)
        assert reply_message is not None, "AI Reply was not sent to WhatsApp Gateway"
        
        # Verify it has the mock Gemini signature
        assert "[Mock Gemini Reply]" in reply_message["message"]
        assert incoming_content in reply_message["message"]

        # 8. Now DISABLE AI auto-reply
        settings_resp = await client.put(f"{BASE_URL}/projects/{proj_id}/settings", headers=headers, json={
            "aiAutoReplyEnabled": False,
            "timezone": "UTC",
            "geminiApiKey": "mock_test_key"
        })
        assert settings_resp.status_code == 200

        # Clear mock sent messages
        clear_resp = await client.post(f"{BASE_URL}/whatsapp/mock/clear")
        assert clear_resp.status_code == 200

        # Ingest another incoming message
        new_message_id = f"msg_{uuid.uuid4().hex}"
        webhook_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": new_message_id,
                "sender": sender_phone,
                "content": "Is anyone there?",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert webhook_resp.status_code == 200

        # Wait again for aggregation window (total 10 seconds)
        print("Waiting 10 seconds again (with AI disabled)...")
        await pytest.importorskip("asyncio").sleep(10.0)

        # Check mock sent messages again - should be empty
        sent_resp = await client.get(f"{BASE_URL}/whatsapp/mock/sent")
        assert sent_resp.status_code == 200
        sent_messages = sent_resp.json()
        
        reply_message_disabled = next((m for m in sent_messages if m["to"] == sender_phone), None)
        assert reply_message_disabled is None, "AI Reply was sent even though auto-reply settings was disabled!"
