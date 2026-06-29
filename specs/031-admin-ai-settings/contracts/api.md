# API Contract: Admin AI Behavior Settings

## GET /api/projects/{id}

Existing response gains `settings.aiBehavior`.

```json
{
  "id": "project-guid",
  "name": "Project name",
  "settings": {
    "aiAutoReplyEnabled": true,
    "aiTonePreference": "العامية المصرية المهذبة والمحترمة",
    "aiTargetAudience": "طلاب كورس كول سنتر",
    "systemPrompt": "Advanced instructions",
    "aiBehavior": {
      "identity": {
        "agentNames": ["سارة", "منى"],
        "nameSelectionMode": "HourlyRotation",
        "signatureEnabled": true,
        "signatureTemplate": "- {agentName} ✨",
        "complaintSignatureTemplate": "- {agentName}"
      },
      "tone": {
        "tonePreset": "polite-egyptian",
        "customTone": "",
        "targetAudience": "طلاب كورس كول سنتر يبحثون عن عمل",
        "allowedPhrases": ["يا فندم"],
        "prohibitedPhrases": ["يا صاحبي"],
        "businessInstructions": "ركز على أول سيشن مجانية"
      },
      "reactions": {
        "enabled": true,
        "allowedReactions": ["👍", "❤️", "😢"],
        "useAiSuggestedReaction": true,
        "rules": "استخدم الريأكشن فقط مع الشكر أو الشكوى"
      },
      "fallbacks": {
        "aiError": "حالياً هنوصل طلبك لفريق خدمة العملاء.",
        "invalidAiOutput": "ثواني يا فندم وهنرد عليك بالتفاصيل.",
        "genericCustomerService": "أهلاً يا فندم، تحت أمرك.",
        "facebookPublicComment": "تم الرد في الرسائل الخاصة يا فندم.",
        "whatsAppTransitionSuccess": "أهلاً يا {customerName}، معاك {agentName}. هنكمل هنا على واتساب.",
        "whatsAppTransitionFailure": "الرقم مش واضح يا فندم، ابعت رقم واتساب صحيح.",
        "followUpDefault": "حبيت أطمن على حضرتك بخصوص التفاصيل."
      },
      "channels": {
        "Messenger": {
          "additionalInstructions": "اطلب رقم واتساب بلطف لو غير موجود."
        },
        "FacebookComment": {
          "fallbacks": {
            "facebookPublicComment": "بعتنالك التفاصيل في الخاص يا فندم."
          }
        }
      },
      "advancedInstructions": "أي تعليمات إضافية لا تكسر القواعد المحمية."
    }
  }
}
```

## PUT /api/projects/{id}/settings

Existing request gains optional `aiBehavior`.

Validation failures:

```json
{
  "error": "Unsupported placeholder '{wrongName}' in aiBehavior.fallbacks.whatsAppTransitionSuccess."
}
```

Rules:
- Unknown channel key returns `400`.
- Unsupported reaction returns `400`.
- Unsupported placeholder returns `400`.
- Template longer than 1000 characters returns `400`.
- Missing `aiBehavior` preserves previous/default behavior.

## Runtime Behavior Contracts

- AI prompt assembly uses protected rules → structured `aiBehavior` → channel override → `systemPrompt`/advanced instructions.
- Reaction sending endpoints must call the reaction policy before saving/sending.
- Fallback message lookup must resolve by channel first, then shared fallback, then protected safe default.
