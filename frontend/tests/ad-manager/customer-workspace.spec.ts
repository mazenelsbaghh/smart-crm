import { expect, mockCustomerPage, test } from './fixtures';

const projectId = '11111111-1111-1111-1111-111111111111';
const customer = {
  id: '33333333-3333-3333-3333-333333333333', projectId, name: 'أحمد محمود',
  phoneNumber: '201000000000', city: 'القاهرة', leadScore: 80, tags: [], notes: '', interests: [],
  budget: null, pipelineStage: 'مهتم', label: 'الدورة المسائية',
};

for (const width of [375, 1440]) {
  test(`customers appear before optional tools and retain useful actions at ${width}px`, async ({ page }) => {
    const bookedCustomer = { ...customer, id: '44444444-4444-4444-4444-444444444444', name: 'سارة محمد', pipelineStage: 'حجز' };
    const otherStages = ['عميل جديد', 'تواصل أولي', 'متابعة'].map((pipelineStage, index) => ({
      ...customer, id: `other-${index}`, name: `عميل ${index + 1}`, pipelineStage,
    }));
    await mockCustomerPage(page, [customer, ...otherStages, bookedCustomer]);
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/crm');
    const row = page.getByRole('row').filter({ hasText: customer.name });
    await expect(row).toBeVisible();
    expect((await row.boundingBox())!.y).toBeLessThan(330);
    await expect(row.getByRole('link', { name: `فتح محادثة واتساب مع ${customer.name}` }))
      .toHaveAttribute('href', `/inbox?customerId=${customer.id}&startConversation=1`);
    await expect(row.getByRole('button', { name: `إضافة متابعة للعميل ${customer.name}` })).toBeVisible();
    await expect(page.getByRole('button', { name: 'استيراد قائمة الحظر', exact: true })).not.toBeVisible();
    if (width < 768) {
      const stages = page.getByRole('combobox', { name: 'مراحل العملاء', exact: true });
      await stages.selectOption('حجز');
      await expect(page.getByRole('row').filter({ hasText: bookedCustomer.name })).toBeVisible();
      await expect(row).toHaveCount(0);
      await stages.selectOption('مهتم');
    } else {
      const bookedFilter = page.getByRole('group', { name: 'مراحل العملاء' }).getByRole('button', { name: /حجز/ });
      await expect(bookedFilter).toBeInViewport({ ratio: 1 });
      await bookedFilter.click();
      await expect(page.getByRole('row').filter({ hasText: bookedCustomer.name })).toBeVisible();
      await expect(row).toHaveCount(0);
      await page.getByRole('group', { name: 'مراحل العملاء' }).getByRole('button', { name: /مهتم/ }).click();
    }
    await expect(row).toBeVisible();
    await expect(page.getByRole('row').filter({ hasText: bookedCustomer.name })).toHaveCount(0);
    await page.getByRole('button', { name: /^تصفية/ }).click();
    await expect(page.getByLabel('تصفية العملاء حسب مرحلة البيع')).toHaveValue('مهتم');
    await page.getByLabel('تصفية العملاء حسب المدينة').selectOption('القاهرة');
    await page.getByRole('button', { name: /^تصفية/ }).click();
    await expect(row).toBeVisible();
    await page.getByRole('searchbox', { name: 'بحث في العملاء بالاسم أو رقم الهاتف' }).fill('اسم غير موجود');
    await expect(page.getByText('لا توجد نتائج مطابقة', { exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'مسح التصفية', exact: true }).click();
    await expect(row).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

test('customer follow-up opens the scheduling task and keeps edits when switching sections', async ({ page }) => {
  await mockCustomerPage(page, [customer]);
  await page.route(`**/api/customers/${customer.id}`, route => route.fulfill({ json: customer }));
  await page.route('**/schedule-availability-preference', route => route.fulfill({ json: { availabilityHorizon: 'AnyTime' } }));
  await page.goto('/crm');
  await page.getByRole('button', { name: `إضافة متابعة للعميل ${customer.name}` }).click();
  await expect(page.getByRole('tab', { name: 'المتابعات', exact: true })).toHaveAttribute('aria-selected', 'true');
  const note = page.getByRole('textbox', { name: 'نص الرسالة أو الملاحظات', exact: true });
  await note.fill('مراجعة موعد الدورة المسائية');
  await page.getByRole('tab', { name: 'بيانات العميل', exact: true }).click();
  const name = page.getByRole('textbox', { name: 'الاسم الكامل', exact: true });
  await name.fill('أحمد محمود علي');
  await page.getByRole('tab', { name: 'المتابعات', exact: true }).click();
  await expect(note).toHaveValue('مراجعة موعد الدورة المسائية');
  await page.getByRole('tab', { name: 'بيانات العميل', exact: true }).click();
  await expect(name).toHaveValue('أحمد محمود علي');
});
