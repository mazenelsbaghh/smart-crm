import { test, expect, mockAdManager, mockAuthorizedProject } from './fixtures';

test('keeps a screen-reader loading state while resources are pending', async ({ page }) => {
  await page.unroute('**/api/**');
  await mockAuthorizedProject(page);
  await page.route('**/api/**/ad-manager/**', async route => { await new Promise(resolve => setTimeout(resolve, 1_000)); await route.fulfill({ status: 500, json: {} }); });
  await page.goto('/management/ad-manager');
  await expect(page.getByLabel('جارٍ تحميل مدير الإعلانات')).toBeVisible();
});

test('shows empty and WAIT resources without inventing performance', async ({ page }) => {
  await page.goto('/management/ad-manager?view=strategy');
  await expect(page.getByText(/WAIT — لا إجراء مالي/)).toBeVisible();
  await page.getByRole('tab', { name: 'الحملات' }).click();
  await expect(page.getByRole('heading', { name: 'لا توجد حملة مدارة' })).toBeVisible();
});

test('keeps stale, degraded and continuing-spend warnings visible', async ({ page }) => {
  await page.unroute('**/api/**');
  await mockAdManager(page, { asOfUtc: '2026-01-01T00:00:00Z', continuingSpend: true, readiness: { ready: false, items: [] } });
  await page.goto('/management/ad-manager');
  await expect(page.getByText(/البيانات قديمة/)).toBeVisible();
  const safetyStatus = page.getByRole('complementary', { name: 'حالة وأمان مدير الإعلانات' });
  await expect(safetyStatus.getByRole('alert')).toContainText('قد تظل تصرف حتى تؤكد Meta الإيقاف');
  await expect(page.getByText(/لن يبدأ صرف/)).toBeVisible();
});

test('announces a failed resource load', async ({ page }) => {
  await page.unroute('**/api/**');
  await mockAuthorizedProject(page);
  await page.route('**/api/**/ad-manager/**', route => route.fulfill({ status: 500, json: { code: 'TEST_FAILURE' } }));
  await page.goto('/management/ad-manager');
  await expect(page.locator('main').getByRole('alert')).toContainText('النظرة العامة');
});
