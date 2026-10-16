import { expect, test } from './fixtures';
import type { ContentStudioData, ContentDocumentDetail, ContentCardGameDetail } from '../../src/packages/content-studio/types';

const studio: ContentStudioData = {
  imageModel: 'Gemini', imageSize: '4K', aspectRatio: '1:1', aiConfigured: true, knowledgeDocumentCount: 3,
  connectedPages: [], posts: [], settings: {
    isEnabled: false, hasApprovedStyle: false, dailyPublishTimeLocal: '10:00', postsPerDay: 1,
    timezone: 'Africa/Cairo', stylePrompt: '', brandColors: [], designSource: 'logo',
    matchReferenceTone: false, referenceColors: [], referenceImages: [],
  },
};
const document: ContentDocumentDetail = {
  document: { id: 'saved-deck', kind: 'Presentation', status: 'AwaitingDesign', title: 'عرض محفوظ', requestedPageCount: 1, createdAt: '2026-10-16T10:00:00Z', updatedAt: '2026-10-16T10:00:00Z' },
  pages: [{ id: 'saved-slide', pageIndex: 0, title: 'عنوان محفوظ', body: 'نص محفوظ في العرض', status: 'Planned' }],
};
const game: ContentCardGameDetail = {
  game: { id: 'saved-game', title: 'لعبة محفوظة', brief: 'لعبة تعاون', mechanic: 'نتعاون لحل الفكرة', instructions: 'قسّم اللاعبين إلى فرق.', cardCount: 8, createdAt: '2026-10-16T10:00:00Z', updatedAt: '2026-10-16T10:00:00Z', brandColors: [], logoUrl: '', backImageUrl: null, designStatus: 'Draft', designError: null, plannerModel: 'Gemini' },
  cards: [],
};
const cases = [
  { view: 'documents', endpoint: '/api/content/documents', recovery: 'button', errorTitle: 'تعذّر تحميل الملفات المحفوظة', retry: 'إعادة تحميل الملفات' },
  { view: 'documents', endpoint: '/api/content/documents/saved-deck', recovery: 'button', errorTitle: 'تعذّر تحميل الملف المختار', retry: 'إعادة تحميل الملف' },
  { view: 'documents', endpoint: '/api/content/documents/saved-deck', recovery: 'record', errorTitle: 'تعذّر تحميل الملف المختار', retry: '' },
  { view: 'games', endpoint: '/api/content/card-games', recovery: 'button', errorTitle: 'تعذّر عرض الألعاب المحفوظة', retry: 'إعادة تحميل الألعاب' },
  { view: 'games', endpoint: '/api/content/card-games/saved-game', recovery: 'record', errorTitle: 'تعذّر عرض الألعاب المحفوظة', retry: '' },
] as const;

for (const scenario of cases) {
  test(`${scenario.endpoint} failure recovers through ${scenario.recovery} and preserves the creation draft`, async ({ page }) => {
    let failed = false;
    const mutations: string[] = [];
    await page.route('**/api/content**', async route => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      if (request.method() !== 'GET') { mutations.push(path); return route.fulfill({ status: 409, json: { error: 'Unexpected write during recovery' } }); }
      if (path === scenario.endpoint && !failed) { failed = true; return route.fulfill({ status: 503, json: { error: 'التحميل متوقف مؤقتًا' } }); }
      if (path === '/api/content') return route.fulfill({ json: studio });
      if (path === '/api/content/documents') return route.fulfill({ json: { documents: [document.document] } });
      if (path === '/api/content/documents/saved-deck') return route.fulfill({ json: document });
      if (path === '/api/content/card-games') return route.fulfill({ json: { games: [game.game] } });
      if (path === '/api/content/card-games/saved-game') return route.fulfill({ json: game });
      return route.fallback();
    });
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto(`/management/content?view=${scenario.view}`);
    await expect(page.getByRole('heading', { name: scenario.errorTitle, exact: true })).toBeVisible();
    const isDocument = scenario.view === 'documents';
    await expect(page.getByRole('heading', { name: isDocument ? 'المحتوى يتحول لملف جاهز' : 'أول لعبة تبدأ من فكرة', exact: true })).toBeHidden();
    const draft = page.getByRole('textbox', { name: isDocument ? /^المحتوى الكامل/ : 'اسم اللعبة', exact: true });
    const creationSummary = page.locator('summary').filter({ hasText: isDocument ? 'إنشاء ملف جديد' : 'إنشاء لعبة جديدة' });
    if (!await draft.isVisible()) await creationSummary.click();
    await draft.fill('مسودة أريد الاحتفاظ بها');
    if (scenario.recovery === 'button') await page.getByRole('button', { name: scenario.retry, exact: true }).click();
    else await page.getByRole('complementary', { name: isDocument ? 'الملفات السابقة' : 'الألعاب المحفوظة', exact: true }).getByRole('button', { name: isDocument ? /^عرض محفوظ / : /^لعبة محفوظة / }).click();
    await expect(page.getByRole('heading', { name: isDocument ? 'عرض محفوظ' : 'لعبة محفوظة', exact: true })).toBeVisible();
    await expect(page.getByText('التحميل متوقف مؤقتًا', { exact: true })).toBeHidden();
    if (!await draft.isVisible()) await creationSummary.click();
    await expect(draft).toHaveValue('مسودة أريد الاحتفاظ بها');
    if (isDocument) await expect(page.getByRole('textbox', { name: 'عنوان الصفحة 1', exact: true })).toHaveValue('عنوان محفوظ');
    else await expect(page.getByText('نتعاون لحل الفكرة', { exact: true })).toBeVisible();
    expect(mutations).toEqual([]);
  });
}

test('the latest selected game stays open when an older detail response arrives later', async ({ page }) => {
  const laterGame: ContentCardGameDetail = { ...game, game: { ...game.game, id: 'later-game', title: 'آخر لعبة مختارة' } };
  let firstGameLoads = 0;
  let releaseEarlierResponse: () => void = () => {};
  const earlierResponse = new Promise<void>(resolve => { releaseEarlierResponse = resolve; });
  await page.route('**/api/content**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/api/content') return route.fulfill({ json: studio });
    if (path === '/api/content/card-games') return route.fulfill({ json: { games: [game.game, laterGame.game] } });
    if (path === '/api/content/card-games/saved-game') {
      if (++firstGameLoads > 1) await earlierResponse;
      return route.fulfill({ json: game });
    }
    if (path === '/api/content/card-games/later-game') return route.fulfill({ json: laterGame });
    return route.fallback();
  });
  await page.goto('/management/content?view=games');
  await expect(page.getByRole('heading', { name: 'لعبة محفوظة', exact: true })).toBeVisible();
  const history = page.getByRole('complementary', { name: 'الألعاب المحفوظة', exact: true });
  await history.getByRole('button', { name: /^لعبة محفوظة / }).click();
  await expect.poll(() => firstGameLoads).toBe(2);
  await history.getByRole('button', { name: /^آخر لعبة مختارة / }).click();
  await expect(page.getByRole('heading', { name: 'آخر لعبة مختارة', exact: true })).toBeVisible();
  const settledEarlierResponse = page.waitForResponse(response => response.url().endsWith('/api/content/card-games/saved-game'));
  releaseEarlierResponse();
  await (await settledEarlierResponse).finished();
  await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
  await expect(page.getByRole('heading', { name: 'آخر لعبة مختارة', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'لعبة محفوظة', exact: true })).toBeHidden();
});
