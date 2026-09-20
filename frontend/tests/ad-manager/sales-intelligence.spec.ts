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
  daily: [{ date: '2026-08-28', newConversations: 40, responded: 38, qualified: 25, bookingIntent: 18, booked: 9, bookedOnDate: 14, paid: 6, attended: 4 }],
  funnelTransitions: [],
  followUpPlan: { sendNow: 0, schedule: 0, scheduled: 0, sendNowToken: '', scheduleToken: '' },
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
  const dailyTable = page.getByRole('table').filter({ has: page.getByRole('columnheader', { name: 'حجز في اليوم', exact: true }) });
  await expect(dailyTable.getByRole('columnheader')).toHaveText(['يوم الدخول', 'شات', 'مؤهل', 'نية حجز', 'حجز', 'حجز في اليوم', 'دفع', 'حضور', 'إجراء']);
  await expect(dailyTable.getByRole('row').nth(1).getByRole('cell')).toHaveText(['٢٨ أغسطس', '40', '25', '18', '9', '14', '6', '4', 'تفاصيل اليوم']);

  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByRole('button', { name: /حلّل الكل/ })).toBeVisible();
  const horizontalOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth);
  expect(horizontalOverflow).toBe(false);
  await dailyTable.getByRole('columnheader', { name: 'حجز في اليوم', exact: true }).scrollIntoViewIfNeeded();
  await page.screenshot({ path: '/tmp/daily-booking-report-mobile.png', fullPage: true });

  await page.getByRole('link', { name: 'عرض الأيام بالتفصيل' }).click();
  const expandedTable = page.getByRole('table');
  await expect(expandedTable.getByRole('columnheader', { name: 'حجز في اليوم', exact: true })).toBeVisible();
  await expect(expandedTable.getByRole('row').nth(1).getByRole('cell').nth(3)).toContainText('٩');
  await expect(expandedTable.getByRole('row').nth(1).getByRole('cell').nth(4)).toContainText('١٤');
  expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)).toBe(false);
  await page.screenshot({ path: '/tmp/daily-booking-expanded-mobile.png', fullPage: true });
});

test('previews custom spacing and priority distribution before confirming a bounded batch', async ({ page }) => {
  const requests: unknown[] = [];
  const report = { ...dashboard, followUpPlan: { sendNow: 418, schedule: 493, scheduled: 0,
    sendNowToken: 'send-token', scheduleToken: 'schedule-token' } };
  await page.route('**/api/**/reports/sales-intelligence**', route => {
    if (route.request().method() === 'POST') {
      requests.push(route.request().postDataJSON());
      return route.fulfill({ json: { queued: 5 } });
    }
    return route.fulfill({ json: report });
  });
  await page.goto('/management/reports');
  await page.getByRole('button', { name: /يتبعت الآن.*٤١٨/ }).click();
  const editor = page.getByRole('form', { name: 'إعداد خطة الإرسال' });
  await editor.getByLabel('عدد الرسائل').fill('5');
  await editor.getByLabel('الفاصل من (ثانية)').fill('10');
  await editor.getByLabel('الفاصل إلى (ثانية)').fill('9');
  await expect(editor.getByRole('button', { name: /تأكيد إرسال/ })).toBeDisabled();
  await editor.getByLabel('الفاصل إلى (ثانية)').fill('20');
  await expect(editor).toContainText('من ٤٠ ثانية إلى ١ دقيقة و٢٠ ثانية');
  expect(requests).toHaveLength(0);
  await editor.getByRole('button', { name: /تأكيد إرسال ٥/ }).click();
  await expect(page.getByText('بدأ إرسال المتابعة إلى 5 عميل بفاصل 10–20 ثانية بين كل رسالة.')).toBeVisible();
  expect(requests[0]).toMatchObject({ action: 'SendNow', planToken: 'send-token',
    dispatchOptions: { count: 5, minIntervalSeconds: 10, maxIntervalSeconds: 20, scheduleDays: 1 } });

  await page.getByRole('button', { name: /جدولة على أيام.*٤٩٣/ }).click();
  await editor.getByLabel('عدد الرسائل').fill('400');
  await editor.getByLabel('التوزيع على كام يوم؟').fill('3');
  await expect(editor.getByRole('listitem')).toHaveCount(3);
  await expect(editor.getByRole('listitem').nth(0)).toContainText('١٣٤ رسالة');
  await expect(editor.getByRole('listitem').nth(1)).toContainText('١٣٣ رسالة');
  await editor.screenshot({ path: '/tmp/sales-follow-up-plan-desktop.png' });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)).toBe(false);
  await editor.screenshot({ path: '/tmp/sales-follow-up-plan-mobile.png' });

  await editor.getByLabel('عدد الرسائل').fill('5');
  await editor.getByLabel('التوزيع على كام يوم؟').fill('2');
  await editor.getByRole('button', { name: /تأكيد جدولة ٥/ }).click();
  await expect(page.getByText('تمت جدولة 5 عميل على 2 أيام، الأعلى أولوية أولًا، بداية من بكرة.')).toBeVisible();
  expect(requests[1]).toMatchObject({ action: 'Schedule', planToken: 'schedule-token',
    dispatchOptions: { count: 5, minIntervalSeconds: 30, maxIntervalSeconds: 60, scheduleDays: 2 } });
});
