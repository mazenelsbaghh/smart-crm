import { expect, test } from './fixtures';

const projectId = '11111111-1111-1111-1111-111111111111';
const phone = '01012345678';
const recoveryPath = `/api/projects/${projectId}/follow-ups/recover`;
const dueDate = () => new Date(Date.now() + 3_600_000).toISOString();

test.beforeEach(async ({ page }) => {
  await page.route('**/api/projects/*/follow-ups/page?**', route => route.fulfill({ json: {
    items: [], totalCount: 0, filteredCount: 0, pendingCount: 0, completedCount: 0, missedCount: 0,
    bypassedCount: 0, cancelledCount: 0, processingCount: 0, deliveryUnknownCount: 0,
  } }));
});

for (const viewport of [{ width: 393, height: 700 }, { width: 844, height: 390 }]) {
  test(`phone recovery keeps its footer and entered values through an error at ${viewport.width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize(viewport);
    const submitted: { phoneNumber: string; dueDate: string }[] = [];
    let recovered = false;
    await page.route('**/api/projects/*/follow-ups/**', route => {
      const request = route.request();
      if (new URL(request.url()).pathname === recoveryPath) {
        submitted.push(request.postDataJSON());
        if (submitted.length === 1) return route.fulfill({ status: 503, json: {} });
        recovered = true;
        return route.fulfill({ json: { code: 'CREATED', customerName: 'عميل للمتابعة',
          followUpId: 'recovered', dueDate: submitted[1].dueDate, status: 'Pending' } });
      }
      return route.fulfill({ json: { items: recovered ? [{
        id: 'recovered', customerId: 'customer', customerName: 'عميل للمتابعة', customerPhoneNumber: phone,
        dueDate: submitted[1].dueDate, status: 'Pending', notes: '', type: 'Nurturing',
      }] : [], totalCount: recovered ? 1 : 0, filteredCount: recovered ? 1 : 0,
      pendingCount: recovered ? 1 : 0, completedCount: 0, missedCount: 0, bypassedCount: 0,
      cancelledCount: 0, processingCount: 0, deliveryUnknownCount: 0 } });
    });
    await page.goto('/management/follow-ups?status=Missed&q=old');
    await page.getByRole('button', { name: 'إرجاع للمتابعة', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'إرجاع عميل للمتابعة' });
    const number = dialog.getByLabel('رقم واتساب العميل', { exact: true });
    await number.fill(phone);
    await expect(dialog.getByText(/بتوقيت القاهرة/)).toBeVisible();
    const schedule = await dialog.getByLabel('موعد المتابعة بتوقيت جهازك').inputValue();
    const expectedDate = await page.evaluate(value => new Date(value).toISOString(), schedule);
    const save = dialog.getByRole('button', { name: 'تأكيد الإرجاع للمتابعة' });
    await expect(save).toBeInViewport({ ratio: 1 });
    expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath('recovery-form.png') });
    await save.click();
    await expect(dialog.getByRole('alert')).toContainText('احتفظنا بالرقم والموعد');
    await expect(number).toHaveValue(phone);
    await expect(dialog.getByLabel('موعد المتابعة بتوقيت جهازك')).toHaveValue(schedule);
    await expect(save).toBeInViewport({ ratio: 1 });
    await save.click();
    await expect(dialog).not.toBeVisible();
    await expect(page.getByRole('status').filter({ hasText: 'تم إرجاع عميل للمتابعة' })).toBeVisible();
    await expect(page.getByRole('row', { name: /عميل للمتابعة/ })).toBeVisible();
    await expect(page).toHaveURL(/q=01012345678/);
    expect(submitted).toEqual([{ phoneNumber: phone, dueDate: expectedDate }, { phoneNumber: phone, dueDate: expectedDate }]);
    await page.screenshot({ path: testInfo.outputPath('recovery-success.png') });
  });
}

test('an existing processing follow-up is shown without claiming a second one was created', async ({ page }) => {
  await page.route(`**${recoveryPath}`, route => route.fulfill({ json: {
    code: 'ALREADY_ACTIVE', customerName: 'عميل موجود', followUpId: 'existing', dueDate: dueDate(), status: 'Processing',
  } }));
  await page.goto('/management/follow-ups');
  await page.getByRole('button', { name: 'إرجاع للمتابعة', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'إرجاع عميل للمتابعة' });
  await dialog.getByLabel('رقم واتساب العميل', { exact: true }).fill(phone);
  await dialog.getByRole('button', { name: 'تأكيد الإرجاع للمتابعة' }).click();
  await expect(dialog).not.toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: 'لم نضف متابعة أخرى' })).toContainText('قيد التنفيذ');
  await expect(page).toHaveURL(/status=Processing/);
});

for (const [code, explanation] of [
  ['CUSTOMER_NOT_FOUND', 'الرقم غير موجود'],
  ['CUSTOMER_BLOCKED', 'القائمة السوداء'],
  ['DELIVERY_UNKNOWN', 'إرسال لم تتأكد نتيجته'],
]) {
  test(`recovery explains ${code} and keeps the number for correction`, async ({ page }) => {
    await page.route(`**${recoveryPath}`, route => route.fulfill({ status: 409, json: { code } }));
    await page.goto('/management/follow-ups');
    await page.getByRole('button', { name: 'إرجاع للمتابعة', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'إرجاع عميل للمتابعة' });
    await dialog.getByLabel('رقم واتساب العميل', { exact: true }).fill(phone);
    await dialog.getByRole('button', { name: 'تأكيد الإرجاع للمتابعة' }).click();
    await expect(dialog.getByRole('alert')).toContainText(explanation);
    await expect(dialog.getByLabel('رقم واتساب العميل', { exact: true })).toHaveValue(phone);
    await dialog.getByRole('button', { name: 'إلغاء', exact: true }).click();
    await expect(dialog).not.toBeVisible();
    await expect(page.getByRole('button', { name: 'إرجاع للمتابعة', exact: true })).toBeEnabled();
  });
}
