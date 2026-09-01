import { expect, test } from './fixtures';

const projectId = '11111111-1111-1111-1111-111111111111';
const dashboard = {
  projectId,
  windowStartUtc: '2026-08-22T00:00:00Z', windowEndUtc: '2026-08-29T00:00:00Z', timezone: 'Africa/Cairo', generatedAtUtc: '2026-08-29T10:00:00Z',
  totalConversations: 200, uniqueCustomers: 180, activeConversations: 25, analyzedConversations: 160, analysisCoverage: 80,
  bookingConversionRate: 21, paymentConversionRate: 15, medianFirstResponseMinutes: 3.5,
  funnel: [
    { key: 'new', label: 'شات جديد', count: 200, rateFromPrevious: 100 },
    { key: 'responded', label: 'تم الرد', count: 180, rateFromPrevious: 90 },
    { key: 'qualified', label: 'عميل مؤهل', count: 120, rateFromPrevious: 66.7 },
    { key: 'intent', label: 'نية حجز', count: 75, rateFromPrevious: 62.5 },
    { key: 'booked', label: 'حجز', count: 42, rateFromPrevious: 56 },
    { key: 'paid', label: 'دفع', count: 30, rateFromPrevious: 71.4 },
  ],
  daily: [{ date: '2026-08-28', newConversations: 40, responded: 38, qualified: 25, bookingIntent: 18, booked: 9, paid: 6, attended: 4 }],
  reasons: [{ reason: 'ScheduleMismatch', label: 'المواعيد غير مناسبة', count: 22, percentage: 28 }],
  opportunities: [{ conversationId: 'conversation-1', customerId: 'customer-1', customerName: 'أحمد', priority: 91, stage: 'BookingIntent', reason: 'MissingFollowUp', reasonLabel: 'لم تتم المتابعة', summary: 'طلب موعدًا بديلًا ولم تصله متابعة.', recommendation: 'اعرض موعدين بديلين.', lastMessageAtUtc: '2026-08-28T12:00:00Z' }],
  analyses: [],
  aiDigest: { executiveSummary: 'أكبر تسرب بعد عرض المواعيد.', findings: ['المواعيد السبب الأكبر.'], recommendations: ['اعرض بدائل.'], risks: [], generatedAtUtc: '2026-08-29T09:00:00Z', model: 'gemini-3.5-flash' },
};

test('renders the AI sales funnel and remains usable on a mobile viewport', async ({ page }) => {
  await page.route('**/api/**/reports/sales-intelligence**', route => route.fulfill({ json: dashboard }));
  await page.goto('/management/reports');

  await expect(page.getByRole('heading', { name: 'مدير المبيعات بالذكاء الاصطناعي' })).toBeVisible();
  await expect(page.getByText('أكبر تسرب بعد عرض المواعيد.')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'فرص تحتاج متابعة' })).toBeVisible();

  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByRole('button', { name: /حلّل الآن/ })).toBeVisible();
  const horizontalOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth);
  expect(horizontalOverflow).toBe(false);
});
