import pytest
import httpx
import uuid
import time
import json
from datetime import datetime, timedelta

BASE_URL = "http://localhost:5000/api"

@pytest.mark.asyncio
async def test_messenger_first_session_reminder():
    # US1: Messenger AI responder explicitly reminds customer that first session is free.
    sender_psid = f"psid_{uuid.uuid4().hex[:8]}"
    message_id = f"msg_{uuid.uuid4().hex}"
    page_id = f"page_free_{uuid.uuid4().hex[:6]}"
    
    async with httpx.AsyncClient(timeout=20.0) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "MsgrFreeSessionProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]
        headers = {"X-Project-Id": proj_id}

        # 2. Confirm Facebook Page
        confirm_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/facebook/pages/confirm",
            json={
                "facebookPageId": page_id,
                "pageName": "MockFreeSessionPage",
                "pageAccessToken": "mock_token"
            }
        )
        assert confirm_resp.status_code == 201

        # 3. Update Project Settings to enable Messenger AI replies and use custom mock reply
        ai_reply_payload = {
            "replyContent": "أهلاً بك! حابب أفكرك إن أول جلسة مجانية تماماً معنا. حابب تسجل دلوقتي؟",
            "intent": "greeting",
            "sentiment": "positive",
            "replyStyle": "Casual"
        }
        settings_resp = await client.put(
            f"{BASE_URL}/projects/{proj_id}/settings",
            headers=headers,
            json={
                "messengerAiAutoReplyEnabled": True,
                "aiAutoReplyEnabled": True,
                "geminiApiKey": "mock_json_" + json.dumps(ai_reply_payload)
            }
        )
        assert settings_resp.status_code == 200

        # 4. Simulate incoming Messenger message using the correct Facebook Webhook payload format
        webhook_payload = {
            "object": "page",
            "entry": [
                {
                    "id": page_id,
                    "messaging": [
                        {
                            "sender": {
                                "id": sender_psid
                            },
                            "recipient": {
                                "id": page_id
                            },
                            "timestamp": int(time.time() * 1000),
                            "message": {
                                "mid": message_id,
                                "text": "عايز اعرف تفاصيل الحجز"
                            }
                        }
                    ]
                }
            ]
        }
        webhook_resp = await client.post(
            f"{BASE_URL}/webhooks/facebook",
            json=webhook_payload
        )
        assert webhook_resp.status_code == 200

        # Wait for the aggregated message background worker to process and generate AI response
        print("Waiting for AI response generation...")
        await pytest.importorskip("asyncio").sleep(15.0)

        # 5. Retrieve conversations for this project (must specify channel=Messenger)
        conv_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/conversations?channel=Messenger", headers=headers)
        assert conv_resp.status_code == 200
        conversations = conv_resp.json()
        assert len(conversations) == 1
        conv = conversations[0]

        # 6. Verify the AI response message contains the first session free reminder
        msg_resp = await client.get(f"{BASE_URL}/conversations/{conv['id']}/messages", headers=headers)
        assert msg_resp.status_code == 200
        messages = msg_resp.json()
        
        # We expect at least two messages: the incoming customer message and the outgoing AI message
        assert len(messages) >= 2
        outgoing_msgs = [m for m in messages if m["direction"] == "Outgoing"]
        assert len(outgoing_msgs) >= 1
        
        # Verify tone and context requirements (Egyptian colloquial / free session reminder)
        ai_reply = outgoing_msgs[0]["content"]
        print(f"AI response: {ai_reply}")
        assert "مجانية" in ai_reply or "مجانيه" in ai_reply or "أول جلسة" in ai_reply or "اول جلسة" in ai_reply


@pytest.mark.asyncio
async def test_messenger_to_whatsapp_transition():
    # US2: Capture Egyptian phone number from Messenger, update customer profile, send WhatsApp message
    sender_psid = f"psid_{uuid.uuid4().hex[:8]}"
    message_id = f"msg_{uuid.uuid4().hex}"
    page_id = f"page_wa_{uuid.uuid4().hex[:6]}"
    
    async with httpx.AsyncClient(timeout=20.0) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "MsgrWATransitionProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]
        headers = {"X-Project-Id": proj_id}

        # 2. Confirm Facebook Page
        confirm_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/facebook/pages/confirm",
            json={
                "facebookPageId": page_id,
                "pageName": "MockTransitionPage",
                "pageAccessToken": "mock_token"
            }
        )
        assert confirm_resp.status_code == 201

        # 3. Setup Mock WhatsApp Session as Connected
        mock_session_resp = await client.post(
            f"{BASE_URL}/whatsapp/session/mock",
            json={
                "projectId": proj_id,
                "status": "Connected",
                "phoneNumber": "201023456789"
            }
        )
        assert mock_session_resp.status_code == 200

        # Enable AI replies
        settings_resp = await client.put(
            f"{BASE_URL}/projects/{proj_id}/settings",
            headers=headers,
            json={
                "messengerAiAutoReplyEnabled": True,
                "aiAutoReplyEnabled": True
            }
        )
        assert settings_resp.status_code == 200

        # Send a message first to establish customer and schedule a follow-up
        webhook_payload_1 = {
            "object": "page",
            "entry": [
                {
                    "id": page_id,
                    "messaging": [
                        {
                            "sender": {
                                "id": sender_psid
                            },
                            "recipient": {
                                "id": page_id
                            },
                            "timestamp": int(time.time() * 1000),
                            "message": {
                                "mid": message_id,
                                "text": "مرحباً"
                            }
                        }
                    ]
                }
            ]
        }
        await client.post(f"{BASE_URL}/webhooks/facebook", json=webhook_payload_1)
        await pytest.importorskip("asyncio").sleep(3.0)

        # Get customer ID
        cust_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/customers", headers=headers)
        assert cust_resp.status_code == 200
        customer = cust_resp.json()[0]
        customer_id = customer["id"]

        # Schedule a pending follow-up on Messenger channel (since phone is empty)
        fu_due = (datetime.utcnow() + timedelta(hours=2)).isoformat() + "Z"
        await client.post(
            f"{BASE_URL}/customers/{customer_id}/follow-ups",
            headers=headers,
            json={
                "dueDate": fu_due,
                "notes": "Follow up on Messenger later"
            }
        )

        # Verify it is scheduled and pending
        fu_list_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/follow-ups?status=Pending", headers=headers)
        assert len(fu_list_resp.json()) >= 1

        # Now send the message with Egyptian phone number
        transition_msg_id = f"msg_{uuid.uuid4().hex}"
        webhook_payload_2 = {
            "object": "page",
            "entry": [
                {
                    "id": page_id,
                    "messaging": [
                        {
                            "sender": {
                                "id": sender_psid
                            },
                            "recipient": {
                                "id": page_id
                            },
                            "timestamp": int(time.time() * 1000),
                            "message": {
                                "mid": transition_msg_id,
                                "text": "رقمي هو 01023456789"
                            }
                        }
                    ]
                }
            ]
        }
        webhook_resp = await client.post(
            f"{BASE_URL}/webhooks/facebook",
            json=webhook_payload_2
        )
        assert webhook_resp.status_code == 200

        # Wait for the transition handler to run
        print("Waiting for transition worker...")
        await pytest.importorskip("asyncio").sleep(12.0)

        # Check customer profile has phone number updated
        cust_detail_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/customers", headers=headers)
        updated_customer = cust_detail_resp.json()[0]
        assert updated_customer["phoneNumber"] == "201023456789"

        # Verify previous pending Messenger follow-up was Cancelled
        fu_history_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/follow-ups?status=Cancelled", headers=headers)
        assert fu_history_resp.status_code == 200
        cancelled_fus = fu_history_resp.json()
        assert len(cancelled_fus) >= 1

        # Verify a new WhatsApp follow-up was scheduled
        wa_fu_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/follow-ups?status=Pending", headers=headers)
        assert wa_fu_resp.status_code == 200
        wa_fus = wa_fu_resp.json()
        assert len(wa_fus) == 1
        assert wa_fus[0]["notes"] == "مرحباً يا فندم، حابين نطمن على تفاصيل الحجز ونعرف لو في أي استفسار آخر؟"


@pytest.mark.asyncio
async def test_messenger_group_booking_requires_and_uses_customer_phone():
    # Regression for the 2026-07-26 production incident where Messenger PSIDs were saved as booking phone numbers.
    sender_psid = f"psid_{uuid.uuid4().hex[:8]}"
    page_id = f"page_booking_{uuid.uuid4().hex[:6]}"
    customer_phone = "201023456789"

    async with httpx.AsyncClient(timeout=20.0) as client:
        project_response = await client.post(
            f"{BASE_URL}/projects",
            json={"name": "MessengerPhoneBookingProj"},
        )
        assert project_response.status_code == 201
        project_id = project_response.json()["id"]
        headers = {"X-Project-Id": project_id}

        page_response = await client.post(
            f"{BASE_URL}/projects/{project_id}/facebook/pages/confirm",
            json={
                "facebookPageId": page_id,
                "pageName": "MockBookingPage",
                "pageAccessToken": "mock_token",
            },
        )
        assert page_response.status_code == 201

        session_response = await client.post(
            f"{BASE_URL}/whatsapp/session/mock",
            json={
                "projectId": project_id,
                "status": "Connected",
                "phoneNumber": "201111111111",
            },
        )
        assert session_response.status_code == 200

        group_response = await client.post(
            f"{BASE_URL}/group-appointments",
            headers=headers,
            json={
                "name": "مجموعة اختبار رقم الموبايل",
                "dateTime": (datetime.utcnow() + timedelta(days=3)).isoformat() + "Z",
                "capacity": 5,
                "isActive": True,
                "mode": "online",
            },
        )
        assert group_response.status_code == 200
        group_id = group_response.json()["id"]

        ai_booking_response = {
            "replyContent": "تم تسجيل الحجز.",
            "intent": "booking",
            "sentiment": "positive",
            "replyStyle": "Sales",
            "suggestedGroupBookingId": group_id,
        }
        settings_response = await client.put(
            f"{BASE_URL}/projects/{project_id}/settings",
            headers=headers,
            json={
                "messengerAiAutoReplyEnabled": True,
                "aiAutoReplyEnabled": True,
                "isGroupAppointmentsEnabled": True,
                "geminiApiKey": "mock_json_" + json.dumps(ai_booking_response),
            },
        )
        assert settings_response.status_code == 200

        async def send_messenger_message(text):
            response = await client.post(
                f"{BASE_URL}/webhooks/facebook",
                json={
                    "object": "page",
                    "entry": [
                        {
                            "id": page_id,
                            "messaging": [
                                {
                                    "sender": {"id": sender_psid},
                                    "recipient": {"id": page_id},
                                    "timestamp": int(time.time() * 1000),
                                    "message": {
                                        "mid": f"msg_{uuid.uuid4().hex}",
                                        "text": text,
                                    },
                                }
                            ],
                        }
                    ],
                },
            )
            assert response.status_code == 200

        await send_messenger_message("عايز أحجز في المجموعة")
        await pytest.importorskip("asyncio").sleep(15.0)

        groups_response = await client.get(
            f"{BASE_URL}/group-appointments",
            headers=headers,
        )
        assert groups_response.status_code == 200
        group = next(item for item in groups_response.json() if item["id"] == group_id)
        assert group["bookedCount"] == 0

        conversations_response = await client.get(
            f"{BASE_URL}/projects/{project_id}/conversations?channel=Messenger",
            headers=headers,
        )
        assert conversations_response.status_code == 200
        conversation_id = conversations_response.json()[0]["id"]
        messages_response = await client.get(
            f"{BASE_URL}/conversations/{conversation_id}/messages",
            headers=headers,
        )
        assert messages_response.status_code == 200
        outgoing_messages = [
            message["content"]
            for message in messages_response.json()
            if message["direction"] == "Outgoing"
        ]
        assert any("رقم موبايلك" in message for message in outgoing_messages)

        await send_messenger_message("رقمي هو 01023456789")
        await pytest.importorskip("asyncio").sleep(12.0)

        await send_messenger_message("تمام، احجزلي في المجموعة")
        await pytest.importorskip("asyncio").sleep(15.0)

        groups_response = await client.get(
            f"{BASE_URL}/group-appointments",
            headers=headers,
        )
        assert groups_response.status_code == 200
        group = next(item for item in groups_response.json() if item["id"] == group_id)
        assert group["bookedCount"] == 1
        assert group["bookings"][0]["customerPhone"] == customer_phone
        assert group["bookings"][0]["customerPhone"] != sender_psid


@pytest.mark.asyncio
async def test_followup_messenger_routing():
    # US3: Route scheduled follow-ups to Messenger if customer has no PhoneNumber but has FacebookPSID
    sender_psid = f"psid_{uuid.uuid4().hex[:8]}"
    message_id = f"msg_{uuid.uuid4().hex}"
    page_id = f"page_route_{uuid.uuid4().hex[:6]}"
    
    async with httpx.AsyncClient(timeout=20.0) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "MsgrRoutingProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]
        headers = {"X-Project-Id": proj_id}

        # 2. Confirm Facebook Page
        confirm_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/facebook/pages/confirm",
            json={
                "facebookPageId": page_id,
                "pageName": "MockRoutingPage",
                "pageAccessToken": "mock_token"
            }
        )
        assert confirm_resp.status_code == 201
        
        # 3. Ingest a message via FB Webhook to create customer with FacebookPSID
        webhook_payload = {
            "object": "page",
            "entry": [
                {
                    "id": page_id,
                    "messaging": [
                        {
                            "sender": {
                                "id": sender_psid
                            },
                            "recipient": {
                                "id": page_id
                            },
                            "timestamp": int(time.time() * 1000),
                            "message": {
                                "mid": message_id,
                                "text": "أهلاً"
                            }
                        }
                    ]
                }
            ]
        }
        await client.post(f"{BASE_URL}/webhooks/facebook", json=webhook_payload)
        await pytest.importorskip("asyncio").sleep(3.0)

        # Get customer details from conversations endpoint (where facebookPSID is projected)
        conv_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/conversations?channel=Messenger", headers=headers)
        assert conv_resp.status_code == 200
        convs = conv_resp.json()
        assert len(convs) == 1
        customer = convs[0]["customer"]
        customer_id = customer["id"]
        assert customer["phone"] == "" or customer["phone"] is None
        assert customer["facebookPSID"] == sender_psid

        # Schedule an overdue follow-up
        past_due = (datetime.utcnow() - timedelta(seconds=5)).isoformat() + "Z"
        await client.post(
            f"{BASE_URL}/customers/{customer_id}/follow-ups",
            headers=headers,
            json={
                "dueDate": past_due,
                "notes": "متابعة ماسنجر دورية"
            }
        )

        # Wait for FollowUpScheduler Hangfire recurrence (checked every minute)
        print("Waiting for scheduler to process the follow-up...")
        completed = False
        start_time = time.time()
        while time.time() - start_time < 75.0:
            pending_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/follow-ups?status=Pending", headers=headers)
            assert pending_resp.status_code == 200
            if len(pending_resp.json()) == 0:
                completed = True
                break
            await pytest.importorskip("asyncio").sleep(2.0)
        
        assert completed, "Follow-up was not processed by the scheduler within 75 seconds"
