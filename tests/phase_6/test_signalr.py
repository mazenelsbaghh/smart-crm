import pytest
import httpx
import uuid
import time
import json
import websockets
import socket
import ssl

def get_base_urls():
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.settimeout(1.0)
    try:
        s.connect(("localhost", 443))
        s.close()
        return "https://localhost:443/api", "wss://localhost:443/hubs/notifications", True
    except Exception:
        return "http://localhost:80/api", "ws://localhost:80/hubs/notifications", False

BASE_URL, WS_URL, IS_HTTPS = get_base_urls()

def get_client_kwargs():
    kwargs = {}
    if IS_HTTPS:
        kwargs["verify"] = False
    return kwargs

def get_ws_kwargs():
    kwargs = {}
    if IS_HTTPS:
        ssl_context = ssl.create_default_context()
        ssl_context.check_hostname = False
        ssl_context.verify_mode = ssl.CERT_NONE
        kwargs["ssl"] = ssl_context
    return kwargs

@pytest.mark.asyncio
async def test_signalr_realtime_chat_delivery():
    sender_phone = f"555{uuid.uuid4().hex[:6]}"
    message_id = f"msg_{uuid.uuid4().hex}"
    
    async with httpx.AsyncClient(timeout=20.0, **get_client_kwargs()) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "RealtimeChatProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]

        # 2. Update Settings
        headers = {"X-Project-Id": proj_id}
        settings_resp = await client.put(f"{BASE_URL}/projects/{proj_id}/settings", headers=headers, json={
            "aiAutoReplyEnabled": False,  # Disable auto reply to isolate manual chat testing
            "timezone": "UTC",
            "geminiApiKey": "MOCK_KEY"
        })
        assert settings_resp.status_code == 200

        # 3. Setup WhatsApp session
        await client.post(f"{BASE_URL}/whatsapp/session/start", json={"projectId": proj_id})
        await client.post(f"{BASE_URL}/whatsapp/session/mock", json={
            "projectId": proj_id,
            "status": "Connected",
            "phoneNumber": "1234567890"
        })

        # 4. Connect to SignalR Notification Hub
        ws_endpoint = f"{WS_URL}?projectId={proj_id}"
        async with websockets.connect(ws_endpoint, **get_ws_kwargs()) as ws:
            # Send SignalR JSON protocol handshake
            handshake = {"protocol": "json", "version": 1}
            await ws.send(json.dumps(handshake) + "\x1e")
            
            # Read handshake response
            handshake_res = await ws.recv()
            assert handshake_res.endswith("\x1e")

            # 5. Ingest message via webhook (this will insert to conversation and broadcast)
            webhook_resp = await client.post(
                f"{BASE_URL}/webhooks/whatsapp/message",
                json={
                    "projectId": proj_id,
                    "messageId": message_id,
                    "sender": sender_phone,
                    "content": "Hello real-time inbox!",
                    "messageType": "Text",
                    "timestamp": int(time.time())
                }
            )
            assert webhook_resp.status_code == 200

            # 6. Listen for incoming message invocation on WebSocket
            received_message = None
            try:
                # Loop to skip keep-alive ping messages (type 6)
                while True:
                    raw_msg = await pytest.importorskip("asyncio").wait_for(ws.recv(), timeout=12.0)
                    assert raw_msg.endswith("\x1e")
                    payload = json.loads(raw_msg[:-1])
                    
                    if payload.get("type") == 6: # Ping
                        continue
                    
                    # Target invocation (type 1)
                    if payload.get("type") == 1 and payload.get("target") == "ReceiveMessage":
                        received_message = payload["arguments"][0]
                        break
            except Exception as e:
                pytest.fail(f"Did not receive message via SignalR hub: {e}")

            assert received_message is not None
            assert received_message["content"] == "Hello real-time inbox!"
            assert received_message["senderType"] == "Customer"
