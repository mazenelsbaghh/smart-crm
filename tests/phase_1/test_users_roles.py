import pytest
import httpx
import uuid
import base64
import json

BASE_URL = "http://localhost:80/api"

def decode_jwt_payload(token):
    parts = token.split('.')
    if len(parts) < 2:
        return {}
    payload = parts[1]
    rem = len(payload) % 4
    if rem > 0:
        payload += '=' * (4 - rem)
    decoded_bytes = base64.urlsafe_b64decode(payload)
    return json.loads(decoded_bytes.decode('utf-8'))

@pytest.mark.asyncio
async def test_users_roles_flow():
    # Generate unique test data
    email_owner = f"owner_{uuid.uuid4().hex[:6]}@smartcore.com"
    email_agent = f"agent_{uuid.uuid4().hex[:6]}@smartcore.com"
    password = "SecurePassword123"

    async with httpx.AsyncClient(timeout=10.0) as client:
        # 1. Create a project
        proj_resp = await client.post(f"{BASE_URL}/projects", json={"name": "UsersRolesProj"})
        assert proj_resp.status_code == 201
        proj_id = proj_resp.json()["id"]

        # 2. Register Owner
        reg_owner_resp = await client.post(
            f"{BASE_URL}/auth/register",
            json={
                "email": email_owner,
                "password": password,
                "projectId": proj_id,
                "role": "Owner"
            }
        )
        assert reg_owner_resp.status_code == 200

        # Login Owner to verify JWT permission claims
        login_owner_resp = await client.post(
            f"{BASE_URL}/auth/login",
            json={
                "email": email_owner,
                "password": password
            }
        )
        assert login_owner_resp.status_code == 200
        owner_token = login_owner_resp.json()["accessToken"]
        
        # Decode token and verify role and permission claims
        owner_payload = decode_jwt_payload(owner_token)
        assert owner_payload.get("role") == "Owner"
        permissions = owner_payload.get("permission")
        assert permissions is not None
        assert "manage_roles" in permissions
        assert "invite" in permissions
        assert "read" in permissions

        # 3. Invite Agent via project invite endpoint
        headers = {"X-Project-Id": proj_id}
        invite_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/users/invite",
            headers=headers,
            json={
                "email": email_agent,
                "password": password,
                "role": "Agent"
            }
        )
        assert invite_resp.status_code == 201
        invited_user = invite_resp.json()
        assert invited_user["email"] == email_agent
        assert invited_user["role"] == "Agent"
        assert invited_user["projectId"] == proj_id
        agent_user_id = invited_user["id"]

        # Invite with invalid role should fail (400)
        invite_invalid_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/users/invite",
            headers=headers,
            json={
                "email": f"invalid_{uuid.uuid4().hex[:6]}@smartcore.com",
                "password": password,
                "role": "SuperUser"
            }
        )
        assert invite_invalid_resp.status_code == 400

        # Login Agent to verify permission claims
        login_agent_resp = await client.post(
            f"{BASE_URL}/auth/login",
            json={
                "email": email_agent,
                "password": password
            }
        )
        assert login_agent_resp.status_code == 200
        agent_token = login_agent_resp.json()["accessToken"]
        agent_payload = decode_jwt_payload(agent_token)
        assert agent_payload.get("role") == "Agent"
        agent_perms = agent_payload.get("permission")
        assert agent_perms is not None
        assert "read" in agent_perms
        assert "write" in agent_perms
        assert "manage_roles" not in agent_perms  # Agent doesn't have manage_roles permission

        # 4. List Users in the project
        list_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/users", headers=headers)
        assert list_resp.status_code == 200
        users_list = list_resp.json()
        assert len(users_list) >= 2
        emails = [u["email"] for u in users_list]
        assert email_owner in emails
        assert email_agent in emails

        # 5. Get User by ID
        get_resp = await client.get(f"{BASE_URL}/users/{agent_user_id}", headers=headers)
        assert get_resp.status_code == 200
        assert get_resp.json()["email"] == email_agent

        # 6. Update User Role
        update_role_resp = await client.put(
            f"{BASE_URL}/users/{agent_user_id}/role",
            headers=headers,
            json={"role": "Supervisor"}
        )
        assert update_role_resp.status_code == 200
        assert update_role_resp.json()["role"] == "Supervisor"

        # Login again and check updated permission claims
        login_updated_resp = await client.post(
            f"{BASE_URL}/auth/login",
            json={
                "email": email_agent,
                "password": password
            }
        )
        assert login_updated_resp.status_code == 200
        updated_token = login_updated_resp.json()["accessToken"]
        updated_payload = decode_jwt_payload(updated_token)
        assert updated_payload.get("role") == "Supervisor"
        updated_perms = updated_payload.get("permission")
        assert "invite" in updated_perms  # Supervisor has invite permission!

        # 7. Delete User
        del_resp = await client.delete(f"{BASE_URL}/users/{agent_user_id}", headers=headers)
        assert del_resp.status_code == 200

        # Get deleted user should return 404
        get_del_resp = await client.get(f"{BASE_URL}/users/{agent_user_id}", headers=headers)
        assert get_del_resp.status_code == 404
