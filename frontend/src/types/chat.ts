export type ConversationStatus = 'Open' | 'Pending' | 'Resolved' | 'Closed';
export type Channel = 'WhatsApp' | 'Messenger' | 'FacebookComment';

export interface CustomerSummary {
  id: string;
  name: string;
  phone: string;
  avatarUrl: string | null;
  label?: string;
  facebookPSID?: string;
  facebookName?: string;
}

export interface Conversation {
  id: string;
  projectId: string;
  customer: CustomerSummary;
  status: ConversationStatus;
  channel: Channel;
  lastMessageAt: string;
  unreadCount: number;
  assignedAgentId: string | null;
  assignedAgentName: string | null;
  isAiTyping?: boolean;
  aiTypingCountdown?: number;
  aiTypingStage?: 'generating' | 'typing';
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
  assetId?: string | null;
  transcription?: string | null;
  facebookPostId?: string | null;
  facebookCommentId?: string | null;
  parentCommentId?: string | null;
}

export interface AISuggestion {
  conversationId: string;
  suggestionText: string;
  confidenceScore: number;
  reasoning: string;
}

export interface ConnectedPage {
  id: string;
  facebookPageId: string;
  pageName: string;
  isActive: boolean;
  tokenExpiresAt: string | null;
  createdAt: string;
}

