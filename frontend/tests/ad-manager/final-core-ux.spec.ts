import type { Page } from '@playwright/test';
import { expect, test } from './fixtures';

const projectId = '11111111-1111-1111-1111-111111111111';
const customer = {
  id: 'customer-comments', projectId, name: 'أحمد عبد الرحمن محمود السيد من فريق التدريب المهني',
  phoneNumber: '201000000000', city: 'القاهرة الجديدة', leadScore: 70,
  tags: [], notes: '', budget: null, interests: [],
};

for (const width of [320, 844, 1440]) {
  test(`comment public and private replies stay reachable and distinct at ${width}px`, async ({ page }) => {
    let sentReply: unknown;
    await page.route('**/api/projects/*/conversations?**', route => route.fulfill({ json: [{
      id: 'comment-conversation', projectId, customer: { id: customer.id, name: customer.name, phone: customer.phoneNumber },
      status: 'Open', channel: 'FacebookComment', lastMessageAt: new Date().toISOString(), unreadCount: 1,
    }] }));
    await page.route('**/api/customers/customer-comments', route => route.fulfill({ json: customer }));
    await page.route('**/api/conversations/comment-conversation/messages?**', route => route.fulfill({ json: [] }));
    await page.route('**/api/projects/*/conversations/comment-conversation/comment-reply', async route => {
      sentReply = route.request().postDataJSON();
      await route.fulfill({ json: {} });
    });
    await page.setViewportSize({ width, height: width === 844 ? 390 : 900 });
    await page.goto('/inbox/comments');
    await page.getByText(customer.name, { exact: true }).first().click();
    const publicReply = page.getByLabel('الرد العام على التعليق', { exact: true });
    const privateReply = page.getByLabel('الرسالة الخاصة في ماسنجر', { exact: true });
    await publicReply.fill('أهلًا، أرسلنا لك التفاصيل على الخاص.');
    await privateReply.fill('موعدك متاح يوم السبت الساعة السادسة.');
    const detailsTrigger = page.getByRole('button', { name: 'بيانات العميل', exact: true });
    await detailsTrigger.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('dialog', { name: 'بيانات العميل', exact: true })).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog', { name: 'بيانات العميل', exact: true })).toBeHidden();
    await expect(detailsTrigger).toBeFocused();
    await expect(publicReply).toHaveValue('أهلًا، أرسلنا لك التفاصيل على الخاص.');
    await expect(privateReply).toHaveValue('موعدك متاح يوم السبت الساعة السادسة.');
    for (const field of [publicReply, privateReply]) {
      await field.scrollIntoViewIfNeeded();
      const bounds = await field.boundingBox();
      expect(bounds).not.toBeNull();
      expect(bounds!.x).toBeGreaterThanOrEqual(0);
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width);
    }
    await page.getByRole('button', { name: '👍 إعجاب', exact: true }).click();
    await page.getByRole('button', { name: 'إرسال الرد المجمع', exact: true }).click();
    await expect(page.getByText('تم إرسال الرد بنجاح', { exact: true })).toBeVisible();
    expect(sentReply).toEqual({
      publicComment: 'أهلًا، أرسلنا لك التفاصيل على الخاص.',
      privateDM: 'موعدك متاح يوم السبت الساعة السادسة.', reaction: 'LIKE',
    });
    await expect(publicReply).toHaveValue('');
    await expect(privateReply).toHaveValue('');
  });
}

async function mockAddonContext(page: Page) {
  await page.route('**/api/group-appointments/automation-overview', route => route.fulfill({ json: {
    isEnabled: false, groups: [], whatsAppGroupsCreated: 0, totalBookingsInWhatsAppGroups: 0,
    pendingFollowUps: 0, totalBookings: 0,
  } }));
  await page.route('**/human-transfer-overview', route => route.fulfill({ json: {
    isReady: false, totalRequests: 0, todayRequests: 0, unreadRequests: 0, recentRequests: [],
  } }));
}

test('recorded schedule requests can be scrolled with the keyboard', async ({ page }) => {
  await mockAddonContext(page);
  await page.route('**/schedule-demand?**', route => route.fulfill({ json: {
    totalPeople: 12, distinctSchedules: 1, pendingLegacyExtraction: 0,
    groups: [{ label: 'السبت مساءً', peopleCount: 12 }],
    rows: Array.from({ length: 12 }, (_, index) => ({
      customerId: `request-${index}`, customerName: `عميل الموعد ${index + 1}`, phoneNumber: '201000000000',
      requestedScheduleText: 'أحتاج مجموعة يوم السبت مساءً بعد انتهاء العمل.',
      requestedScheduleLabel: 'السبت مساءً', lastMessageAtUtc: '2026-10-16T10:00:00Z',
    })),
  } }));
  await page.setViewportSize({ width: 375, height: 900 });
  await page.goto('/settings?tab=addons');
  const requests = page.getByRole('region', { name: 'طلبات المواعيد المسجلة', exact: true });
  await requests.focus();
  await expect(requests).toBeFocused();
  await page.keyboard.press('End');
  await expect(requests.getByText('عميل الموعد 12', { exact: true })).toBeInViewport();
  await expect.poll(() => requests.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
});

for (const scenario of [
  {
    name: 'groups', route: '/management/groups', endpoint: '**/api/group-appointments?**',
    failure: 'فشل تحميل مجموعات المواعيد.', empty: 'لا توجد مجموعات بعد',
    retry: 'إعادة تحميل المجموعات', response: [],
  },
  {
    name: 'schedule requests', route: '/settings?tab=addons', endpoint: '**/schedule-demand?**',
    failure: 'بيانات المواعيد غير متاحة. استخدم «تحديث الشيت» للمحاولة مجددًا.',
    empty: 'لا توجد مواعيد بديلة صريحة مسجلة حتى الآن.', retry: 'تحديث الشيت',
    response: { totalPeople: 0, distinctSchedules: 0, pendingLegacyExtraction: 0, groups: [], rows: [] },
  },
]) {
  test(`failed ${scenario.name} loading does not claim an empty list and can recover`, async ({ page }) => {
    await mockAddonContext(page);
    let unavailable = true;
    await page.route(scenario.endpoint, route => route.fulfill(unavailable
      ? { status: 503, json: { error: 'Temporarily unavailable' } }
      : { json: scenario.response }));
    await page.setViewportSize({ width: 320, height: 900 });
    await page.goto(scenario.route);
    await expect(page.getByText(scenario.failure, { exact: true })).toBeVisible();
    await expect(page.getByText(scenario.empty, { exact: true })).toBeHidden();
    if (scenario.name === 'schedule requests') {
      await expect(page.getByRole('region', { name: 'شيت المواعيد المطلوبة', exact: true }).getByText('0', { exact: true })).toHaveCount(0);
    }
    unavailable = false;
    await page.getByRole('button', { name: scenario.retry, exact: true }).click();
    await expect(page.getByText(scenario.empty, { exact: true })).toBeVisible();
    await expect(page.getByText(scenario.failure, { exact: true })).toBeHidden();
  });
}

for (const width of [320, 1440]) {
  test(`populated group subscribers keep their status and actions reachable at ${width}px`, async ({ page }) => {
    await page.route('**/api/group-appointments?**', route => route.fulfill({ json: [{
      id: 'group-long-name', name: 'مجموعة المهارات العملية المسائية', dateTime: '2026-10-20T15:00:00Z',
      capacity: 2, bookedCount: 1, isActive: true, days: '2', mode: 'online',
      bookings: [{ id: 'booking-long-name', customerId: customer.id, customerName: customer.name,
        customerPhone: customer.phoneNumber, createdAt: '2026-10-16T10:00:00Z', isPaid: false, isAttended: true }],
    }] }));
    await page.route('**/api/group-appointments/instructors', route => route.fulfill({ json: {
      instructors: [], instructorContacts: [], supervisors: [],
    } }));
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/management/groups');
    await page.getByRole('button', { name: 'المشتركين (1)', exact: true }).click();
    const record = page.getByRole('row').filter({ hasText: customer.name });
    await expect(record.getByText(customer.name, { exact: true })).toBeVisible();
    await expect(record.getByRole('checkbox', { name: `تحديد حضور ${customer.name}`, exact: true })).toBeChecked();
    await expect(record.getByRole('checkbox', { name: `تحديد دفع ${customer.name}`, exact: true })).not.toBeChecked();
    const actions = [
      page.getByRole('button', { name: 'إضافة مشترك يدويًا', exact: true }),
      page.getByRole('textbox', { name: 'البحث في مشتركي المجموعة بالاسم أو رقم الهاتف', exact: true }),
      record.getByRole('link', { name: 'فتح المحادثة', exact: true }),
      record.getByRole('button', { name: 'حذف', exact: true }),
    ];
    for (const action of actions) {
      await action.scrollIntoViewIfNeeded();
      const bounds = await action.boundingBox();
      expect(bounds).not.toBeNull();
      expect(bounds!.x).toBeGreaterThanOrEqual(0);
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width);
    }
  });
}
