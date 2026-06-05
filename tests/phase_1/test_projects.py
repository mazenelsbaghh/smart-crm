import pytest
import httpx
import uuid

BASE_URL = "http://localhost:80/api/projects"
AUTH_URL = "http://localhost:80/api/auth"

@pytest.mark.asyncio
async def test_project_isolation():
    async with httpx.AsyncClient(timeout=10.0) as client:
        # Create Project A
        proj_a_name = f"Project_A_{uuid.uuid4().hex[:6]}"
        create_a_resp = await client.post(BASE_URL, json={"name": proj_a_name})
        assert create_a_resp.status_code == 201
        proj_a_data = create_a_resp.json()
        proj_a_id = proj_a_data["id"]

        # Create Project B
        proj_b_name = f"Project_B_{uuid.uuid4().hex[:6]}"
        create_b_resp = await client.post(BASE_URL, json={"name": proj_b_name})
        assert create_b_resp.status_code == 201
        proj_b_data = create_b_resp.json()
        proj_b_id = proj_b_data["id"]

        # Register User A in Project A
        user_a_email = f"user_a_{uuid.uuid4().hex[:6]}@smartcore.com"
        await client.post(f"{AUTH_URL}/register", json={
            "email": user_a_email,
            "password": "Password123",
            "projectId": proj_a_id,
            "role": "Admin"
        })

        # Register User B in Project B
        user_b_email = f"user_b_{uuid.uuid4().hex[:6]}@smartcore.com"
        await client.post(f"{AUTH_URL}/register", json={
            "email": user_b_email,
            "password": "Password123",
            "projectId": proj_b_id,
            "role": "Admin"
        })

        # Login User A -> Get JWT A
        login_a_resp = await client.post(f"{AUTH_URL}/login", json={
            "email": user_a_email,
            "password": "Password123"
        })
        token_a = login_a_resp.json()["accessToken"]

        # Login User B -> Get JWT B
        login_b_resp = await client.post(f"{AUTH_URL}/login", json={
            "email": user_b_email,
            "password": "Password123"
        })
        token_b = login_b_resp.json()["accessToken"]

        # 1. User A updates settings for Project A
        headers_a = {"Authorization": f"Bearer {token_a}"}
        update_settings_a = await client.put(f"{BASE_URL}/{proj_a_id}/settings", headers=headers_a, json={
            "aiAutoReplyEnabled": True,
            "timezone": "EST",
            "geminiApiKey": "AI_KEY_A"
        })
        assert update_settings_a.status_code == 200

        # Verify User A settings were updated
        get_settings_a = await client.get(f"{BASE_URL}/{proj_a_id}", headers=headers_a)
        assert get_settings_a.status_code == 200
        settings_a = get_settings_a.json()["settings"]
        assert settings_a["aiAutoReplyEnabled"] is True
        assert settings_a["timezone"] == "EST"

        # 2. User B updates settings for Project B
        headers_b = {"Authorization": f"Bearer {token_b}"}
        update_settings_b = await client.put(f"{BASE_URL}/{proj_b_id}/settings", headers=headers_b, json={
            "aiAutoReplyEnabled": False,
            "timezone": "PST",
            "geminiApiKey": "AI_KEY_B"
        })
        assert update_settings_b.status_code == 200

        # Verify User B settings were updated
        get_settings_b = await client.get(f"{BASE_URL}/{proj_b_id}", headers=headers_b)
        assert get_settings_b.status_code == 200
        settings_b = get_settings_b.json()["settings"]
        assert settings_b["aiAutoReplyEnabled"] is False
        assert settings_b["timezone"] == "PST"

        # 3. Security/Isolation check: User A tries to read Project B settings
        # ProjectSettings implements ITenantEntity, so User A's query filter hides Project B's settings.
        get_b_with_a = await client.get(f"{BASE_URL}/{proj_b_id}", headers=headers_a)
        assert get_b_with_a.status_code == 200
        assert get_b_with_a.json()["settings"] is None


@pytest.mark.asyncio
async def test_project_custom_tone_and_audience_settings():
    async with httpx.AsyncClient(timeout=10.0) as client:
        # Create Project
        proj_name = f"Proj_Custom_{uuid.uuid4().hex[:6]}"
        create_resp = await client.post(BASE_URL, json={"name": proj_name})
        assert create_resp.status_code == 201
        proj_data = create_resp.json()
        proj_id = proj_data["id"]

        # Get settings initially - should have default values
        headers = {"X-Project-Id": proj_id}
        get_resp = await client.get(f"{BASE_URL}/{proj_id}", headers=headers)
        assert get_resp.status_code == 200
        settings = get_resp.json()["settings"]
        assert settings["aiTonePreference"] == "العامية المصرية الروشة والصايعة"
        assert settings["aiTargetAudience"] == "طلاب كورس كول سنتر يبحثون عن عمل"
        assert settings["replyDelay"] == 3
        assert settings["maxDailyMessages"] == 500

        # Update settings with custom values
        custom_tone = "خليجية مهذبة ومحترمة"
        custom_audience = "عملاء VIP يبحثون عن عقارات"
        update_resp = await client.put(f"{BASE_URL}/{proj_id}/settings", headers=headers, json={
            "aiAutoReplyEnabled": True,
            "timezone": "GST",
            "geminiApiKey": "MOCK_KEY",
            "aiTonePreference": custom_tone,
            "aiTargetAudience": custom_audience,
            "replyDelay": 5,
            "maxDailyMessages": 100
        })
        assert update_resp.status_code == 200

        # Get settings again and assert
        get_updated_resp = await client.get(f"{BASE_URL}/{proj_id}", headers=headers)
        assert get_updated_resp.status_code == 200
        updated_settings = get_updated_resp.json()["settings"]
        assert updated_settings["aiTonePreference"] == custom_tone
        assert updated_settings["aiTargetAudience"] == custom_audience
        assert updated_settings["replyDelay"] == 5
        assert updated_settings["maxDailyMessages"] == 100
