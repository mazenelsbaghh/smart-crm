# API Contracts: UX/UI Unified Inbox Redesign & GSAP Animations

## 1. Get Customers List
Exposes the extended properties for the conversation worklist and CRM data.

* **URL**: `GET /api/projects/{projectId}/customers`
* **Response Status**: `200 OK`
* **Response JSON Schema**:
```json
[
  {
    "id": "guid",
    "projectId": "guid",
    "phoneNumber": "string",
    "name": "string",
    "city": "string",
    "leadScore": 0,
    "tags": ["string"],
    "notes": "string",
    "budget": 0.0,
    "interests": ["string"],
    "label": "string",
    "isBlacklisted": false,
    "pipelineStage": "string",
    "purchaseProbability": 0,
    "aiInsights": "string | null",
    "automationRules": "string | null"
  }
]
```

---

## 2. Get Customer Detail
Exposes a single customer's extended CRM details.

* **URL**: `GET /api/customers/{id}`
* **Response Status**: `200 OK`
* **Response JSON Schema**:
```json
{
  "id": "guid",
  "projectId": "guid",
  "phoneNumber": "string",
  "name": "string",
  "city": "string",
  "leadScore": 0,
  "tags": ["string"],
  "notes": "string",
  "budget": 0.0,
  "interests": ["string"],
  "label": "string",
  "isBlacklisted": false,
  "pipelineStage": "string",
  "purchaseProbability": 0,
  "aiInsights": "string | null",
  "automationRules": "string | null"
}
```

---

## 3. Update Customer CRM Metadata
Allows updating the extended parameters from the right context details panel.

* **URL**: `PUT /api/customers/{id}`
* **Request JSON Schema**:
```json
{
  "name": "string (optional)",
  "city": "string (optional)",
  "leadScore": 0,
  "tags": ["string"],
  "notes": "string (optional)",
  "label": "string (optional)",
  "isBlacklisted": false,
  "budget": 0.0,
  "pipelineStage": "string (optional)",
  "purchaseProbability": 0,
  "aiInsights": "string (optional)",
  "automationRules": "string (optional)"
}
```
* **Response Status**: `200 OK`
* **Response JSON Schema**: (Same as Get Customer Detail above)
