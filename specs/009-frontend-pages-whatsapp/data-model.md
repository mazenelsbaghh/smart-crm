# Client Data Models & API Contracts

## 1. WhatsApp Connection Status

### GET `/api/whatsapp/session/status?projectId={projectId}`
**Response Shape**:
```json
{
  "projectId": "d3b07384-d113-4a15-bbf9-000000000000",
  "status": "Disconnected" | "Initializing" | "Connected",
  "phoneNumber": "1234567890" | null
}
```

### GET `/api/whatsapp/session/qr?projectId={projectId}`
**Response Shape**:
```json
{
  "qr": "1@..."
}
```

---

## 2. AI Auto-Reply Approvals Queue

### GET `/api/projects/{projectId}/approvals?status=Pending`
**Response Shape**:
```json
[
  {
    "id": "guid-approval-request-1",
    "customerId": "guid-customer-1",
    "customerName": "John Doe",
    "messageId": "guid-message-1",
    "originalMessage": "Hello, is this service available?",
    "proposedContent": "Yes, we are open 24/7!",
    "riskReason": "Mentions pricing or core configurations",
    "status": "Pending" | "Approved" | "Rejected"
  }
}
```

### POST `/api/projects/{projectId}/approvals/{id}/action`
**Request Body**:
```json
{
  "action": "Approved" | "Rejected"
}
```

---

## 3. CRM Follow-ups Registry

### GET `/api/projects/{projectId}/follow-ups`
**Response Shape**:
```json
[
  {
    "id": "guid-follow-up-1",
    "customerId": "guid-customer-1",
    "customerName": "John Doe",
    "dueDate": "2026-05-25T12:00:00Z",
    "notes": "Follow up on product request",
    "status": "Pending" | "Completed" | "Overdue"
  }
]
```

### POST `/api/projects/{projectId}/follow-ups/{id}/complete`
**Response**: `200 OK` or `204 No Content`

---

## 4. Campaigns Dashboard

### GET `/api/projects/{projectId}/campaigns`
**Response Shape**:
```json
[
  {
    "id": "guid-campaign-1",
    "name": "Summer Discount Offer",
    "templateContent": "Get 20% off all packages!",
    "status": "Draft" | "Pending" | "Sending" | "Completed",
    "targetSegment": "All Cairo Leads",
    "sentCount": 42
  }
]
```

### POST `/api/projects/{projectId}/campaigns`
**Request Body**:
```json
{
  "name": "Summer Discount Offer",
  "templateContent": "Get 20% off all packages!",
  "targetSegment": "All Cairo Leads"
}
```
