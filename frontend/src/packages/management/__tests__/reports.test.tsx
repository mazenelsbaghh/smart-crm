import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../../context/auth-context';
import { api } from '../../../services/api';
import Reports from '../Reports';

const project = { id: 'project-1', name: 'المشروع', settings: { aiAutoReplyEnabled: true } };
const currentTime = '2026-08-31T12:00:00.000Z';
const reportWindow = {
  fromUtc: '2026-08-24T12:00:00.000Z',
  toUtc: currentTime,
};
const dashboard = {
  projectId: project.id,
  windowStartUtc: '2026-08-22T00:00:00Z', windowEndUtc: '2026-08-29T00:00:00Z', timezone: 'Africa/Cairo', generatedAtUtc: '2026-08-29T10:00:00Z',
  totalConversations: 200, uniqueCustomers: 180, activeConversations: 25, analyzedConversations: 160, analysisCoverage: 80,
  bookingConversionRate: 21, paymentConversionRate: 15, medianFirstResponseMinutes: 3.5,
  funnel: [
    { key: 'new', label: 'شات جديد', count: 200, rateFromPrevious: 100 },
    { key: 'responded', label: 'تم الرد', count: 180, rateFromPrevious: 90 },
    { key: 'qualified', label: 'عميل مؤهل', count: 120, rateFromPrevious: 66.7 },
    { key: 'intent', label: 'نية حجز', count: 75, rateFromPrevious: 62.5 },
    { key: 'booked', label: 'حجز', count: 42, rateFromPrevious: 56 },
  ],
  funnelTransitions: [{
    key: 'intent-booked', fromLabel: 'نية حجز', toLabel: 'حجز', fromCount: 75, toCount: 42,
    dropOffCount: 33, conversionRate: 56, dropOffRate: 44, needsFollowUp: 12,
    reasons: [{ reason: 'ScheduleMismatch', label: 'المواعيد غير مناسبة', count: 22, percentage: 66.7, needsFollowUp: 9 }],
  }],
  daily: [{ date: '2026-08-28', newConversations: 40, responded: 38, qualified: 25, bookingIntent: 18, booked: 9, paid: 6, attended: 4 }],
  reasons: [{ reason: 'ScheduleMismatch', label: 'المواعيد غير مناسبة', count: 22, percentage: 28 }],
  followUpPlan: {
    sendNow: 12,
    schedule: 7,
    scheduled: 3,
    sendNowToken: 'send-plan-token',
    scheduleToken: 'schedule-plan-token',
  },
  opportunities: [{ conversationId: 'conversation-1', customerId: 'customer-1', customerName: 'أحمد', channel: 'WhatsApp', priority: 91, stage: 'BookingIntent', reason: 'MissingFollowUp', reasonLabel: 'لم تتم المتابعة', summary: 'طلب موعدًا بديلًا ولم تصله متابعة.', recommendation: 'اعرض موعدين بديلين.', recommendedAction: 'SendNow', actionToken: 'opportunity-token', scheduledForUtc: null, lastMessageAtUtc: '2026-08-28T12:00:00Z' }],
  analyses: [{ conversationId: 'conversation-1', customerId: 'customer-1', customerName: 'أحمد', channel: 'WhatsApp', stage: 'BookingIntent', outcome: 'Dormant', reason: 'MissingFollowUp', reasonLabel: 'لم تتم المتابعة', summary: 'طلب موعدًا بديلًا.', recommendation: 'اعرض موعدين.', confidence: .91, replyQualityScore: 64, followUpPriority: 91, needsFollowUp: true, missedOpportunity: true, manuallyCorrected: false, evidence: [{ messageId: 'message-1', quote: 'في مواعيد تانية؟' }], conversationStartedAtUtc: '2026-08-28T10:00:00Z', lastMessageAtUtc: '2026-08-28T12:00:00Z', analyzedAtUtc: '2026-08-29T09:00:00Z' }],
  aiDigest: { executiveSummary: 'أكبر تسرب بعد عرض المواعيد.', findings: ['المواعيد السبب الأكبر.'], recommendations: ['اعرض بدائل.'], risks: [], generatedAtUtc: '2026-08-29T09:00:00Z', model: 'gemini-3.5-flash' },
};

const mockGet = (url: string, report = dashboard) => {
  if (url === '/api/projects') return Promise.resolve({ data: [{ id: project.id }] });
  if (url === `/api/projects/${project.id}`) return Promise.resolve({ data: project });
  if (url.endsWith('/sales-intelligence')) return Promise.resolve({ data: report });
  return Promise.reject(new Error(`Unexpected GET: ${url}`));
};

const renderReports = () => render(<AuthProvider><Reports /></AuthProvider>);

describe('Sales intelligence reports', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(currentTime);
    localStorage.clear();
    localStorage.setItem('user', JSON.stringify({ id: 'owner-1', email: 'owner@example.test', role: 'Owner' }));
    vi.restoreAllMocks();
  });

  afterEach(() => vi.useRealTimers());

  it('يعرض المسار وملخص AI وأسباب عدم الحجز والفرص المدعومة', async () => {
    const channelAwareDashboard = {
      ...dashboard,
      opportunities: [
        dashboard.opportunities[0],
        { ...dashboard.opportunities[0], conversationId: 'conversation-2', customerId: 'customer-2', customerName: 'منى', channel: 'Messenger', recommendedAction: 'OpenConversation' },
        { ...dashboard.opportunities[0], conversationId: 'conversation-3', customerId: 'customer-3', customerName: 'سارة', channel: 'FacebookComment', recommendedAction: 'OpenConversation' },
        { ...dashboard.opportunities[0], conversationId: 'conversation-4', customerId: 'customer-4', customerName: 'ليلى', channel: 'Email', recommendedAction: 'Scheduled' },
      ],
    };
    vi.spyOn(api, 'get').mockImplementation((url) => mockGet(String(url), channelAwareDashboard) as never);
    renderReports();

    expect(await screen.findByText('أكبر تسرب بعد عرض المواعيد.')).toBeInTheDocument();
    expect(screen.getByLabelText('المؤشرات الأساسية')).toHaveTextContent('٢٠٠');
    const transitionAnalysis = within(screen.getByRole('region', { name: 'لماذا يتوقف العملاء بين المراحل؟' }));
    expect(transitionAnalysis.getByText('المواعيد غير مناسبة')).toBeInTheDocument();
    expect(transitionAnalysis.getByText('٣٣')).toBeInTheDocument();
    expect(transitionAnalysis.getByText('تسرب ٤٤٪')).toBeInTheDocument();
    expect(transitionAnalysis.getByText('١٢ محتاجين متابعة')).toBeInTheDocument();
    expect(screen.getByText('للمحادثات المتوقفة والمفقودة فقط')).toBeInTheDocument();
    expect(screen.getAllByText('أحمد').length).toBeGreaterThan(0);
    expect(screen.getByRole('link', { name: 'فتح محادثة أحمد' })).toHaveAttribute(
      'href',
      '/inbox?conversationId=conversation-1',
    );
    expect(screen.getByRole('link', { name: 'فتح محادثة منى' })).toHaveAttribute(
      'href',
      '/inbox/messenger?conversationId=conversation-2',
    );
    expect(screen.getByRole('link', { name: 'فتح محادثة سارة' })).toHaveAttribute(
      'href',
      '/inbox/comments?conversationId=conversation-3',
    );
    expect(screen.queryByRole('link', { name: 'فتح محادثة ليلى' })).not.toBeInTheDocument();
    const messengerCard = screen.getByRole('link', { name: 'فتح محادثة منى' }).closest('article');
    const commentCard = screen.getByRole('link', { name: 'فتح محادثة سارة' }).closest('article');
    expect(messengerCard).not.toBeNull();
    expect(commentCard).not.toBeNull();
    expect(within(messengerCard as HTMLElement).queryByRole('button')).not.toBeInTheDocument();
    expect(within(commentCard as HTMLElement).queryByRole('button')).not.toBeInTheDocument();
    expect(within(messengerCard as HTMLElement).getByText('المقترح: افتح المحادثة ورد يدويًا')).toBeInTheDocument();
    expect(screen.getByLabelText('خطة المتابعات لكل الفرص')).toHaveTextContent('يتبعت الآن١٢');
    expect(screen.getByLabelText('خطة المتابعات لكل الفرص')).toHaveTextContent('يتجدول 24 ساعة٧');
    expect(screen.getByText(/النتائج تُتبع حتى 30 يومًا/)).toBeInTheDocument();
  });

  it('يبدأ تحليل كل المحادثات المتبقية في الخلفية', async () => {
    vi.spyOn(api, 'get').mockImplementation((url) => mockGet(String(url)) as never);
    const post = vi.spyOn(api, 'post').mockImplementation(async (url, body) => {
      if (String(url) !== `/api/projects/${project.id}/reports/sales-intelligence/analyze-all`) {
        throw new Error(`Unexpected POST: ${String(url)}`);
      }
      expect(body).toEqual(reportWindow);
      return { data: { pending: 947, jobId: 'job-1' } } as never;
    });
    renderReports();
    await screen.findByText('أكبر تسرب بعد عرض المواعيد.');

    fireEvent.click(screen.getByRole('button', { name: /حلّل الكل/ }));

    expect(await screen.findByText('بدأ تحليل كل الشاتات في الخلفية: ٩٤٧ شات منتظر.')).toBeInTheDocument();
    expect(post).toHaveBeenCalledOnce();
  });

  it('يرسل سؤالًا لمحلل المبيعات ويعرض الإجابة', async () => {
    vi.spyOn(api, 'get').mockImplementation((url) => mockGet(String(url)) as never);
    const post = vi.spyOn(api, 'post').mockImplementation(async (url, body) => {
      if (String(url) !== `/api/projects/${project.id}/reports/sales-intelligence/ask`) {
        throw new Error(`Unexpected POST: ${String(url)}`);
      }
      expect(body).toEqual({ ...reportWindow, question: 'ليه الناس مش بتحجز؟' });
      return { data: { answer: 'المشكلة الأساسية في المواعيد.', conversationIds: ['conversation-1'], generatedAtUtc: '2026-08-29T10:00:00Z', model: 'gemini-3.5-flash', totalConversations: 200, analyzedConversations: 160, detailedAnalysesReviewed: 160, analysisCoverage: 80 } } as never;
    });
    renderReports();
    await screen.findByText('أكبر تسرب بعد عرض المواعيد.');

    fireEvent.change(screen.getByLabelText('سؤال لمحلل المبيعات'), { target: { value: 'ليه الناس مش بتحجز؟' } });
    fireEvent.click(screen.getByRole('button', { name: /اسأل/ }));

    expect(await screen.findByText('المشكلة الأساسية في المواعيد.')).toBeInTheDocument();
    expect(screen.getByLabelText('نطاق تحليل الإجابة')).toHaveTextContent('تحليل محفوظ١٦٠');
    expect(screen.getByLabelText('نطاق تحليل الإجابة')).toHaveTextContent('راجع AI تفاصيلها١٦٠');
    expect(screen.getByLabelText('نطاق تحليل الإجابة')).toHaveTextContent('غير محلل٤٠');
    expect(screen.getByLabelText('نطاق تحليل الإجابة')).toHaveTextContent('التغطية٨٠٪');
    expect(post).toHaveBeenCalledOnce();
  });

  it('يجدول متابعة الفرصة من التقرير', async () => {
    vi.spyOn(api, 'get').mockImplementation((url) => mockGet(String(url)) as never);
    const post = vi.spyOn(api, 'post').mockImplementation(async (url) => {
      if (String(url) !== '/api/projects/project-1/reports/sales-intelligence/follow-ups') {
        throw new Error(`Unexpected POST: ${String(url)}`);
      }
      return { data: { queued: 1 } } as never;
    });
    renderReports();
    await screen.findByText('أكبر تسرب بعد عرض المواعيد.');

    fireEvent.click(screen.getByRole('button', { name: 'جدولة 24 ساعة' }));
    expect(await screen.findByText('تمت جدولة متابعة أحمد بعد 24 ساعة.')).toBeInTheDocument();
    expect(post).toHaveBeenCalledWith('/api/projects/project-1/reports/sales-intelligence/follow-ups', {
      ...reportWindow,
      action: 'Schedule',
      conversationId: 'conversation-1',
      planToken: 'opportunity-token',
    });
  });

  it('يطلب تأكيدًا قبل إرسال متابعة الفرصة فورًا', async () => {
    vi.spyOn(api, 'get').mockImplementation((url) => mockGet(String(url)) as never);
    const post = vi.spyOn(api, 'post').mockImplementation(async (url) => {
      const path = String(url);
      if (path === '/api/projects/project-1/reports/sales-intelligence/follow-ups') return { data: { queued: 1 } } as never;
      throw new Error(`Unexpected POST: ${path}`);
    });
    renderReports();
    await screen.findByText('أكبر تسرب بعد عرض المواعيد.');

    fireEvent.click(screen.getByRole('button', { name: 'إرسال الآن' }));
    expect(screen.getByRole('button', { name: 'تأكيد الإرسال' })).toBeInTheDocument();
    expect(post).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'تأكيد الإرسال' }));
    expect(await screen.findByText('بدأ إرسال متابعة أحمد.')).toBeInTheDocument();
    expect(post).toHaveBeenCalledWith('/api/projects/project-1/reports/sales-intelligence/follow-ups', {
      ...reportWindow,
      action: 'SendNow',
      conversationId: 'conversation-1',
      planToken: 'opportunity-token',
    });
    expect(post).toHaveBeenCalledOnce();
  });

  it('يطلب تأكيدًا قبل تجهيز إرسال كل العملاء ذوي الأولوية العالية', async () => {
    vi.spyOn(api, 'get').mockImplementation((url) => mockGet(String(url)) as never);
    const post = vi.spyOn(api, 'post').mockImplementation(async (url, body) => {
      if (String(url) !== '/api/projects/project-1/reports/sales-intelligence/follow-ups') {
        throw new Error(`Unexpected POST: ${String(url)}`);
      }
      expect(body).toEqual({ ...reportWindow, action: 'SendNow', planToken: 'send-plan-token' });
      return { data: { queued: 12 } } as never;
    });
    renderReports();
    await screen.findByText('أكبر تسرب بعد عرض المواعيد.');

    fireEvent.click(screen.getByRole('button', { name: /يتبعت الآن.*١٢/ }));
    expect(screen.getByRole('button', { name: /تأكيد إرسال ١٢/ })).toBeInTheDocument();
    expect(post).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: /تأكيد إرسال ١٢/ }));

    expect(await screen.findByText('بدأ إرسال المتابعة إلى 12 عميل.')).toBeInTheDocument();
    expect(post).toHaveBeenCalledWith(
      '/api/projects/project-1/reports/sales-intelligence/follow-ups',
      { ...reportWindow, action: 'SendNow', planToken: 'send-plan-token' },
    );
  });

  it('يمنع تداخل إجراء فردي مع خطة المتابعة الجماعية', async () => {
    let resolveIndividual!: (value: { data: { queued: number } }) => void;
    let resolveBulk!: (value: { data: { queued: number } }) => void;
    const twoOpportunities = {
      ...dashboard,
      opportunities: [
        { ...dashboard.opportunities[0], recommendedAction: 'Schedule' },
        { ...dashboard.opportunities[0], conversationId: 'conversation-2', customerId: 'customer-2', customerName: 'منى' },
      ],
    };
    vi.spyOn(api, 'get').mockImplementation((url) => mockGet(String(url), twoOpportunities) as never);
    vi.spyOn(api, 'post').mockImplementation((url, body) => {
      const path = String(url);
      if (path === '/api/projects/project-1/reports/sales-intelligence/follow-ups'
        && (body as { conversationId?: string }).conversationId === 'conversation-1') {
        return new Promise((resolve) => { resolveIndividual = resolve; }) as never;
      }
      if (path === '/api/projects/project-1/reports/sales-intelligence/follow-ups') {
        return new Promise((resolve) => { resolveBulk = resolve; }) as never;
      }
      throw new Error(`Unexpected POST: ${path}`);
    });
    renderReports();
    await screen.findByText('أكبر تسرب بعد عرض المواعيد.');

    const firstCard = screen.getByRole('link', { name: 'فتح محادثة أحمد' }).closest('article') as HTMLElement;
    const secondCard = screen.getByRole('link', { name: 'فتح محادثة منى' }).closest('article') as HTMLElement;
    fireEvent.click(within(firstCard).getByRole('button', { name: 'جدولة 24 ساعة' }));

    expect(screen.getByRole('button', { name: /يتبعت الآن.*١٢/ })).toBeDisabled();
    expect(within(secondCard).getByRole('button', { name: 'جدولة 24 ساعة' })).toBeDisabled();
    await act(async () => resolveIndividual({ data: { queued: 1 } }));
    await screen.findByText('تمت جدولة متابعة أحمد بعد 24 ساعة.');

    fireEvent.click(screen.getByRole('button', { name: /يتبعت الآن.*١٢/ }));
    fireEvent.click(screen.getByRole('button', { name: /تأكيد إرسال ١٢/ }));
    expect(within(secondCard).getByRole('button', { name: 'جدولة 24 ساعة' })).toBeDisabled();
    expect(within(secondCard).getByRole('button', { name: 'إرسال الآن' })).toBeDisabled();
    await act(async () => resolveBulk({ data: { queued: 12 } }));
    await screen.findByText('بدأ إرسال المتابعة إلى 12 عميل.');
  });

  it('يبقي إجراءات المتابعة معطلة إذا فشل تحميل فترة تقرير جديدة', async () => {
    let dashboardRequests = 0;
    vi.spyOn(api, 'get').mockImplementation((url) => {
      const path = String(url);
      if (path.endsWith('/sales-intelligence')) {
        dashboardRequests += 1;
        return dashboardRequests === 1
          ? Promise.resolve({ data: dashboard }) as never
          : Promise.reject(new Error('report unavailable')) as never;
      }
      return mockGet(path) as never;
    });
    const post = vi.spyOn(api, 'post').mockRejectedValue(new Error('Unexpected POST'));
    renderReports();
    await screen.findByText('أكبر تسرب بعد عرض المواعيد.');

    fireEvent.click(screen.getByRole('button', { name: '30 يومًا' }));

    expect(await screen.findByText('تعذر تحميل تحليلات المبيعات. تحقق من اتصال الخادم ثم أعد المحاولة.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /يتبعت الآن.*١٢/ })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: /يتبعت الآن.*١٢/ }));
    expect(post).not.toHaveBeenCalled();
  });

  it('يعرض غياب البيانات بدل وصف المراحل الصفرية كنجاح', async () => {
    const emptyTransitionsDashboard = {
      ...dashboard,
      funnelTransitions: dashboard.funnelTransitions.map((transition) => ({
        ...transition,
        fromCount: 0,
        toCount: 0,
        dropOffCount: 0,
        conversionRate: 0,
        dropOffRate: 0,
        needsFollowUp: 0,
        reasons: [],
      })),
    };
    vi.spyOn(api, 'get').mockImplementation((url) => mockGet(String(url), emptyTransitionsDashboard) as never);
    renderReports();

    expect(await screen.findByText('تفاصيل التسرب بين المراحل غير متاحة بعد.')).toBeInTheDocument();
    expect(screen.queryByText('لا يوجد تسرب في هذه الخطوة.')).not.toBeInTheDocument();
  });

  it('يخفي تصحيح وإعادة تحليل المحادثة عن المستخدم غير الإداري', async () => {
    localStorage.setItem('user', JSON.stringify({ id: 'agent-1', email: 'agent@example.test', role: 'Agent' }));
    vi.spyOn(api, 'get').mockImplementation((url) => mockGet(String(url)) as never);
    renderReports();

    fireEvent.click(await screen.findByRole('button', { name: /أحمد.*WhatsApp/ }));

    expect(screen.queryByRole('button', { name: /إعادة التحليل/ })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('تصحيح السبب')).not.toBeInTheDocument();
  });
});
