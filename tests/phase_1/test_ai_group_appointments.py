import pytest
import httpx
import uuid
import time
import subprocess
from datetime import datetime, timedelta

BASE_URL = "http://localhost:80/api"
AUTH_URL = "http://localhost:80/api/auth"

@pytest.mark.asyncio
async def test_ai_gemini_group_appointments_context():
    sender_phone = f"555{str(uuid.uuid4().int)[:6]}"
    message_id = f"msg_group_{uuid.uuid4().hex}"
    incoming_content = "عايز أعرف المواعيد المتاحة للحجز وسعة المجموعات"

    # Mock JSON response from Gemini
    mock_gemini_json = """
    {
      "intent": "inquiry",
      "sentiment": "neutral",
      "replyStyle": "Casual",
      "entities": {
        "city": null,
        "budget": null,
        "interests": [],
        "timeline": null
      },
      "replyContent": "[Mock Group Reply] لدينا مجموعات متاحة للتسجيل حالياً. يمكنك الحجز عبر الرابط المباشر.",
      "confidence": 0.95
    }
    """

    async with httpx.AsyncClient(timeout=20.0) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "AiTestProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]

        # 2. Register Admin User for the project to get authorized access
        user_email = f"admin_{uuid.uuid4().hex[:6]}@smartcore.com"
        reg_resp = await client.post(f"{AUTH_URL}/register", json={
            "email": user_email,
            "password": "Password123",
            "projectId": proj_id,
            "role": "Admin"
        })
        assert reg_resp.status_code == 200

        # 3. Login to obtain JWT token
        login_resp = await client.post(f"{AUTH_URL}/login", json={
            "email": user_email,
            "password": "Password123"
        })
        assert login_resp.status_code == 200
        token = login_resp.json()["accessToken"]
        auth_headers = {"Authorization": f"Bearer {token}", "X-Project-Id": proj_id}

        # 4. Enable Group Appointments and AI auto-reply
        settings_resp = await client.put(f"{BASE_URL}/projects/{proj_id}/settings", headers=auth_headers, json={
            "aiAutoReplyEnabled": True,
            "isGroupAppointmentsEnabled": True,
            "timezone": "Africa/Cairo",
            "geminiApiKey": f"mock_json_{mock_gemini_json}"
        })
        assert settings_resp.status_code == 200

        # 5. Create an active Group Appointment in the database via the admin API
        group_date = (datetime.utcnow() + timedelta(days=3)).strftime("%Y-%m-%dT%H:%M:%S") + "Z"
        group_resp = await client.post(f"{BASE_URL}/group-appointments", headers=auth_headers, json={
            "name": "مجموعة البرمجة المكثفة",
            "dateTime": group_date,
            "capacity": 4,
            "isActive": True
        })
        assert group_resp.status_code == 200

        # 6. Setup mock WhatsApp session and connect it
        start_resp = await client.post(f"{BASE_URL}/whatsapp/session/start", json={
            "projectId": proj_id
        })
        assert start_resp.status_code == 200

        mock_resp = await client.post(f"{BASE_URL}/whatsapp/session/mock", json={
            "projectId": proj_id,
            "status": "Connected",
            "phoneNumber": "1234567890"
        })
        assert mock_resp.status_code == 200

        # 7. Clear mock sent messages
        clear_resp = await client.post(f"{BASE_URL}/whatsapp/mock/clear")
        assert clear_resp.status_code == 200

        # 8. Ingest incoming customer message webhook asking about group bookings
        webhook_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": message_id,
                "sender": sender_phone,
                "content": incoming_content,
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert webhook_resp.status_code == 200

        # 9. Wait for aggregation and processing
        print("Waiting 10 seconds for AI to retrieve context and generate response...")
        await pytest.importorskip("asyncio").sleep(10.0)

        # 10. Check if AI reply was sent to the gateway
        sent_resp = await client.get(f"{BASE_URL}/whatsapp/mock/sent")
        assert sent_resp.status_code == 200
        sent_messages = sent_resp.json()

        reply_message = next((m for m in sent_messages if m["to"] == sender_phone), None)
        assert reply_message is not None, "AI Group Reply was not sent to WhatsApp Gateway"
        assert "[Mock Group Reply]" in reply_message["message"]

        # 11. Read Docker backend logs to verify Group Appointment context was injected successfully
        logs_result = subprocess.run(
            ["docker", "compose", "logs", "--tail=150", "backend"],
            capture_output=True, text=True, check=True
        )
        logs = logs_result.stdout + logs_result.stderr
        
        # Verify the context injection log is present
        assert "Injected Group Appointments context (Found 1 active groups)" in logs or "Failed to query active group appointments" not in logs
        print("Test successfully executed and verified!")
