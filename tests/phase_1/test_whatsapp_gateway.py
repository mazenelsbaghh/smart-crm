import pytest
import httpx
import uuid

BASE_URL = "http://localhost:80/api/whatsapp"

@pytest.mark.asyncio
async def test_whatsapp_gateway_flow():
    project_id = str(uuid.uuid4())
    to_phone = "1234567890"
    message_text = "Hello from Smart Customer Core!"

    async with httpx.AsyncClient(timeout=15.0) as client:
        # 1. Start WhatsApp session for Project
        start_resp = await client.post(f"{BASE_URL}/session/start", json={
            "projectId": project_id
        })
        assert start_resp.status_code == 200
        assert start_resp.json()["status"] == "Initializing"

        # 2. Check session status - should be Initializing or Disconnected (since no real phone scanned it)
        status_resp = await client.get(f"{BASE_URL}/session/status?projectId={project_id}")
        assert status_resp.status_code == 200
        assert status_resp.json()["status"] in ["Initializing", "Disconnected"]

        # 3. Use mock endpoint to force status to "Connected" for tests
        mock_resp = await client.post(f"{BASE_URL}/session/mock", json={
            "projectId": project_id,
            "status": "Connected",
            "phoneNumber": to_phone
        })
        assert mock_resp.status_code == 200
        assert "Mocked" in mock_resp.json()["message"]

        # 4. Re-check session status - should now be Connected
        status_connected_resp = await client.get(f"{BASE_URL}/session/status?projectId={project_id}")
        assert status_connected_resp.status_code == 200
        assert status_connected_resp.json()["status"] == "Connected"
        assert status_connected_resp.json()["phoneNumber"] == to_phone

        # 5. Send message
        send_resp = await client.post(f"{BASE_URL}/send", json={
            "projectId": project_id,
            "to": to_phone,
            "message": message_text
        })
        assert send_resp.status_code == 200
        send_data = send_resp.json()
        assert send_data["status"] == "Sent"
        assert "messageId" in send_data


