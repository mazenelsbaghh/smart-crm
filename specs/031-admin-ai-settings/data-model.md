# Data Model: Admin AI Behavior Settings

## ProjectSettings Extension

Existing entity: `Modules.Projects.Domain.ProjectSettings`

New field:
- `AiBehaviorSettingsJson: string?`
  - PostgreSQL type: `text`
  - Stores serialized `AIBehaviorSettings`
  - `null` means resolve all v1 defaults

## AIBehaviorSettings

Project-level structured configuration.

Fields:
- `Identity: AIIdentitySettings`
- `Tone: AIToneSettings`
- `Reactions: ReactionPolicySettings`
- `Fallbacks: FallbackMessageSettings`
- `Channels: Dictionary<string, ChannelAIBehaviorSettings>`
- `AdvancedInstructions: string?`

Validation:
- Channel keys allowed: `WhatsApp`, `Messenger`, `FacebookComment`
- Serialized payload max size: 64 KB
- Unknown JSON fields ignored during deserialization but not emitted by API defaults

## AIIdentitySettings

Fields:
- `AgentNames: string[]`
- `NameSelectionMode: string` (`First`, `HourlyRotation`)
- `SignatureEnabled: bool`
- `SignatureTemplate: string`
- `ComplaintSignatureTemplate: string`

Defaults:
- Agent names: existing shift names for backward compatibility
- Name selection: `HourlyRotation`
- Signature enabled: `true`
- Normal template: `- {agentName} ✨`
- Complaint template: `- {agentName}`

## AIToneSettings

Fields:
- `TonePreset: string`
- `CustomTone: string?`
- `TargetAudience: string`
- `AllowedPhrases: string[]`
- `ProhibitedPhrases: string[]`
- `BusinessInstructions: string?`

Defaults:
- Existing `AiTonePreference` and `AiTargetAudience` remain source-compatible.

## ReactionPolicySettings

Fields:
- `Enabled: bool`
- `AllowedReactions: string[]`
- `UseAiSuggestedReaction: bool`
- `Rules: string?`

Defaults:
- Enabled: `true`
- Allowed reactions: `👍`, `❤️`, `💖`, `😢`, `😂`, `😮`
- Use AI suggested reaction: `true`

State behavior:
- If disabled, AI prompt asks for `null`; backend blocks persistence and sending.
- If enabled but reaction not allowed, backend drops it.

## FallbackMessageSettings

Fields:
- `AiError: string`
- `InvalidAiOutput: string`
- `GenericCustomerService: string`
- `FacebookPublicComment: string`
- `WhatsAppTransitionSuccess: string`
- `WhatsAppTransitionFailure: string`
- `FollowUpDefault: string`

Supported placeholders:
- `{customerName}`
- `{agentName}`
- `{projectName}`
- `{phoneNumber}`
- `{channel}`

Validation:
- Each template max length: 1000 characters
- Reject unsupported placeholders before saving

## ChannelAIBehaviorSettings

Fields:
- `Identity: AIIdentitySettings?`
- `Tone: AIToneSettings?`
- `Reactions: ReactionPolicySettings?`
- `Fallbacks: FallbackMessageSettings?`
- `AdditionalInstructions: string?`

Merge rule:
- Undefined channel fields inherit shared defaults.
- Defined channel fields override only the matching shared field.
- `AdditionalInstructions` appends after shared behavior instructions for that channel.
