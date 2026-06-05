# Research Notes: WhatsApp Media & AI Processing

## Decision 1: WhatsApp Media Download Approach
- **Decision**: In the Node.js `whatsapp-gateway` (`baileys-manager.js`), use the Baileys library function `downloadContentFromMessage` (or wrapper `downloadMediaMessage`) to fetch incoming media payloads as buffer streams, and immediately upload them via multi-part HTTP POST to the backend's `/api/assets/upload` endpoint.
- **Rationale**: Keeps the gateway lightweight and stateless. The gateway handles the WhatsApp connection and session decrypting, while the C# Backend handles storage policies, database indexing, and user permissions.
- **Alternatives considered**:
  - *Direct Upload from Gateway to MinIO*: Rejected because the gateway would need database credentials or S3 secret keys, exposing S3 endpoints in the Node app, which breaks Modular Monolith security boundaries.

## Decision 2: Backend Media Storage & Optimization
- **Decision**: The C# Backend `Media` module receives media files via an HTTP controller, uploads them to MinIO (`smartcore-media` bucket) under partitioned folders `/projects/{projectId}/assets/`, generates `Asset` and `AssetVariant` records in PostgreSQL, and creates 150x150 thumbnails for images.
- **Rationale**: Keeps the storage decoupled and isolated by ProjectId. ImageSharp provides standard and efficient image resizing/optimization. MinIO offers an S3-compatible, production-ready storage layer.
- **Alternatives considered**:
  - *Storing media in PostgreSQL bytea/LOB fields*: Rejected due to database bloating and poor performance with large files.

## Decision 3: Multimodal Gemini 3.5 Flash Integration
- **Decision**: Overload `IGeminiClient.GenerateReplyAsync` in the `AI` module to accept a collection of media assets (MIME type and Base64 representation). When constructing the REST request to `v1beta/models/gemini-3.5-flash:generateContent`, append `inlineData` part objects to the JSON payload.
- **Rationale**: Gemini 3.5 Flash supports native multimodal prompts (text, images, and audio/voice notes) in its payload parts. This eliminates the need for separate Whisper (Speech-to-text) or OCR containers, complying with Principle III of the Constitution.
- **Alternatives considered**:
  - *Using a separate speech-to-text API (e.g. Whisper)*: Rejected as it introduces extra infrastructure components and latency, which violates the system constitution.

## Decision 4: Inbox rendering and playback
- **Decision**: Serve media files to the React Frontend dashboard using temporary secure pre-signed URLs (generated via S3 API, valid for 1 hour). Render images via typical HTML img tags and audio voice notes using native playbars (`<audio controls />`).
- **Rationale**: Protects file assets from public exposure (denying direct S3 public access) while ensuring native and low-latency browser performance.
