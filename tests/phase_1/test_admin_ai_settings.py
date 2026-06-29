import os
import uuid

import httpx
import pytest


BASE_URL = os.getenv("TEST_API_BASE_URL", "http://localhost:80/api")


def ai_behavior_payload(**overrides):
    payload = {
        "identity": {
            "agentNames": ["منى", "كريم"],
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
            "allowedReactions": ["❤️", "😮"],
            "useAiSuggestedReaction": True,
            "rules": "استخدم ❤️ مع الشكر و😮 مع الاستفسارات المهمة.",
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
        },
        "channels": {
            "WhatsApp": {"additionalInstructions": "خلي الرد مختصر على واتساب."},
            "Messenger": {"additionalInstructions": "لو ظهر رقم واتساب انقل الحوار للقناة المناسبة."},
            "FacebookComment": {"additionalInstructions": "لا تكتب تفاصيل سعر كاملة في التعليق العام."},
        },
        "advancedInstructions": "القواعد المنظمة أعلى أولوية من هذا الحقل.",
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
async def test_admin_ai_behavior_settings_are_saved_and_read_back():
    async with httpx.AsyncClient(timeout=10.0) as client:
        project_name = f"AISettings_{uuid.uuid4().hex[:6]}"
        create_resp = await client.post(f"{BASE_URL}/projects", json={"name": project_name})
        assert create_resp.status_code == 201
        project_id = create_resp.json()["id"]
        headers = await get_auth_headers(client, project_id)

        behavior = ai_behavior_payload()
        update_resp = await client.put(
            f"{BASE_URL}/projects/{project_id}/settings",
            headers=headers,
            json={
                "projectName": project_name,
                "aiAutoReplyEnabled": True,
                "aiTonePreference": "نبرة مصرية هادئة ومباشرة",
                "aiTargetAudience": "عملاء محتملون يسألون عن كورس كول سنتر",
                "aiBehavior": behavior,
            },
        )
        assert update_resp.status_code == 200

        get_resp = await client.get(f"{BASE_URL}/projects/{project_id}", headers=headers)
        assert get_resp.status_code == 200
        settings = get_resp.json()["settings"]
        assert settings["aiBehavior"]["identity"]["agentNames"] == ["منى", "كريم"]
        assert settings["aiBehavior"]["identity"]["nameSelectionMode"] == "First"
        assert settings["aiBehavior"]["reactions"]["allowedReactions"] == ["❤️", "😮"]
        assert settings["aiBehavior"]["fallbacks"]["whatsAppTransitionMessage"] == "أهلاً يا {customerName}، معاك {agentName} من {projectName}."
        assert settings["aiBehavior"]["channels"]["Messenger"]["additionalInstructions"] == "لو ظهر رقم واتساب انقل الحوار للقناة المناسبة."


@pytest.mark.asyncio
@pytest.mark.parametrize(
    ("mutate_payload", "expected_text"),
    [
        (
            lambda behavior: behavior["fallbacks"].update({"aiError": "أهلاً يا {unsupportedName}"}),
            "Unsupported placeholder",
        ),
        (
            lambda behavior: behavior["fallbacks"].update({"aiError": "ا" * 1001}),
            "exceeds 1000 characters",
        ),
        (
            lambda behavior: behavior["channels"].update({"Instagram": {"additionalInstructions": "رد مختصر"}}),
            "Unsupported aiBehavior channel",
        ),
        (
            lambda behavior: behavior["reactions"].update({"allowedReactions": ["🔥"]}),
            "Unsupported reaction",
        ),
    ],
)
async def test_admin_ai_behavior_rejects_invalid_templates_channels_and_reactions(mutate_payload, expected_text):
    async with httpx.AsyncClient(timeout=10.0) as client:
        project_name = f"BadAISettings_{uuid.uuid4().hex[:6]}"
        create_resp = await client.post(f"{BASE_URL}/projects", json={"name": project_name})
        assert create_resp.status_code == 201
        project_id = create_resp.json()["id"]
        headers = await get_auth_headers(client, project_id)

        behavior = ai_behavior_payload()
        mutate_payload(behavior)

        update_resp = await client.put(
            f"{BASE_URL}/projects/{project_id}/settings",
            headers=headers,
            json={"projectName": project_name, "aiBehavior": behavior},
        )
        assert update_resp.status_code == 400
        assert expected_text in update_resp.text


@pytest.mark.asyncio
async def test_admin_ai_behavior_keeps_project_specific_staff_and_fallbacks_isolated():
    async with httpx.AsyncClient(timeout=10.0) as client:
        project_a_name = f"AISettingsA_{uuid.uuid4().hex[:6]}"
        project_b_name = f"AISettingsB_{uuid.uuid4().hex[:6]}"
        create_a_resp = await client.post(f"{BASE_URL}/projects", json={"name": project_a_name})
        create_b_resp = await client.post(f"{BASE_URL}/projects", json={"name": project_b_name})
        assert create_a_resp.status_code == 201
        assert create_b_resp.status_code == 201
        project_a_id = create_a_resp.json()["id"]
        project_b_id = create_b_resp.json()["id"]

        headers_a = await get_auth_headers(client, project_a_id)
        headers_b = await get_auth_headers(client, project_b_id)

        behavior_a = ai_behavior_payload()
        behavior_a["identity"]["agentNames"] = ["أسماء"]
        behavior_a["fallbacks"]["aiError"] = "رسالة مشروع أ"

        behavior_b = ai_behavior_payload()
        behavior_b["identity"]["agentNames"] = ["هاني"]
        behavior_b["fallbacks"]["aiError"] = "رسالة مشروع ب"

        update_a_resp = await client.put(
            f"{BASE_URL}/projects/{project_a_id}/settings",
            headers=headers_a,
            json={"projectName": project_a_name, "aiBehavior": behavior_a},
        )
        update_b_resp = await client.put(
            f"{BASE_URL}/projects/{project_b_id}/settings",
            headers=headers_b,
            json={"projectName": project_b_name, "aiBehavior": behavior_b},
        )
        assert update_a_resp.status_code == 200
        assert update_b_resp.status_code == 200

        get_a_resp = await client.get(f"{BASE_URL}/projects/{project_a_id}", headers=headers_a)
        get_b_resp = await client.get(f"{BASE_URL}/projects/{project_b_id}", headers=headers_b)
        assert get_a_resp.status_code == 200
        assert get_b_resp.status_code == 200

        settings_a = get_a_resp.json()["settings"]["aiBehavior"]
        settings_b = get_b_resp.json()["settings"]["aiBehavior"]
        assert settings_a["identity"]["agentNames"] == ["أسماء"]
        assert settings_a["fallbacks"]["aiError"] == "رسالة مشروع أ"
        assert settings_b["identity"]["agentNames"] == ["هاني"]
        assert settings_b["fallbacks"]["aiError"] == "رسالة مشروع ب"
