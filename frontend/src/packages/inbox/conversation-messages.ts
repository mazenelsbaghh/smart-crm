import type { Message } from '../../types/chat';

export function mergeConversationMessages(...groups: Message[][]) {
  const messagesById = new Map<string, Message>();
  groups.forEach((group) => group.forEach((message) => messagesById.set(message.id, message)));
  return Array.from(messagesById.values()).sort((left, right) => {
    const timestampDifference = new Date(left.createdAt).getTime() - new Date(right.createdAt).getTime();
    return timestampDifference || left.id.localeCompare(right.id);
  });
}
