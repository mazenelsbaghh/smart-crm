# Feature Specification: WhatsApp Media & AI Processing

**Feature Branch**: `017-whatsapp-media-ai`

**Created**: 2026-06-01

**Status**: Draft

**Input**: User description: "WhatsApp Media & AI - استقبال وتحليل الصور والصوت بالذكاء الاصطناعي وتخزينها في MinIO"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Receiving and Understanding Customer Voice Notes (Priority: P1)

As a customer support representative (or an automated AI Agent), I want the system to automatically transcribe and understand incoming customer voice notes so that the customer receives a prompt, relevant answer without human operators having to listen to every voice message.

**Why this priority**: High value. Many customers prefer sending voice notes over writing long texts on WhatsApp. Automating the understanding of voice messages is critical to achieving full auto-reply coverage.

**Independent Test**: Send a 10-second voice note saying "أنا مهتم بكورس الذكاء الاصطناعي وبدي أعرف السعر" to the WhatsApp number, and verify that the system auto-transcribes it, extracts the interest (AI Course), and generates a relevant pricing response.

**Acceptance Scenarios**:

1. **Given** a WhatsApp customer has sent a voice note, **When** the gateway receives the message, **Then** it downloads the audio file, uploads it to the Object Storage, transcribes the voice note, and feeds the transcription to the AI Marketing Brain to reply.
2. **Given** an incoming voice note is successfully transcribed, **When** viewed in the Agent Dashboard inbox, **Then** it shows the audio player alongside a written transcription text in Arabic.

---

### User Story 2 - Receiving and Processing Customer Images/Documents (Priority: P1)

As a sales agent or automated system, I want customers to be able to send images (such as payment receipts, invoices, or product screenshots) and have the system read them directly using multimodal AI, so that facts (like payment details or product inquiries) can be auto-registered.

**Why this priority**: Essential for business workflows. Customers often send payment receipts to confirm orders, and automating the verification of receipts or reading inquiries from images dramatically speeds up conversion.

**Independent Test**: Send an image containing text "Receipt No: 987654, Paid: 50 USD" to the WhatsApp number, and verify that the CRM updates the customer budget or notes with these details automatically.

**Acceptance Scenarios**:

1. **Given** a customer sends an image message on WhatsApp, **When** the gateway intercepts it, **Then** the file is uploaded to the Object Storage, sent to the multimodal AI engine, and relevant details (e.g. Budget = 50 USD, Tag = payment receipt) are suggested or applied.
2. **Given** the image contains no legible text, **When** analyzed by the AI, **Then** the AI describes the visual content and registers it in the chat timeline without failing.

---

### User Story 3 - Viewing and Replaying Media inside the Conversation History (Priority: P2)

As a support agent, I want to be able to view images and listen to audio voice notes directly inside the conversation history in my dashboard inbox, so that I can have full context of the customer journey.

**Why this priority**: High UX value. Agents need to see exactly what the customer sent to intervene effectively.

**Independent Test**: Open the customer chat in the Inbox page, and verify that sent/received images render as clickable previews and voice notes render as audio play bars.

**Acceptance Scenarios**:

1. **Given** a conversation history contains media messages, **When** the agent opens the chat window, **Then** temporary secure pre-signed URLs are generated to load the media files directly from the Object Storage.
2. **Given** a voice note is rendered, **When** the agent clicks play, **Then** the audio plays back successfully inside the browser.

---

### User Story 4 - Broadcasters sending media attachments in campaigns (Priority: P3)

As a campaign manager, I want to attach images or documents to broadcast messages so that I can run visually appealing promotions or send brochures to segments.

**Why this priority**: Marketing enhancement. Visual media increases campaign conversion rates.

**Independent Test**: Create a broadcast campaign with an attached product image, and verify that recipients receive the message containing both the text template and the image.

**Acceptance Scenarios**:

1. **Given** a campaign is configured with a media attachment, **When** the campaign scheduler runs, **Then** the gateway sends the media files along with the personalized texts.

---

### Edge Cases

- **Media size exceeds limits**: If a customer sends a very large video or file (e.g., > 16MB), the gateway should reject or skip downloading the full payload, logging a limit warning, and placing a placeholder in the chat: `[ملف كبير جداً تم تجاهله]`.
- **Unsupported media formats**: If a media type is not supported by the AI or gateway, it should be uploaded to storage but marked as a generic file asset, allowing agents to download it manually without AI processing.
- **Upload failure to Object Storage**: If Object Storage is temporarily offline, the system must write the incoming message as a text placeholder `[وسائط غير متوفرة حالياً]` and schedule a retry or log an audit error, preventing the gateway webhook from crashing.
- **AI multimodal parsing failure**: If Gemini fails to analyze the image or audio file, the system should fall back to saving the media file as a reference and reply with a generic fallback response.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The WhatsApp gateway MUST download incoming media files (images, audio voice notes, documents) from WhatsApp servers using Baileys keys.
- **FR-002**: The gateway MUST upload all downloaded media files directly to the secure Object Storage bucket (`smartcore-media`).
- **FR-003**: The system MUST store metadata for every media file in an `Assets` table, linking each asset to a `ProjectId` and generating reference IDs.
- **FR-004**: The system MUST support generating secure pre-signed URLs with a 1-hour expiration to serve media assets to the Frontend dashboard.
- **FR-005**: The AI Engine MUST accept multi-modal requests (text + raw media bytes/stream) to process voice note transcriptions and image contents directly without relying on external OCR/Speech-to-text tools.
- **FR-006**: The system MUST save voice note transcriptions as metadata on the `Message` entity or a sub-table, making them searchable via Elasticsearch.
- **FR-007**: The system MUST resize and optimize images (creating a compressed version and a thumbnail) before saving to reduce storage and delivery bandwidth.

### Key Entities *(include if feature involves data)*

- **Asset (CustomerMedia)**:
  - `Id` (UUID)
  - `FileName` (String)
  - `FilePath` (String - storage key)
  - `MimeType` (String)
  - `FileSize` (Long)
  - `ProjectId` (UUID - tenant isolation)
  - `CreatedAt` (DateTime)
- **AssetVariant**:
  - `Id` (UUID)
  - `AssetId` (UUID - reference to parent Asset)
  - `VariantType` (String - e.g. "Thumbnail", "Optimized")
  - `FilePath` (String)
  - `FileSize` (Long)
- **Message**:
  - `AssetId` (UUID - optional reference to a media Asset)
  - `Transcription` (String - optional text transcription for voice notes)

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of incoming WhatsApp media files under 10MB are successfully uploaded to Object Storage and registered within 3 seconds of webhook delivery.
- **SC-002**: Voice note transcriptions are generated and stored with an word accuracy rate above 85% for standard Arabic/dialect voice notes.
- **SC-003**: Pre-signed secure URLs for dashboard image previews load under 1.5 seconds.
- **SC-004**: Images are optimized to reduce file size by at least 40% while maintaining visual readability for OCR-free extraction.

## Assumptions

- We assume the system has access to an active Object Storage service (e.g. MinIO container) and the `smartcore-media` bucket is configured.
- We assume the Gemini API key configured for the project supports multimodal inputs (Gemini 3.5 Flash natively does).
- High-resolution videos are stored but not sent to the AI for full processing (video frames are out of scope for the current AI reply loop).
