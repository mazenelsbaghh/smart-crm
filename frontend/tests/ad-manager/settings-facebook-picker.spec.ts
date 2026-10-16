import { expect, test } from './fixtures';

const projectId = '11111111-1111-1111-1111-111111111111';
const availablePages = Array.from({ length: 14 }, (_, index) => ({
  pageId: `page-${index + 1}`,
  pageName: `صفحة ${index + 1} لتعليم المحادثة والأنشطة والتدريبات اليومية للمشتركين`,
  accessToken: `test-page-token-${index + 1}`,
}));

for (const viewport of [{ width: 320, height: 640 }, { width: 844, height: 390 }]) {
  test(`Facebook picker keeps actions visible and retries the selected page at ${viewport.width}x${viewport.height}`, async ({ page, context }, testInfo) => {
    const confirmations: Record<string, unknown>[] = [];
    await page.route('**/api/facebook/oauth/login?**', route => route.fulfill({ json: {
      authorizationUrl: new URL('/facebook-picker-test', page.url()).href,
    } }));
    await context.route('**/facebook-picker-test', route => route.fulfill({
      contentType: 'text/html; charset=utf-8', body: `<meta charset="utf-8"><script>window.opener.postMessage(${JSON.stringify({
        type: 'facebook-oauth-success', projectId, userAccessToken: 'test-user-token', pages: availablePages,
      })}, window.location.origin); window.close();</script>`,
    }));
    await page.route(`**/api/projects/${projectId}/facebook/pages/confirm`, route => {
      confirmations.push(route.request().postDataJSON());
      return route.fulfill(confirmations.length === 1
        ? { status: 503, json: { error: 'تعذر الربط مؤقتًا. حاول مرة أخرى.' } }
        : { json: {} });
    });
    await page.route(`**/api/projects/${projectId}/facebook/pages`, route => route.fulfill({ json: confirmations.length < 2 ? [] : [{
      id: 'connection-1', pageId: 'page-14', pageName: availablePages.at(-1)!.pageName, connectedAt: '2026-10-16T17:00:00Z',
    }] }));
    await page.setViewportSize(viewport);
    await page.goto('/settings?section=connections');
    const launch = page.getByRole('button', { name: 'ربط صفحة فيسبوك', exact: true });
    await launch.click();

    const picker = page.getByRole('dialog', { name: 'ربط صفحة فيسبوك', exact: true });
    const confirm = picker.getByRole('button', { name: 'ربط الصفحة المحددة', exact: true });
    const close = picker.getByRole('button', { name: 'إغلاق', exact: true });
    await expect(confirm).toBeInViewport();
    await expect(close).toBeInViewport();
    await expect(confirm).toBeDisabled();
    const footerPosition = await confirm.boundingBox();
    const selected = picker.getByRole('radio', { name: new RegExp(availablePages.at(-1)!.pageName) });
    await selected.check();
    await expect(selected).toBeChecked();
    await expect(confirm).toBeInViewport();
    await expect(close).toBeInViewport();
    expect((await confirm.boundingBox())!.y).toBeCloseTo(footerPosition!.y, 0);
    expect(confirmations).toEqual([]);
    await page.screenshot({ path: testInfo.outputPath('facebook-picker-selection.png') });

    await confirm.click();
    await expect(picker.getByRole('alert')).toHaveText('تعذر الربط مؤقتًا. حاول مرة أخرى.');
    await expect(selected).toBeChecked();
    await expect(confirm).toBeInViewport();
    await expect(close).toBeInViewport();
    await page.screenshot({ path: testInfo.outputPath('facebook-picker-retry.png') });
    await confirm.click();
    await expect(picker).toBeHidden();
    await expect(page.getByText(availablePages.at(-1)!.pageName, { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'ربط صفحة أخرى', exact: true })).toBeFocused();
    expect(confirmations).toEqual([0, 1].map(() => ({
      facebookPageId: 'page-14', pageName: availablePages.at(-1)!.pageName,
      pageAccessToken: 'test-page-token-14', userAccessToken: 'test-user-token', facebookUserId: '',
    })));
  });
}
