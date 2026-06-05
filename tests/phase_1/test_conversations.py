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


@pytest.mark.asyncio
async def test_webhook_reaction_ingestion():
    sender_phone = f"555{uuid.uuid4().hex[:6]}"
    message_id = f"msg_{uuid.uuid4().hex}"
    content = "[تفاعل] 👍"

    async with httpx.AsyncClient(timeout=10.0) as client:
        # Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "WebhookReactionProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]

        # Ingest message of type "Reaction" via Webhook
        webhook_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": message_id,
                "sender": sender_phone,
                "content": content,
                "messageType": "Reaction",
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

        # Check message content and messageType were persisted correctly
        msg_resp = await client.get(f"{BASE_URL}/conversations/{conv_id}/messages", headers=headers)
        assert msg_resp.status_code == 200
        messages = msg_resp.json()
        assert len(messages) == 1
        assert messages[0]["content"] == content
        assert messages[0]["messageType"] == "Reaction"
        assert messages[0]["direction"] == "Incoming"


@pytest.mark.asyncio
async def test_agent_send_reaction():
    sender_phone = f"555{uuid.uuid4().hex[:6]}"
    message_id = f"msg_to_react_{uuid.uuid4().hex}"
    content = "Please react to this message."

    async with httpx.AsyncClient(timeout=10.0) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "ReactionSendProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]

        # 2. Mock the gateway session as Connected for this Project
        mock_resp = await client.post(f"{BASE_URL}/whatsapp/session/mock", json={
            "projectId": proj_id,
            "status": "Connected",
            "phoneNumber": sender_phone
        })
        assert mock_resp.status_code == 200

        # 3. Clear mock sent messages to start fresh
        await client.post(f"{BASE_URL}/whatsapp/mock/clear")

        # 4. Ingest an incoming message
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

        # 5. Fetch conversations to get the conversation ID
        headers = {"X-Project-Id": proj_id}
        conv_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/conversations", headers=headers)
        assert conv_resp.status_code == 200
        conv_id = conv_resp.json()[0]["id"]

        # 6. Post reaction to the message
        react_resp = await client.post(
            f"{BASE_URL}/conversations/{conv_id}/messages/{message_id}/react",
            json={"reactionText": "💖"}
        )
        assert react_resp.status_code == 200
        react_data = react_resp.json()
        assert react_data["content"] == "[تفاعل] 💖"
        assert react_data["messageType"] == "Reaction"

        # 7. Check that the reaction was persisted in DB messages
        msg_resp = await client.get(f"{BASE_URL}/conversations/{conv_id}/messages", headers=headers)
        assert msg_resp.status_code == 200
        messages = msg_resp.json()
        
        # There should be 2 messages now: the original incoming text, and the agent's reaction
        assert len(messages) == 2
        assert messages[1]["content"] == "[تفاعل] 💖"
        assert messages[1]["messageType"] == "Reaction"
        assert messages[1]["direction"] == "Outgoing"

        # 8. Check that the gateway received the reaction request properly
        mock_sent_resp = await client.get(f"{BASE_URL}/whatsapp/mock/sent")
        assert mock_sent_resp.status_code == 200
        sent_messages = mock_sent_resp.json()
        assert len(sent_messages) == 1
        assert sent_messages[0]["reaction"] == "💖"
        assert sent_messages[0]["targetMessageId"] == message_id
        assert sent_messages[0]["targetFromMe"] is False


