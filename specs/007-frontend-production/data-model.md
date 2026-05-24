# Data Models & Contracts: Frontend Dashboard, Realtime & Production Hardening

This document defines the client-side data representations, state interfaces, and real-time event signatures.

## 1. Client Session State

Stores credentials, active project metadata, and user permission claims.

```typescript
interface UserSession {
  accessToken: string;
  refreshToken: string;
  expiresAt: number; // Unix timestamp
  user: {
    id: string;
    email: string;
    fullName: string;
    role: string; // Predefined roles (Owner, Admin, Supervisor, Agent)
  };
  activeProject: {
    id: string;
    name: string;
    settings: ProjectSettings;
  } | null;
}

interface ProjectSettings {
  whatsappConnected: boolean;
  whatsappNumber: string | null;
  aiAutoReplyEnabled: boolean;
  leadScoreThreshold: number;
}
```

---

## 2. Conversation & Message Models

The structure of the chat panels in the 3-panel inbox.

```typescript
type ConversationStatus = 'Open' | 'Pending' | 'Resolved' | 'Closed';

interface Conversation {
  id: string;
  projectId: string;
  customer: CustomerSummary;
  status: ConversationStatus;
  lastMessageAt: string; // ISO DateTime
  unreadCount: number;
  assignedAgentId: string | null;
  assignedAgentName: string | null;
}

interface CustomerSummary {
  id: string;
  name: string;
  phone: string;
  avatarUrl: string | null;
}

interface Message {
  id: string;
  conversationId: string;
  senderType: 'Customer' | 'Agent' | 'System' | 'AI';
  content: string;
  createdAt: string; // ISO DateTime
  status: 'Sent' | 'Delivered' | 'Read';
  mediaUrl: string | null;
  mediaType: 'Image' | 'Voice' | 'Document' | null;
}

interface AISuggestion {
  conversationId: string;
  suggestionText: string;
  confidenceScore: number;
  reasoning: string;
}
```

---

## 3. CRM & Pipeline Models

The customer profile detail and Kanban board model.

```typescript
type PipelineStage = 'New' | 'Contacted' | 'Qualified' | 'Proposal' | 'Negotiation' | 'Won' | 'Lost';

interface Customer {
  id: string;
  projectId: string;
  name: string;
  phone: string;
  city: string | null;
  budget: number | null;
  interests: string[];
  tags: string[];
  notes: string;
  leadScore: number;
  pipelineStage: PipelineStage;
  createdAt: string;
}

interface DealOpportunity {
  id: string;
  customerId: string;
  customerName: string;
  value: number;
  stage: PipelineStage;
  updatedAt: string;
}
```

---

## 4. SignalR Event Contracts

Real-time events pushed to the client frontend over SignalR.

### Events Emitted from Backend:

- `ReceiveMessage(message: Message)`: Fired when a new message (inbound or outbound) is saved.
- `ConversationStatusChanged(conversationId: string, status: ConversationStatus)`: Fired when a conversation is updated.
- `AgentAssigned(conversationId: string, agentId: string | null, agentName: string | null)`: Fired when ownership transitions.
- `AgentPresenceUpdated(agentId: string, status: 'Online' | 'Offline')`: Fired for real-time presence indicators.
- `AISuggestionGenerated(suggestion: AISuggestion)`: Fired when Gemini returns a proposed reply.
- `NotificationReceived(title: string, body: string, type: string)`: Fired for alerts (SLA breach, VIP activity).

### Events Emitted from Client:

- `JoinProjectGroup(projectId: string)`: Join WebSocket group for scoping.
- `UpdatePresence(status: 'Online' | 'Offline')`: Broadcast active status to other agents.
