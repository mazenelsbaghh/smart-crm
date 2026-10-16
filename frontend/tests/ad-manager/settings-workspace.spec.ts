import type { Page } from '@playwright/test';
import { expect, test } from './fixtures';

const projectId = '11111111-1111-1111-1111-111111111111';

async function selectSettingsSection(page: Page, width: number, key: string, label: string) {
  if (width <= 900) {
    await page.getByRole('combobox', { name: 'قسم الإعدادات', exact: true }).selectOption(key);
  } else {
    await page.getByRole('button', { name: label, exact: true }).click();
  }
}

for (const width of [375, 1440]) {
  test(`channel drafts survive disclosure, activation and section changes before save at ${width}px`, async ({ page }) => {
    let savedSettings: Record<string, unknown> = {
      aiAutoReplyEnabled: true, replyDelay: 4, maxDailyMessages: 600,
      messengerAiAutoReplyEnabled: false, messengerReplyDelay: 9,
      commentsAiAutoReplyEnabled: false, commentsReplyDelay: 7,
      humanTransferEnabled: true, humanTransferPhone: '201000000009',
      timezone: 'Africa/Cairo', geminiModel: 'gemini-3.5-flash',
    };
    let projectName = 'مشروع الاختبار';
    await page.route(`**/api/projects/${projectId}`, route => route.fulfill({
      json: { id: projectId, name: projectName, settings: savedSettings },
    }));
    await page.route(`**/api/projects/${projectId}/settings`, async route => {
      savedSettings = route.request().postDataJSON();
      projectName = String(savedSettings.projectName);
      await route.fulfill({ json: {} });
    });
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/settings');
    const save = page.getByRole('button', { name: 'حفظ الإعدادات', exact: true });
    const messengerDelay = page.getByLabel('تأخير الرد (ماسنجر)', { exact: true });
    const messengerSwitch = page.getByRole('switch', { name: 'تفعيل الرد الآلي على الماسنجر', exact: true });
    const messengerSettings = page.getByRole('button', { name: 'إعدادات ماسنجر', exact: true });
    await expect(save).toBeDisabled();
    await expect(messengerDelay).toBeHidden();
    await messengerSettings.click();
    await messengerDelay.fill('11');
    await messengerSwitch.check();
    await messengerSwitch.uncheck();
    await expect(messengerDelay).toBeHidden();
    await messengerSettings.click();
    await expect(messengerDelay).toHaveValue('11');
    await selectSettingsSection(page, width, 'project', 'بيانات المشروع');
    await page.getByRole('textbox', { name: 'اسم المشروع', exact: true }).fill('مكتب القاهرة');
    await selectSettingsSection(page, width, 'replies', 'تشغيل الرد الآلي');
    await expect(messengerDelay).toHaveValue('11');
    await expect(page.getByText('توجد تغييرات غير محفوظة', { exact: true })).toBeVisible();
    await expect(save).toBeInViewport();
    await save.click();
    await expect(page.getByText('تم حفظ إعدادات الرد الآلي بنجاح.', { exact: true })).toBeVisible();
    expect(savedSettings).toMatchObject({
      projectName: 'مكتب القاهرة', aiAutoReplyEnabled: true, replyDelay: 4, maxDailyMessages: 600,
      messengerAiAutoReplyEnabled: false, messengerReplyDelay: 11,
      commentsAiAutoReplyEnabled: false, commentsReplyDelay: 7,
      humanTransferEnabled: true, humanTransferPhone: '201000000009', timezone: 'Africa/Cairo',
    });
    await page.reload();
    await expect(save).toBeDisabled();
    await expect(messengerDelay).toBeHidden();
    await messengerSettings.click();
    await expect(messengerDelay).toHaveValue('11');
    await selectSettingsSection(page, width, 'project', 'بيانات المشروع');
    await expect(page.getByRole('textbox', { name: 'اسم المشروع', exact: true })).toHaveValue('مكتب القاهرة');
  });

  test(`save reveals an invalid value inside a collapsed inactive channel at ${width}px`, async ({ page }) => {
    let saves = 0;
    await page.route(`**/api/projects/${projectId}/settings`, route => {
      saves += 1;
      return route.fulfill({ json: {} });
    });
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/settings');
    const disclosure = page.getByRole('button', { name: 'إعدادات ماسنجر', exact: true });
    const delay = page.getByLabel('تأخير الرد (ماسنجر)', { exact: true });
    await disclosure.click();
    await delay.fill('61');
    await disclosure.click();
    await expect(delay).toBeHidden();
    await selectSettingsSection(page, width, 'project', 'بيانات المشروع');
    await page.getByRole('button', { name: 'حفظ الإعدادات', exact: true }).click();
    await expect(delay).toBeVisible();
    await expect(delay).toBeFocused();
    await expect(disclosure).toHaveAttribute('aria-expanded', 'true');
    await expect(page.getByRole('alert').filter({ hasText: 'راجع الحقول المعلّمة قبل الحفظ.' })).toBeVisible();
    expect(saves).toBe(0);
  });
}

for (const width of [375, 1440]) {
  test(`addon drafts must be saved before activation or opening groups at ${width}px`, async ({ page }) => {
    const changes: Record<string, unknown>[] = [];
    await page.route(`**/api/projects/${projectId}`, route => route.fulfill({ json: {
      id: projectId, name: 'مشروع الاختبار', settings: { timezone: 'Africa/Cairo', isGroupAppointmentsEnabled: true,
        isWhatsAppGroupAutomationEnabled: false, groupAutomationManagerPhone: '201000000001', humanTransferEnabled: false, humanTransferPhone: '201000000002' },
    } }));
    await page.route(`**/api/projects/${projectId}/settings`, route => {
      changes.push(route.request().postDataJSON());
      return route.fulfill({ json: {} });
    });
    await page.route('**/api/group-appointments/automation-overview', route => route.fulfill({ json: {
      isEnabled: false, groups: [], whatsAppGroupsCreated: 0, totalBookingsInWhatsAppGroups: 0, pendingFollowUps: 0, totalBookings: 0,
    } }));
    await page.route('**/human-transfer-overview', route => route.fulfill({ json: {
      isReady: false, totalRequests: 0, todayRequests: 0, unreadRequests: 0, recentRequests: [],
    } }));
    await page.route('**/schedule-demand?**', route => route.fulfill({ json: {
      totalPeople: 0, distinctSchedules: 0, pendingLegacyExtraction: 0, groups: [], rows: [],
    } }));
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/settings?tab=addons');
    const phone = page.getByRole('textbox', { name: 'رقم هاتف المدير للتنبيهات والمجموعات', exact: true });
    const toggle = page.getByRole('switch', { name: 'تفعيل أتمتة مجموعات واتساب', exact: true });
    await phone.fill('201000000003');
    await toggle.click();
    await expect(toggle).not.toBeChecked();
    await expect(phone).toBeFocused();
    await expect(page.getByRole('alert').filter({ hasText: 'احفظ رقم المدير المعدّل أولًا' })).toBeVisible();
    await page.getByRole('button', { name: 'إدارة مواعيد الكورس العادي', exact: true }).click();
    await expect(page).toHaveURL(/\/settings\?tab=addons$/);
    await expect(phone).toBeFocused();
    expect(changes).toHaveLength(0);
    await page.getByRole('button', { name: 'حفظ رقم المدير', exact: true }).click();
    await expect(page.getByRole('status').filter({ hasText: 'تم تحديث رقم هاتف المدير' })).toBeVisible();
    expect(changes[0]).toMatchObject({ groupAutomationManagerPhone: '201000000003' });
    await toggle.click();
    await expect(toggle).toBeChecked();
    expect(changes[1]).toMatchObject({ isWhatsAppGroupAutomationEnabled: true });
    await page.getByRole('button', { name: 'إدارة مواعيد الكورس العادي', exact: true }).click();
    await expect(page).toHaveURL(/\/settings\?tab=addons&view=manage-groups$/);
    await expect(page.getByRole('button', { name: 'إضافة مجموعة جديدة', exact: true })).toBeVisible();
  });
}
