export type ConversationStatus = 'Open' | 'Pending' | 'Resolved' | 'Closed';

export interface CustomerSummary {
  id: string;
  name: string;
  phone: string;
  avatarUrl: string | null;
}

export interface Conversation {
  id: string;
  projectId: string;
  customer: CustomerSummary;
  status: ConversationStatus;
  lastMessageAt: string;
  unreadCount: number;
  assignedAgentId: string | null;
  assignedAgentName: string | null;
}

export interface Message {
  id: string;
  conversationId: string;
  senderType: 'Customer' | 'Agent' | 'System' | 'AI';
  content: string;
  createdAt: string;
  status: 'Sent' | 'Delivered' | 'Read';
  mediaUrl: string | null;
  mediaType: 'Image' | 'Voice' | 'Document' | null;
}

export interface AISuggestion {
  conversationId: string;
  suggestionText: string;
  confidenceScore: number;
  reasoning: string;
}
