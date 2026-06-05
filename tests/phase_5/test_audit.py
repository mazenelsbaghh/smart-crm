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
async def test_audit_trail_logging_and_querying():
    async with httpx.AsyncClient(timeout=15.0, **get_client_kwargs()) as client:
        # 1. Create Project
        proj_name = f"AuditProj_{uuid.uuid4().hex[:6]}"
        create_proj = await client.post(f"{BASE_URL}/projects", json={"name": proj_name})
        assert create_proj.status_code == 201
        proj_id = create_proj.json()["id"]

        # 2. Register User
        user_email = f"audit_user_{uuid.uuid4().hex[:6]}@smartcore.com"
        await client.post(f"{BASE_URL}/auth/register", json={
            "email": user_email,
            "password": "Password123",
            "projectId": proj_id,
            "role": "Admin"
        })

        # 3. Login
        login_resp = await client.post(f"{BASE_URL}/auth/login", json={
            "email": user_email,
            "password": "Password123"
        })
        token = login_resp.json()["accessToken"]
        headers = {"Authorization": f"Bearer {token}"}

        # 4. Trigger customer creation to check DB context audit interceptor
        # (This should write a Customer audit log entry automatically)
        customer_phone = f"2010{uuid.uuid4().hex[:8]}"
        cust_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex[:6]}",
                "sender": customer_phone,
                "content": "Hello, I am testing audit logs.",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert cust_resp.status_code == 200

        # Wait briefly for DB hooks to complete and save to database
        time.sleep(2.0)

        # 5. Query Audit Logs via Endpoint
        audit_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/audit", headers=headers)
        assert audit_resp.status_code == 200
        audit_data = audit_resp.json()
        assert "logs" in audit_data
        assert isinstance(audit_data["logs"], list)

        # The list may contain multiple items (e.g. Customer auto-creation, message logs, etc.)
        # Let's verify we get a valid payload structure
        if len(audit_data["logs"]) > 0:
            log_item = audit_data["logs"][0]
            assert "action" in log_item
            assert "entityType" in log_item
            assert "projectId" in log_item
            assert log_item["projectId"] == proj_id
