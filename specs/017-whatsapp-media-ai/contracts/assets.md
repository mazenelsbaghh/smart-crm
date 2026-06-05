# API Contracts: Media Assets API

## 1. Asset Upload
Endpoint called by the WhatsApp Gateway (or frontend) to store raw files.

### HTTP POST `/api/projects/{projectId}/assets/upload`

**Headers**:
- `Content-Type`: `multipart/form-data`

**Form Fields**:
- `file`: (Binary File stream)
- `uploadedBy`: (Guid - optional user reference)

**Response**:
- Status: `201 Created`
- Body JSON:
  ```json
  {
    "id": "e1f13b67-72fb-4899-b1d5-bc440eb61b40",
    "projectId": "a3b8d91c-1481-4566-a36c-2f960fdf6643",
    "fileName": "audio_recording.ogg",
    "contentType": "audio/ogg",
    "fileSize": 142030,
    "storagePath": "projects/a3b8d91c-1481-4566-a36c-2f960fdf6643/assets/e1f13b67-72fb-4899-b1d5-bc440eb61b40_audio_recording.ogg",
    "createdAt": "2026-06-01T21:40:26Z"
  }
  ```

---

## 2. Get Secure Pre-signed Asset URL
Endpoint called by the React frontend to fetch temporary secure URLs for rendering media assets.

### HTTP GET `/api/projects/{projectId}/assets/{id}/url`

**Headers**:
- `Authorization`: `Bearer <JWT_TOKEN>`

**Response**:
- Status: `200 OK`
- Body JSON:
  ```json
  {
    "assetId": "e1f13b67-72fb-4899-b1d5-bc440eb61b40",
    "url": "http://localhost:9000/smartcore-media/projects/a3b8d91c-1481-4566-a36c-2f960fdf6643/assets/e1f13b67-72fb-4899-b1d5-bc440eb61b40_audio_recording.ogg?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=...",
    "expiry": "2026-06-01T22:40:26Z"
  }
  ```
