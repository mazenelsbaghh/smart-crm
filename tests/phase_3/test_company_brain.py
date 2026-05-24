import pytest
import httpx
import uuid

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_company_brain_sync_and_search():
    async with httpx.AsyncClient(timeout=15.0) as client:
        # Create Project
        proj_name = f"Brain_Project_{uuid.uuid4().hex[:6]}"
        create_proj = await client.post(f"{BASE_URL}/projects", json={"name": proj_name})
        assert create_proj.status_code == 201
        proj_data = create_proj.json()
        proj_id = proj_data["id"]

        # Register User
        user_email = f"brain_user_{uuid.uuid4().hex[:6]}@smartcore.com"
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
        assert login_resp.status_code == 200
        token = login_resp.json()["accessToken"]
        headers = {"Authorization": f"Bearer {token}"}

        # 1. Test Sync Brain (should ingest mock documents)
        sync_resp = await client.post(f"{BASE_URL}/projects/{proj_id}/brain/sync", headers=headers)
        # Note: If not implemented, this should fail (e.g. 404 or 500)
        # Once implemented, it should return 200/202
        assert sync_resp.status_code == 202 or sync_resp.status_code == 200

        # 2. Test Search Brain
        search_resp = await client.get(
            f"{BASE_URL}/projects/{proj_id}/brain/search?q=subscription",
            headers=headers
        )
        assert search_resp.status_code == 200
        search_data = search_resp.json()
        assert isinstance(search_data, list)
