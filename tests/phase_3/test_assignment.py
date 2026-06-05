import pytest
import httpx
import uuid
import time
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
async def test_advanced_routing_and_reassignment():
    async with httpx.AsyncClient(timeout=15.0, **get_client_kwargs()) as client:
        # Create Project
        proj_name = f"Route_Project_{uuid.uuid4().hex[:6]}"
        create_proj = await client.post(f"{BASE_URL}/projects", json={"name": proj_name})
        assert create_proj.status_code == 201
        proj_id = create_proj.json()["id"]

        # Register Admin
        admin_email = f"admin_{uuid.uuid4().hex[:6]}@smartcore.com"
        await client.post(f"{BASE_URL}/auth/register", json={
            "email": admin_email,
            "password": "Password123",
            "projectId": proj_id,
            "role": "Admin"
        })

        # Register Supervisor
        super_email = f"supervisor_{uuid.uuid4().hex[:6]}@smartcore.com"
        await client.post(f"{BASE_URL}/auth/register", json={
            "email": super_email,
            "password": "Password123",
            "projectId": proj_id,
            "role": "Supervisor"
        })

        # Register Normal Agent
        agent_email = f"agent_{uuid.uuid4().hex[:6]}@smartcore.com"
        await client.post(f"{BASE_URL}/auth/register", json={
            "email": agent_email,
            "password": "Password123",
            "projectId": proj_id,
            "role": "Agent"
        })

        # Login as Admin
        login_resp = await client.post(f"{BASE_URL}/auth/login", json={
            "email": admin_email,
            "password": "Password123"
        })
        token = login_resp.json()["accessToken"]
        headers = {"Authorization": f"Bearer {token}"}

        # 1. Normal customer message -> should auto-route to eligible agent
        phone1 = f"999{uuid.uuid4().hex[:6]}"
        msg_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex[:6]}",
                "sender": phone1,
                "content": "Hello, normal inquiry",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert msg_resp.status_code == 200

        # Retrieve normal customer conversation and assert it got assigned (fallbacks to offline agent if none online)
        get_custs_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/customers", headers=headers)
        assert get_custs_resp.status_code == 200
        customers = get_custs_resp.json()
        assert len(customers) > 0
        customer = next(c for c in customers if c["phoneNumber"] == phone1)
        customer_id = customer["id"]

        get_convs_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/conversations", headers=headers)
        assert get_convs_resp.status_code == 200
        conversations = get_convs_resp.json()
        conv = next(c for c in conversations if c["customer"]["id"] == customer_id)
        assert conv["assignedAgentId"] is not None

        # 2. VIP customer (LeadScore >= 80) -> should assign to Admin/Owner
        phone2 = f"777{uuid.uuid4().hex[:6]}"
        
        # Send initial message to create customer dynamically
        msg_resp_init = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_vip_init_{uuid.uuid4().hex[:6]}",
                "sender": phone2,
                "content": "Initial contact",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert msg_resp_init.status_code == 200

        # Retrieve the auto-created customer ID
        get_custs_resp2 = await client.get(f"{BASE_URL}/projects/{proj_id}/customers", headers=headers)
        assert get_custs_resp2.status_code == 200
        vip_customer = next(c for c in get_custs_resp2.json() if c["phoneNumber"] == phone2)
        vip_cust_id = vip_customer["id"]

        # Retrieve the initial conversation ID
        get_convs_init = await client.get(f"{BASE_URL}/projects/{proj_id}/conversations", headers=headers)
        assert get_convs_init.status_code == 200
        vip_conv_init = next(c for c in get_convs_init.json() if c["customer"]["id"] == vip_cust_id)
        vip_conv_init_id = vip_conv_init["id"]

        # Close the initial conversation thread
        close_conv_resp = await client.put(
            f"{BASE_URL}/conversations/{vip_conv_init_id}/status",
            headers=headers,
            json={"status": "Closed"}
        )
        assert close_conv_resp.status_code == 200

        # Update customer LeadScore to 85 to qualify as VIP
        update_cust_resp = await client.put(
            f"{BASE_URL}/customers/{vip_cust_id}",
            headers=headers,
            json={
                "name": "VIP Customer",
                "city": "Cairo",
                "leadScore": 85,
                "tags": [],
                "notes": "Very Important Person"
            }
        )
        assert update_cust_resp.status_code == 200

        # Send message from VIP
        msg_resp2 = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex[:6]}",
                "sender": phone2,
                "content": "Need assistance now",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert msg_resp2.status_code == 200

        # Retrieve conversation and assert assigned user is Admin (role Admin)
        get_convs_resp2 = await client.get(f"{BASE_URL}/projects/{proj_id}/conversations", headers=headers)
        vip_conv = next(c for c in get_convs_resp2.json() if c["customer"]["id"] == vip_cust_id)
        assert vip_conv["assignedAgentId"] is not None
        
        # Verify the assigned user is indeed the Admin or Supervisor (not the normal agent)
        assigned_user_id = vip_conv["assignedAgentId"]
        users_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/users", headers=headers)
        assert users_resp.status_code == 200
        assigned_user = next(u for u in users_resp.json() if u["id"] == assigned_user_id)
        assert assigned_user["role"] in ["Admin", "Owner", "Supervisor"]
