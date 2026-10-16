import type { Page } from '@playwright/test';
import { expect, test } from './fixtures';
import type { ContentStudioData, ContentVideo, ContentVideoReadiness } from '../../src/packages/content-studio/types';

const studio: ContentStudioData = {
  imageModel: 'Gemini', imageSize: '4K', aspectRatio: '1:1', aiConfigured: true, knowledgeDocumentCount: 3,
  connectedPages: [], posts: [],
  settings: {
    isEnabled: false, hasApprovedStyle: false, dailyPublishTimeLocal: '10:00', postsPerDay: 1,
    timezone: 'Africa/Cairo', stylePrompt: '', brandColors: [], designSource: 'logo',
    matchReferenceTone: false, referenceColors: [], referenceImages: [],
  },
};

const readiness: ContentVideoReadiness = {
  configured: true, geminiApiKeyConfigured: true, geminiAgentPlatformApiKeyConfigured: true,
  enterpriseProjectId: 'project-video', model: 'Gemini Omni', supportedAspectRatios: ['9:16', '16:9'],
  supportedResolutions: ['720p', '1080p'], knowledgeDocumentCount: 3, reason: null,
};
const videos: ContentVideo[] = Array.from({ length: 12 }, (_, index) => ({
  id: `video-${index + 1}`, status: index === 11 ? 'Generating' : 'AwaitingApproval', ideaTitle: `فيديو محفوظ ${index + 1}`,
  hook: 'خطوة جديدة في التعلم', summary: 'تجربة عملية يتعلم فيها الطلاب معًا.', caption: 'ابدأ مع مجموعتك الجديدة.',
  aspectRatio: '9:16', resolution: '720p', sceneCount: 4, requestedSceneCount: 4, requestedSceneDurationSeconds: 5,
  completedSceneCount: index === 11 ? 2 : 0, knowledgeWasTruncated: false, error: null,
  createdAt: '2026-10-16T10:00:00Z', updatedAt: '2026-10-16T10:00:00Z', finalVideoUrl: null,
  scenes: ['المشهد الأول', 'المشهد الثاني', 'المشهد الثالث', 'المشهد الرابع'].map((title, sceneIndex) => ({
    id: `video-${index + 1}-scene-${sceneIndex + 1}`, sceneIndex, title, narrative: 'شرح الفكرة ومشاركة الطلاب في نشاط قصير.',
    visualPrompt: 'Students learning together', audioPrompt: 'شرح صوتي بالعربية', transitionPrompt: 'انتقال هادئ', durationSeconds: 5,
    status: index !== 11 ? 'Planned' : sceneIndex < 2 ? 'Completed' : sceneIndex === 2 ? 'Failed' : 'Queued',
    error: index === 11 && sceneIndex === 2 ? 'توقف توليد المشهد.' : null,
    videoUrl: index === 11 && sceneIndex < 2 ? `/api/content/test-assets/scene-${sceneIndex + 1}.mp4` : null,
  })),
}));

async function installVideos(page: Page, configuredReadiness: ContentVideoReadiness) {
  const detailLoads: string[] = [];
  const mutations: string[] = [];
  const assetLoads: string[] = [];
  await page.route('**/api/content**', async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() !== 'GET') { mutations.push(path); return route.fulfill({ status: 409, json: { error: 'Unexpected mutation in review test' } }); }
    if (path === '/api/content') return route.fulfill({ json: studio });
    if (path === '/api/content/videos') return route.fulfill({ json: { readiness: configuredReadiness, videos } });
    const video = videos.find(candidate => path === `/api/content/videos/${candidate.id}`);
    if (video) { detailLoads.push(video.id); return route.fulfill({ json: video }); }
    if (path.startsWith('/api/content/test-assets/')) { assetLoads.push(path); return route.fulfill({ contentType: 'video/mp4', body: Buffer.alloc(0) }); }
    return route.fallback();
  });
  return { detailLoads, mutations, assetLoads };
}

for (const reducedMotion of ['no-preference', 'reduce'] as const) {
  test(`video and scene selection reveal the review while polling preserves drafts with motion ${reducedMotion}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion });
    const requests = await installVideos(page, readiness);
    await page.goto('/management/content?view=videos');
    const history = page.locator('details').filter({ has: page.locator('summary').filter({ hasText: 'الفيديوهات المحفوظة' }) });
    await expect(history).not.toHaveAttribute('open', '');
    await page.locator('summary').filter({ hasText: 'إنشاء فيديو جديد' }).click();
    const draft = page.getByRole('textbox', { name: /^توجيه اختياري/ });
    await draft.fill('فكرة كتبتها وأريد الاحتفاظ بها');
    await history.locator('summary').click();
    await history.getByRole('button', { name: /^فيديو محفوظ 12 / }).click();
    const review = page.getByRole('region', { name: 'مراجعة الفيديو: فيديو محفوظ 12', exact: true });
    await expect(review).toBeFocused();
    await expect(review.getByRole('heading', { name: 'فيديو محفوظ 12', exact: true })).toBeInViewport();
    await expect(history).not.toHaveAttribute('open', '');
    await expect(draft).toHaveValue('فكرة كتبتها وأريد الاحتفاظ بها');

    const secondScene = review.getByRole('article').filter({ has: page.getByRole('heading', { name: 'المشهد الثاني', exact: true }) });
    await secondScene.getByRole('button', { name: 'عاين المشهد', exact: true }).click();
    const previewHeading = review.getByRole('heading', { name: 'معاينة المشهد 2: المشهد الثاني', exact: true });
    await expect(previewHeading).toBeFocused();
    await expect(previewHeading).toBeInViewport();
    expect(requests.assetLoads).toEqual([]);

    const loadsBeforeTyping = requests.detailLoads.filter(id => id === 'video-12').length;
    await draft.focus();
    await expect.poll(() => requests.detailLoads.filter(id => id === 'video-12').length).toBeGreaterThan(loadsBeforeTyping);
    await expect(draft).toBeFocused();
    await expect(draft).toHaveValue('فكرة كتبتها وأريد الاحتفاظ بها');
    await secondScene.getByRole('button', { name: 'المشهد معروض', exact: true }).click();
    await expect(previewHeading).toBeFocused();
    await expect(previewHeading).toBeInViewport();
    await page.screenshot({ path: testInfo.outputPath('selected-scene-mobile.png') });

    await history.locator('summary').click();
    await history.getByRole('button', { name: /^فيديو محفوظ 12 / }).click();
    await expect(review).toBeFocused();
    await expect(review.getByRole('heading', { name: 'فيديو محفوظ 12', exact: true })).toBeVisible();
    expect(requests.mutations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

test('read-only review keeps missing video prerequisites and disables generation and retry', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await page.addInitScript(() => localStorage.setItem('user', JSON.stringify({ id: 'agent', fullName: 'عضو الفريق', email: 'agent@example.test', role: 'Agent' })));
  const requests = await installVideos(page, { ...readiness, configured: false, geminiAgentPlatformApiKeyConfigured: false, enterpriseProjectId: null, reason: 'مفتاح الفيديو غير مضاف' });
  await page.goto('/management/content?view=videos');
  await expect(page.getByText('إعدادات الفيديو تحتاج استكمال', { exact: false })).toBeVisible();
  await expect(page.getByText('أضف مفتاح Agent Platform المستقل', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'اعتمد وولّد المشاهد', exact: true })).toBeDisabled();
  await page.locator('summary').filter({ hasText: 'إنشاء فيديو جديد' }).click();
  await expect(page.getByRole('textbox', { name: /^توجيه اختياري/ })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'اقترح الفكرة وقسّم المشاهد', exact: true })).toBeDisabled();
  const history = page.locator('details').filter({ has: page.locator('summary').filter({ hasText: 'الفيديوهات المحفوظة' }) });
  await history.locator('summary').click();
  await history.getByRole('button', { name: /^فيديو محفوظ 12 / }).click();
  await expect(page.getByRole('button', { name: 'أعد المشهد', exact: true })).toBeDisabled();
  expect(requests.mutations).toEqual([]);
});

for (const recovery of ['retry button', 'saved record'] as const) {
  test(`failed video selection recovers through ${recovery} without showing an empty workspace`, async ({ page }) => {
    const requests = await installVideos(page, readiness);
    let failed = false;
    await page.route('**/api/content/videos/video-1', async route => {
      if (failed) return route.fallback();
      failed = true;
      return route.fulfill({ status: 503, json: { error: 'تعذر تحميل التفاصيل مؤقتًا' } });
    });
    await page.goto('/management/content?view=videos');
    await expect(page.getByRole('heading', { name: 'تعذّر تحميل الفيديو المختار', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'ابدأ من فكرة حقيقية', exact: true })).toBeHidden();
    if (recovery === 'retry button') await page.getByRole('button', { name: 'إعادة تحميل الفيديو', exact: true }).click();
    else {
      const history = page.locator('details').filter({ has: page.locator('summary').filter({ hasText: 'الفيديوهات المحفوظة' }) });
      await history.locator('summary').click();
      await history.getByRole('button', { name: /^فيديو محفوظ 1 / }).click();
    }
    const review = page.getByRole('region', { name: 'مراجعة الفيديو: فيديو محفوظ 1', exact: true });
    await expect(review).toBeFocused();
    await expect(review.getByRole('heading', { name: 'فيديو محفوظ 1', exact: true })).toBeVisible();
    await expect(page.getByText('تعذر تحميل التفاصيل مؤقتًا', { exact: true })).toBeHidden();
    expect(requests.mutations).toEqual([]);
  });
}
