# Feature Specification: Admin AI Behavior Settings

**Feature Branch**: `031-admin-ai-settings`

**Created**: 2026-06-27

**Status**: Draft

**Input**: User description: "شوف اي تاني عايزه يتغير لدينامك ف البروماتا و حاجاات دي و كل ده بتعدل من من الادمن بشكل احسن حتي اسماء الموظفين و كل ده"

## Clarifications

### Session 2026-06-27

- Q: ما شكل واجهة الإدارة المطلوبة لإعدادات سلوك الذكاء الاصطناعي؟ → A: واجهة منظمة بأقسام واضحة: الهوية والتوقيع، النبرة والجمهور، القنوات، الريأكشن، رسائل fallback، ومعها حقل Advanced للتعليمات الإضافية. يجب أن تكون الإعدادات عبارة عن inputs واضحة لكل قيمة، مع اختيارات ثابتة لبعض الحقول وإمكانية كتابة قيمة مخصصة عند الحاجة.
- Q: كيف يجب أن يتعامل النظام مع حقل SystemPrompt الحالي عند إضافة إعدادات AI المنظمة الجديدة؟ → A: يبقى SystemPrompt كحقل Advanced instructions ثانوي فقط. الأولوية تكون للقواعد المحمية أولاً، ثم الإعدادات المنظمة، ثم تعليمات Advanced، ولا يسمح لأي تعليمات متقدمة بكسر JSON/schema أو قواعد CRM المحمية.
- Q: ما نموذج الوراثة والأولوية المطلوب بين الإعدادات العامة وتجاوزات القنوات المختلفة؟ → A: الإعدادات العامة هي الافتراضي. أي قيمة معرفة داخل قناة معينة تتجاوز نفس القيمة العامة فقط، ويمكن لكل قناة إضافة تعليمات أو رسائل أو قيم إضافية خاصة بها دون التأثير على القنوات الأخرى.
- Q: ما سياسة التحقق من القوالب والـ placeholders قبل حفظ رسائل fallback والانتقال؟ → A: يرفض النظام حفظ أي قالب يحتوي على placeholder غير مدعوم أو يتجاوز الحد الأقصى للطول، ويعرض رسالة خطأ واضحة للأدمن قبل أن يؤثر القالب على العملاء.
- Q: هل سياسة التفاعلات الجديدة تتحكم فقط في اقتراحات الذكاء الاصطناعي، أم أيضًا في تنفيذ التفاعل فعليًا على WhatsApp/Facebook؟ → A: الأدمن يمكنه التحكم في مسار التفاعل بالكامل. السياسة تتحكم في اقتراح الـ AI، السماح أو المنع في backend، الحفظ في المحادثة، والإرسال الفعلي لكل قناة.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Configure AI identity and prompt behavior (Priority: P1)

As a project admin, I want one organized admin settings experience where I can configure staff names, signatures, tone, target audience, prompt behavior instructions, fallback messages, and reaction behavior without editing code, so the AI reply behavior matches my business and can change safely after deployment.

**Why this priority**: This directly removes the current hardcoded behavior that causes AI replies to ignore admin intent or fall back to generic static messages.

**Independent Test**: Update AI behavior settings for a project, send a sample customer message through the existing AI reply flow, and verify the reply uses the configured staff identity, signature, tone, and fallback/reaction policy without deployment.

**Acceptance Scenarios**:

1. **Given** a project admin opens project settings, **When** they update staff names, signature settings, tone/audience, and custom behavior instructions, **Then** the settings are saved and returned on reload for that same project only.
2. **Given** a project has custom AI behavior settings, **When** the AI generates a WhatsApp or Messenger reply, **Then** the prompt uses those settings while still returning the required structured AI response for CRM automation.
3. **Given** a project admin disables automatic reactions, **When** the AI analyzes a positive, greeting, or complaint message, **Then** no reaction is created unless the configured policy explicitly allows it.
4. **Given** a channel has additional custom instructions beyond shared defaults, **When** that channel generates a reply, **Then** the AI uses the shared defaults plus the channel-specific additions without applying those additions to other channels.

---

### User Story 2 - Configure channel-specific messages and fallbacks (Priority: P2)

As a project admin, I want to configure channel-specific fallback and transition messages for WhatsApp, Messenger, and Facebook comments, so customers do not receive generic static text when AI or gateway flows fail.

**Why this priority**: The current static messages appear in high-risk customer-facing paths such as invalid AI output, WhatsApp transition, WhatsApp send failure, and Facebook public comment fallback.

**Independent Test**: Configure different fallback messages for WhatsApp, Messenger, and Facebook comments, simulate each fallback path, and verify each channel uses its own configured text.

**Acceptance Scenarios**:

1. **Given** a configured AI engine error fallback, **When** the AI provider fails or returns an unusable response, **Then** the customer receives the configured fallback message for that channel.
2. **Given** a configured Messenger-to-WhatsApp transition message, **When** a Messenger customer shares a phone number and WhatsApp delivery succeeds, **Then** the WhatsApp message uses the configured template with supported dynamic values.
3. **Given** a configured WhatsApp send failure fallback, **When** the Messenger-to-WhatsApp send fails, **Then** the Messenger fallback message uses the configured failure template instead of a static hardcoded sentence.
4. **Given** a configured Facebook public comment fallback, **When** AI public comment content is missing, **Then** the public reply uses the configured fallback.

---

### User Story 3 - Preserve protected AI invariants and defaults (Priority: P3)

As an owner, I want admins to customize brand behavior without being able to break protected AI rules, so CRM updates, JSON parsing, pricing guards, booking rules, and tenant isolation remain reliable.

**Why this priority**: Admin customization must not compromise the system contract that downstream workers rely on.

**Independent Test**: Save aggressive or incomplete custom prompt text, then verify the system still enforces structured AI output, protected rules, defaults, and project isolation.

**Acceptance Scenarios**:

1. **Given** an admin enters custom instructions that conflict with protected JSON/schema rules, **When** a reply is generated, **Then** the protected format and CRM schema still take precedence.
2. **Given** existing projects have no new structured AI behavior settings, **When** AI replies are generated, **Then** defaults remain equivalent to current behavior unless the admin changes them.
3. **Given** two projects configure different staff names and fallback messages, **When** both projects generate replies, **Then** each project uses only its own settings.

### Edge Cases

- Empty or partially configured AI behavior settings fall back to safe project defaults.
- Invalid or unsupported reaction values are rejected by validation or blocked by backend enforcement without saving or sending a reaction.
- Admin custom text may contain placeholders; unsupported placeholders or over-limit templates must be rejected before saving with a clear validation error.
- Existing `SystemPrompt` text is treated as secondary Advanced instructions; protected rules and structured admin settings take precedence over it.
- Prompt/context caching must refresh when AI behavior settings change.
- Protected rules such as JSON response format, anti-hallucination pricing behavior, group booking safety, and tenant isolation cannot be disabled by admin settings.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide structured project-level AI behavior settings editable by admins, separate from raw advanced prompt text.
- **FR-002**: System MUST support admin-configurable staff/agent identity including one or more display names, signature enabled/disabled state, normal signature template, and complaint/negative signature template.
- **FR-003**: System MUST support admin-configurable tone, target audience, allowed phrase guidance, prohibited phrase guidance, and additional business behavior instructions.
- **FR-004**: System MUST support admin-configurable reaction policy including enable/disable state, allowed reactions, when reactions should be suggested or suppressed, and backend enforcement that prevents disallowed reactions from being saved or sent.
- **FR-005**: System MUST support admin-configurable fallback message templates for AI engine errors, invalid AI output, WhatsApp/Messenger generic fallback, Facebook public comment fallback, WhatsApp transition success, and WhatsApp transition failure.
- **FR-006**: System MUST support per-channel overrides for WhatsApp, Messenger, and Facebook comments while retaining shared defaults; channel values override only the same shared keys and may add channel-specific extra instructions, templates, or values.
- **FR-007**: System MUST assemble AI prompts with this precedence order: protected system rules first, structured admin AI behavior settings second, and legacy/advanced `SystemPrompt` instructions third.
- **FR-008**: System MUST preserve the required structured AI response format used by CRM updates, follow-ups, public comment replies, reactions, group booking, cancellation, and transcription.
- **FR-009**: System MUST validate admin settings before saving so invalid reaction values, invalid channel keys, excessive template sizes, or unsupported template placeholders are rejected with clear feedback and cannot silently break reply flows.
- **FR-010**: System MUST return saved AI behavior settings through the existing project settings API and persist updates per project.
- **FR-011**: System MUST maintain backward compatibility for existing projects by providing defaults equivalent to current behavior when no structured settings exist.
- **FR-012**: System MUST ensure prompt cache keys or cache invalidation include AI behavior setting changes so stale prompts are not reused after admin edits.
- **FR-013**: System MUST document which current hardcoded AI-facing strings are configurable and which are protected invariants.
- **FR-014**: System MUST update the admin settings UI to expose the new configuration in organized sections rather than requiring admins to edit one large prompt textarea.
- **FR-015**: System MUST provide explicit input controls for each configurable AI behavior value, including fixed selectable options where appropriate and a custom/free-form option where admins need project-specific values.

### Key Entities *(include if feature involves data)*

- **AI Behavior Settings**: Project-specific structured configuration for identity, signature, tone/audience, phrase rules, reactions, fallback templates, channel overrides, and advanced instructions.
- **AI Identity Settings**: Staff names, name selection mode, signature templates, and signature enablement.
- **Reaction Policy Settings**: Whether automatic reactions are enabled, allowed reaction values, mapping or instruction text for when to use reactions, and enforcement rules for AI suggestion, persistence, and channel delivery.
- **Fallback Message Settings**: Customer-facing fallback and transition templates with supported dynamic placeholders.
- **Channel Behavior Override**: Optional channel-specific overrides for WhatsApp, Messenger, and Facebook comments. Undefined channel fields inherit shared defaults; defined channel fields override the matching shared field; channel-specific additions apply only to that channel.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An admin can change staff names, signature wording, and fallback messages from project settings and observe changed AI replies without code changes or redeployment.
- **SC-002**: 100% of customer-facing hardcoded AI reply/fallback/transition/public-comment strings in the AI reply flow are either configurable or documented as protected invariants.
- **SC-003**: Existing projects without custom structured settings continue to generate valid AI replies within the existing response window and do not lose existing auto-reply behavior.
- **SC-004**: AI behavior settings are isolated per project; tests verify that one project's configured staff names and fallback messages do not appear in another project's replies across at least 2 projects.
- **SC-005**: Invalid admin configuration, including unsupported placeholders and templates over 1000 characters, is rejected with clear feedback before it can affect customer replies.
- **SC-006**: AI output parsing and CRM automation continue to work even when admin custom prompt instructions are changed.
- **SC-007**: Runtime resolution of AI behavior settings adds less than 5 ms per AI reply path under normal settings size.

## Assumptions

- Existing project authentication and project settings access control will be reused.
- The first implementation will manage settings per project, not per individual user account.
- This feature changes behavior/style configuration, not knowledge base business facts such as prices, locations, or course details.
- Protected system rules remain code-owned and are not exposed as editable admin fields.
- Existing `SystemPrompt` remains available as a secondary advanced instructions field for backward compatibility and cannot override protected rules or structured settings.
- The admin UI will use structured sections and explicit inputs for identity/signature, tone/audience, channels, reactions, fallback messages, and Advanced instructions.
