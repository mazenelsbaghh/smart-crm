import pytest
import httpx
import uuid
import time

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_webhook_ingestion():
    sender_phone = f"555{uuid.uuid4().hex[:6]}"
    message_id = f"msg_{uuid.uuid4().hex}"
    content = "Hello there! I want to inquire about your product."

    async with httpx.AsyncClient(timeout=10.0) as client:
        # Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "WebhookTestProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]

        # Ingest message via Webhook
        webhook_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": message_id,
                "sender": sender_phone,
                "content": content,
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert webhook_resp.status_code == 200
        assert webhook_resp.json()["status"] == "Received"

        # Check that conversation was created
        headers = {"X-Project-Id": proj_id}
        conv_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/conversations", headers=headers)
        assert conv_resp.status_code == 200
        conversations = conv_resp.json()
        assert len(conversations) == 1
        conv_id = conversations[0]["id"]

        # Check message content was persisted
        msg_resp = await client.get(f"{BASE_URL}/conversations/{conv_id}/messages", headers=headers)
        assert msg_resp.status_code == 200
        messages = msg_resp.json()
        assert len(messages) == 1
        assert messages[0]["content"] == content
        assert messages[0]["direction"] == "Incoming"
