import { render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../../context/auth-context';
import { ToastProvider } from '../../../context/toast-context';
import { api } from '../../../services/api';
import type { Conversation } from '../../../types/chat';
import Inbox from '../Inbox';

vi.mock('next/navigation', () => ({
  usePathname: () => '/inbox',
  useRouter: () => ({ push: vi.fn() }),
  useSearchParams: () => new URLSearchParams(window.location.search),
}));

const project = { id: '11111111-1111-1111-1111-111111111111', name: 'المشروع', settings: { aiAutoReplyEnabled: true } };
const recentConversation: Conversation = {
  id: '22222222-2222-2222-2222-222222222222', projectId: project.id, status: 'Open', channel: 'WhatsApp',
  lastMessageAt: '2026-08-31T08:00:00Z', unreadCount: 0, assignedAgentId: null, assignedAgentName: null,
  customer: { id: '33333333-3333-3333-3333-333333333333', name: 'عميل حديث', phone: '201000000001', avatarUrl: null },
};
const requestedConversation: Conversation = {
  id: '44444444-4444-4444-4444-444444444444', projectId: project.id, status: 'Pending', channel: 'WhatsApp',
  lastMessageAt: '2026-08-20T08:00:00Z', unreadCount: 0, assignedAgentId: null, assignedAgentName: null,
  customer: { id: '55555555-5555-5555-5555-555555555555', name: 'العميل المطلوب', phone: '201000000002', avatarUrl: null },
};
const secondRequestedConversation: Conversation = {
  id: '66666666-6666-6666-6666-666666666666', projectId: project.id, status: 'Open', channel: 'WhatsApp',
  lastMessageAt: '2026-08-19T08:00:00Z', unreadCount: 0, assignedAgentId: null, assignedAgentName: null,
  customer: { id: '77777777-7777-7777-7777-777777777777', name: 'العميل الثاني', phone: '201000000003', avatarUrl: null },
};
const requestedConversations = [requestedConversation, secondRequestedConversation];

const renderInbox = () => render(
  <AuthProvider><ToastProvider><Inbox /></ToastProvider></AuthProvider>,
);

const installApiMock = () => vi.spyOn(api, 'get').mockImplementation(async (url, config) => {
  const path = String(url);
  if (path === '/api/projects') return { data: [{ id: project.id }] } as never;
  if (path === `/api/projects/${project.id}`) return { data: project } as never;
  if (path === `/api/projects/${project.id}/conversations`) {
    const params = (config as { params?: Record<string, unknown> } | undefined)?.params;
    const requested = requestedConversations.find((conversation) => (
      (!params?.conversationId || params.conversationId === conversation.id)
      && (!params?.customerId || params.customerId === conversation.customer.id)
    ));
    if (params?.conversationId || params?.customerId) return { data: requested ? [requested] : [] } as never;
    return { data: [recentConversation] } as never;
  }

  const messageConversation = requestedConversations.find((conversation) => (
    path === `/api/conversations/${conversation.id}/messages`
  ));
  if (messageConversation) return { data: [{
    id: `message-${messageConversation.id}`,
    conversationId: messageConversation.id,
    senderType: 'Customer',
    content: `رسالة ${messageConversation.customer.name}`,
    createdAt: messageConversation.lastMessageAt,
    status: 'Read',
    mediaUrl: null,
    mediaType: null,
  }] } as never;

  const customerConversation = requestedConversations.find((conversation) => (
    path === `/api/customers/${conversation.customer.id}`
  ));
  if (customerConversation) return { data: {
    id: customerConversation.customer.id,
    projectId: project.id,
    name: customerConversation.customer.name,
    phoneNumber: customerConversation.customer.phone,
    city: '',
    leadScore: 90,
    tags: [],
    notes: '',
    budget: null,
    interests: [],
  } } as never;

  if (path === `/api/projects/${project.id}/follow-ups`) return { data: [] } as never;
  if (requestedConversations.some((conversation) => path === `/api/customers/${conversation.customer.id}/tasks`)) {
    return { data: [] } as never;
  }
  throw new Error(`Unexpected GET: ${path}`);
});

describe('Inbox deep links', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    localStorage.setItem('user', JSON.stringify({ id: 'owner-1', email: 'owner@example.test', role: 'Owner' }));
    window.history.replaceState({}, '', '/inbox');
    Element.prototype.scrollIntoView = vi.fn();
  });

  it.each([
    ['conversationId', requestedConversation.id],
    ['customerId', requestedConversation.customer.id],
  ] as const)('يجلب ويفتح المحادثة المطلوبة عبر %s حتى لو لم تكن ضمن أحدث صفحة', async (parameter, value) => {
    window.history.replaceState({}, '', `/inbox?${parameter}=${value}`);
    installApiMock();

    renderInbox();

    await waitFor(() => expect(
      screen.getByRole('button', { name: /العميل المطلوب، قيد المتابعة/ }),
    ).toHaveAttribute('aria-pressed', 'true'));
    expect(await screen.findByText('رسالة العميل المطلوب')).toBeInTheDocument();
    expect(screen.queryByText(/تعذر تحميل رسائل المحادثة/)).not.toBeInTheDocument();
  });

  it('يفتح الهدف الجديد عند تغير رابط المحادثة مع بقاء الصفحة مفتوحة', async () => {
    window.history.replaceState({}, '', `/inbox?conversationId=${requestedConversation.id}`);
    installApiMock();
    const view = renderInbox();

    await waitFor(() => expect(
      screen.getByRole('button', { name: /العميل المطلوب، قيد المتابعة/ }),
    ).toHaveAttribute('aria-pressed', 'true'));

    window.history.replaceState({}, '', `/inbox?conversationId=${secondRequestedConversation.id}`);
    view.rerender(<AuthProvider><ToastProvider><Inbox /></ToastProvider></AuthProvider>);

    await waitFor(() => expect(
      screen.getByRole('button', { name: /العميل الثاني، مفتوحة/ }),
    ).toHaveAttribute('aria-pressed', 'true'));
    expect(await screen.findByText('رسالة العميل الثاني')).toBeInTheDocument();
  });

  it('يغلق المحادثة القديمة إذا تغيّر الرابط إلى هدف غير موجود', async () => {
    window.history.replaceState({}, '', `/inbox?conversationId=${requestedConversation.id}`);
    installApiMock();
    const view = renderInbox();

    expect(await screen.findByText('رسالة العميل المطلوب')).toBeInTheDocument();

    window.history.replaceState({}, '', '/inbox?conversationId=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa');
    view.rerender(<AuthProvider><ToastProvider><Inbox /></ToastProvider></AuthProvider>);

    expect(await screen.findByText('المحادثة المطلوبة غير موجودة في واتساب أو لم تعد متاحة.')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText('رسالة العميل المطلوب')).not.toBeInTheDocument());
    expect(screen.queryByRole('button', { name: /العميل المطلوب، قيد المتابعة/ })).not.toBeInTheDocument();
  });

  it('يعامل معرّف الرابط غير الصالح كهدف غير موجود ويحافظ على القائمة الأساسية', async () => {
    window.history.replaceState({}, '', '/inbox?conversationId=not-a-guid');
    const get = installApiMock();

    renderInbox();

    expect(await screen.findByRole('button', { name: /عميل حديث، مفتوحة/ })).toBeInTheDocument();
    expect(await screen.findByText('المحادثة المطلوبة غير موجودة في واتساب أو لم تعد متاحة.')).toBeInTheDocument();
    expect(screen.queryByText('تعذر تحميل المحادثات. تحقق من الاتصال ثم أعد المحاولة.')).not.toBeInTheDocument();
    const conversationRequests = get.mock.calls.filter(([url]) => (
      String(url) === `/api/projects/${project.id}/conversations`
    ));
    expect(conversationRequests).toHaveLength(1);
  });
});
