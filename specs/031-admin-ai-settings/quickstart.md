# Quickstart: Admin AI Behavior Settings

## 1. Save structured AI behavior settings

```bash
curl -X PUT "http://localhost:80/api/projects/<PROJECT_ID>/settings" \
  -H "Content-Type: application/json" \
  -H "X-Project-Id: <PROJECT_ID>" \
  -d '{
    "aiAutoReplyEnabled": true,
    "timezone": "Africa/Cairo",
    "aiBehavior": {
      "identity": {
        "agentNames": ["سارة", "منى"],
        "nameSelectionMode": "HourlyRotation",
        "signatureEnabled": true,
        "signatureTemplate": "- {agentName} ✨",
        "complaintSignatureTemplate": "- {agentName}"
      },
      "fallbacks": {
        "whatsAppTransitionFailure": "الرقم مش واضح يا فندم، ابعت رقم واتساب صحيح."
      },
      "reactions": {
        "enabled": false,
        "allowedReactions": ["👍", "❤️"],
        "useAiSuggestedReaction": false
      }
    }
  }'
```

Expected result: `200 OK`.

## 2. Verify settings return on reload

```bash
curl "http://localhost:80/api/projects/<PROJECT_ID>" -H "X-Project-Id: <PROJECT_ID>"
```

Expected result: response includes `settings.aiBehavior.identity.agentNames`.

## 3. Verify invalid template is rejected

```bash
curl -X PUT "http://localhost:80/api/projects/<PROJECT_ID>/settings" \
  -H "Content-Type: application/json" \
  -H "X-Project-Id: <PROJECT_ID>" \
  -d '{
    "aiAutoReplyEnabled": true,
    "aiBehavior": {
      "fallbacks": {
        "whatsAppTransitionSuccess": "Hello {wrongName}"
      }
    }
  }'
```

Expected result: `400 Bad Request` with unsupported placeholder message.

## 4. Verification commands

```bash
dotnet build backend/backend.csproj
pytest tests/phase_1/test_admin_ai_settings.py
pytest tests/phase_1/test_ai_gemini.py::test_ai_gemini_reaction_and_no_buttons
pytest tests/phase_1/test_messenger_whatsapp_followup.py::test_messenger_to_whatsapp_transition
cd frontend && npm run build
```
