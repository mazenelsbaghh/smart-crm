# API & Event Contracts: Campaigns, Analytics & Search

## HTTP REST Endpoints

### 1. Campaigns API

#### Create Campaign
- **Route**: `POST /api/projects/{projectId}/campaigns`
- **Request Body**:
```json
{
  "name": "Summer Special Offer",
  "segmentId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "messageTemplateA": "Hi {{CustomerName}}, we have a special discount: 30% off using code HOT30!",
  "messageTemplateB": "Hello {{CustomerName}}! Get 30% off today with code HOT30. Shop now!"
}
```
- **Response**: `201 Created` with Campaign JSON.

#### Schedule Campaign
- **Route**: `POST /api/campaigns/{campaignId}/schedule`
- **Request Body**: `"2026-05-26T10:00:00+03:00"` (ISO string)
- **Response**: `200 OK`

#### Get Campaign Results
- **Route**: `GET /api/campaigns/{campaignId}/results`
- **Response**: `200 OK`
```json
{
  "campaignId": "4a3b7c2d-1111-2222-3333-444455556666",
  "name": "Summer Special Offer",
  "status": "Completed",
  "sentCount": 100,
  "deliveredCount": 98,
  "readCount": 85,
  "responseCount": 24,
  "conversionRate": 0.24,
  "variants": {
    "A": {
      "sent": 50,
      "responses": 10
    },
    "B": {
      "sent": 50,
      "responses": 14
    }
  }
}
```

---

### 2. Analytics & Reports API

#### Get Aggregated Analytics
- **Route**: `GET /api/projects/{projectId}/analytics/{type}`
- **Parameters**: `type` can be `customer`, `sales`, `complaint`, `team`, `ai`, `campaign`.
- **Response**: `200 OK`
```json
[
  {
    "date": "2026-05-25",
    "metricType": "AI_Accuracy",
    "value": 0.942,
    "metadata": "{\"totalAI\":180,\"correctAI\":170}"
  }
]
```

#### Generate Report On-Demand
- **Route**: `POST /api/projects/{projectId}/reports/generate`
- **Request Body**:
```json
{
  "reportType": "AIPerformance",
  "startDate": "2026-05-01",
  "endDate": "2026-05-25"
}
```
- **Response**: `200 OK` with PDF/JSON report metadata.

---

### 3. Search API

#### Full-Text Search
- **Route**: `GET /api/projects/{projectId}/search`
- **Parameters**:
  - `q`: Search keyword (string)
  - `type`: Target type (e.g. `conversations`, `customers`, `messages`)
- **Response**: `200 OK`
```json
{
  "totalMatches": 2,
  "matches": [
    {
      "id": "7fa85f64-5717-4562-b3fc-2c963f66afa6",
      "type": "Message",
      "snippet": "I want to buy the summer discount package",
      "score": 1.45,
      "timestamp": "2026-05-25T10:15:30Z"
    }
  ]
}
```

---

## RabbitMQ Integration Events

### 1. `CampaignStartedEvent`
- **Routing Key**: `campaign.started`
- **Payload**:
```json
{
  "campaignId": "4a3b7c2d-1111-2222-3333-444455556666",
  "projectId": "11112222-3333-4444-5555-666677778888",
  "timestamp": "2026-05-25T11:00:00Z"
}
```

### 2. `CampaignRecipientDispatchedEvent`
- **Routing Key**: `campaign.recipient.dispatched`
- **Payload**:
```json
{
  "campaignId": "4a3b7c2d-1111-2222-3333-444455556666",
  "customerId": "88889999-aaaa-bbbb-cccc-ddddeeeeffff",
  "recipientId": "77778888-9999-0000-aaaa-bbbbccccdddd",
  "recipientPhone": "201000000000",
  "messageText": "Hello John! Get 30% off today with code HOT30. Shop now!",
  "variant": "B",
  "delaySeconds": 8
}
```

### 3. `EntityIndexedEvent`
- **Routing Key**: `search.index`
- **Payload**:
```json
{
  "entityId": "a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d",
  "entityType": "Message",
  "projectId": "11112222-3333-4444-5555-666677778888",
  "action": "Upsert",
  "contentJson": "{\"id\":\"a1b2...\",\"text\":\"I want to buy the summer...\",\"sender\":\"Customer\"}"
}
```
