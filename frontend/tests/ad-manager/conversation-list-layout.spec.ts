import type { Locator } from '@playwright/test';
import type { Channel, Conversation } from '../../src/types/chat';
import { expect, test } from './fixtures';

const channels: { path: string; channel: Channel }[] = [
  { path: '/inbox', channel: 'WhatsApp' },
  { path: '/inbox/messenger', channel: 'Messenger' },
  { path: '/inbox/comments', channel: 'FacebookComment' },
];
const scenarios = [
  ...channels.flatMap(channel => [393, 1440].map(width => ({ ...channel, width, height: 700 }))),
  { ...channels[0], width: 320, height: 568 },
  { ...channels[0], width: 844, height: 390 },
];

function conversationPage(channel: Channel, start: number): Conversation[] {
  return Array.from({ length: 20 }, (_, offset) => ({
    id: `conversation-${start + offset}`, projectId: '11111111-1111-1111-1111-111111111111',
    channel, status: 'Open', lastMessageAt: '2026-10-16T22:09:00Z', unreadCount: 1,
    assignedAgentId: null, assignedAgentName: 'مسؤولة متابعة حجوزات الكورس المسائي',
    whatsAppAccountName: 'حساب استقبال استفسارات وحجوزات الكورس',
    customer: { id: `customer-${start + offset}`, name: `عميل تجريبي ${start + offset} باسم طويل جدًا`,
      phone: '201000000000', avatarUrl: null, label: 'الاستفسار عن المدة والشهادة ومواعيد الكورس المسائي' },
  }));
}

async function expectRowsContainTheirContent(rows: Locator) {
  const clipped = await rows.evaluateAll(elements => elements.flatMap(element => {
    const row = element.getBoundingClientRect();
    const content = Array.from(element.querySelectorAll('span, p, svg')).map(child => child.getBoundingClientRect());
    return content.some(child => child.top < row.top - 1 || child.bottom > row.bottom + 1)
      ? [element.getAttribute('aria-label')] : [];
  }));
  expect(clipped, 'Conversation names, previews and badges must stay inside their own row').toEqual([]);
}

for (const scenario of scenarios) {
  test(`dense ${scenario.channel} rows stay readable after loading older conversations at ${scenario.width}x${scenario.height}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: scenario.width, height: scenario.height });
    const olderConversations = conversationPage(scenario.channel, 20);
    await page.route('**/api/projects/*/conversations?**', route => {
      const before = new URL(route.request().url()).searchParams.get('before');
      return route.fulfill({ json: before ? olderConversations : conversationPage(scenario.channel, 0) });
    });
    await page.goto(scenario.path);
    const rows = page.getByRole('button', { name: /، آخر نشاط /, includeHidden: true });
    await expect(rows).toHaveCount(20);
    await page.evaluate(() => document.fonts.ready);
    await page.screenshot({ path: testInfo.outputPath('conversation-list.png'), scale: 'css' });
    await expectRowsContainTheirContent(rows);
    const search = page.getByRole('searchbox', { name: /بحث في محادثات/ });
    const headerTop = (await search.boundingBox())!.y;

    await page.getByRole('button', { name: 'تحميل محادثات أقدم', exact: true }).click();
    await expect(rows).toHaveCount(40);
    await expectRowsContainTheirContent(rows);
    for (const label of [olderConversations.at(-1)!.customer.name, 'مفتوحة']) {
      const content = rows.last().getByText(label, { exact: true });
      await content.scrollIntoViewIfNeeded();
      await expect(content).toBeInViewport({ ratio: 1 });
    }
    await expect(rows.first()).not.toBeInViewport();
    await expect(search).toBeInViewport({ ratio: 1 });
    expect((await search.boundingBox())!.y).toBeCloseTo(headerTop, 0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await rows.last().click();
    await expect(rows.last()).toHaveAttribute('aria-pressed', 'true');
  });
}
