import { test, expect } from './fixtures';

const cases = [
  { route: '/management/campaigns', search: 'بحث في الحملات', filter: 'حالة الحملات', status: '0', defaultStatus: 'all', query: 'نوفمبر', record: 'حجز نوفمبر', paginated: true },
  { route: '/management/knowledge', search: 'بحث في مصادر المعرفة', filter: 'حالة مصادر المعرفة', status: 'pending', defaultStatus: 'all', query: 'الحجز', record: 'سياسة الحجز', paginated: true },
  { route: '/management/follow-ups', search: 'بحث في المتابعات باسم العميل أو رقم الهاتف', filter: 'تصفية المتابعات حسب الحالة', status: 'Completed', defaultStatus: 'Pending', query: 'عمر', record: 'عمر حسن', paginated: true },
  { route: '/management/workflows', search: 'بحث في قواعد الأتمتة', filter: 'حالة قواعد الأتمتة', status: 'active', defaultStatus: 'all', query: 'التسجيل', record: 'تأكيد التسجيل', paginated: false },
] as const;

const followUps = [
  { id: 'follow-pending', customerId: 'customer-1', customerName: 'أحمد محمود', customerPhoneNumber: '201001234567', dueDate: '2026-10-18T10:00:00Z', status: 'Pending', notes: 'متابعة الحجز', type: 'Nurturing', tone: 'Salesy' },
  { id: 'follow-completed', customerId: 'customer-2', customerName: 'عمر حسن', customerPhoneNumber: '201002345678', dueDate: '2026-10-16T10:00:00Z', status: 'Completed', notes: 'أرسلت تفاصيل الحجز', type: 'Nurturing', tone: 'Salesy' },
];

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 900 });
  await page.route('**/api/projects/*/**', async route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/campaigns')) return route.fulfill({ json: [
      { id: 'campaign-draft', name: 'حجز نوفمبر', segmentId: 'segment-1', messageTemplateA: 'تفاصيل الحجز', status: 0, sentCount: 0, deliveredCount: 0, readCount: 0, responseCount: 0 },
      { id: 'campaign-done', name: 'تذكير اللقاء', segmentId: 'segment-1', messageTemplateA: 'موعد اللقاء', status: 4, sentCount: 20, deliveredCount: 20, readCount: 15, responseCount: 4 },
    ] });
    if (url.pathname.endsWith('/segments')) return route.fulfill({ json: [{ id: 'segment-1', name: 'العملاء المهتمون' }] });
    if (url.pathname.endsWith('/knowledge')) return route.fulfill({ json: [
      { id: 'source-pending', title: 'سياسة الحجز', content: 'تفاصيل الحجز والمواعيد', status: 'PendingApproval', version: 1 },
      { id: 'source-published', title: 'الأسئلة المتكررة', content: 'تفاصيل الدورة', status: 'Published', version: 2, sourceUrl: 'https://example.test/course' },
    ] });
    if (url.pathname.endsWith('/workflows')) return route.fulfill({ json: [
      { id: 'workflow-active', name: 'تأكيد التسجيل', triggerType: 'MessageReceived', filtersJson: '{}', actionsJson: '[{"type":"CreateFollowUp"}]', isActive: true, version: 1 },
      { id: 'workflow-paused', name: 'متابعة العملاء', triggerType: 'CustomerTagAdded', filtersJson: '{}', actionsJson: '[{"type":"CreateFollowUp"}]', isActive: false, version: 1 },
    ] });
    if (url.pathname.endsWith('/follow-ups/page')) {
      const status = url.searchParams.get('status'), search = url.searchParams.get('search') || '';
      const rows = followUps.filter(row => (status === 'All' || row.status === status) && `${row.customerName} ${row.customerPhoneNumber}`.includes(search));
      return route.fulfill({ json: { items: rows, filteredCount: rows.length, totalCount: 2, pendingCount: 1, completedCount: 1, missedCount: 0, bypassedCount: 0 } });
    }
    return route.fallback();
  });
});

for (const scenario of cases) {
  test(`${scenario.route} keeps list context through reload, navigation and history, and resets only owned parameters`, async ({ page }) => {
    await page.goto(`${scenario.route}?source=review&tag=a&tag=b#records`);
    const search = page.getByRole('searchbox', { name: scenario.search, exact: true });
    const filter = page.getByRole('combobox', { name: scenario.filter, exact: true });
    await expect(search).toBeVisible();
    const historyLength = await page.evaluate(() => history.length);
    await search.pressSequentially(scenario.query);
    await expect(search).toHaveValue(scenario.query);
    expect(new URL(page.url()).searchParams.get('q')).toBe(scenario.query);
    expect(await page.evaluate(() => history.length)).toBe(historyLength);
    await filter.selectOption(scenario.status);
    await expect(page.getByText(scenario.record, { exact: true })).toBeVisible();

    await page.reload();
    await expect(search).toHaveValue(scenario.query);
    await expect(filter).toHaveValue(scenario.status);
    await expect(page.getByText(scenario.record, { exact: true })).toBeVisible();
    await page.goto('/dashboard');
    await page.goBack();
    await expect(search).toHaveValue(scenario.query);
    await expect(filter).toHaveValue(scenario.status);

    await filter.selectOption(scenario.defaultStatus);
    await expect(filter).toHaveValue(scenario.defaultStatus);
    await page.goBack();
    await expect(filter).toHaveValue(scenario.status);
    await page.goForward();
    await expect(filter).toHaveValue(scenario.defaultStatus);
    await page.getByRole('button', { name: 'إعادة ضبط التصفية', exact: true }).click();
    await expect(search).toHaveValue('');
    const url = new URL(page.url());
    expect(url.searchParams.has('q')).toBe(false);
    expect(url.searchParams.has('status')).toBe(false);
    expect(url.searchParams.has('page')).toBe(false);
    expect(url.searchParams.get('source')).toBe('review');
    expect(url.searchParams.getAll('tag')).toEqual(['a', 'b']);
    expect(url.hash).toBe('#records');
  });

  test(`${scenario.route} sanitizes unknown filters, invalid pagination and oversized search`, async ({ page }) => {
    const query = new URLSearchParams({ status: 'unsupported', page: '2147483647', pageSize: '999', q: '\0' + 'a'.repeat(210), source: 'review' });
    await page.goto(`${scenario.route}?${query}`);
    await expect(page.getByRole('searchbox', { name: scenario.search, exact: true })).toHaveValue('a'.repeat(200));
    await expect(page.getByRole('combobox', { name: scenario.filter, exact: true })).toHaveValue(scenario.defaultStatus);
    await expect.poll(() => new URL(page.url()).searchParams.get('status')).toBeNull();
    const url = new URL(page.url());
    expect(url.searchParams.get('page')).toBe(scenario.paginated ? null : '2147483647');
    expect(url.searchParams.get('pageSize')).toBe(scenario.paginated ? null : '999');
    expect(url.searchParams.get('q')).toBe('a'.repeat(200));
    expect(url.searchParams.get('source')).toBe('review');
    await page.getByRole('button', { name: 'إعادة ضبط التصفية', exact: true }).click();
    await expect(page.getByRole('searchbox', { name: scenario.search, exact: true })).toHaveValue('');
    if (!scenario.paginated) expect(new URL(page.url()).searchParams.get('pageSize')).toBe('999');
  });
}

test('follow-up history restores a later page after visiting a shorter result set and repairs stale page links', async ({ page }) => {
  const completed = Array.from({ length: 12 }, (_, index) => ({ ...followUps[1], id: `completed-${index}`, customerName: `عميل مكتمل ${index + 1}` }));
  await page.route('**/follow-ups/page?**', async route => {
    const params = new URL(route.request().url()).searchParams;
    const rows = params.get('status') === 'Completed' ? completed : [followUps[0]];
    const pageNumber = Number(params.get('page')), pageSize = Number(params.get('pageSize'));
    return route.fulfill({ json: { items: rows.slice((pageNumber - 1) * pageSize, pageNumber * pageSize), filteredCount: rows.length, totalCount: 13, pendingCount: 1, completedCount: 12, missedCount: 0, bypassedCount: 0 } });
  });
  await page.goto('/management/follow-ups?status=Completed&page=3&pageSize=5');
  await expect(page.getByText('عميل مكتمل 11', { exact: true })).toBeVisible();
  await page.getByRole('combobox', { name: 'تصفية المتابعات حسب الحالة' }).selectOption('Pending');
  await expect(page.getByText('أحمد محمود', { exact: true })).toBeVisible();
  await page.goBack();
  await expect(page.getByText('عميل مكتمل 11', { exact: true })).toBeVisible();
  expect(new URL(page.url()).searchParams.get('page')).toBe('3');
  await page.goto('/management/follow-ups?status=Completed&page=99&pageSize=5');
  await expect(page.getByText('عميل مكتمل 11', { exact: true })).toBeVisible();
  expect(new URL(page.url()).searchParams.get('page')).toBe('3');
});

test('approvals retain status and pagination through reload and history, sanitize invalid links and reset only supported filters', async ({ page }) => {
  const approved = Array.from({ length: 12 }, (_, index) => ({ id: `approved-${index}`, projectId: 'project-1', actionType: `طلب معتمد ${index + 1}`, payloadJson: '{"title":"تفاصيل الدورة"}', riskLevel: 'Low', status: 'Approved', requestedBy: 'فريق العمل' }));
  await page.route('**/api/projects/*/approvals?**', async route => {
    const status = new URL(route.request().url()).searchParams.get('status');
    return route.fulfill({ json: status === 'Approved' ? approved : [{ ...approved[0], id: 'pending-1', status: 'Pending', actionType: 'طلب معلق' }] });
  });
  await page.goto('/management/approvals?status=Approved&page=3&pageSize=5&source=review&q=external#records');
  const filter = page.getByRole('combobox', { name: 'تصفية طلبات الاعتماد حسب الحالة' });
  const pageSize = page.getByRole('combobox', { name: 'عدد طلبات الاعتماد في الصفحة' });
  const record = page.getByRole('heading', { name: 'طلب معتمد 11', exact: true });
  await expect(record).toBeVisible();
  await page.reload();
  await expect(record).toBeVisible();
  await expect(filter).toHaveValue('Approved');
  await expect(pageSize).toHaveValue('5');
  await page.goto('/dashboard');
  await page.goBack();
  await expect(record).toBeVisible();
  await filter.selectOption('Pending');
  await expect(page.getByRole('heading', { name: 'طلب معلق', exact: true })).toBeVisible();
  await page.goBack();
  await expect(record).toBeVisible();
  expect(new URL(page.url()).searchParams.get('page')).toBe('3');
  await page.goForward();
  await expect(filter).toHaveValue('Pending');
  await page.getByRole('button', { name: 'إعادة ضبط التصفية', exact: true }).click();
  await expect(pageSize).toHaveValue('10');
  const resetUrl = new URL(page.url());
  for (const key of ['status', 'page', 'pageSize']) expect(resetUrl.searchParams.has(key)).toBe(false);
  expect(resetUrl.searchParams.get('source')).toBe('review');
  expect(resetUrl.searchParams.get('q')).toBe('external');
  expect(resetUrl.hash).toBe('#records');

  await page.goto('/management/approvals?status=unsupported&page=-2&pageSize=999');
  await expect(filter).toHaveValue('Pending');
  await expect(pageSize).toHaveValue('10');
  await expect.poll(() => new URL(page.url()).search).toBe('');
  await page.goto('/management/approvals?status=Approved&page=99&pageSize=5');
  await expect(record).toBeVisible();
  expect(new URL(page.url()).searchParams.get('page')).toBe('3');
});
