# Data Models: WhatsApp Gateway Fixes

## Webhook Incoming Message Payload

The gateway forwards incoming messages to the backend webhook (`POST /api/webhooks/whatsapp/message`) using the following format:

```json
{
  "projectId": "uuid-string",
  "messageId": "string",
  "sender": "string (digits only, e.g. '1234567890')",
  "content": "string (message text)",
  "messageType": "string (Text | Image | Voice)",
  "timestamp": 1672531199
}
```

## Gateway Outgoing Message Request

The backend sends messages via the gateway (`POST /api/whatsapp/send`):

```json
{
  "projectId": "uuid-string",
  "to": "string (raw recipient number, e.g., '+123-456 7890')",
  "message": "string (text message to send)"
}
```

## Gateway Outgoing Message Response

Successful response from `POST /api/whatsapp/send`:

```json
{
  "messageId": "string",
  "status": "Sent"
}
```
