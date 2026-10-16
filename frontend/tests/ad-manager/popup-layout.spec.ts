import type { Locator, Page } from '@playwright/test';
import { expect, test } from './fixtures';

const projectId = '11111111-1111-1111-1111-111111111111';
const campaignPath = `/api/projects/${projectId}/campaigns`;

async function openCampaign(page: Page) {
  await page.route('**/api/projects/*/segments', route => route.fulfill({ json: [
    { id: 'segment-1', projectId, name: 'عملاء مهتمون بالكورس المسائي في الإسكندرية' },
  ] }));
  await page.goto('/management/campaigns');
  await page.getByRole('button', { name: 'إنشاء حملة', exact: true }).first().click();
  return page.getByRole('dialog', { name: 'إنشاء حملة', exact: true });
}

async function fillCampaign(dialog: Locator) {
  await dialog.getByLabel('اسم الحملة', { exact: true }).fill('متابعة الكورس المسائي');
  await dialog.getByLabel('المجموعة المستهدفة', { exact: true }).selectOption('segment-1');
  await dialog.getByLabel('قالب الرسالة أ', { exact: true }).fill('أهلًا {{name}}، هل يناسبك موعد الكورس المسائي؟');
}

async function expectFooterFits(page: Page, dialog: Locator) {
  const viewport = page.viewportSize()!;
  const bounds = await dialog.locator('footer').boundingBox();
  expect(bounds).not.toBeNull();
  expect(bounds!.y).toBeGreaterThanOrEqual(0);
  expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(viewport.height);
  expect(bounds!.x).toBeGreaterThanOrEqual(0);
  expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(viewport.width);
  expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
  return bounds!;
}

for (const viewport of [{ width: 393, height: 600 }, { width: 844, height: 390 }]) {
  test(`campaign footer remains usable through validation, scrolling and save retry at ${viewport.width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize(viewport);
    const submissions: unknown[] = [];
    await page.route(`**${campaignPath}`, route => {
      if (route.request().method() !== 'POST') return route.fulfill({ json: [] });
      submissions.push(route.request().postDataJSON());
      return submissions.length === 1
        ? route.fulfill({ status: 503, json: { error: 'تعذر الحفظ مؤقتًا' } })
        : route.fulfill({ json: { id: 'campaign-1' } });
    });
    const dialog = await openCampaign(page);
    const save = dialog.getByRole('button', { name: 'حفظ كمسودة', exact: true });
    await expect(dialog).toBeVisible();
    const initialFooter = await expectFooterFits(page, dialog);
    await save.click();
    expect(submissions).toHaveLength(0);
    await expect(dialog.getByLabel('اسم الحملة', { exact: true })).toBeFocused();

    await fillCampaign(dialog);
    await dialog.getByLabel('قالب الرسالة ب (اختياري لاختبار A/B)', { exact: true }).fill('نسخة بديلة للرسالة');
    const body = dialog.locator(':scope > div');
    expect(await body.evaluate(element => element.scrollHeight > element.clientHeight)).toBe(true);
    await body.evaluate(element => { element.scrollTop = element.scrollHeight; });
    const scrolledFooter = await expectFooterFits(page, dialog);
    expect(Math.abs(scrolledFooter.y - initialFooter.y)).toBeLessThanOrEqual(1);
    await page.screenshot({ path: testInfo.outputPath('campaign-footer.png') });

    await save.click();
    await expect(dialog.getByRole('alert')).toHaveText('فشل إنشاء الحملة.');
    await page.screenshot({ path: testInfo.outputPath('campaign-save-error.png') });
    await expect(dialog.getByRole('alert')).toBeInViewport();
    await expect(save).toBeEnabled();
    await expect(dialog.getByLabel('اسم الحملة', { exact: true })).toHaveValue('متابعة الكورس المسائي');
    await expectFooterFits(page, dialog);
    await save.click();
    await expect(dialog).not.toBeVisible();
    expect(submissions).toEqual([{
      name: 'متابعة الكورس المسائي', segmentId: 'segment-1',
      messageTemplateA: 'أهلًا {{name}}، هل يناسبك موعد الكورس المسائي؟', messageTemplateB: 'نسخة بديلة للرسالة',
    }, submissions[0]]);
  });
}

test('scheduled campaign confirmation opens above its editor, cancels safely and submits only after confirmation', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 393, height: 700 });
  const mutations: { path: string; payload: unknown }[] = [];
  await page.route('**/api/**', route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === campaignPath && request.method() === 'GET') return route.fulfill({ json: [] });
    if (request.method() === 'POST' && (path === campaignPath || path === '/api/campaigns/campaign-1/schedule')) {
      mutations.push({ path, payload: request.postDataJSON() });
      return route.fulfill({ json: path === campaignPath ? { id: 'campaign-1' } : {} });
    }
    return route.fallback();
  });
  const editor = await openCampaign(page);
  await fillCampaign(editor);
  const scheduledAt = new Date(Date.now() + 86_400_000).toISOString().slice(0, 16);
  await editor.getByLabel('تاريخ ووقت الجدولة بتوقيت جهازك (اتركه فارغًا للحفظ كمسودة)', { exact: true }).fill(scheduledAt);
  const submit = editor.getByRole('button', { name: 'حفظ وجدولة', exact: true });
  await submit.focus();
  await page.keyboard.press('Enter');
  const confirmation = page.getByRole('dialog', { name: 'إنشاء وجدولة الحملة', exact: true });
  const cancel = confirmation.getByRole('button', { name: 'إلغاء', exact: true });
  await expect(cancel).toBeFocused();
  await expect(cancel).toBeInViewport({ ratio: 1 });
  await page.screenshot({ path: testInfo.outputPath('campaign-confirmation.png') });
  expect(mutations).toHaveLength(0);
  await page.keyboard.press('Escape');
  await expect(confirmation).not.toBeVisible();
  await expect(submit).toBeFocused();
  await expect(editor.getByLabel('اسم الحملة', { exact: true })).toHaveValue('متابعة الكورس المسائي');

  await submit.click();
  const confirm = confirmation.getByRole('button', { name: 'إنشاء وجدولة', exact: true });
  await expect(confirm).toBeInViewport({ ratio: 1 });
  await confirm.click();
  await expect(editor).not.toBeVisible();
  expect(mutations).toHaveLength(2);
  expect(mutations[0]).toEqual({ path: campaignPath, payload: {
    name: 'متابعة الكورس المسائي', segmentId: 'segment-1',
    messageTemplateA: 'أهلًا {{name}}، هل يناسبك موعد الكورس المسائي؟', messageTemplateB: '',
  } });
  expect(mutations[1]).toEqual({ path: '/api/campaigns/campaign-1/schedule', payload: new Date(scheduledAt).toISOString() });
});

test('Back navigation releases both modal scroll locks when a nested confirmation and editor unmount together', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.route('**/api/projects/*/segments', route => route.fulfill({ json: [
    { id: 'segment-1', projectId, name: 'عملاء مهتمون' },
  ] }));
  await page.goto('/management/workflows');
  const originalOverflow = await page.evaluate(() => document.body.style.overflow);
  const documentCreatedAt = await page.evaluate(() => performance.timeOrigin);
  const navigation = page.getByRole('navigation', { name: 'التنقل الرئيسي', exact: true });
  await navigation.locator('summary').filter({ hasText: 'النمو والتسويق' }).click();
  await navigation.getByRole('link', { name: 'حملات واتساب', exact: true }).click();
  await expect(page).toHaveURL(/\/management\/campaigns$/);
  await page.getByRole('button', { name: 'إنشاء حملة', exact: true }).first().click();
  const editor = page.getByRole('dialog', { name: 'إنشاء حملة', exact: true });
  await fillCampaign(editor);
  await editor.getByLabel('تاريخ ووقت الجدولة بتوقيت جهازك (اتركه فارغًا للحفظ كمسودة)', { exact: true })
    .fill(new Date(Date.now() + 86_400_000).toISOString().slice(0, 16));
  await editor.getByRole('button', { name: 'حفظ وجدولة', exact: true }).click();
  await expect(page.locator('dialog[open]')).toHaveCount(2);
  expect(await page.evaluate(() => document.body.style.overflow)).toBe('hidden');

  await page.goBack();
  await expect(page).toHaveURL(/\/management\/workflows$/);
  await expect(page.locator('dialog[open]')).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => document.body.style.overflow)).toBe(originalOverflow);
  expect(await page.evaluate(() => performance.timeOrigin)).toBe(documentCreatedAt);
});
