import pytest
import httpx
import uuid

BASE_URL = "http://localhost:80/api/auth"

@pytest.mark.asyncio
async def test_auth_flow():
    # 1. Register a new user
    email = f"test_{uuid.uuid4().hex[:6]}@smartcore.com"
    password = "SecurePassword123"
    project_id = str(uuid.uuid4())
    
    async with httpx.AsyncClient(timeout=10.0) as client:
        # Register
        reg_response = await client.post(
            f"{BASE_URL}/register",
            json={
                "email": email,
                "password": password,
                "projectId": project_id,
                "role": "Admin"
            }
        )
        assert reg_response.status_code == 200
        reg_data = reg_response.json()
        assert "userId" in reg_data
        assert reg_data["message"] == "User registered successfully"

        # Register duplicate email should fail
        reg_dup_response = await client.post(
            f"{BASE_URL}/register",
            json={
                "email": email,
                "password": password,
                "projectId": project_id,
                "role": "Agent"
            }
        )
        assert reg_dup_response.status_code == 400

        # 2. Login
        login_response = await client.post(
            f"{BASE_URL}/login",
            json={
                "email": email,
                "password": password
            }
        )
        assert login_response.status_code == 200
        login_data = login_response.json()
        assert "accessToken" in login_data
        assert "refreshToken" in login_data
        access_token = login_data["accessToken"]
        refresh_token = login_data["refreshToken"]

        # Login with wrong password should fail
        login_fail_response = await client.post(
            f"{BASE_URL}/login",
            json={
                "email": email,
                "password": "WrongPassword"
            }
        )
        assert login_fail_response.status_code == 401

        # 3. Refresh Token
        refresh_response = await client.post(
            f"{BASE_URL}/refresh",
            json={
                "refreshToken": refresh_token
            }
        )
        assert refresh_response.status_code == 200
        refresh_data = refresh_response.json()
        assert "accessToken" in refresh_data
        assert "refreshToken" in refresh_data
        new_refresh_token = refresh_data["refreshToken"]

        # 4. Logout
        logout_response = await client.post(
            f"{BASE_URL}/logout",
            json={
                "refreshToken": new_refresh_token
            }
        )
        assert logout_response.status_code == 200
        assert logout_response.json()["message"] == "Logged out successfully"

        # 5. Refresh again should fail (token revoked)
        refresh_fail_response = await client.post(
            f"{BASE_URL}/refresh",
            json={
                "refreshToken": new_refresh_token
            }
        )
        assert refresh_fail_response.status_code == 401
