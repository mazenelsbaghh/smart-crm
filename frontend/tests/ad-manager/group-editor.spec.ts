import { expect, test } from './fixtures';

for (const viewport of [{ width: 320, height: 640 }, { width: 844, height: 390 }, { width: 1440, height: 900 }]) {
  test(`group editor keeps actions visible and preserves form values after a failed save at ${viewport.width}x${viewport.height}`, async ({ page }, testInfo) => {
    const saves: Record<string, unknown>[] = [];
    await page.route('**/api/group-appointments/instructors', route => route.fulfill({ json: {
      instructors: ['مصطفى الشامي'], instructorContacts: [], supervisors: [],
    } }));
    await page.route('**/api/group-appointments/booking-stats?**', route => route.fulfill({ json: {
      courseType: 'regular', totalBooked: 0, bookedToday: 0, timezone: 'Africa/Cairo',
      dayStartUtc: '2026-10-15T21:00:00Z', dayEndUtc: '2026-10-16T21:00:00Z', asOfUtc: '2026-10-16T12:00:00Z',
    } }));
    await page.route('**/api/group-appointments', route => {
      saves.push(route.request().postDataJSON());
      return route.fulfill(saves.length === 1
        ? { status: 503, json: { error: 'Service unavailable' } }
        : { json: { id: 'created-group' } });
    });
    await page.setViewportSize(viewport);
    await page.goto('/management/groups');
    const trigger = page.getByRole('button', { name: 'إضافة مجموعة جديدة', exact: true });
    await trigger.focus();
    await page.keyboard.press('Enter');
    const editor = page.getByRole('dialog', { name: 'مجموعة جديدة', exact: true });
    const submit = editor.getByRole('button', { name: 'إنشاء المجموعة', exact: true });
    const cancel = editor.getByRole('button', { name: 'إلغاء', exact: true });
    for (const select of await editor.getByRole('combobox').all()) {
      expect((await select.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    }
    await expect(submit).toBeInViewport();
    await expect(cancel).toBeInViewport();
    await submit.click();
    const footerPosition = await submit.boundingBox();
    expect(saves).toEqual([]);
    await editor.getByLabel('دكتور المجموعة', { exact: true }).selectOption('مصطفى الشامي');
    await editor.getByLabel('موعد الجلسة المجانية', { exact: true }).fill('2026-10-20T18:30');
    await editor.getByLabel('الحد الأقصى للمشتركين', { exact: true }).fill('12');
    await expect(submit).toBeInViewport();
    expect((await submit.boundingBox())!.y).toBeCloseTo(footerPosition!.y, 0);
    expect(await editor.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    await editor.getByLabel('نوع المجموعة', { exact: true }).scrollIntoViewIfNeeded();
    await page.mouse.move(0, 0);
    await page.screenshot({ path: testInfo.outputPath('group-editor.png') });

    await submit.click();
    await expect(editor.getByRole('alert')).toHaveText('تعذر حفظ المجموعة. حاول مرة أخرى.');
    await expect(editor.getByRole('alert')).toBeInViewport();
    await expect(submit).toBeInViewport();
    await expect(editor.getByLabel('الحد الأقصى للمشتركين', { exact: true })).toHaveValue('12');
    await submit.click();
    await expect(editor).toBeHidden();
    await expect(trigger).toBeFocused();
    expect(saves).toEqual([0, 1].map(() => ({
      courseType: 'regular', dateTime: '2026-10-20T15:30:00.000Z', freeSessionDateTime: '2026-10-20T15:30:00.000Z',
      capacity: 12, isActive: true, mode: 'offline', supervisorPhone: '', instructorName: 'مصطفى الشامي',
    })));
    await trigger.focus();
    await page.keyboard.press('Enter');
    await expect(editor.getByRole('alert')).toHaveCount(0);
    await page.keyboard.press('Escape');
    await expect(editor).toBeHidden();
    await expect(trigger).toBeFocused();
  });
}
