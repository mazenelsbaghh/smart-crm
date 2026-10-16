import type { Page } from '@playwright/test';
import { expect, test } from './fixtures';

function deferred() {
  let release!: () => void;
  const promise = new Promise<void>(resolve => { release = resolve; });
  return { promise, release };
}

async function mockBookings(page: Page) {
  const intensiveStats = deferred();
  const refresh = deferred();
  const requests = { details: [] as string[], summaries: [] as string[], statistics: [] as string[] };
  let inactive = false;
  const group = (course: string) => ({
    id: `group-${course}`, name: course === 'regular' ? 'مجموعة المحادثة العادية' : 'مجموعة المكثف المسائية',
    dateTime: '2026-10-20T15:00:00Z', capacity: 10, bookedCount: course === 'regular' ? 4 : 2,
    isActive: course !== 'intensive' || !inactive, days: '2', mode: 'online', bookings: [],
  });
  await page.route('**/api/group-appointments?**', async route => {
    const params = new URL(route.request().url()).searchParams;
    const course = params.get('courseType') ?? 'regular';
    requests.summaries.push(course);
    expect(params.get('includeBookings')).toBe('false');
    if (inactive) await refresh.promise;
    await route.fulfill({ json: [group(course)] });
  });
  await page.route('**/api/group-appointments/booking-stats?**', async route => {
    const course = new URL(route.request().url()).searchParams.get('courseType') ?? 'regular';
    requests.statistics.push(course);
    if (course === 'intensive') await intensiveStats.promise;
    if (inactive) await refresh.promise;
    await route.fulfill({ json: {
      courseType: course, totalBooked: course === 'regular' ? 4 : 2, bookedToday: course === 'regular' ? 3 : 1,
      timezone: 'Africa/Cairo', dayStartUtc: '2026-10-15T21:00:00Z', dayEndUtc: '2026-10-16T21:00:00Z',
      asOfUtc: '2026-10-16T12:00:00Z', todayIncludesTransfers: true,
    } });
  });
  await page.route('**/api/group-appointments/*/bookings', route => {
    const id = new URL(route.request().url()).pathname.split('/').at(-2)!;
    requests.details.push(id);
    return route.fulfill({ json: Array.from({ length: id === 'group-regular' ? 4 : 2 }, (_, index) => ({
      id: `${id}-booking-${index}`, customerId: `${id}-customer-${index}`, customerName: `طالب ${id === 'group-regular' ? 'العادي' : 'المكثف'} ${index + 1}`,
      customerPhone: `20100000000${index}`, createdAt: index < (id === 'group-regular' ? 3 : 1) ? '2026-10-16T10:00:00Z' : '2026-10-14T10:00:00Z',
      isPaid: index === 0, isAttended: false,
    })) });
  });
  await page.route('**/api/group-appointments/group-intensive/toggle', route => {
    inactive = true;
    return route.fulfill({ json: {} });
  });
  await page.route('**/api/group-appointments/instructors', route => route.fulfill({ json: {
    instructors: [], instructorContacts: [], supervisors: [],
  } }));
  return { requests, intensiveStats, refresh };
}

for (const width of [390, 1440]) {
  test(`course booking overview loads summaries first and keeps statistics scoped through filters and refresh at ${width}px`, async ({ page }) => {
    const fixture = await mockBookings(page);
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/management/groups');
    const today = page.getByRole('group', { name: 'حجوزات سُجلت اليوم', exact: true });
    const total = page.getByRole('group', { name: 'إجمالي الحجوزات الحالية', exact: true });
    await expect(today.locator('dd').first()).toHaveText('٣');
    await expect(total.locator('dd').first()).toHaveText('٤');
    await expect(page.getByRole('button', { name: 'المشتركين (4)', exact: true })).toBeVisible();
    expect(fixture.requests.details).toEqual([]);
    expect(fixture.requests.summaries).toEqual(['regular']);

    const filter = page.getByRole('combobox', { name: 'حالة المجموعات', exact: true });
    if (width < 768) await filter.selectOption('full');
    else await page.getByRole('group', { name: 'حالة المجموعات', exact: true }).getByRole('button', { name: /مكتملة/ }).click();
    await expect(page.getByText('لا توجد مجموعات مطابقة.', { exact: false })).toBeVisible();
    await expect(total.locator('dd').first()).toHaveText('٤');
    if (width < 768) await filter.selectOption('all');
    else await page.getByRole('group', { name: 'حالة المجموعات', exact: true }).getByRole('button', { name: /الكل/ }).click();
    await page.getByRole('button', { name: 'المشتركين (4)', exact: true }).click();
    await expect(page.getByText('طالب العادي 1', { exact: true })).toBeVisible();
    expect(fixture.requests.details).toEqual(['group-regular']);

    await page.getByLabel('اختيار نوع الكورس', { exact: true }).click();
    await page.getByRole('link', { name: 'الكورس المكثف', exact: true }).click();
    await expect(page).toHaveURL(/course=intensive/);
    await expect(page.getByRole('button', { name: 'المشتركين (2)', exact: true })).toBeVisible();
    await expect(page.getByText('طالب العادي 1', { exact: true })).toBeHidden();
    await expect(total.locator('dd').first()).toHaveText('جاري التحميل');
    fixture.intensiveStats.release();
    await expect(total.locator('dd').first()).toHaveText('٢');
    await expect(today.locator('dd').first()).toHaveText('١');
    expect(fixture.requests.details).toEqual(['group-regular']);

    const intensiveRow = page.getByRole('row').filter({ hasText: 'مجموعة المكثف المسائية' });
    await intensiveRow.locator('summary').click();
    await intensiveRow.getByRole('button', { name: 'إيقاف الحجز', exact: true }).click();
    await expect.poll(() => fixture.requests.statistics.filter(course => course === 'intensive').length).toBe(2);
    await expect(page.getByText('جاري التحديث. الأرقام المعروضة من آخر تحميل.', { exact: false })).toBeVisible();
    await expect(total.locator('dd').first()).toHaveText('٢');
    await expect(page.getByRole('button', { name: 'المشتركين (2)', exact: true })).toBeVisible();
    fixture.refresh.release();
    await expect(intensiveRow.getByText('غير نشطة', { exact: true })).toBeVisible();
    await expect(total.locator('dd').first()).toHaveText('٢');
    await page.getByRole('button', { name: 'المشتركين (2)', exact: true }).click();
    await expect(page.getByText('طالب المكثف 1', { exact: true })).toBeVisible();
    expect(fixture.requests.details).toEqual(['group-regular', 'group-intensive']);
  });
}
