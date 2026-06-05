import pytest
import httpx
import uuid
import time
import json
import socket

def get_base_urls():
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.settimeout(1.0)
    try:
        s.connect(("localhost", 443))
        s.close()
        return "https://localhost:443/api", True
    except Exception:
        return "http://localhost:80/api", False

BASE_URL, IS_HTTPS = get_base_urls()

def get_client_kwargs():
    kwargs = {}
    if IS_HTTPS:
        kwargs["verify"] = False
    return kwargs


@pytest.mark.asyncio
async def test_customer_memory_generation_on_close():
    async with httpx.AsyncClient(timeout=15.0, **get_client_kwargs()) as client:
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

        print(f"DEBUG facts: {facts}")
        print(f"DEBUG objections: {objections}")

        # Check keyword fallbacks/mock responses were triggered
        assert any("email" in f.lower() for f in facts)
        assert any("expensive" in o.lower() or "price" in o.lower() for o in objections)


@pytest.mark.asyncio
async def test_manual_customer_memory_generation():
    async with httpx.AsyncClient(timeout=15.0, **get_client_kwargs()) as client:
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

        cust_phone = f"555{uuid.uuid4().hex[:6]}"

        # 1. Trigger AI profile generation with a random customer ID (non-existent). Should return 400 Bad Request
        non_existent_customer_id = str(uuid.uuid4())
        gen_err_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/customers/{non_existent_customer_id}/memory/generate",
            headers=headers
        )
        assert gen_err_resp.status_code == 400
        assert "لا توجد رسائل سابقة" in gen_err_resp.text

        # 2. Send a WhatsApp webhook message to create a customer, conversation and message history
        msg_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex[:6]}",
                "sender": cust_phone,
                "content": "معاك أدهم مدبولي، عايز احجز الدورة المكثفة للشحن للقاهرة؟",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert msg_resp.status_code == 200

        # Retrieve the auto-created customer ID
        get_convs = await client.get(f"{BASE_URL}/projects/{proj_id}/conversations", headers=headers)
        assert get_convs.status_code == 200
        conversations = get_convs.json()
        assert len(conversations) > 0
        customer_id = conversations[0]["customer"]["id"]

        # 3. Trigger manual AI profile generation. Should succeed (200 OK)
        gen_success_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/customers/{customer_id}/memory/generate",
            headers=headers
        )
        assert gen_success_resp.status_code == 200
        memory_data = gen_success_resp.json()
        assert memory_data["customerId"] == customer_id

        # Verify parsed fields from mock/real AI
        facts_list = json.loads(memory_data["factsJson"])
        objections_list = json.loads(memory_data["objectionsJson"])
        triggers_list = json.loads(memory_data["triggersJson"])
        summary_text = memory_data["longTermSummary"]

        assert any("القاهرة" in f or "واتساب" in f or "دورة" in f for f in facts_list)
        assert any("السعر" in o or "مرتفع" in o for o in objections_list)
        assert any("خصم" in t or "البدء" in t for t in triggers_list)
        assert "عميل مهتم" in summary_text

        # 4. Double check database via GET memory endpoint
        get_mem = await client.get(f"{BASE_URL}/customers/{customer_id}/memory", headers=headers)
        assert get_mem.status_code == 200
        get_mem_data = get_mem.json()
        assert get_mem_data["longTermSummary"] == summary_text

        # 5. Verify customer details update
        get_cust = await client.get(f"{BASE_URL}/customers/{customer_id}", headers=headers)
        assert get_cust.status_code == 200
        cust_data = get_cust.json()
        assert cust_data["name"] == "أدهم مدبولي"
        assert cust_data["city"] == "القاهرة"
        assert cust_data["budget"] == 1500
        assert cust_data["leadScore"] == 85
        assert cust_data["pipelineStage"] == "Proposal"
        assert cust_data["label"] == "طلب حجز"

