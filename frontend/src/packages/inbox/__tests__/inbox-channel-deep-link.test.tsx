import { render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../../context/auth-context';
import { ToastProvider } from '../../../context/toast-context';
import { api } from '../../../services/api';
import type { Channel, Conversation } from '../../../types/chat';
import CommentsInbox from '../CommentsInbox';
import MessengerInbox from '../MessengerInbox';

vi.mock('next/navigation', () => ({
  usePathname: () => window.location.pathname,
  useRouter: () => ({ push: vi.fn() }),
  useSearchParams: () => new URLSearchParams(window.location.search),
}));

const project = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'المشروع',
  settings: { aiAutoReplyEnabled: true },
};

const channelCases = [
  {
    channel: 'Messenger' as Channel,
    path: '/inbox/messenger',
    customerName: 'عميل ماسنجر',
    Component: MessengerInbox,
  },
  {
    channel: 'FacebookComment' as Channel,
    path: '/inbox/comments',
    customerName: 'عميل التعليقات',
    Component: CommentsInbox,
  },
];

describe('Channel-aware inbox deep links', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    localStorage.setItem('user', JSON.stringify({ id: 'owner-1', email: 'owner@example.test', role: 'Owner' }));
    Element.prototype.scrollIntoView = vi.fn();
  });

  it.each(channelCases)('يفتح محادثة $channel المطلوبة داخل صندوق القناة الصحيح', async ({
    channel,
    path,
    customerName,
    Component,
  }) => {
    const targetConversation: Conversation = {
      id: channel === 'Messenger'
        ? '22222222-2222-2222-2222-222222222222'
        : '33333333-3333-3333-3333-333333333333',
      projectId: project.id,
      channel,
      status: 'Open',
      lastMessageAt: '2026-08-31T08:00:00Z',
      unreadCount: 0,
      assignedAgentId: null,
      assignedAgentName: null,
      customer: {
        id: channel === 'Messenger'
          ? '44444444-4444-4444-4444-444444444444'
          : '55555555-5555-5555-5555-555555555555',
        name: customerName,
        phone: '201000000001',
        avatarUrl: null,
      },
    };
    const recentConversation: Conversation = {
      ...targetConversation,
      id: channel === 'Messenger'
        ? '66666666-6666-6666-6666-666666666666'
        : '77777777-7777-7777-7777-777777777777',
      customer: {
        ...targetConversation.customer,
        id: channel === 'Messenger'
          ? '88888888-8888-8888-8888-888888888888'
          : '99999999-9999-9999-9999-999999999999',
        name: 'عميل حديث',
      },
    };

    window.history.replaceState({}, '', `${path}?conversationId=${targetConversation.id}`);
    vi.spyOn(api, 'get').mockImplementation(async (url, config) => {
      const requestPath = String(url);
      if (requestPath === '/api/projects') return { data: [{ id: project.id }] } as never;
      if (requestPath === `/api/projects/${project.id}`) return { data: project } as never;
      if (requestPath === `/api/projects/${project.id}/conversations`) {
        const params = (config as { params?: Record<string, unknown> } | undefined)?.params;
        expect(params?.channel).toBe(channel);
        return { data: params?.conversationId === targetConversation.id
          ? [targetConversation]
          : [recentConversation] } as never;
      }
      if (requestPath === `/api/conversations/${targetConversation.id}/messages`) return { data: [{
        id: `message-${targetConversation.id}`,
        conversationId: targetConversation.id,
        senderType: 'Customer',
        content: `رسالة ${customerName}`,
        createdAt: targetConversation.lastMessageAt,
        status: 'Read',
        mediaUrl: null,
        mediaType: null,
      }] } as never;
      if (requestPath === `/api/customers/${targetConversation.customer.id}`) return { data: {
        id: targetConversation.customer.id,
        projectId: project.id,
        name: customerName,
        phoneNumber: targetConversation.customer.phone,
        city: '',
        leadScore: 80,
        tags: [],
        notes: '',
        budget: null,
        interests: [],
      } } as never;
      if (requestPath === `/api/projects/${project.id}/follow-ups`) return { data: [] } as never;
      if (requestPath === `/api/customers/${targetConversation.customer.id}/tasks`) return { data: [] } as never;
      throw new Error(`Unexpected GET: ${requestPath}`);
    });

    render(<AuthProvider><ToastProvider><Component /></ToastProvider></AuthProvider>);

    await waitFor(() => expect(
      screen.getByRole('button', { name: new RegExp(`${customerName}، مفتوحة`) }),
    ).toHaveAttribute('aria-pressed', 'true'));
    expect(await screen.findByText(`رسالة ${customerName}`)).toBeInTheDocument();
  });
});
