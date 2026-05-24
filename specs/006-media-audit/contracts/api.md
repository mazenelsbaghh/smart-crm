# API Contracts: Shared Assets, Media Engine & Audit Trail

## HTTP REST Endpoints

### 1. Shared Assets API

#### Upload Asset
- **Route**: `POST /api/assets/upload`
- **Request (Multipart Form Data)**:
  - `file`: The media file binary
  - `projectId`: GUID of the project
- **Response**: `201 Created`
```json
{
  "id": "7fa85f64-5717-4562-b3fc-2c963f66afa6",
  "projectId": "11112222-3333-4444-5555-666677778888",
  "fileName": "avatar.jpg",
  "contentType": "image/jpeg",
  "fileSize": 1048576,
  "fileHash": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
  "storagePath": "projects/11112222-3333-4444-5555-666677778888/assets/7fa85f64-5717-4562-b3fc-2c963f66afa6.jpg",
  "referenceCount": 1,
  "uploadedBy": "22223333-4444-5555-6666-777788889999",
  "createdAt": "2026-05-25T10:15:30Z"
}
```

#### Download Asset (Secure Signed URL)
- **Route**: `GET /api/assets/{id}/download`
- **Response**: `200 OK`
```json
{
  "assetId": "7fa85f64-5717-4562-b3fc-2c963f66afa6",
  "downloadUrl": "http://localhost:9000/smartcore-media/projects/11112222-3333-4444-5555-666677778888/assets/7fa85f64.jpg?X-Amz-Algorithm=AWS4-HMAC-SHA256&...",
  "expiresAt": "2026-05-25T11:15:30Z"
}
```

#### Retrieve Thumbnail
- **Route**: `GET /api/assets/{id}/thumbnail`
- **Response**: `200 OK` (binary image payload) or redirect to signed variant path.

#### Delete Asset
- **Route**: `DELETE /api/assets/{id}`
- **Response**: `204 No Content` (if reference count becomes 0 and file is deleted from MinIO) or `200 OK` (if reference count is decremented but still active).

---

### 2. Audit Trail API

#### Query Audit Logs
- **Route**: `GET /api/projects/{projectId}/audit`
- **Query Parameters**:
  - `action`: string (optional, e.g. "CustomerUpdate")
  - `user`: string (optional, GUID of modifier)
  - `from`: string (optional, ISO date)
  - `to`: string (optional, ISO date)
- **Response**: `200 OK`
```json
{
  "totalCount": 1,
  "logs": [
    {
      "id": "a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d",
      "projectId": "11112222-3333-4444-5555-666677778888",
      "userId": "22223333-4444-5555-6666-777788889999",
      "action": "CustomerUpdate",
      "entityType": "Customer",
      "entityId": "88889999-aaaa-bbbb-cccc-ddddeeeeffff",
      "originalState": "{\"City\":\"Cairo\",\"LeadScore\":50}",
      "newState": "{\"City\":\"Alexandria\",\"LeadScore\":75}",
      "ipAddress": "192.168.1.100",
      "timestamp": "2026-05-25T10:18:22Z"
    }
  ]
}
```

---

### 3. System Health API

#### Check System Health
- **Route**: `GET /api/system/health`
- **Response**: `200 OK` (Healthy) or `503 Service Unavailable` (Unhealthy)
```json
{
  "status": "Healthy",
  "components": {
    "PostgreSQL": "Healthy",
    "Redis": "Healthy",
    "RabbitMQ": "Healthy",
    "MinIO": "Healthy",
    "Elasticsearch": "Healthy",
    "WhatsAppGateway": "Healthy",
    "GeminiAPI": "Healthy"
  },
  "timestamp": "2026-05-25T10:20:00Z"
}
```

#### Get System Metrics
- **Route**: `GET /api/system/metrics`
- **Response**: `200 OK`
```json
{
  "rabbitMQ": {
    "queueDepth": 0
  },
  "redis": {
    "connectedClients": 5,
    "usedMemoryBytes": 1543200
  },
  "postgreSQL": {
    "activeConnections": 8
  },
  "gemini": {
    "averageLatencyMs": 850
  },
  "timestamp": "2026-05-25T10:20:00Z"
}
```
