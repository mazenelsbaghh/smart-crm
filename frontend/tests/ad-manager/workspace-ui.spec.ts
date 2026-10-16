import { expect, test } from './fixtures';

for (const width of [375, 768, 1440]) {
  test(`workspace navigation stays readable and has one active destination at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/inbox/messenger');
    await expect(page.getByRole('heading', { name: 'المحادثات', exact: true })).toBeVisible();
    if (width < 1024) await page.getByRole('button', { name: 'فتح قائمة التنقل', exact: true }).click();
    const navigation = page.getByRole('navigation', { name: 'التنقل الرئيسي', exact: true });
    await navigation.locator('summary').filter({ hasText: 'الإدارة' }).click();
    await expect(navigation.getByRole('link', { name: 'أتمتة العمليات', exact: true })).toBeVisible();
    await expect(navigation.getByRole('link', { name: 'الموافقات', exact: true })).toBeVisible();
    await expect(navigation.locator('[aria-current="page"]')).toHaveCount(1);
    await expect(navigation.locator('[aria-current="page"]')).toHaveAccessibleName('المحادثات');
    if (width < 1024) {
      await page.keyboard.press('Escape');
      await expect(page.getByRole('button', { name: 'فتح قائمة التنقل', exact: true })).toBeFocused();
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect(await page.evaluate(() => getComputedStyle(document.documentElement).colorScheme)).toBe('light');
  });
}

test('theme preference also applies after opening an inbox directly and reloading it', async ({ page }) => {
  await page.goto('/management/ad-manager');
  await page.getByRole('button', { name: 'تفعيل الوضع الداكن', exact: true }).click();
  await page.goto('/inbox');
  await page.reload();
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.documentElement).colorScheme)).toBe('dark');
  await page.getByRole('button', { name: 'تفعيل الوضع الفاتح', exact: true }).click();
  await page.reload();
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.documentElement).colorScheme)).toBe('light');
});

for (const width of [375, 1440]) {
test(`conversation retains message and task drafts when details close at ${width}px`, async ({ page }) => {
  const projectId = '11111111-1111-1111-1111-111111111111';
  const customer = { id: '33333333-3333-3333-3333-333333333333', projectId, name: 'أحمد محمود',
    phoneNumber: '201000000000', city: 'القاهرة', leadScore: 80, tags: [], notes: '', budget: null };
  const conversation = { id: '22222222-2222-2222-2222-222222222222', projectId, channel: 'WhatsApp', status: 'Open',
    lastMessageAt: new Date().toISOString(), unreadCount: 1, assignedAgentId: null, assignedAgentName: null,
    customer: { id: customer.id, name: customer.name, phone: customer.phoneNumber, avatarUrl: null } };
  await page.route('**/api/**', async route => {
    const pathname = new URL(route.request().url()).pathname;
    if (pathname.endsWith('/conversations')) return route.fulfill({ json: [conversation] });
    if (pathname.endsWith(`/customers/${customer.id}`)) return route.fulfill({ json: customer });
    if (pathname.endsWith('/messages')) return route.fulfill({ json: [{ id: 'message-1', conversationId: conversation.id,
      senderType: 'Customer', content: 'هل يوجد موعد متاح؟', createdAt: new Date().toISOString(), status: 'Read' }] });
    return route.fallback();
  });
  await page.setViewportSize({ width, height: 812 });
  await page.goto(`/inbox?conversationId=${conversation.id}`);
  const composer = page.getByRole('textbox', { name: 'اكتب ردًا إلى أحمد محمود', exact: true });
  await expect(composer).toBeInViewport();
  const composerBounds = await composer.boundingBox();
  const messagesBounds = await page.getByRole('log').boundingBox();
  expect(composerBounds!.y + composerBounds!.height).toBeLessThanOrEqual(812);
  expect(composerBounds!.y).toBeGreaterThan(650);
  expect(messagesBounds!.y).toBeLessThan(220);
  await composer.fill('سأراجع المواعيد المتاحة');
  await page.getByRole('button', { name: 'بيانات العميل', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'بيانات العميل', exact: true })).toBeVisible();
  await page.getByRole('textbox', { name: 'مهمة جديدة', exact: true }).fill('اتصال بعد العمل');
  await page.keyboard.press('Escape');
  await expect(composer).toHaveValue('سأراجع المواعيد المتاحة');
  await expect(page.getByRole('button', { name: 'بيانات العميل', exact: true })).toBeFocused();
  await page.getByRole('button', { name: 'بيانات العميل', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'مهمة جديدة', exact: true })).toHaveValue('اتصال بعد العمل');
});
}

test('campaign editor traps keyboard focus and restores its trigger when dismissed', async ({ page }) => {
  await page.goto('/management/campaigns');
  const trigger = page.getByRole('button', { name: 'إنشاء حملة', exact: true }).first();
  await trigger.click();
  const dialog = page.getByRole('dialog', { name: 'إنشاء حملة', exact: true });
  await expect(dialog).toBeVisible();
  const controls = dialog.locator('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled)');
  await controls.last().focus();
  await page.keyboard.press('Tab');
  await expect(controls.first()).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(dialog).not.toBeVisible();
  await expect(trigger).toBeFocused();
});

test('settings reopen the selected section through their URL and groups have a direct destination', async ({ page }) => {
  await page.route('**/api/group-appointments/automation-overview', route => route.fulfill({ json: {
    isEnabled: false, managerPhone: '', totalGroups: 0, activeGroups: 0, inactiveGroups: 0,
    whatsAppGroupsCreated: 0, totalBookings: 0, totalBookingsInWhatsAppGroups: 0, pendingFollowUps: 0, groups: [],
  } }));
  await page.route('**/human-transfer-overview', route => route.fulfill({ json: {
    humanTransferEnabled: false, isReady: false, totalRequests: 0, todayRequests: 0, unreadRequests: 0, recentRequests: [],
  } }));
  await page.route('**/schedule-demand?**', route => route.fulfill({ json: {
    totalPeople: 0, distinctSchedules: 0, pendingLegacyExtraction: 0, groups: [], rows: [], openAppointments: [],
  } }));
  await page.goto('/settings?tab=addons');
  await expect(page.getByRole('tab', { name: 'الإضافات والتشغيل', exact: true })).toHaveAttribute('aria-selected', 'true');
  await page.reload();
  await expect(page.getByRole('tab', { name: 'الإضافات والتشغيل', exact: true })).toHaveAttribute('aria-selected', 'true');
  await page.goto('/management/groups?course=intensive');
  await expect(page.getByLabel('اختيار نوع الكورس', { exact: true })).toHaveText('الكورس المكثف');
  await expect(page.getByRole('button', { name: 'إضافة مجموعة جديدة', exact: true })).toBeVisible();
});

test('channel shortcuts remain available after consolidating sidebar destinations', async ({ page }) => {
  await page.goto('/inbox');
  await expect(page.getByRole('heading', { name: 'المحادثات', exact: true })).toBeVisible();
  await page.keyboard.press('Alt+3');
  await expect(page).toHaveURL(/\/inbox\/messenger$/);
  await expect(page.getByRole('navigation', { name: 'قنوات المحادثات' }).getByRole('link', { name: 'ماسنجر' }))
    .toHaveAttribute('aria-current', 'page');
});
