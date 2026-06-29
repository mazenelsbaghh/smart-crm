import os
import uuid
import httpx
import pytest

BASE_URL = os.getenv("TEST_API_BASE_URL", "http://localhost:80/api")

def ai_behavior_payload(**overrides):
    payload = {
        "identity": {
            "agentNames": ["منى"],
            "nameSelectionMode": "First",
            "signatureEnabled": True,
            "signatureTemplate": "- {agentName}",
            "complaintSignatureTemplate": "- {agentName}",
        },
        "tone": {
            "tonePreset": "custom",
            "customTone": "نبرة مصرية هادئة ومباشرة",
            "targetAudience": "عملاء محتملون يسألون عن كورس كول سنتر",
            "allowedPhrases": ["يا فندم", "تمام"],
            "prohibitedPhrases": ["مش عارف"],
            "businessInstructions": "اسأل عن الموعد المناسب قبل عرض الحجز.",
        },
        "reactions": {
            "enabled": True,
            "allowedReactions": ["❤️"],
            "useAiSuggestedReaction": True,
            "rules": "استخدم الموشحات والرموز التعبيرية.",
        },
        "fallbacks": {
            "aiError": "هنراجع رسالتك ونرد عليك يا {customerName}.",
            "invalidAiOutput": "هنراجع رسالتك ونرد عليك قريباً.",
            "genericCustomerService": "خدمة العملاء هترد عليك قريباً.",
            "facebookPublicComment": "بعتنالك في الخاص يا {customerName}.",
            "whatsAppTransitionSuccess": "بعتلك على واتساب يا {customerName}.",
            "whatsAppTransitionFailure": "الرقم {phoneNumber} مش ظاهر عليه واتساب.",
            "whatsAppTransitionMessage": "أهلاً يا {customerName}، معاك {agentName} من {projectName}.",
            "followUpDefault": "حابين نتابع معاك يا {customerName}.",
            "groupReminderOnline": "تنبيه خاص بالجروب الأونلاين للعميل {customerName}: {groupInviteLink}",
            "groupReminderOffline": "تنبيه خاص بالجروب الأوفلاين للعميل {customerName}: {groupInviteLink}. العنوان بالتفصيل.",
        },
        "channels": {
            "WhatsApp": {"additionalInstructions": "خلي الرد مختصر على واتساب."},
        },
        "advancedInstructions": "",
    }
    payload.update(overrides)
    return payload

async def get_auth_headers(client, project_id):
    user_email = f"user_{uuid.uuid4().hex[:6]}@smartcore.com"
    # Register
    reg_resp = await client.post(
        f"{BASE_URL}/auth/register",
        json={
            "email": user_email,
            "password": "Password123",
            "projectId": project_id,
            "role": "Admin",
        },
    )
    assert reg_resp.status_code in (200, 201)

    # Login
    login_resp = await client.post(
        f"{BASE_URL}/auth/login",
        json={"email": user_email, "password": "Password123"},
    )
    assert login_resp.status_code == 200
    token = login_resp.json()["accessToken"]
    return {"Authorization": f"Bearer {token}", "X-Project-Id": project_id}

@pytest.mark.asyncio
async def test_whatsapp_group_automation_settings_toggle_and_save():
    async with httpx.AsyncClient(timeout=10.0) as client:
        project_name = f"GroupAutomation_{uuid.uuid4().hex[:6]}"
        create_resp = await client.post(f"{BASE_URL}/projects", json={"name": project_name})
        assert create_resp.status_code == 201
        project_id = create_resp.json()["id"]
        headers = await get_auth_headers(client, project_id)

        # Get default settings
        get_resp = await client.get(f"{BASE_URL}/projects/{project_id}", headers=headers)
        assert get_resp.status_code == 200
        settings = get_resp.json()["settings"]
        assert settings["isWhatsAppGroupAutomationEnabled"] is False
        assert settings["groupAutomationManagerPhone"] == "+201068690092"

        # Update settings to enable group automation
        behavior = ai_behavior_payload()
        update_resp = await client.put(
            f"{BASE_URL}/projects/{project_id}/settings",
            headers=headers,
            json={
                "projectName": project_name,
                "aiAutoReplyEnabled": True,
                "isWhatsAppGroupAutomationEnabled": True,
                "groupAutomationManagerPhone": "+201068690092",
                "aiBehavior": behavior
            }
        )
        assert update_resp.status_code == 200

        # Read back settings
        get_resp = await client.get(f"{BASE_URL}/projects/{project_id}", headers=headers)
        assert get_resp.status_code == 200
        settings = get_resp.json()["settings"]
        assert settings["isWhatsAppGroupAutomationEnabled"] is True
        assert settings["groupAutomationManagerPhone"] == "+201068690092"
        assert settings["aiBehavior"]["fallbacks"]["groupReminderOnline"] == "تنبيه خاص بالجروب الأونلاين للعميل {customerName}: {groupInviteLink}"
        assert settings["aiBehavior"]["fallbacks"]["groupReminderOffline"] == "تنبيه خاص بالجروب الأوفلاين للعميل {customerName}: {groupInviteLink}. العنوان بالتفصيل."
