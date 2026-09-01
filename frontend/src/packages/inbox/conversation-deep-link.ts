import { api } from '../../services/api';
import type { Channel, Conversation } from '../../types/chat';

interface ResolveConversationDeepLinkRequest {
  projectId: string;
  channel: Channel;
  conversationId?: string | null;
  customerId?: string | null;
  conversationPage: Conversation[];
  signal: AbortSignal;
}

interface ConversationDeepLinkResolution {
  conversation: Conversation | undefined;
  conversationPage: Conversation[];
}

const guidPattern = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

const matchesConversation = (
  conversation: Conversation,
  channel: Channel,
  conversationId?: string | null,
  customerId?: string | null,
) => conversation.channel === channel
  && (!conversationId || conversation.id === conversationId)
  && (!customerId || conversation.customer.id === customerId);

export async function resolveConversationDeepLink({
  projectId,
  channel,
  conversationId,
  customerId,
  conversationPage,
  signal,
}: ResolveConversationDeepLinkRequest): Promise<ConversationDeepLinkResolution> {
  if (!conversationId && !customerId) return { conversation: undefined, conversationPage };
  if ((conversationId && !guidPattern.test(conversationId))
    || (customerId && !guidPattern.test(customerId))) {
    return { conversation: undefined, conversationPage };
  }

  let conversation = conversationPage.find((item) => (
    matchesConversation(item, channel, conversationId, customerId)
  ));

  if (!conversation) {
    const response = await api.get<Conversation[]>(`/api/projects/${projectId}/conversations`, {
      params: {
        channel,
        conversationId: conversationId || undefined,
        customerId: customerId || undefined,
        limit: 1,
      },
      signal,
      timeout: 15_000,
    });
    conversation = response.data.find((item) => (
      matchesConversation(item, channel, conversationId, customerId)
    ));
  }

  if (!conversation) return { conversation, conversationPage };

  return {
    conversation,
    conversationPage: [
      conversation,
      ...conversationPage.filter((item) => item.id !== conversation.id),
    ],
  };
}
