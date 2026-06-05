import pytest
import httpx
import uuid
import io
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
async def test_image_transformation_and_thumbnail_retrieval():
    async with httpx.AsyncClient(timeout=15.0, **get_client_kwargs()) as client:
        # 1. Create Project
        proj_name = f"MediaProj_{uuid.uuid4().hex[:6]}"
        create_proj = await client.post(f"{BASE_URL}/projects", json={"name": proj_name})
        assert create_proj.status_code == 201
        proj_id = create_proj.json()["id"]

        # 2. Register User
        user_email = f"media_user_{uuid.uuid4().hex[:6]}@smartcore.com"
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

        # 4. Upload Image File (using small valid 1x1 pixel GIF/PNG mock content)
        # 1x1 PNG transparent pixel hex values
        png_bytes = b'\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR\x00\x00\x00\x01\x00\x00\x00\x01\x08\x06\x00\x00\x00\x1f\x15c4\x00\x00\x00\rIDATx\x9cc`\x00\x01\x00\x00\x0c\x00\x01\x1c\xed\xee\x1d\x00\x00\x00\x00IEND\xaeB`\x82'
        files = {"file": ("test.png", io.BytesIO(png_bytes), "image/png")}
        data = {"projectId": proj_id}

        upload_resp = await client.post(f"{BASE_URL}/assets/upload", files=files, data=data, headers=headers)
        assert upload_resp.status_code == 201
        asset_info = upload_resp.json()
        asset_id = asset_info["id"]

        # 5. Wait for Hangfire job to execute (mock environment executes quickly)
        time.sleep(3.0)

        # 6. Retrieve Thumbnail (Should succeed or redirect)
        thumb_resp = await client.get(f"{BASE_URL}/assets/{asset_id}/thumbnail", headers=headers)
        assert thumb_resp.status_code in (200, 302, 307)
