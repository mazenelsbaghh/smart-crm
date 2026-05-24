import pytest
import httpx
import uuid
import time
import json
import websockets

BASE_URL = "http://localhost:80/api"
WS_URL = "ws://localhost:80/hubs/notifications"

@pytest.mark.asyncio
async def test_signalr_notification_push_flow():
    sender_phone = f"555{uuid.uuid4().hex[:6]}"
    message_id = f"msg_{uuid.uuid4().hex}"
    
    # Setup mock JSON response for Gemini complaint
    mock_gemini_json = """
    {
      "intent": "complaint",
      "sentiment": "angry",
      "replyStyle": "Complaint",
      "entities": {},
      "replyContent": "We are very sorry for the issue. How can we resolve this?",
      "confidence": 0.95
    }
    """

    async with httpx.AsyncClient(timeout=20.0) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "NotifProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]

        # 2. Update Settings
        headers = {"X-Project-Id": proj_id}
        settings_resp = await client.put(f"{BASE_URL}/projects/{proj_id}/settings", headers=headers, json={
            "aiAutoReplyEnabled": True,
            "timezone": "UTC",
            "geminiApiKey": f"mock_json_{mock_gemini_json}"
        })
        assert settings_resp.status_code == 200

        # 3. Setup WhatsApp session
        start_resp = await client.post(f"{BASE_URL}/whatsapp/session/start", json={
            "projectId": proj_id
        })
        assert start_resp.status_code == 200

        await client.post(f"{BASE_URL}/whatsapp/session/mock", json={
            "projectId": proj_id,
            "status": "Connected",
            "phoneNumber": "1234567890"
        })

        await client.post(f"{BASE_URL}/whatsapp/mock/clear")

        # 4. Connect to SignalR Notification Hub via WebSocket
        ws_endpoint = f"{WS_URL}?projectId={proj_id}"
        print(f"Connecting to WS: {ws_endpoint}")
        
        async with websockets.connect(ws_endpoint) as ws:
            # Send SignalR JSON protocol handshake
            handshake = {"protocol": "json", "version": 1}
            # Terminate with ASCII Record Separator 0x1E
            await ws.send(json.dumps(handshake) + "\x1e")
            
            # Read handshake response
            handshake_res = await ws.recv()
            assert handshake_res.endswith("\x1e")
            handshake_json = json.loads(handshake_res[:-1])
            assert handshake_json == {} # SignalR response to handshake is empty object

            # 5. Ingest angry message via webhook (will trigger complaint alert in CRMAutoUpdateEngine)
            webhook_resp = await client.post(
                f"{BASE_URL}/webhooks/whatsapp/message",
                json={
                    "projectId": proj_id,
                    "messageId": message_id,
                    "sender": sender_phone,
                    "content": "Terrible service! Fix this now!",
                    "messageType": "Text",
                    "timestamp": int(time.time())
                }
            )
            assert webhook_resp.status_code == 200

            # 6. Listen for incoming message on WebSocket (timeout after 12 seconds)
            print("Listening for SignalR push notification...")
            notif_msg = None
            try:
                # Loop to skip keep-alive ping messages (type 6 in SignalR JSON protocol)
                while True:
                    raw_msg = await pytest.importorskip("asyncio").wait_for(ws.recv(), timeout=12.0)
                    assert raw_msg.endswith("\x1e")
                    payload = json.loads(raw_msg[:-1])
                    
                    if payload.get("type") == 6: # Ping
                        continue
                    
                    # Target invocation (type 1)
                    if payload.get("type") == 1 and payload.get("target") == "ReceiveNotification":
                        notif_msg = payload["arguments"][0]
                        break
            except Exception as e:
                pytest.fail(f"Did not receive push notification: {e}")

            assert notif_msg is not None
            assert notif_msg["type"] == "Complaint"
            assert "Negative sentiment detected" in notif_msg["message"]
            assert notif_msg["payload"]["customerId"] is not None
