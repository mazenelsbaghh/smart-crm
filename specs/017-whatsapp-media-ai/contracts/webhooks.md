# API Contracts: Webhook Media Messages

## 1. Incoming Message Webhook
Endpoint called by the Node.js WhatsApp Gateway to forward incoming messages. We will add a nullable `AssetId` and `Transcription` field to the payload.

### HTTP POST `/api/webhooks/whatsapp/message`

**Headers**:
- `Content-Type`: `application/json`

**Request Body JSON**:
```json
{
  "projectId": "a3b8d91c-1481-4566-a36c-2f960fdf6643",
  "messageId": "ABG3HJK892301",
  "sender": "201099887766",
  "senderJid": "201099887766@s.whatsapp.net",
  "senderLid": null,
  "name": "Mazen Elsbagh",
  "content": "[Voice Note]",
  "messageType": "Voice",
  "timestamp": 1780336643,
  "assetId": "e1f13b67-72fb-4899-b1d5-bc440eb61b40"
}
```

*Note: For media messages, the `content` contains a textual fallback description (e.g. `[Voice Note]`, `[Image]`), `messageType` indicates the media type (`Voice`, `Image`), and `assetId` contains the UUID of the newly uploaded S3 media asset in the backend.*

**Response**:
- Status: `200 OK`
- Body:
  ```json
  { "status": "Received" }
  ```
