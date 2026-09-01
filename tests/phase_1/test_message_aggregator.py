import pytest
import httpx
import uuid
import time
import asyncio
import os

BASE_URL = os.getenv("TEST_API_BASE_URL", "http://localhost:80/api")

@pytest.mark.asyncio
async def test_message_aggregation_flow():
    sender_phone = f"555{uuid.uuid4().hex[:6]}"

    async with httpx.AsyncClient(timeout=10.0) as client:
        # Create an explicitly test-scoped project so the configured short aggregation
        # window is used without coupling this behavior test to one queue transport.
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "AggregatorTestProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]

        email = f"aggregator_{uuid.uuid4().hex[:8]}@smartcore.com"
        register = await client.post(f"{BASE_URL}/auth/register", json={
            "email": email,
            "password": "Password123",
            "projectId": proj_id,
            "role": "Admin",
        })
        assert register.status_code in (200, 201), register.text
        login = await client.post(f"{BASE_URL}/auth/login", json={
            "email": email,
            "password": "Password123",
        })
        assert login.status_code == 200, login.text
        headers = {"Authorization": f"Bearer {login.json()['accessToken']}"}
        settings = await client.put(f"{BASE_URL}/projects/{proj_id}/settings", json={
            "aiAutoReplyEnabled": True,
            "timezone": "UTC",
            "geminiApiKey": "mock_api_key_for_testing",
        }, headers=headers)
        assert settings.status_code == 200, settings.text

        # Send 3 webhook messages in rapid succession (0.5s apart)
        contents = ["ممكن", "تفاصيل", "السعر"]
        for text in contents:
            webhook_resp = await client.post(
                f"{BASE_URL}/webhooks/whatsapp/message",
                json={
                    "projectId": proj_id,
                    "messageId": f"msg_{uuid.uuid4().hex}",
                    "sender": sender_phone,
                    "content": text,
                    "messageType": "Text",
                    "timestamp": int(time.time())
                }
            )
            assert webhook_resp.status_code == 200
            await asyncio.sleep(0.5)

        # The three incoming messages must produce one price-specific CRM analysis.
        # This proves the final aggregate included the last fragment without claiming
        # that a disconnected WhatsApp session delivered an outbound message.
        messages = []
        customer = None
        deadline = time.monotonic() + 40
        while time.monotonic() < deadline:
            conversations = await client.get(
                f"{BASE_URL}/projects/{proj_id}/conversations", headers=headers
            )
            assert conversations.status_code == 200, conversations.text
            if conversations.json():
                response = await client.get(
                    f"{BASE_URL}/conversations/{conversations.json()[0]['id']}/messages",
                    headers=headers,
                )
                assert response.status_code == 200, response.text
                messages = response.json()
            customers = await client.get(
                f"{BASE_URL}/projects/{proj_id}/customers", headers=headers
            )
            assert customers.status_code == 200, customers.text
            matches = [item for item in customers.json() if item["phoneNumber"] == sender_phone]
            customer = matches[0] if matches else None
            if customer and customer.get("label") == "استفسار عن السعر":
                break
            await asyncio.sleep(0.5)

        incoming = [message for message in messages if message["direction"] == "Incoming"]
        assert len(incoming) == 3
        assert customer is not None
        assert customer["label"] == "استفسار عن السعر"
