import pytest
import httpx
import uuid
import time
import json

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_customer_memory_generation_on_close():
    async with httpx.AsyncClient(timeout=15.0) as client:
        # Create Project
        proj_name = f"Mem_Project_{uuid.uuid4().hex[:6]}"
        create_proj = await client.post(f"{BASE_URL}/projects", json={"name": proj_name})
        assert create_proj.status_code == 201
        proj_id = create_proj.json()["id"]

        # Register User
        user_email = f"mem_user_{uuid.uuid4().hex[:6]}@smartcore.com"
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

        # 1. Send WhatsApp message expressing email preference
        sender_phone = f"555{uuid.uuid4().hex[:6]}"
        msg_resp1 = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex[:6]}",
                "sender": sender_phone,
                "content": "Please only contact me via email from now on.",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert msg_resp1.status_code == 200

        # Retrieve conversation and customer IDs
        get_convs = await client.get(f"{BASE_URL}/projects/{proj_id}/conversations", headers=headers)
        assert get_convs.status_code == 200
        conversations = get_convs.json()
        assert len(conversations) > 0
        conversation = conversations[0]
        conversation_id = conversation["id"]
        customer_id = conversation["customer"]["id"]

        # 2. Send another WhatsApp message in the same conversation expressing price objection
        msg_resp2 = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex[:6]}",
                "sender": sender_phone,
                "content": "Your services are a bit too expensive for us.",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert msg_resp2.status_code == 200

        # 3. Close the conversation
        close_resp = await client.put(
            f"{BASE_URL}/conversations/{conversation_id}/status",
            headers=headers,
            json={"status": "Closed"}
        )
        assert close_resp.status_code == 200

        # 4. Wait up to 5 seconds for the background RabbitMQ handler to process memory updates
        memory = None
        start_time = time.time()
        while time.time() - start_time < 5.0:
            get_mem = await client.get(f"{BASE_URL}/customers/{customer_id}/memory", headers=headers)
            if get_mem.status_code == 200:
                memory = get_mem.json()
                break
            time.sleep(0.5)

        # Assert customer memory is populated with the extracted facts and objections
        assert memory is not None
        assert memory["customerId"] == customer_id
        
        facts = json.loads(memory["factsJson"])
        objections = json.loads(memory["objectionsJson"])

        # Check keyword fallbacks/mock responses were triggered
        assert any("email" in f.lower() for f in facts)
        assert any("expensive" in o.lower() or "price" in o.lower() for o in objections)
