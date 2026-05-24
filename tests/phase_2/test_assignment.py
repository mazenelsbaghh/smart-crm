import pytest
import httpx
import uuid
import time

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_assignment_routing_flow():
    async with httpx.AsyncClient(timeout=20.0) as client:
        # 1. Create Project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "AssignmentProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]
        headers = {"X-Project-Id": proj_id}

        # 2. Register two agents
        agent_a_email = f"agent_a_{uuid.uuid4().hex[:6]}@smartcore.com"
        agent_b_email = f"agent_b_{uuid.uuid4().hex[:6]}@smartcore.com"
        password = "SecurePassword123"

        reg_a = await client.post(
            f"{BASE_URL}/auth/register",
            json={"email": agent_a_email, "password": password, "projectId": proj_id, "role": "Agent"}
        )
        assert reg_a.status_code == 200
        agent_a_id = reg_a.json()["userId"]

        reg_b = await client.post(
            f"{BASE_URL}/auth/register",
            json={"email": agent_b_email, "password": password, "projectId": proj_id, "role": "Agent"}
        )
        assert reg_b.status_code == 200
        agent_b_id = reg_b.json()["userId"]

        # 3. Update presence for both (make them online)
        pres_a = await client.post(
            f"{BASE_URL}/projects/{proj_id}/agents/{agent_a_id}/presence",
            json={"isOnline": True}
        )
        assert pres_a.status_code == 200

        pres_b = await client.post(
            f"{BASE_URL}/projects/{proj_id}/agents/{agent_b_id}/presence",
            json={"isOnline": True}
        )
        assert pres_b.status_code == 200

        # 4. Check Workload Report
        workload_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/agents/workload", headers=headers)
        assert workload_resp.status_code == 200
        workload = workload_resp.json()
        assert len(workload) == 2
        
        rep_a = next((w for w in workload if w["agentId"] == agent_a_id), None)
        rep_b = next((w for w in workload if w["agentId"] == agent_b_id), None)
        assert rep_a is not None and rep_a["isOnline"] is True and rep_a["activeConversationsCount"] == 0
        assert rep_b is not None and rep_b["isOnline"] is True and rep_b["activeConversationsCount"] == 0

        # 5. Create first conversation (via webhook message)
        phone_1 = f"555{uuid.uuid4().hex[:6]}"
        webhook_a = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex}",
                "sender": phone_1,
                "content": "Hi, I need assistance.",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert webhook_a.status_code == 200

        # Fetch conversations to get ID
        convs_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/conversations", headers=headers)
        assert convs_resp.status_code == 200
        conversations = convs_resp.json()
        
        conv_1 = next((c for c in conversations if c["status"] == "Open"), None)
        assert conv_1 is not None
        conv_1_id = conv_1["id"]

        # 6. Auto-assign first conversation
        assign_a = await client.post(
            f"{BASE_URL}/conversations/{conv_1_id}/assign",
            headers=headers,
            json={}
        )
        assert assign_a.status_code == 200
        assigned_user_a = assign_a.json()["assignedUserId"]
        assert assigned_user_a in [agent_a_id, agent_b_id]

        # 7. Check Workload Report again
        workload_resp2 = await client.get(f"{BASE_URL}/projects/{proj_id}/agents/workload", headers=headers)
        workload2 = workload_resp2.json()
        rep_a2 = next((w for w in workload2 if w["agentId"] == agent_a_id), None)
        rep_b2 = next((w for w in workload2 if w["agentId"] == agent_b_id), None)
        
        # One agent should have count=1, the other count=0
        if assigned_user_a == agent_a_id:
            assert rep_a2["activeConversationsCount"] == 1
            assert rep_b2["activeConversationsCount"] == 0
            other_agent_id = agent_b_id
        else:
            assert rep_a2["activeConversationsCount"] == 0
            assert rep_b2["activeConversationsCount"] == 1
            other_agent_id = agent_a_id

        # 8. Create second conversation
        phone_2 = f"555{uuid.uuid4().hex[:6]}"
        webhook_b = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex}",
                "sender": phone_2,
                "content": "Hello, second message.",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert webhook_b.status_code == 200

        # Fetch conversations to get ID of the new one
        convs_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/conversations", headers=headers)
        conversations = convs_resp.json()
        conv_2 = next((c for c in conversations if c["id"] != conv_1_id), None)
        assert conv_2 is not None
        conv_2_id = conv_2["id"]

        # 9. Auto-assign second conversation (should go to the other agent who has 0 load)
        assign_b = await client.post(
            f"{BASE_URL}/conversations/{conv_2_id}/assign",
            headers=headers,
            json={}
        )
        assert assign_b.status_code == 200
        assigned_user_b = assign_b.json()["assignedUserId"]
        assert assigned_user_b == other_agent_id

        # 10. Manual direct assignment: Assign Conversation 2 to Agent A explicitly
        manual_assign = await client.post(
            f"{BASE_URL}/conversations/{conv_2_id}/assign",
            headers=headers,
            json={"agentId": agent_a_id}
        )
        assert manual_assign.status_code == 200
        assert manual_assign.json()["assignedUserId"] == agent_a_id
