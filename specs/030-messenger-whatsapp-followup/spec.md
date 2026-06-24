# Feature Specification: Messenger & WhatsApp Follow-Up Integration

**Feature Branch**: `030-messenger-whatsapp-followup`

**Created**: 2026-06-24

**Status**: Draft

**Input**: User description: "عايز يكون فيه فولوا اب برضو للماسنجر عايز احاول اخد رقم التلفون من ماسنجر و ابعتلا مسدج واتساب اول ما اخد الرقم و اقولوا نتواصل هنا و بتاع لاي حد ولو اخت رقموا يوقفلوا متابعه ماسنجر و يبقي للواتساب بس وف المسانجر داميا انبه عليه ان اول سيشن مجانيه شبه ما بقول علي الواتساب"

## Clarifications

### Session 2026-06-24
- Q: كيف سيتم الحصول على رقم الهاتف من ماسنجر؟ → A: يقوم الذكاء الاصطناعي (AI) باستخراج رقم الهاتف من رسائل المستخدم العادية في المحادثة، وبمجرد إرسال الرقم، يرسل النظام رسالة واتساب مخصصة باسم العميل، ويرد على ماسنجر لإعلامه بإرسال رسالة الواتساب.
- Q: ما هو نص الرسالة الافتراضية التي سيتم إرسالها على واتساب عند أخذ رقم الهاتف؟ → A: رسالة بالعامية المصرية بأسلوب سيلز محترم وباسم المشروع ديناميكياً: "أهلاً يا [اسم العميل]، منورنا يا فندم! 😊 معاك [اسم البروجكت].. زي ما اتفقنا على ماسنجر، هنكمل كلامنا هنا على واتساب عشان نتابع مع بعض أسرع ونبعتلك كل التفاصيل بسهولة. وحابب أفكرك إن أول جلسة ليك معانا مجانية تماماً! لو تحب تحجزها دلوقتي، قولي الميعاد المناسب ليك وهسجلك فيه فوراً.".
- Q: هل نقوم بإعادة تفعيل متابعة ماسنجر إذا فشل إرسال رسالة واتساب الأولى؟ → A: نعم، يتم إرسال رسالة على ماسنجر للعميل ("حاولنا نبعتلك على الواتساب بس غالباً الرقم غلط أو مش عليه واتساب. يا ريت تبعتلي الرقم الصح هنا عشان نتواصل هناك.") ويُطلب منه إرسال الرقم مجدداً، وإذا لم ينجح الأمر يستمر النظام في متابعة العميل عبر ماسنجر كقناة بديلة.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Messenger First Session Reminder (Priority: P1)
As a lead interacting with the business on Facebook Messenger, I want to be reminded that the first session is free, so that I am encouraged to book.

**Why this priority**: Immediate conversion driver, aligns Messenger UX with WhatsApp.

**Independent Test**: Can be fully tested by sending a message on Messenger and verifying that the AI responder mentions that the first session is free.

**Acceptance Scenarios**:
1. **Given** a new chat session on Facebook Messenger, **When** a user sends any message, **Then** the AI responder includes a friendly reminder that the first session is free (e.g., "أول جلسة مجانية").

---

### User Story 2 - Capture Phone Number and Send WhatsApp Message (Priority: P1)
As a system, when a lead provides their phone number in a Messenger conversation, I want to automatically trigger a WhatsApp message to them introducing the transition, so they know we can communicate there.

**Why this priority**: Crucial for multi-channel lead engagement and moving them to the preferred channel.

**Independent Test**: Can be tested by simulating a phone number input on Messenger and checking if a WhatsApp message is sent to that number.

**Acceptance Scenarios**:
1. **Given** an ongoing conversation on Messenger, **When** the lead shares a valid phone number, **Then** the system extracts the phone number and immediately sends a WhatsApp message (e.g., "نتواصل هنا...").

---

### User Story 3 - Transition Follow-Up Channel (Priority: P2)
As a business owner, I want Messenger follow-ups to stop and WhatsApp follow-ups to begin once the lead's phone number is captured, so that the lead isn't spammed across multiple channels and is nurtured on WhatsApp only.

**Why this priority**: Prevent multi-channel spamming and keep customer record unified.

**Independent Test**: Can be verified by checking that pending Messenger follow-up tasks are deactivated/cancelled and new WhatsApp follow-up tasks are scheduled.

**Acceptance Scenarios**:
1. **Given** a lead with active Messenger follow-up schedule, **When** their phone number is captured and verified, **Then** the Messenger follow-up schedule is marked inactive/stopped, and a WhatsApp follow-up sequence is initialized.

---

### Edge Cases

- **Invalid or Malformed Phone Numbers**: What happens if the phone number provided is invalid or has an unsupported country code? System will fail validation and continue tracking via Messenger.
- **User already has a WhatsApp record**: If the phone number belongs to an existing WhatsApp contact, how do we merge the Messenger lead with the WhatsApp contact? We merge the contact profiles.
- **WhatsApp message sending fails**: If the initial WhatsApp message fails to send (e.g., number doesn't have WhatsApp), the system automatically falls back to Messenger, sends a Messenger alert asking for the correct number, and resumes Messenger follow-ups if no working number is provided.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: AI Responder for Messenger MUST detect the context of the chat and explicitly mention that the first session is free.
- **FR-002**: The system MUST detect phone numbers in Messenger messages by utilizing AI (Gemini)/Regex parsing from the user's natural text messages, extract the phone number, and immediately trigger the WhatsApp message.
- **FR-003**: Once a phone number is detected, the system MUST format it to E.164 and immediately send a WhatsApp transition message in Egyptian colloquial salesy style using the dynamic project/brand name: "أهلاً يا [اسم العميل]، منورنا يا فندم! 😊 معاك [اسم البروجكت].. زي ما اتفقنا على ماسنجر، هنكمل كلامنا هنا على واتساب عشان نتابع مع بعض أسرع ونبعتلك كل التفاصيل بسهولة. وحابب أفكرك إن أول جلسة ليك معانا مجانية تماماً! لو تحب تحجزها دلوقتي، قولي الميعاد المناسب ليك وهسجلك فيه فوراً.".
- **FR-004**: The system MUST automatically update the customer's communication channel preference to WhatsApp and disable/cancel all scheduled Messenger follow-ups.
- **FR-005**: The system MUST schedule subsequent follow-ups only on the WhatsApp channel.

### Key Entities

- **Customer / Lead**: Represents the user. Attributes: Messenger ID, Phone/WhatsApp Number, Follow-up Channel Preference (Messenger, WhatsApp, Both), Active Follow-up Status.
- **Follow-up Task / Schedule**: Represents scheduled messages. Attributes: Customer ID, Channel, Scheduled Time, Status (Pending, Sent, Cancelled).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of Messenger leads providing a phone number are transitioned to WhatsApp within 10 seconds of number capture.
- **SC-002**: Messenger follow-up tasks are cancelled and replaced with WhatsApp follow-up tasks with 0% duplication.
- **SC-003**: Messenger AI responses include the "first session free" reminder in at least 95% of new conversation sessions.

## Assumptions

- We have a functioning WhatsApp Gateway capable of sending messages.
- The Facebook Messenger webhook is active and can receive and send messages.
- The CRM database has fields for tracking lead channels and follow-up states.
