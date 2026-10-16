import { test, expect } from './fixtures';

for (const width of [375, 768, 1024, 1440]) {
  test(`workspace stays usable at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/management/ad-manager');
    await expect(page.getByRole('heading', { name: 'مدير الإعلانات' })).toBeVisible();
    await expect(page.getByRole('button', { name: /إيقاف طارئ/ })).toBeVisible();
    if (width < 768) {
      const section = page.getByRole('combobox', { name: 'أقسام مدير الإعلانات', exact: true });
      await section.selectOption('strategy');
      await expect(section).toHaveValue('strategy');
    } else {
      const tabs = page.getByRole('tab');
      await tabs.first().focus();
      await page.keyboard.press('ArrowLeft');
      await expect(page.getByRole('tab', { name: 'الاستراتيجية' })).toHaveAttribute('aria-selected', 'true');
    }
    await expect(page.getByRole('tabpanel')).toHaveAttribute('id', 'ad-manager-panel-strategy');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}
