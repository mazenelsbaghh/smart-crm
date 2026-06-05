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
async def test_workflows_trigger_and_action():
    async with httpx.AsyncClient(timeout=15.0, **get_client_kwargs()) as client:
        # Create Project
        proj_name = f"WF_Project_{uuid.uuid4().hex[:6]}"
        create_proj = await client.post(f"{BASE_URL}/projects", json={"name": proj_name})
        assert create_proj.status_code == 201
        proj_id = create_proj.json()["id"]

        # Register User
        user_email = f"wf_user_{uuid.uuid4().hex[:6]}@smartcore.com"
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

        # 1. Create Customer in CRM
        cust_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex[:6]}",
                "sender": f"555{uuid.uuid4().hex[:6]}",
                "content": "Hello there!",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert cust_resp.status_code == 200

        # Retrieve the newly created customer
        get_custs_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/customers", headers=headers)
        assert get_custs_resp.status_code == 200
        customers = get_custs_resp.json()
        assert len(customers) > 0
        customer = customers[0]
        customer_id = customer["id"]

        # 2. Configure a Workflow trigger: when tag "VIP" is added, update leadScore to 95 and status to "Hot Lead"
        workflow_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/workflows",
            headers=headers,
            json={
                "name": "Auto VIP Lead Promoter",
                "triggerType": "CustomerTagAdded",
                "filtersJson": "{\"tag\":\"VIP\"}",
                "actionsJson": "[{\"type\":\"UpdateCRM\",\"parameters\":{\"leadScore\":95,\"notes\":\"Promoted automatically by VIP Lead Promoter workflow\"}}]",
                "isActive": True
            }
        )
        assert workflow_resp.status_code == 201

        # 3. Simulate tag addition by updating the customer tags to include "VIP"
        update_cust_resp = await client.put(
            f"{BASE_URL}/customers/{customer_id}",
            headers=headers,
            json={
                "name": customer["name"],
                "city": customer.get("city") or "Cairo",
                "leadScore": customer.get("leadScore") or 10,
                "tags": ["VIP"],
                "notes": customer.get("notes") or "VIP customer update"
            }
        )
        assert update_cust_resp.status_code == 200

        # 4. Wait up to 5 seconds for the background RabbitMQ consumer workflow execution to fire
        updated_customer = None
        start_time = time.time()
        while time.time() - start_time < 5.0:
            get_cust_resp = await client.get(f"{BASE_URL}/customers/{customer_id}", headers=headers)
            assert get_cust_resp.status_code == 200
            updated_customer = get_cust_resp.json()
            if updated_customer.get("leadScore") == 95:
                break
            time.sleep(0.5)

        # Assert workflow was triggered and updated CRM
        assert updated_customer is not None
        assert updated_customer["leadScore"] == 95
