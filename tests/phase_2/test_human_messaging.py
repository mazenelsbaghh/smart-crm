import pytest
import httpx
import uuid
import time

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_human_messaging_flow():
    sender_phone = f"555{uuid.uuid4().int % 1000000:06d}"
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

@pytest.mark.asyncio
async def test_human_messaging_blacklist():
    sender_phone = f"555{uuid.uuid4().int % 1000000:06d}"
    
    mock_gemini_json = """
    {
      "intent": "inquiry",
      "sentiment": "neutral",
      "replyStyle": "Casual",
      "entities": {},
      "replyContent": "Blacklisted response should not be sent.",
      "confidence": 0.95
    }
    """

    async with httpx.AsyncClient(timeout=25.0) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "HumanMessagingBlacklistProj"})
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

        # 5. Ingest first message to create customer
        webhook_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_init_{uuid.uuid4().hex}",
                "sender": sender_phone,
                "content": "Initialize customer to database",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert webhook_resp.status_code == 200

        # 6. Fetch customers and blacklist this customer
        cust_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/customers", headers=headers)
        assert cust_resp.status_code == 200
        customers = cust_resp.json()
        target_cust = next(c for c in customers if c["phoneNumber"] == sender_phone)
        cust_id = target_cust["id"]

        update_resp = await client.put(f"{BASE_URL}/customers/{cust_id}", headers=headers, json={
            "isBlacklisted": True
        })
        assert update_resp.status_code == 200
        assert update_resp.json()["isBlacklisted"] is True

        # 7. Clear mock sent messages (clear the initialization auto-reply if any got generated)
        await client.post(f"{BASE_URL}/whatsapp/mock/clear")

        # 8. Ingest test message while blacklisted
        webhook_resp2 = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_test_{uuid.uuid4().hex}",
                "sender": sender_phone,
                "content": "This is a test message from a blacklisted customer",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert webhook_resp2.status_code == 200

        # 9. Wait 6 seconds (enough time for aggregation and processing, but should skip)
        print("Waiting 6 seconds to ensure no reply is generated...")
        await pytest.importorskip("asyncio").sleep(6.0)

        # 10. Check sent messages - should be 0
        sent_resp = await client.get(f"{BASE_URL}/whatsapp/mock/sent")
        assert sent_resp.status_code == 200
        sent_messages = [m for m in sent_resp.json() if m["to"] == sender_phone]

        assert len(sent_messages) == 0, f"Expected 0 messages due to blacklist, got {len(sent_messages)}"

        # 11. Verify that no pending follow-ups exist for this project (blacklisted customer skips scheduling)
        followups_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/reports/follow-ups")
        assert followups_resp.status_code == 200
        followup_data = followups_resp.json()
        assert followup_data["pendingCount"] == 0

