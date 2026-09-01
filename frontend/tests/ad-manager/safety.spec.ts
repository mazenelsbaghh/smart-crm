import { test, expect } from './fixtures';

test('Emergency Stop confirms, exposes progress state, and offers guarded recovery', async ({ page }) => {
  await page.goto('/management/ad-manager');
  await page.getByRole('button', { name: /إيقاف طارئ/ }).click();
  await expect(page.getByRole('dialog', { name: 'تنفيذ إيقاف طارئ؟' })).toBeVisible();
  await page.getByRole('button', { name: 'نفّذ الإيقاف الطارئ' }).click();
  await expect(page.getByText('الإعلانات المملوكة متوقفة للحماية')).toBeVisible();
  await expect(page.getByText(/تقدم الإيقاف الطارئ: 3\/3/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'فحص الاستعادة الآمنة' })).toBeVisible();
  await page.getByRole('button', { name: 'فحص الاستعادة الآمنة' }).click();
  await expect(page.getByRole('dialog', { name: 'رفع قفل الطوارئ؟' })).toBeVisible();
  await page.getByRole('button', { name: 'ارفع القفل' }).click();
  const safetyStatus = page.getByRole('complementary', { name: 'حالة وأمان مدير الإعلانات' });
  await expect(safetyStatus.getByRole('status').filter({ hasText: /لن يبدأ صرف|النظام جاهز/ })).toBeVisible();
});
