import pytest
import httpx
import uuid
import io

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
async def test_asset_lifecycle_upload_download_deduplicate():
    async with httpx.AsyncClient(timeout=15.0, **get_client_kwargs()) as client:
        # 1. Create Project
        proj_name = f"AssetProj_{uuid.uuid4().hex[:6]}"
        create_proj = await client.post(f"{BASE_URL}/projects", json={"name": proj_name})
        assert create_proj.status_code == 201
        proj_id = create_proj.json()["id"]

        # 2. Register User
        user_email = f"asset_user_{uuid.uuid4().hex[:6]}@smartcore.com"
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

        # 4. Upload File (First time)
        file_content = b"This is a dummy asset file content for testing Phase 5."
        files = {"file": ("test.txt", io.BytesIO(file_content), "text/plain")}
        data = {"projectId": proj_id}

        upload_resp = await client.post(f"{BASE_URL}/assets/upload", files=files, data=data, headers=headers)
        assert upload_resp.status_code == 201
        asset_info = upload_resp.json()
        assert asset_info["fileName"] == "test.txt"
        assert asset_info["contentType"] == "text/plain"
        assert asset_info["fileSize"] == len(file_content)
        assert "id" in asset_info
        asset_id = asset_info["id"]

        # 5. Upload identical File (Deduplication Check)
        files_dup = {"file": ("test_duplicate.txt", io.BytesIO(file_content), "text/plain")}
        upload_dup_resp = await client.post(f"{BASE_URL}/assets/upload", files=files_dup, data=data, headers=headers)
        assert upload_dup_resp.status_code == 201
        asset_dup_info = upload_dup_resp.json()
        
        # Verify it returns the exact same Asset ID
        assert asset_dup_info["id"] == asset_id
        # Verify reference count in response (should be 2 now)
        assert asset_dup_info["referenceCount"] == 2

        # 6. Retrieve Download Signed URL
        download_resp = await client.get(f"{BASE_URL}/assets/{asset_id}/download", headers=headers)
        assert download_resp.status_code == 200
        download_info = download_resp.json()
        assert download_info["assetId"] == asset_id
        assert "downloadUrl" in download_info
        assert "minio" in download_info["downloadUrl"] or "localhost" in download_info["downloadUrl"]

        # 6b. Cross-Tenant Isolation Check
        # Create Project B
        proj_b_name = f"AssetProjB_{uuid.uuid4().hex[:6]}"
        create_proj_b = await client.post(f"{BASE_URL}/projects", json={"name": proj_b_name})
        assert create_proj_b.status_code == 201
        proj_b_id = create_proj_b.json()["id"]

        # Register User B
        user_b_email = f"asset_user_b_{uuid.uuid4().hex[:6]}@smartcore.com"
        await client.post(f"{BASE_URL}/auth/register", json={
            "email": user_b_email,
            "password": "Password123",
            "projectId": proj_b_id,
            "role": "Admin"
        })

        # Login User B
        login_b_resp = await client.post(f"{BASE_URL}/auth/login", json={
            "email": user_b_email,
            "password": "Password123"
        })
        token_b = login_b_resp.json()["accessToken"]
        headers_b = {"Authorization": f"Bearer {token_b}"}

        # Try to download Project A's asset using Project B's token (must fail with 403 Forbidden)
        download_forbidden = await client.get(f"{BASE_URL}/assets/{asset_id}/download", headers=headers_b)
        assert download_forbidden.status_code == 403

        # Try to delete Project A's asset using Project B's token (must fail with 403 Forbidden)
        delete_forbidden = await client.delete(f"{BASE_URL}/assets/{asset_id}", headers=headers_b)
        assert delete_forbidden.status_code == 403

        # 7. Delete Asset (First time: decrements reference count to 1)
        delete_resp1 = await client.delete(f"{BASE_URL}/assets/{asset_id}", headers=headers)
        assert delete_resp1.status_code == 200
        assert delete_resp1.json()["referenceCount"] == 1

        # 8. Delete Asset (Second time: reference count becomes 0, completely deleted)
        delete_resp2 = await client.delete(f"{BASE_URL}/assets/{asset_id}", headers=headers)
        assert delete_resp2.status_code == 204
