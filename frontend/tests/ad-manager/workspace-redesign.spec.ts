import { expect, test } from './fixtures';

for (const width of [375, 1440]) {
  test(`page search supports keyboard navigation and dismissal at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/inbox');
    const trigger = page.getByRole('button', { name: 'البحث في مساحة العمل', exact: true });
    await trigger.click();
    const search = page.getByRole('combobox', { name: 'البحث في صفحات لوحة التحكم' });
    await expect(search).toBeFocused();
    await search.fill('اسم غير موجود');
    await expect(page.getByText('لا توجد صفحة بهذا الاسم.', { exact: false })).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(trigger).toBeFocused();
    await trigger.click();
    await search.fill('الموافقات');
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/management\/approvals$/);
    await expect(page.getByRole('heading', { name: 'الموافقات', exact: true })).toBeVisible();
  });

  test(`settings save reveals and focuses an invalid field in another section at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/settings');
    const sectionPicker = page.getByRole('combobox', { name: 'قسم الإعدادات', exact: true });
    const projectSection = page.getByRole('button', { name: 'بيانات المشروع', exact: true });
    if (width < 768) await sectionPicker.selectOption('project');
    else await projectSection.click();
    const projectName = page.getByRole('textbox', { name: 'اسم المشروع', exact: true });
    await projectName.fill('');
    if (width < 768) await sectionPicker.selectOption('replies');
    else await page.getByRole('button', { name: 'تشغيل الرد الآلي', exact: true }).click();
    await expect(projectName).toBeHidden();
    await page.getByRole('button', { name: 'حفظ الإعدادات', exact: true }).click();
    await expect(projectName).toBeVisible();
    await expect(projectName).toBeFocused();
    if (width < 768) {
      await expect(sectionPicker).toHaveValue('project');
      await expect(sectionPicker).toBeInViewport();
    } else {
      await expect(projectSection).toHaveAttribute('aria-current', 'true');
      await expect(projectSection).toBeInViewport();
    }
  });
}

test('collapsed navigation groups reveal their destinations and follow the active route', async ({ page }) => {
  await page.goto('/inbox');
  const navigation = page.getByRole('navigation', { name: 'التنقل الرئيسي', exact: true });
  const administration = navigation.locator('details').filter({ has: page.locator('summary').filter({ hasText: 'الإدارة' }) });
  await expect(administration).not.toHaveAttribute('open', '');
  await administration.locator('summary').click();
  const approvals = navigation.getByRole('link', { name: 'الموافقات', exact: true });
  await expect(approvals).toBeVisible();
  await approvals.click();
  await expect(page).toHaveURL(/\/management\/approvals$/);
  await expect(approvals).toHaveAttribute('aria-current', 'page');
  await expect(approvals).toBeVisible();
});

const records = [
  { route: '/management/campaigns', endpoint: '**/api/projects/*/campaigns', recordName: 'متابعة المهتمين بالدورة', status: 'مسودة', context: ['المهتمون بالدورة المسائية'], labels: ['الحملة', 'الحالة', 'نتائج الإرسال', 'الإجراءات'], action: 'إطلاق الآن', dialog: 'إطلاق الحملة الآن', response: [{ id: 'campaign-1', name: 'متابعة المهتمين بالدورة', segmentId: 'segment-1', messageTemplateA: 'تفاصيل الدورة', status: 0, sentCount: 0, deliveredCount: 0, readCount: 0, responseCount: 0 }] },
  { route: '/management/knowledge', endpoint: '**/api/projects/*/knowledge', recordName: 'تفاصيل البرامج والمواعيد', status: 'معتمد', context: ['نص مرفوع يدوياً', 'تفاصيل الدورة والمواعيد والأسعار وطريقة التسجيل.'], labels: ['العنوان', 'المصدر', 'الحالة', 'إجراءات الاعتماد'], action: 'سحب لمسودة', dialog: 'سحب المستند إلى مسودة', response: [{ id: 'document-1', projectId: '11111111-1111-1111-1111-111111111111', title: 'تفاصيل البرامج والمواعيد', content: 'تفاصيل الدورة والمواعيد والأسعار وطريقة التسجيل.', status: 'Approved', version: 1 }] },
  { route: '/management/approvals', endpoint: '**/api/projects/*/approvals?**', recordName: 'تحديث محتوى المعرفة', status: 'خطورة منخفض', context: ['فريق العمل', 'الإجراء المقترح', 'title', 'تفاصيل الدورة'], labels: [], action: 'مراجعة اعتماد وتنفيذ الإجراء تحديث محتوى المعرفة', dialog: 'تأكيد الموافقة والتنفيذ', response: [{ id: 'approval-1', projectId: '11111111-1111-1111-1111-111111111111', actionType: 'تحديث محتوى المعرفة', payloadJson: '{"title":"تفاصيل الدورة","apiToken":"secret-must-stay-hidden"}', riskLevel: 'Low', status: 'Pending', requestedBy: 'فريق العمل', createdAt: '2026-10-16T09:20:00Z' }] },
  { route: '/management/follow-ups', endpoint: '**/api/projects/*/follow-ups/page?**', recordName: 'أحمد محمود', status: 'معلقة', context: ['متابعة عميل', 'مراجعة الموعد المناسب'], labels: ['العميل', 'الموعد والحالة', 'رسالة المتابعة', 'الإجراء'], action: 'إرسال', dialog: 'إرسال المتابعة الآن', response: { items: [{ id: 'followup-1', customerId: 'customer-1', customerName: 'أحمد محمود', customerPhoneNumber: '201000000000', dueDate: '2026-10-17T12:00:00Z', status: 'Pending', notes: 'مراجعة الموعد المناسب', type: 'Nurturing' }], filteredCount: 1, totalCount: 1, pendingCount: 1, completedCount: 0, missedCount: 0, bypassedCount: 0 } },
];

for (const scenario of records) {
  test(`populated ${scenario.route} records retain their labels and actions on a narrow phone`, async ({ page }) => {
    await page.route(scenario.endpoint, route => route.fulfill({ json: scenario.response }));
    await page.route('**/api/projects/*/segments', route => route.fulfill({ json: [{ id: 'segment-1', name: 'المهتمون بالدورة المسائية' }] }));
    const writes: string[] = [];
    page.on('request', request => {
      if (request.method() !== 'GET' && /\/api\/(campaigns|knowledge|approvals|follow-ups)\//.test(request.url())) writes.push(request.url());
    });
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto(scenario.route);
    const main = page.getByRole('main');
    const record = main.getByRole(scenario.route === '/management/approvals' ? 'article' : 'row').filter({ hasText: scenario.recordName });
    await expect(record.getByText(scenario.recordName, { exact: true })).toBeVisible();
    await expect(record.getByText(scenario.status, { exact: true })).toBeVisible();
    for (const context of scenario.context) await expect(record.getByText(context, { exact: true })).toBeVisible();
    for (const label of scenario.labels) await expect(record.locator(`[data-label="${label}"]`)).toBeVisible();

    if (scenario.route === '/management/campaigns') {
      for (const metric of ['مرسل', 'مسلّم', 'مقروء', 'متفاعل']) await expect(record.getByText(metric, { exact: true })).toBeVisible();
      await record.getByText('عرض الرسالة', { exact: true }).click();
      await expect(record.getByText('تفاصيل الدورة', { exact: true })).toBeVisible();
    } else if (scenario.route === '/management/knowledge') {
      await expect(record.getByRole('button', { name: 'فتح وتعديل', exact: true })).toBeVisible();
    } else if (scenario.route === '/management/approvals') {
      await expect(record.getByText(/بتوقيت القاهرة/)).toBeVisible();
      await expect(record).not.toContainText('secret-must-stay-hidden');
      await expect(record.getByRole('button', { name: 'رفض الإجراء تحديث محتوى المعرفة', exact: true })).toBeVisible();
    } else {
      await expect(record.getByText(/بتوقيت القاهرة/)).toBeVisible();
      await expect(record.getByRole('button', { name: 'تعديل', exact: true })).toBeVisible();
    }

    const action = record.getByRole('button', { name: scenario.action, exact: true });
    await expect(action).toBeEnabled();
    await action.click();
    const confirmation = page.getByRole('dialog', { name: scenario.dialog, exact: true });
    await expect(confirmation).toBeVisible();
    await expect(confirmation).toContainText(scenario.recordName);
    await confirmation.getByRole('button', { name: 'إلغاء', exact: true }).click();
    await expect(confirmation).toBeHidden();
    await expect(action).toBeFocused();
    expect(writes).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

test('shared settings tabs keep unsaved changes when keyboard navigation is blocked', async ({ page }) => {
  await page.goto('/settings');
  await page.getByRole('button', { name: 'بيانات المشروع', exact: true }).click();
  await page.getByRole('textbox', { name: 'اسم المشروع', exact: true }).fill('تعديل لم يتم حفظه');
  const general = page.getByRole('tab', { name: 'إعدادات المشروع', exact: true });
  await general.focus();
  await page.keyboard.press('ArrowLeft');
  await expect(general).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('alert').filter({ hasText: 'احفظ تغييرات إعدادات المشروع أولًا' })).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'اسم المشروع', exact: true })).toHaveValue('تعديل لم يتم حفظه');
});

test('page search does not expose administration destinations to a regular team member', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('user', JSON.stringify({ id: 'agent', fullName: 'عضو الفريق', email: 'agent@example.test', role: 'Agent' })));
  await page.goto('/inbox');
  await page.getByRole('button', { name: 'البحث في مساحة العمل', exact: true }).click();
  await page.getByRole('combobox', { name: 'البحث في صفحات لوحة التحكم' }).fill('إعدادات المشروع');
  await expect(page.getByRole('listbox', { name: 'نتائج البحث' }).getByRole('option')).toHaveCount(0);
  await expect(page.getByText('لا توجد صفحة بهذا الاسم.', { exact: false })).toBeVisible();
});
