import { readFile } from 'node:fs/promises';
import * as XLSX from 'xlsx';
import { expect, test } from './fixtures';

test('downloads selected CRM audience as Excel preserving phone numbers and names as text', async ({ page }) => {
  let requestedAudience: string | null = null;
  await page.route('**/customers/export?**', route => {
    requestedAudience = new URL(route.request().url()).searchParams.get('audience');
    return route.fulfill({ json: [{ phoneNumber: '201012345678', name: '=1+1', city: 'القاهرة', label: null }] });
  });
  await page.goto('/crm');
  await expect(page.getByLabel('بيانات التنزيل')).toHaveValue('inactive30');
  await page.setViewportSize({ width: 375, height: 812 });
  await expect(page.getByRole('button', { name: 'تنزيل Excel' })).toBeInViewport();
  const downloading = page.waitForEvent('download');
  await page.getByRole('button', { name: 'تنزيل Excel' }).click();
  const download = await downloading;
  expect(requestedAudience).toBe('inactive30');
  const workbook = XLSX.read(await readFile((await download.path())!), { type: 'buffer' });
  const sheet = workbook.Sheets[workbook.SheetNames[0]];
  expect(sheet.A2).toMatchObject({ t: 's', v: '201012345678' });
  expect(sheet.B2).toMatchObject({ t: 's', v: '=1+1' });
  expect(sheet.B2.f).toBeUndefined();
  await page.screenshot({ path: '/tmp/crm-export-mobile.png', fullPage: true });
  await page.getByLabel('بيانات التنزيل').selectOption('nonSubscribers');
  const secondDownload = page.waitForEvent('download');
  await page.getByRole('button', { name: 'تنزيل Excel' }).click();
  await secondDownload;
  expect(requestedAudience).toBe('nonSubscribers');
});

test('empty export explains that no matching numbers are available', async ({ page }) => {
  await page.route('**/customers/export?**', route => route.fulfill({ json: [] }));
  await page.goto('/crm');
  await page.getByRole('button', { name: 'تنزيل Excel' }).click();
  await expect(page.getByText('لا توجد أرقام مطابقة لشروط التنزيل.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'تنزيل Excel' })).toBeEnabled();
});
