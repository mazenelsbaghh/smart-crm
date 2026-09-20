import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '@/context/auth-context';
import { api } from '@/services/api';
import DailyReplyReviewPage from '../DailyReplyReviewPage';
import { messagesFixture, processingFixture, reviewFixture } from './fixtures';

const project = { id: 'project-1', name: 'مشروع اختبار', settings: { aiAutoReplyEnabled: true } };
function mockNetwork(review: typeof reviewFixture = reviewFixture) {
  return vi.spyOn(api, 'get').mockImplementation(async url => {
    if (url === '/api/projects') return { data: [project] };
    if (url === `/api/projects/${project.id}`) return { data: project };
    if (String(url).endsWith('/messages')) return { data: messagesFixture };
    if (String(url).endsWith('/daily-review')) return { data: review };
    if (String(url).endsWith('/processing')) return { data: processingFixture };
    throw new Error(`Unexpected GET ${url}`);
  });
}
function mount() { return render(<AuthProvider><DailyReplyReviewPage /></AuthProvider>); }
beforeEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
  localStorage.setItem('user', JSON.stringify({ id: 'owner', role: 'Owner', email: 'owner@example.test' }));
});
afterEach(() => vi.restoreAllMocks());

describe('Daily reply review', () => {
  it('shows measured gaps, voice text and unknown historical delivery without claiming success', async () => {
    mockNetwork(); mount();
    fireEvent.click(await screen.findByRole('button', { name: /عميلة اختبار — سهيلة/ }));
    expect(await screen.findByText('بعد ١٧ دقيقة')).toBeInTheDocument();
    expect(screen.getByText('تفريغ الصوت: ممكن حد يرد عليا ويفيدني؟')).toBeInTheDocument();
    expect(screen.getByText('مكتملة بلا دليل إرسال')).toBeInTheDocument();
    expect(screen.getByText('متأخر ٧ دقيقة')).toBeInTheDocument();
    expect(screen.getByText(/العميل ينتظر موظفًا/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'فتح المحادثة ↗' })).toHaveAttribute('href', '/inbox?conversationId=conversation-1');
  });

  it('keeps correction editable and requires an explicit move to the inbox instead of sending', async () => {
    mockNetwork();
    const post = vi.spyOn(api, 'post').mockResolvedValue({ data: { content: 'مسودة اختبار للمراجعة', basedOnMessageId: 'incoming-2', generatedAtUtc: '2026-09-07T15:01:00Z' } });
    mount();
    fireEvent.click(await screen.findByRole('button', { name: /عميلة اختبار — سهيلة/ }));
    fireEvent.click(screen.getByRole('button', { name: 'تجهيز مسودة رد مصحح' }));
    const field = await screen.findByRole('textbox', { name: 'مسودة للمراجعة' });
    fireEvent.change(field, { target: { value: 'رد راجعه الموظف' } });
    expect(field).toHaveValue('رد راجعه الموظف');
    expect(screen.queryByRole('button', { name: /^إرسال/ })).not.toBeInTheDocument();
    expect(post.mock.calls.map(call => call[0])).toEqual(['/api/projects/project-1/reports/daily-review/conversations/conversation-1/draft-reply']);
  });

  it('never replaces a newly chosen day with a slow response from the previous day', async () => {
    const get = mockNetwork();
    let resolvePrevious!: (value: { data: typeof reviewFixture }) => void;
    mount(); await screen.findByRole('button', { name: /عميلة اختبار — سهيلة/ });
    get.mockImplementation(async (url, config) => {
      if (String(url).endsWith('/processing')) return { data: processingFixture };
      if (String(url).endsWith('/daily-review')) {
        if ((config?.params as { date?: string })?.date === '2026-09-06') return new Promise(resolve => { resolvePrevious = resolve; });
        return { data: { ...reviewFixture, date: '2026-09-05', conversations: [], filteredCount: 0 } };
      }
      throw new Error('Unexpected request');
    });
    fireEvent.change(screen.getByLabelText('يوم المراجعة'), { target: { value: '2026-09-06' } });
    fireEvent.change(screen.getByLabelText('يوم المراجعة'), { target: { value: '2026-09-05' } });
    await screen.findByText('لا توجد محادثات في الفلتر الحالي');
    await act(async () => resolvePrevious({ data: reviewFixture }));
    expect(screen.queryByRole('button', { name: /عميلة اختبار — سهيلة/ })).not.toBeInTheDocument();
    expect(screen.getByLabelText('يوم المراجعة')).toHaveValue('2026-09-05');
  });

  it('loads older messages using both time and id and preserves the current transcript', async () => {
    const get = mockNetwork();
    get.mockImplementation(async (url, config) => {
      if (url === '/api/projects') return { data: [project] };
      if (url === `/api/projects/${project.id}`) return { data: project };
      if (String(url).endsWith('/daily-review')) return { data: reviewFixture };
      if (String(url).endsWith('/processing')) return { data: processingFixture };
      if (String(url).endsWith('/messages')) return { data: (config?.params as { beforeId?: string })?.beforeId
        ? [{ ...messagesFixture[0], id: 'oldest', content: 'أول سؤال من العميل', createdAt: '2026-09-06T12:00:00Z' }]
        : Array.from({ length: 100 }, (_, index) => ({ ...messagesFixture[0], id: `message-${index}` })) };
      throw new Error('Unexpected request');
    });
    mount(); fireEvent.click(await screen.findByRole('button', { name: /عميلة اختبار — سهيلة/ }));
    fireEvent.click(await screen.findByRole('button', { name: 'تحميل رسائل أقدم' }));
    expect(await screen.findByText('أول سؤال من العميل')).toBeInTheDocument();
    expect(within(screen.getByLabelText('رسائل المحادثة')).getAllByRole('article')).toHaveLength(101);
    expect(get.mock.calls.at(-1)?.[1]?.params).toEqual({ limit: 100, beforeId: 'message-0', before: '2026-09-07T11:40:00Z' });
  });

  it('shows an error on network failure without presenting an empty day as a valid result', async () => {
    const get = mockNetwork();
    mount(); await screen.findByRole('button', { name: /عميلة اختبار — سهيلة/ });
    get.mockRejectedValue(new Error('offline'));
    fireEvent.click(screen.getByRole('button', { name: 'تحديث المراجعة' }));
    expect(await screen.findByText('تعذر تحديث المراجعة. أعد المحاولة؛ البيانات المعروضة قد تكون أقدم.')).toHaveAttribute('role', 'alert');
    expect(screen.getByRole('button', { name: /عميلة اختبار — سهيلة/ })).toBeInTheDocument();
  });
});
