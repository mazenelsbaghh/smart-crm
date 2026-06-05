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
async def test_incoming_voice_note_transcription_and_reply():
    async with httpx.AsyncClient(timeout=30.0, **get_client_kwargs()) as client:
        # 1. Create Project
        proj_name = f"MediaAIProj_{uuid.uuid4().hex[:6]}"
        create_proj = await client.post(f"{BASE_URL}/projects", json={"name": proj_name})
        assert create_proj.status_code == 201
        proj_id = create_proj.json()["id"]

        # 2. Register User
        user_email = f"media_ai_user_{uuid.uuid4().hex[:6]}@smartcore.com"
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

        # Enable AI Auto-Reply and set a mock key in Project Settings
        # (This enables the worker reply loop)
        settings_resp = await client.put(f"{BASE_URL}/projects/{proj_id}/settings", json={
            "aiAutoReplyEnabled": True,
            "timezone": "UTC",
            "geminiApiKey": "mock_api_key_for_testing"
        }, headers=headers)
        assert settings_resp.status_code == 200

        # 4. Upload Dummy audio file
        audio_content = b"Mock voice note ogg stream binary data"
        files = {"file": ("voice.ogg", io.BytesIO(audio_content), "audio/ogg")}
        
        # Call anonymous endpoint directly used by Baileys gateway
        upload_resp = await client.post(f"{BASE_URL}/projects/{proj_id}/assets/upload", files=files)
        assert upload_resp.status_code == 201
        asset_id = upload_resp.json()["id"]

        # 5. Mock gateway message webhook with voice message type and asset ID
        sender_phone = f"2012{uuid.uuid4().hex[:8]}"
        webhook_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex[:6]}",
                "sender": sender_phone,
                "content": "[Voice Note]",
                "messageType": "Voice",
                "timestamp": int(time.time()),
                "assetId": asset_id
            }
        )
        assert webhook_resp.status_code == 200

        # 6. Wait for Hangfire/RabbitMQ background processing and AI completion
        time.sleep(5.0)

        # 7. Check conversation messages
        get_convs = await client.get(f"{BASE_URL}/projects/{proj_id}/conversations", headers=headers)
        assert get_convs.status_code == 200
        convs = get_convs.json()
        assert len(convs) > 0
        conv_id = convs[0]["id"]

        get_msgs = await client.get(f"{BASE_URL}/conversations/{conv_id}/messages", headers=headers)
        assert get_msgs.status_code == 200
        msgs = get_msgs.json()
        
        # Verify incoming voice note message has assetId and transcription
        voice_msg = next((m for m in msgs if m["direction"] == "Incoming"), None)
        assert voice_msg is not None
        assert voice_msg["assetId"] == asset_id
        assert voice_msg["mediaType"] == "Voice"
        # Since we ran in mock mode, it returns the mock transcription
        assert voice_msg["transcription"] == "أنا مهتم بكورس الذكاء الاصطناعي وبدي أعرف السعر"

        # Verify AI auto-reply is generated in response
        ai_msg = next((m for m in msgs if m["senderType"] == "AI"), None)
        assert ai_msg is not None
        assert "سعر كورس الذكاء الاصطناعي" in ai_msg["content"]

        # 8. Check pre-signed S3 URL endpoint
        url_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/assets/{asset_id}/url", headers=headers)
        assert url_resp.status_code == 200
        url_data = url_resp.json()
        assert url_data["assetId"] == asset_id
        assert "url" in url_data
        assert "minio" in url_data["url"] or "localhost" in url_data["url"] or "localhost" in url_data["url"]


@pytest.mark.asyncio
async def test_incoming_image_understanding_and_reply():
    async with httpx.AsyncClient(timeout=30.0, **get_client_kwargs()) as client:
        # 1. Create Project
        proj_name = f"MediaAIProj_{uuid.uuid4().hex[:6]}"
        create_proj = await client.post(f"{BASE_URL}/projects", json={"name": proj_name})
        assert create_proj.status_code == 201
        proj_id = create_proj.json()["id"]

        # 2. Register User
        user_email = f"media_ai_user_{uuid.uuid4().hex[:6]}@smartcore.com"
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

        # Enable AI Auto-Reply
        settings_resp = await client.put(f"{BASE_URL}/projects/{proj_id}/settings", json={
            "aiAutoReplyEnabled": True,
            "timezone": "UTC",
            "geminiApiKey": "mock_api_key_for_testing"
        }, headers=headers)
        assert settings_resp.status_code == 200

        # 4. Upload transparent 1x1 image
        png_bytes = b'\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR\x00\x00\x00\x01\x00\x00\x00\x01\x08\x06\x00\x00\x00\x1f\x15c4\x00\x00\x00\rIDATx\x9cc`\x00\x01\x00\x00\x0c\x00\x01\x1c\xed\xee\x1d\x00\x00\x00\x00IEND\xaeB`\x82'
        files = {"file": ("receipt.png", io.BytesIO(png_bytes), "image/png")}
        upload_resp = await client.post(f"{BASE_URL}/projects/{proj_id}/assets/upload", files=files)
        assert upload_resp.status_code == 201
        asset_id = upload_resp.json()["id"]

        # 5. Mock gateway message webhook with image message
        sender_phone = f"2012{uuid.uuid4().hex[:8]}"
        webhook_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex[:6]}",
                "sender": sender_phone,
                "content": "[Image]",
                "messageType": "Image",
                "timestamp": int(time.time()),
                "assetId": asset_id
            }
        )
        assert webhook_resp.status_code == 200

        # 6. Wait for background processing
        time.sleep(5.0)

        # 7. Check if CRM Update suggestion was created automatically by image analysis
        proposals_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/crm-proposals", headers=headers)
        assert proposals_resp.status_code == 200
        proposals = proposals_resp.json()
        assert len(proposals) > 0
        
        # Verify the suggested properties matching the image mock response
        city_proposal = next((p for p in proposals if p.get("fieldName") == "City" and p.get("suggestedValue") == "القاهرة"), None)
        budget_proposal = next((p for p in proposals if p.get("fieldName") == "Budget" and p.get("suggestedValue") == "50"), None)
        assert city_proposal is not None
        assert budget_proposal is not None
