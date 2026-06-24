# Data Model: Messenger & WhatsApp Follow-Up Integration

No new database tables or columns are required. We leverage existing schema columns:

### 1. Customer (`Customers` Table)
- `PhoneNumber`: Stores the E.164 phone number (e.g. `201012345678`) once successfully captured.
- `FacebookPSID`: Stores the Facebook Page-Scoped ID representing the Messenger lead.
- If `PhoneNumber` is present, WhatsApp is preferred.
- If `PhoneNumber` is empty and `FacebookPSID` is present, Messenger is used.

### 2. Follow-Up (`FollowUps` Table)
- Reuses all existing properties:
  - `CustomerId`: Reference to the customer.
  - `Status`: Pending, Completed, Cancelled, Missed.
  - `Notes`: Re-written by Gemini for tone preference before delivery.
