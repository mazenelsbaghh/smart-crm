import { expect, test } from './fixtures';

const projectId = '11111111-1111-1111-1111-111111111111';

for (const course of [
  { type: 'regular', view: 'manage-groups', destination: '/management/groups', name: 'مجموعة الكورس العادي' },
  { type: 'intensive', view: 'manage-intensive-groups', destination: '/management/groups?course=intensive', name: 'مجموعة الكورس المكثف' },
]) {
  test(`legacy ${course.type} group link opens bookings without waiting for unrelated settings requests`, async ({ page }) => {
    let projectListReads = 0;
    let projectDetailReads = 0;
    const unrelatedRequests: string[] = [];
    const requestedCourses: string[] = [];
    await page.route('**/api/projects', route => {
      projectListReads += 1;
      return route.fulfill({ json: [{ id: projectId, name: 'مشروع الاختبار' }] });
    });
    await page.route(`**/api/projects/${projectId}`, route => {
      projectDetailReads += 1;
      if (projectDetailReads > projectListReads) {
        return route.fulfill({ status: 503, json: { error: 'إعدادات المشروع غير متاحة' } });
      }
      return route.fulfill({ json: { id: projectId, name: 'مشروع الاختبار', settings: { timezone: 'Africa/Cairo' } } });
    });
    await page.route(/\/api\/.*(?:facebook\/pages|automation-overview|human-transfer-overview|schedule-demand|whatsapp\/accounts)(?:\?|$)/, route => {
      unrelatedRequests.push(new URL(route.request().url()).pathname);
      return route.fulfill({ status: 503, json: { error: 'الخدمة غير متاحة' } });
    });
    await page.route('**/api/group-appointments?**', route => {
      const params = new URL(route.request().url()).searchParams;
      requestedCourses.push(params.get('courseType') ?? 'regular');
      expect(params.get('includeBookings')).toBe('false');
      return route.fulfill({ json: [{
        id: 'group-1', name: course.name, dateTime: '2099-10-20T15:00:00Z',
        capacity: 10, bookedCount: 2, isActive: true, days: '2', mode: 'online', bookings: [],
      }] });
    });
    await page.route('**/api/group-appointments/booking-stats?**', route => route.fulfill({ json: {
      courseType: course.type, totalBooked: 2, bookedToday: 1, timezone: 'Africa/Cairo',
      dayStartUtc: '2026-10-15T21:00:00Z', dayEndUtc: '2026-10-16T21:00:00Z',
      asOfUtc: '2026-10-16T12:00:00Z', todayIncludesTransfers: true,
    } }));
    await page.route('**/api/group-appointments/instructors', route => route.fulfill({ json: {
      instructors: [], instructorContacts: [], supervisors: [],
    } }));

    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto(`/settings?tab=addons&view=${course.view}`);
    await expect(page).toHaveURL(course.destination);
    await expect(page.getByText(course.name, { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'المشتركين (2)', exact: true })).toBeVisible();
    expect(projectDetailReads).toBeGreaterThan(0);
    expect(projectDetailReads).toBe(projectListReads);
    expect(unrelatedRequests).toEqual([]);
    expect(requestedCourses.length).toBeGreaterThan(0);
    expect(requestedCourses.every(type => type === course.type)).toBe(true);
  });
}

test('legacy group link preserves the manager permission boundary', async ({ page }) => {
  const bookingRequests: string[] = [];
  await page.addInitScript(() => localStorage.setItem('user', JSON.stringify({
    id: 'agent', fullName: 'عضو الفريق', email: 'agent@example.test', role: 'Agent',
  })));
  page.on('request', request => {
    if (new URL(request.url()).pathname.startsWith('/api/group-appointments')) bookingRequests.push(request.url());
  });

  await page.goto('/settings?tab=addons&view=manage-groups');
  await expect(page).toHaveURL('/management/groups');
  await expect(page.getByText('إدارة المجموعات متاحة للمالك أو المدير', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'إضافة مجموعة جديدة', exact: true })).toBeHidden();
  expect(bookingRequests).toEqual([]);
});
