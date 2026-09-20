import { expect, test } from './fixtures';

const pixel = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=', 'base64');

test('one shared count produces separate session previews and independent downloads on mobile', async ({ page }) => {
  let saved = false;
  const files = ['Session 11', 'سيشن ١٢'].map((title, section) => ({ section, title, pageCount: 4,
    pages: Array.from({ length: 4 }, (_, pageIndex) => ({ pageIndex, title: pageIndex === 0 ? title : '',
      body: pageIndex === 0 ? '' : `تفاصيل ${title} جزء ${pageIndex}`,
      blocks: pageIndex === 0 ? [] : [{ type: 'paragraph', items: [`تفاصيل ${title} جزء ${pageIndex}`] }],
    })),
  }));
  const documents = files.map(file => ({ id: `session-${file.section}`, title: file.title, kind: 'Presentation', status: 'Ready', requestedPageCount: file.pageCount }));
  await page.route('**/api/content**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/api/content') return route.fulfill({ json: {
      aiConfigured: true, connectedPages: [], posts: [], weeklyPlans: [], imageModel: 'test', imageSize: '4K', knowledgeDocumentCount: 0,
      settings: { logoUrl: '/api/slide-test-image', brandColors: [], dailyPublishTimeLocal: '10:00', stylePrompt: '', isEnabled: false, hasApprovedStyle: false },
    } });
    if (path.endsWith('/suggest-page-count')) return route.fulfill({ json: { pageCount: 5, minPageCount: 3, maxPageCount: 40, sessionCount: 2,
      fileSections: files.map(file => ({ section: file.section, title: file.title, pageCount: 2, minPageCount: 1, maxPageCount: 20 })),
    } });
    if (path.endsWith('/preview')) {
      expect(route.request().postDataJSON()).toMatchObject({ pagesPerSession: 4, pageCount: 0, coverTitle: '' });
      return route.fulfill({ json: { title: 'Session 11', pageCount: 8, pages: [], documents: files, fingerprint: 'approved-batch' } });
    }
    if (path === '/api/content/documents' && route.request().method() === 'POST') {
      expect(route.request().postDataJSON()).toMatchObject({ pagesPerSession: 4, previewFingerprint: 'approved-batch' });
      saved = true;
      return route.fulfill({ json: { id: documents[0].id, ids: documents.map(document => document.id), message: 'اتحفظ ملف كل سيشن مستقل.' } });
    }
    if (path === '/api/content/documents') return route.fulfill({ json: { documents: saved ? documents : [] } });
    const index = documents.findIndex(document => path === `/api/content/documents/${document.id}`);
    if (index >= 0) return route.fulfill({ json: { document: documents[index],
      pages: files[index].pages.map(slide => ({ ...slide, id: `${index}-${slide.pageIndex}`, status: 'Ready', imageUrl: '/api/slide-test-image' })),
    } });
    return route.fulfill({ status: 404, json: {} });
  });
  await page.route('**/api/slide-test-image', route => route.fulfill({ contentType: 'image/png', body: pixel }));
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/management/content?view=documents');
  await page.getByLabel('المحتوى الكامل').fill('مقدمة الدورة\nSession 11\nتفاصيل السيشن الأول مع الشرح والأمثلة كاملة\nسيشن ١٢\nتفاصيل السيشن الثاني مع الشرح والأمثلة كاملة');
  const count = page.getByRole('spinbutton', { name: 'عدد السلايدات لكل سيشن شامل الغلاف' });
  await count.fill('4');
  await expect(page.getByRole('spinbutton')).toHaveCount(1);
  await page.getByRole('complementary', { name: 'إنشاء عرض أو مستند' }).screenshot({ path: '/tmp/session-batch-composer-mobile.png' });
  await page.getByRole('button', { name: 'اعرض التقسيم', exact: true }).click();
  for (const file of files) {
    const preview = page.getByRole('region', { name: `معاينة ملف ${file.title}`, exact: true });
    await expect(preview.getByRole('region', { name: 'معاينة الصفحة 1', exact: true }).getByRole('heading')).toHaveText(file.title);
    await expect(preview.getByRole('region', { name: /معاينة الصفحة/ })).toHaveCount(4);
  }
  await page.getByRole('region', { name: 'معاينة ملف Session 11', exact: true }).screenshot({ path: '/tmp/session-batch-preview-mobile.png' });
  await page.getByRole('button', { name: 'اعتمد التقسيم وابدأ التصميم' }).click();
  for (const [index, file] of files.entries()) {
    await page.getByRole('complementary', { name: 'الملفات السابقة' }).getByRole('button', { name: new RegExp(file.title) }).click();
    await expect(page.getByLabel('عنوان الصفحة 1', { exact: true })).toHaveValue(file.title);
    await expect(page.getByLabel('نص الصفحة 2', { exact: true })).toHaveValue(`تفاصيل ${file.title} جزء 1`);
    const downloading = page.waitForEvent('download');
    await page.getByRole('button', { name: 'PPTX', exact: true }).click();
    const download = await downloading;
    expect(download.suggestedFilename()).toBe(`${file.title}.pptx`);
    expect(await download.failure()).toBeNull();
    await download.saveAs(`/tmp/session-batch-${index}.pptx`);
  }
  expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)).toBe(false);
});

test('separate sessions allow designing one slide on mobile and show queued progress', async ({ page }) => {
  const documents = [4, 5, 6, 7, 8].map(number => ({ id: `session-${number}`, title: `Session ${number}`,
    kind: 'Presentation', status: 'AwaitingDesign', requestedPageCount: number === 4 ? 13 : 16 }));
  const pages = Object.fromEntries(documents.map(document => [document.id, Array.from({ length: document.requestedPageCount }, (_, index) => ({
    id: `${document.id}-page-${index + 1}`, pageIndex: index, title: index === 0 ? document.title : '', body: `Training topic ${index + 1}`,
    status: document.id === 'session-4' && index < 12 ? 'Ready' : 'Planned',
    imageUrl: document.id === 'session-4' && index < 12 ? '/api/slide-test-image' : null as string | null,
  }))]));
  const requested: string[] = [];
  await page.route('**/api/content**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/api/content') return route.fulfill({ json: {
      aiConfigured: true, connectedPages: [], posts: [], weeklyPlans: [], imageModel: 'test', imageSize: '4K', knowledgeDocumentCount: 0,
      settings: { logoUrl: '/api/slide-test-image', brandColors: [], dailyPublishTimeLocal: '10:00', stylePrompt: '', isEnabled: false, hasApprovedStyle: false },
    } });
    if (path === '/api/content/documents') return route.fulfill({ json: { documents } });
    const document = documents.find(candidate => path === `/api/content/documents/${candidate.id}`);
    if (document) return route.fulfill({ json: { document, pages: pages[document.id] } });
    if (path.endsWith('/regenerate-image') && route.request().method() === 'POST') {
      requested.push(path);
      documents[1].status = 'GeneratingImages';
      pages['session-5'][1].status = 'Queued';
      return route.fulfill({ json: { message: 'تمت جدولة تصميم السلايد 2.' } });
    }
    return route.fulfill({ status: 404, json: {} });
  });
  await page.route('**/api/slide-test-image', route => route.fulfill({ contentType: 'image/png', body: pixel }));
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/management/content?view=documents');
  await expect(page.getByRole('heading', { name: 'Session 4', exact: true })).toBeVisible();
  await expect(page.getByText('اكتمل 12 من 13.', { exact: false })).toBeVisible();
  await expect(page.getByRole('button', { name: 'PPTX', exact: true })).toBeDisabled();
  await page.getByRole('complementary', { name: 'الملفات السابقة' }).getByRole('button', { name: /^Session 5/ }).click();
  const slide = page.getByRole('region', { name: 'السلايد 2', exact: true });
  await expect(slide.getByText('جاهز للتصميم')).toBeVisible();
  await slide.screenshot({ path: '/tmp/session-slide-button-mobile.png' });
  await slide.getByRole('button', { name: 'تصميم السلايد', exact: true }).click();
  await expect(slide.getByText('في انتظار بدء التصميم')).toBeVisible();
  await expect(slide.getByRole('button', { name: 'تصميم السلايد', exact: true })).toBeDisabled();
  expect(requested).toEqual(['/api/content/documents/session-5/pages/session-5-page-2/regenerate-image']);

  pages['session-5'][1].status = 'Ready';
  pages['session-5'][1].imageUrl = '/api/slide-test-image';
  documents[1].status = 'AwaitingDesign';
  await expect(slide.getByRole('button', { name: 'إعادة تصميم السلايد', exact: true })).toBeEnabled();
  await expect(page.getByText('اكتمل 1 من 16.', { exact: false })).toBeVisible();
  await expect(page.getByRole('button', { name: 'PPTX', exact: true })).toBeDisabled();
  expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)).toBe(false);
});

test('finished deck supports adding, reordering and downloading slides on mobile', async ({ page }) => {
  const deckSummary = { id: 'finished', title: 'عرض مكتمل', kind: 'Presentation', status: 'Ready', requestedPageCount: 2 };
  let pages = [0, 1].map(pageIndex => ({ id: `saved-${pageIndex}`, pageIndex, title: `عنوان ${pageIndex + 1}`,
    body: `محتوى ${pageIndex + 1}`, status: 'Ready', imageUrl: '/api/slide-test-image' as string | null }));
  await page.route('**/api/content**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/api/content') return route.fulfill({ json: {
      aiConfigured: true, connectedPages: [], posts: [], weeklyPlans: [], imageModel: 'test', imageSize: '4K', knowledgeDocumentCount: 0,
      settings: { logoUrl: '/api/slide-test-image', brandColors: [], dailyPublishTimeLocal: '10:00', stylePrompt: '', isEnabled: false, hasApprovedStyle: false },
    } });
    if (path === '/api/content/documents') return route.fulfill({ json: { documents: [deckSummary] } });
    if (path === '/api/content/documents/finished') return route.fulfill({ json: { document: deckSummary, pages } });
    if (path.endsWith('/pages/order') && route.request().method() === 'PUT') {
      const { pageIds } = route.request().postDataJSON();
      pages = pageIds.map((id: string, pageIndex: number) => ({ ...pages.find(slide => slide.id === id)!, pageIndex }));
      return route.fulfill({ status: 204 });
    }
    if (path.endsWith('/finished/pages') && route.request().method() === 'POST') {
      const { title, body, beforePageId } = route.request().postDataJSON();
      const pageIndex = beforePageId ? pages.findIndex(slide => slide.id === beforePageId) : pages.length;
      pages.splice(pageIndex, 0, { id: 'added', pageIndex, title, body, status: 'Planned', imageUrl: null });
      pages.forEach((slide, index) => { slide.pageIndex = index; });
      deckSummary.requestedPageCount = 3;
      deckSummary.status = 'AwaitingDesign';
      return route.fulfill({ json: { id: 'added', pageIndex } });
    }
    if (path.endsWith('/added/regenerate-image')) {
      pages.find(slide => slide.id === 'added')!.status = 'Queued';
      deckSummary.status = 'GeneratingImages';
      return route.fulfill({ json: { message: 'بدأ تصميم السلايد الجديد' } });
    }
    return route.fulfill({ status: 404, json: {} });
  });
  await page.route('**/api/slide-test-image', route => route.fulfill({ contentType: 'image/png', body: pixel }));
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/management/content?view=documents');
  await page.getByRole('button', { name: 'إضافة سلايد', exact: true }).click();
  const form = page.getByRole('form', { name: 'سلايد جديد' });
  await expect(form.getByLabel('عنوان السلايد الجديد')).toBeFocused();
  await form.getByLabel('عنوان السلايد الجديد').fill('سلايد مضاف');
  await form.getByLabel('محتوى السلايد الجديد').fill('تفاصيل إضافية بعد انتهاء العرض');
  await form.getByLabel('مكان السلايد الجديد').selectOption('saved-1');
  await expect(form.getByText('هيكون السلايد رقم 2 من 3.')).toBeVisible();
  await form.screenshot({ path: '/tmp/content-new-slide-mobile.png' });
  await form.getByRole('button', { name: 'إضافة وتصميم السلايد' }).click();
  const addedSlide = page.getByRole('region', { name: 'السلايد 2', exact: true });
  await expect(addedSlide.getByText('في انتظار بدء التصميم')).toBeVisible();
  pages[1].status = 'Ready';
  pages[1].imageUrl = '/api/slide-test-image';
  deckSummary.status = 'Ready';
  await expect(page.getByLabel('عنوان الصفحة 2', { exact: true })).toHaveValue('سلايد مضاف');
  const moveToFirst = page.getByRole('button', { name: 'نقل السلايد 3 لأول العرض' });
  await expect(moveToFirst).toBeEnabled();
  await moveToFirst.click();
  await expect(page.getByLabel('عنوان الصفحة 1', { exact: true })).toHaveValue('عنوان 2');
  await page.reload();
  await expect(page.getByLabel('عنوان الصفحة 1', { exact: true })).toHaveValue('عنوان 2');
  await expect(page.getByLabel('عنوان الصفحة 2', { exact: true })).toHaveValue('عنوان 1');
  await expect(page.getByLabel('عنوان الصفحة 3', { exact: true })).toHaveValue('سلايد مضاف');
  await page.getByRole('region', { name: 'السلايد 2', exact: true }).screenshot({ path: '/tmp/content-reordered-slide-mobile.png' });
  expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)).toBe(false);
  const downloadPromise = page.waitForEvent('download');
  await page.getByRole('button', { name: 'PPTX', exact: true }).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe('عرض مكتمل.pptx');
  expect(await download.failure()).toBeNull();
  await download.saveAs('/tmp/content-edited-deck.pptx');
});
