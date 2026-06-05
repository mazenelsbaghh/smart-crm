import pytest
import httpx
import uuid
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
async def test_configure_and_trigger_integration():
    async with httpx.AsyncClient(timeout=15.0, **get_client_kwargs()) as client:
        # Create Project
        proj_name = f"Integ_Project_{uuid.uuid4().hex[:6]}"
        create_proj = await client.post(f"{BASE_URL}/projects", json={"name": proj_name})
        assert create_proj.status_code == 201
        proj_id = create_proj.json()["id"]

        # Register User
        user_email = f"integ_user_{uuid.uuid4().hex[:6]}@smartcore.com"
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

        # 1. Configure Integration
        config_payload = {
            "providerName": "CustomERP",
            "configJson": "{\"baseUrl\":\"https://erp.example.com/api\",\"apiKey\":\"secret_key_123\"}",
            "isActive": True,
            "syncIntervalMinutes": 60
        }
        configure_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/integrations",
            headers=headers,
            json=config_payload
        )
        assert configure_resp.status_code == 201
        integration = configure_resp.json()
        assert integration["providerName"] == "CustomERP"
        integration_id = integration["id"]

        # 2. Trigger Sync
        trigger_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/integrations/{integration_id}/sync",
            headers=headers
        )
        assert trigger_resp.status_code == 202
        assert trigger_resp.json()["message"] == "Sync job triggered successfully"
