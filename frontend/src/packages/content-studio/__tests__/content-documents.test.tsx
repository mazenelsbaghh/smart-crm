import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { api } from '../../../services/api';
import ContentDocuments from '../ContentDocuments';
import type { ContentDocumentDetail } from '../types';

const source = `  ${'التفاصيل الأصلية كاملة وبنفس ترتيبها، من غير حذف أو تلخيص.\n'.repeat(5)}  `;
const countResponse = { pageCount: 3, minPageCount: 2, maxPageCount: 8 };
const previewResponse = { title: 'الغلاف', pageCount: 3, fingerprint: 'reviewed-draft', pages: [
  { pageIndex: 0, title: 'الغلاف', body: '', blocks: [] },
  { pageIndex: 1, title: '', body: source.slice(0, 100), blocks: [{ type: 'paragraph', items: [source.slice(0, 100)] }] },
  { pageIndex: 2, title: '', body: source.slice(100), blocks: [{ type: 'paragraph', items: [source.slice(100)] }] },
] };

beforeEach(() => {
  vi.spyOn(api, 'get').mockImplementation(async url => {
    if (url === '/api/content/documents') return { data: { documents: [] } };
    if (url === '/api/content/documents/document-new') return { data: {
      document: { id: 'document-new', kind: 'Presentation', status: 'Planning', title: '', requestedPageCount: 3 },
      pages: [],
    } };
    throw new Error(`Unexpected GET: ${url}`);
  });
});

function mockFinishedDeck() {
  const deck: ContentDocumentDetail = {
    document: { id: 'finished', title: 'عرض مكتمل', kind: 'Presentation', status: 'Ready', requestedPageCount: 2, createdAt: '', updatedAt: '' },
    pages: [0, 1].map(pageIndex => ({ id: `existing-${pageIndex}`, pageIndex, title: `عنوان ${pageIndex + 1}`, body: `محتوى ${pageIndex + 1}`, status: 'Ready' })),
  };
  vi.mocked(api.get).mockImplementation(async url => {
    if (url === '/api/content/documents') return { data: { documents: [{ ...deck.document }] } };
    if (url === '/api/content/documents/finished') return { data: structuredClone(deck) };
    throw new Error(`Unexpected GET: ${url}`);
  });
  return deck;
}

it.each([[0, false], [1, false], [2, false], [1, true]] as const)('inserts at position %s and keeps the slide saved when design queue fails: %s', async (position, failQueue) => {
  const deck = mockFinishedDeck();
  const originalPages = structuredClone(deck.pages);
  vi.spyOn(api, 'post').mockImplementation(async (url, payload) => {
    if (url === '/api/content/documents/finished/pages') {
      const { title, body, beforePageId } = payload as { title: string; body: string; beforePageId: string | null };
      expect(beforePageId).toBe(originalPages[position]?.id ?? null);
      deck.pages.splice(position, 0, { id: 'added', pageIndex: position, title, body, status: 'Planned' });
      deck.pages.forEach((page, pageIndex) => { page.pageIndex = pageIndex; });
      deck.document.requestedPageCount = 3;
      deck.document.status = 'AwaitingDesign';
      return { data: { id: 'added', pageIndex: position } };
    }
    if (url.endsWith('/added/regenerate-image')) {
      if (failQueue) throw new Error('تعذر بدء التصميم');
      deck.pages[position].status = 'Queued';
      deck.document.status = 'GeneratingImages';
      return { data: { message: 'بدأ تصميم السلايد الجديد' } };
    }
    throw new Error(`Unexpected POST: ${url}`);
  });
  render(<ContentDocuments canManage brandReady aiReady onDraftDirtyChange={() => {}} />);
  fireEvent.click(await screen.findByRole('button', { name: 'إضافة سلايد' }));
  const form = screen.getByRole('form', { name: 'سلايد جديد' });
  expect(within(form).getByRole('button', { name: 'إضافة وتصميم السلايد' })).toBeDisabled();
  fireEvent.change(within(form).getByLabelText('مكان السلايد الجديد'), { target: { value: originalPages[position]?.id ?? '' } });
  expect(within(form).getByText(`هيكون السلايد رقم ${position + 1} من 3.`)).toBeInTheDocument();
  fireEvent.change(within(form).getByLabelText('عنوان السلايد الجديد'), { target: { value: 'إضافة بعد الانتهاء' } });
  fireEvent.change(within(form).getByLabelText('محتوى السلايد الجديد'), { target: { value: 'تفاصيل جديدة كاملة' } });
  fireEvent.click(within(form).getByRole('button', { name: 'إضافة وتصميم السلايد' }));

  await waitFor(() => expect(screen.getByLabelText(`عنوان الصفحة ${position + 1}`)).toHaveValue('إضافة بعد الانتهاء'));
  const slide = screen.getByRole('region', { name: `السلايد ${position + 1}` });
  expect(within(slide).getByLabelText(`نص الصفحة ${position + 1}`)).toHaveValue('تفاصيل جديدة كاملة');
  const originals = deck.pages.filter(page => page.id !== 'added');
  expect(originals.map(page => [page.id, page.title, page.body, page.status])).toEqual(originalPages.map(page => [page.id, page.title, page.body, page.status]));
  expect(screen.queryByRole('form', { name: 'سلايد جديد' })).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'PPTX' })).toBeDisabled();
  if (failQueue) {
    expect(screen.getByRole('alert')).toHaveTextContent('تعذر بدء التصميم');
    expect(within(slide).getByRole('button', { name: 'تصميم السلايد' })).toBeEnabled();
  } else expect(within(slide).getByText('في انتظار بدء التصميم')).toBeInTheDocument();
});

it('retains a new slide draft when saving fails and allows retry', async () => {
  mockFinishedDeck();
  vi.spyOn(api, 'post').mockRejectedValue(new Error('تعذر حفظ السلايد'));
  render(<ContentDocuments canManage brandReady aiReady onDraftDirtyChange={() => {}} />);
  fireEvent.click(await screen.findByRole('button', { name: 'إضافة سلايد' }));
  fireEvent.change(screen.getByLabelText('محتوى السلايد الجديد'), { target: { value: 'كلام لازم يفضل محفوظ في المحرر' } });
  fireEvent.change(screen.getByLabelText('مكان السلايد الجديد'), { target: { value: 'existing-1' } });
  fireEvent.click(screen.getByRole('button', { name: 'إضافة وتصميم السلايد' }));

  expect(await screen.findByRole('alert')).toHaveTextContent('تعذر حفظ السلايد');
  await waitFor(() => expect(screen.getByRole('button', { name: 'إضافة وتصميم السلايد' })).toBeEnabled());
  expect(screen.getByLabelText('محتوى السلايد الجديد')).toHaveValue('كلام لازم يفضل محفوظ في المحرر');
  expect(screen.getByLabelText('مكان السلايد الجديد')).toHaveValue('existing-1');
  expect(screen.queryByRole('region', { name: 'السلايد 3' })).not.toBeInTheDocument();
});

it('moves a slide with its unsaved text and saves that text against the same slide', async () => {
  const deck = mockFinishedDeck();
  vi.spyOn(api, 'put').mockImplementation(async (url, payload) => {
    if (url.endsWith('/pages/order')) {
      const { pageIds } = payload as { pageIds: string[] };
      deck.pages = pageIds.map((id, pageIndex) => ({ ...deck.pages.find(page => page.id === id)!, pageIndex }));
    } else if (url.endsWith('/pages/existing-1')) Object.assign(deck.pages[0], payload);
    else throw new Error(`Unexpected PUT: ${url}`);
    return { data: {} };
  });
  vi.spyOn(api, 'post').mockImplementation(async url => {
    if (!url.endsWith('/existing-1/regenerate-image')) throw new Error(`Unexpected POST: ${url}`);
    deck.pages[0].status = 'Queued';
    deck.document.status = 'GeneratingImages';
    return { data: { message: 'بدأ التصميم' } };
  });
  render(<ContentDocuments canManage brandReady aiReady onDraftDirtyChange={() => {}} />);
  const body = await screen.findByLabelText('نص الصفحة 2');
  fireEvent.change(body, { target: { value: 'تعديل لم أحفظه بعد' } });
  fireEvent.click(screen.getByRole('button', { name: 'تحريك السلايد 2 لأعلى' }));

  await waitFor(() => expect(screen.getByLabelText('نص الصفحة 1')).toHaveValue('تعديل لم أحفظه بعد'));
  expect(screen.getByLabelText('نص الصفحة 2')).toHaveValue('محتوى 1');
  expect(screen.getByRole('button', { name: 'تحريك السلايد 1 لأعلى' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'تحريك السلايد 2 لأسفل' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'أعد تصميم الكل' })).toBeDisabled();
  fireEvent.click(within(screen.getByRole('region', { name: 'السلايد 1' })).getByRole('button', { name: 'حفظ وإعادة التصميم' }));
  await waitFor(() => expect(deck.pages[0].status).toBe('Queued'));
  expect(deck.pages[0].body).toBe('تعديل لم أحفظه بعد');
  expect(deck.pages[1].body).toBe('محتوى 1');
});

it('moves the last slide directly to the beginning without reversing the others', async () => {
  const deck = mockFinishedDeck();
  deck.pages.push({ id: 'existing-2', pageIndex: 2, title: 'عنوان 3', body: 'محتوى 3', status: 'Ready' });
  deck.document.requestedPageCount = 3;
  vi.spyOn(api, 'put').mockImplementation(async (url, payload) => {
    expect(url).toBe('/api/content/documents/finished/pages/order');
    const { pageIds } = payload as { pageIds: string[] };
    expect(pageIds).toEqual(['existing-2', 'existing-0', 'existing-1']);
    deck.pages = pageIds.map((id, pageIndex) => ({ ...deck.pages.find(page => page.id === id)!, pageIndex }));
    return { data: {} };
  });
  render(<ContentDocuments canManage brandReady aiReady onDraftDirtyChange={() => {}} />);
  fireEvent.change(await screen.findByLabelText('نص الصفحة 3'), { target: { value: 'تعديل قبل النقل' } });
  fireEvent.click(screen.getByRole('button', { name: 'نقل السلايد 3 لأول العرض' }));
  await waitFor(() => expect(screen.getByLabelText('نص الصفحة 1')).toHaveValue('تعديل قبل النقل'));
  expect(screen.getByLabelText('نص الصفحة 2')).toHaveValue('محتوى 1');
  expect(screen.getByLabelText('نص الصفحة 3')).toHaveValue('محتوى 2');
  expect(screen.getByRole('button', { name: 'نقل السلايد 1 لأول العرض' })).toBeDisabled();
});

it('keeps the saved order visible after a rejected reorder', async () => {
  mockFinishedDeck();
  vi.spyOn(api, 'put').mockRejectedValue(new Error('تعذر حفظ الترتيب'));
  render(<ContentDocuments canManage brandReady aiReady onDraftDirtyChange={() => {}} />);
  fireEvent.click(await screen.findByRole('button', { name: 'تحريك السلايد 2 لأعلى' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('تعذر حفظ الترتيب');
  await waitFor(() => expect(screen.getByRole('button', { name: 'تحريك السلايد 2 لأعلى' })).toBeEnabled());
  expect(screen.getByLabelText('نص الصفحة 1')).toHaveValue('محتوى 1');
  expect(screen.getByLabelText('نص الصفحة 2')).toHaveValue('محتوى 2');
});

it('designs only the selected slide in a split session and shows a queued state instead of an endless spinner', async () => {
  const document = { id: 'session-5', title: 'Session 5', kind: 'Presentation', status: 'AwaitingDesign', requestedPageCount: 2 };
  const pages = [1, 2].map(index => ({ id: `slide-${index}`, pageIndex: index - 1, title: '', body: `Topic ${index}`, status: 'Planned', imageUrl: null }));
  vi.mocked(api.get).mockImplementation(async url => {
    if (url === '/api/content/documents') return { data: { documents: [{ ...document }] } };
    if (url === '/api/content/documents/session-5') return { data: { document: { ...document }, pages: pages.map(page => ({ ...page })) } };
    throw new Error(`Unexpected GET: ${url}`);
  });
  const post = vi.spyOn(api, 'post').mockImplementation(async url => {
    if (url !== '/api/content/documents/session-5/pages/slide-2/regenerate-image') throw new Error(`Unexpected POST: ${url}`);
    document.status = 'GeneratingImages';
    pages[1].status = 'Queued';
    return { data: { message: 'تمت جدولة تصميم السلايد 2.' } };
  });
  render(<ContentDocuments canManage brandReady aiReady onDraftDirtyChange={() => {}} />);

  const selectedSlide = await screen.findByRole('region', { name: 'السلايد 2' });
  expect(within(selectedSlide).getByText('جاهز للتصميم')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'PPTX' })).toBeDisabled();
  fireEvent.click(within(selectedSlide).getByRole('button', { name: 'تصميم السلايد' }));

  await within(selectedSlide).findByText('في انتظار بدء التصميم');
  expect(post).toHaveBeenCalledTimes(1);
  expect(within(screen.getByRole('region', { name: 'السلايد 1' })).getByText('جاهز للتصميم')).toBeInTheDocument();
  expect(within(selectedSlide).getByRole('button', { name: 'تصميم السلايد' })).toBeDisabled();
});

function mockPosts() {
  return vi.spyOn(api, 'post').mockImplementation(async url => {
    if (url.endsWith('/suggest-page-count')) return { data: countResponse };
    if (url.endsWith('/preview')) return { data: previewResponse };
    if (url === '/api/content/documents') return { data: { id: 'document-new', status: 'Planning', message: 'بدأ التصميم بالمحتوى الكامل.' } };
    throw new Error(`Unexpected POST: ${url}`);
  });
}

it.each([['عرض تقديمي', 'Presentation'], ['مستند A4', 'A4']])('previews exact page text before approving generation for %s', async (label, kind) => {
  const post = mockPosts();
  render(<ContentDocuments canManage brandReady aiReady onDraftDirtyChange={() => {}} />);
  fireEvent.click(screen.getByRole('radio', { name: new RegExp(label) }));
  fireEvent.change(screen.getByRole('textbox', { name: /المحتوى الكامل/ }), { target: { value: source } });
  const preview = screen.getByRole('button', { name: 'اعرض التقسيم' });
  expect(preview).toBeDisabled();
  await waitFor(() => expect(preview).toBeEnabled());
  expect(screen.getByLabelText('العدد المقترح')).toHaveTextContent('شامل الغلاف التعريفي');
  expect(screen.getByRole('spinbutton')).toHaveValue(3);
  fireEvent.click(preview);
  const approve = await screen.findByRole('button', { name: 'اعتمد التقسيم وابدأ التصميم' });
  const body1 = within(screen.getByRole('region', { name: 'معاينة الصفحة 2' })).getByText((_, node) => node?.textContent === source.slice(0, 100), { selector: 'p' });
  const body2 = within(screen.getByRole('region', { name: 'معاينة الصفحة 3' })).getByText((_, node) => node?.textContent === source.slice(100), { selector: 'p' });
  expect(body1.textContent! + body2.textContent!).toBe(source);
  expect(post.mock.calls.filter(([url]) => url === '/api/content/documents')).toHaveLength(0);
  fireEvent.click(approve);
  await screen.findByText('بدأ التصميم بالمحتوى الكامل.');
  expect(post).toHaveBeenCalledWith('/api/content/documents', { kind, content: source, pageCount: 3, coverTitle: '', previewFingerprint: 'reviewed-draft' });
});

it('invalidates approval when count, source, or cover changes and sends the chosen count for a new preview', async () => {
  const post = mockPosts();
  render(<ContentDocuments canManage brandReady aiReady onDraftDirtyChange={() => {}} />);
  fireEvent.change(screen.getByRole('textbox', { name: /المحتوى الكامل/ }), { target: { value: source } });
  const preview = screen.getByRole('button', { name: 'اعرض التقسيم' });
  await waitFor(() => expect(preview).toBeEnabled());
  for (const [role, name, value] of [
    ['spinbutton', /شامل الغلاف/, '5'],
    ['textbox', /عنوان الغلاف/, 'Session 1'],
    ['textbox', /المحتوى الكامل/, `${source}معلومة جديدة`],
  ] as const) {
    fireEvent.click(preview);
    await screen.findByRole('button', { name: 'اعتمد التقسيم وابدأ التصميم' });
    fireEvent.change(screen.getByRole(role, { name }), { target: { value } });
    expect(screen.queryByRole('button', { name: 'اعتمد التقسيم وابدأ التصميم' })).not.toBeInTheDocument();
    await waitFor(() => expect(preview).toBeEnabled());
  }
  expect(post).toHaveBeenCalledWith('/api/content/documents/preview', { kind: 'Presentation', content: source, pageCount: 5, coverTitle: 'Session 1' }, expect.objectContaining({ timeout: 60_000 }));
  expect(post.mock.calls.filter(([url]) => url === '/api/content/documents')).toHaveLength(0);
});

it('ignores an in-flight preview after edits and allows AI preview without a brand logo', async () => {
  let resolvePreview!: (value: { data: typeof previewResponse }) => void;
  vi.spyOn(api, 'post').mockImplementation(async url => {
    if (url.endsWith('/suggest-page-count')) return { data: countResponse };
    return await new Promise<{ data: typeof previewResponse }>(resolve => { resolvePreview = resolve; });
  });
  render(<ContentDocuments canManage brandReady={false} aiReady onDraftDirtyChange={() => {}} />);
  fireEvent.change(screen.getByRole('textbox', { name: /المحتوى الكامل/ }), { target: { value: source } });
  const preview = screen.getByRole('button', { name: 'اعرض التقسيم' });
  await waitFor(() => expect(preview).toBeEnabled());
  fireEvent.click(preview);
  fireEvent.change(screen.getByRole('spinbutton'), { target: { value: '4' } });
  resolvePreview({ data: previewResponse });
  await waitFor(() => expect(preview).toBeEnabled());
  expect(screen.queryByRole('button', { name: 'اعتمد التقسيم وابدأ التصميم' })).not.toBeInTheDocument();
  fireEvent.click(preview);
  resolvePreview({ data: previewResponse });
  expect(await screen.findByRole('button', { name: 'اعتمد التقسيم وابدأ التصميم' })).toBeDisabled();
});

it('lets the user retry a failed suggestion and rejects invalid counts', async () => {
  let failSuggestion = true;
  vi.spyOn(api, 'post').mockImplementation(async () => {
    if (failSuggestion) throw new Error('تعذر الاتصال');
    return { data: countResponse };
  });
  render(<ContentDocuments canManage brandReady aiReady onDraftDirtyChange={() => {}} />);
  fireEvent.change(screen.getByRole('textbox', { name: /المحتوى الكامل/ }), { target: { value: source } });
  const preview = screen.getByRole('button', { name: 'اعرض التقسيم' });
  await screen.findByText('تعذر الاتصال');
  expect(preview).toBeDisabled();
  failSuggestion = false;
  fireEvent.click(screen.getByRole('button', { name: 'أعد اقتراح العدد' }));
  await waitFor(() => expect(preview).toBeEnabled());
  for (const value of ['', '1', '9', '3.5']) {
    fireEvent.change(screen.getByRole('spinbutton'), { target: { value } });
    expect(preview).toBeDisabled();
  }
});

it('renders AI groups as source headings and bullet lists before approval', async () => {
  const heading = 'مهارات التواصل';
  const points = ['الاستماع باهتمام لكل تفاصيل العميل قبل الرد.', 'التأكد من فهم السؤال وتقديم إجابة واضحة ومتكاملة.'];
  vi.spyOn(api, 'post').mockImplementation(async url => ({ data: url.endsWith('/suggest-page-count') ? countResponse : {
    ...previewResponse,
    pages: [previewResponse.pages[0], { pageIndex: 1, title: '', body: [heading, ...points].join('\n'), blocks: [
      { type: 'heading', items: [heading] }, { type: 'bullets', items: points },
    ] }],
  } }));
  render(<ContentDocuments canManage brandReady aiReady onDraftDirtyChange={() => {}} />);
  fireEvent.change(screen.getByRole('textbox', { name: /المحتوى الكامل/ }), { target: { value: [heading, ...points].join('\n') } });
  await waitFor(() => expect(screen.getByRole('button', { name: 'اعرض التقسيم' })).toBeEnabled());
  fireEvent.click(screen.getByRole('button', { name: 'اعرض التقسيم' }));
  expect(await screen.findByRole('heading', { name: heading })).toBeInTheDocument();
  expect(screen.getAllByRole('listitem').map(item => item.textContent)).toEqual(points);
});

it('requires an AI key for semantic preview', async () => {
  mockPosts();
  render(<ContentDocuments canManage brandReady aiReady={false} onDraftDirtyChange={() => {}} />);
  fireEvent.change(screen.getByRole('textbox', { name: /المحتوى الكامل/ }), { target: { value: source } });
  await waitFor(() => expect(screen.getByRole('spinbutton')).toHaveValue(3));
  expect(screen.getByRole('button', { name: 'اعرض التقسيم' })).toBeDisabled();
});

const sessionSections = [
  { section: 0, title: 'Session 11', pageCount: 2, minPageCount: 1, maxPageCount: 20 },
  { section: 1, title: 'سيشن ١٢', pageCount: 1, minPageCount: 1, maxPageCount: 20 },
];
const sessionSource = 'مقدمة الدورة\nSession 11\nشرح السيشن الأول كاملًا مع أمثلته\nسيشن ١٢\nشرح السيشن الثاني كاملًا مع أمثلته';
const sessionPreview: import('../types').ContentDocumentPreview = { title: 'Session 11', pageCount: 8, pages: [], fingerprint: 'approved-files',
  documents: sessionSections.map(section => ({ section: section.section, title: section.title, pageCount: 4,
    pages: Array.from({ length: 4 }, (_, pageIndex) => ({ pageIndex, title: pageIndex === 0 ? section.title : '',
      body: pageIndex === 0 ? '' : `تفاصيل ${section.title} جزء ${pageIndex}`,
      blocks: pageIndex === 0 ? [] : [{ type: 'paragraph' as const, items: [`تفاصيل ${section.title} جزء ${pageIndex}`] }],
    })),
  })),
};

it('applies one count to all session files, previews each named cover and opens the independent saved files', async () => {
  let saved = false;
  const documents = sessionSections.map(section => ({ id: `session-${section.section}`, title: section.title, kind: 'Presentation', status: 'Planning', requestedPageCount: 4 }));
  vi.mocked(api.get).mockImplementation(async url => {
    if (url === '/api/content/documents') return { data: { documents: saved ? documents : [] } };
    const index = documents.findIndex(document => url.endsWith(`/${document.id}`));
    if (index < 0) throw new Error(`Unexpected GET: ${url}`);
    return { data: { document: documents[index], pages: sessionPreview.documents![index].pages.map(page => ({ ...page, id: `page-${index}-${page.pageIndex}`, status: 'Planned' })) } };
  });
  const post = vi.spyOn(api, 'post').mockImplementation(async url => {
    if (url.endsWith('/suggest-page-count')) return { data: { ...countResponse, sessionCount: 2,
      sections: [{ section: 0, title: 'المقدمة', pageCount: 1, minPageCount: 1, maxPageCount: 1 }, ...sessionSections], fileSections: sessionSections } };
    if (url.endsWith('/preview')) return { data: sessionPreview };
    saved = true;
    return { data: { id: documents[0].id, ids: documents.map(document => document.id), message: 'بدأ تصميم ملفين مستقلين.' } };
  });
  render(<ContentDocuments canManage brandReady aiReady onDraftDirtyChange={() => {}} />);
  fireEvent.change(screen.getByRole('textbox', { name: /المحتوى الكامل/ }), { target: { value: sessionSource } });
  const count = await screen.findByRole('spinbutton', { name: 'عدد السلايدات لكل سيشن شامل الغلاف' });
  expect(screen.getAllByRole('spinbutton')).toHaveLength(1);
  expect(screen.queryByRole('textbox', { name: /عنوان الغلاف/ })).not.toBeInTheDocument();
  fireEvent.change(count, { target: { value: '4' } });
  fireEvent.click(screen.getByRole('button', { name: 'اعرض التقسيم' }));
  const approve = await screen.findByRole('button', { name: 'اعتمد التقسيم وابدأ التصميم' });
  expect(screen.getByRole('heading', { name: '2 ملفات مستقلة، 8 سلايد شامل الأغلفة' })).toBeInTheDocument();
  for (const section of sessionSections) {
    const file = screen.getByRole('region', { name: `معاينة ملف ${section.title}` });
    expect(within(within(file).getByRole('region', { name: 'معاينة الصفحة 1' })).getByRole('heading')).toHaveTextContent(section.title);
    expect(within(file).getAllByRole('region', { name: /معاينة الصفحة/ })).toHaveLength(4);
    expect(within(file).getByText(`تفاصيل ${section.title} جزء 3`)).toBeInTheDocument();
  }
  const request = { kind: 'Presentation', content: sessionSource, pageCount: 0, coverTitle: '', pagesPerSession: 4 };
  expect(post).toHaveBeenCalledWith('/api/content/documents/preview', request, expect.any(Object));
  fireEvent.click(approve);
  await screen.findByText('بدأ تصميم ملفين مستقلين.');
  expect(post).toHaveBeenCalledWith('/api/content/documents', { ...request, previewFingerprint: 'approved-files' });
  const history = screen.getByRole('complementary', { name: 'الملفات السابقة' });
  fireEvent.click(await within(history).findByRole('button', { name: /سيشن ١٢/ }));
  await waitFor(() => expect(screen.getByLabelText('عنوان الصفحة 1')).toHaveValue('سيشن ١٢'));
  expect(screen.getByLabelText('نص الصفحة 2')).toHaveValue('تفاصيل سيشن ١٢ جزء 1');
});

it('invalidates all file previews when the shared count changes and rejects empty or out-of-range counts', async () => {
  let resolvePreview!: (value: { data: typeof sessionPreview }) => void;
  vi.spyOn(api, 'post').mockImplementation(async url => {
    if (url.endsWith('/suggest-page-count')) return { data: { ...countResponse, sessionCount: 2, fileSections: sessionSections } };
    return new Promise<{ data: typeof sessionPreview }>(resolve => { resolvePreview = resolve; });
  });
  render(<ContentDocuments canManage brandReady aiReady onDraftDirtyChange={() => {}} />);
  const content = screen.getByRole('textbox', { name: /المحتوى الكامل/ });
  fireEvent.change(content, { target: { value: sessionSource } });
  const count = await screen.findByRole('spinbutton', { name: 'عدد السلايدات لكل سيشن شامل الغلاف' });
  const preview = screen.getByRole('button', { name: 'اعرض التقسيم' });
  fireEvent.click(preview);
  fireEvent.change(count, { target: { value: '4' } });
  resolvePreview({ data: sessionPreview });
  await waitFor(() => expect(preview).toBeEnabled());
  expect(screen.queryByRole('button', { name: 'اعتمد التقسيم وابدأ التصميم' })).not.toBeInTheDocument();
  fireEvent.click(preview);
  resolvePreview({ data: sessionPreview });
  await screen.findByRole('button', { name: 'اعتمد التقسيم وابدأ التصميم' });
  for (const value of ['1', '', '22', '2.5']) {
    fireEvent.change(count, { target: { value } });
    expect(preview).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'اعتمد التقسيم وابدأ التصميم' })).not.toBeInTheDocument();
  }
  fireEvent.change(content, { target: { value: `${sessionSource}محتوى جديد` } });
  await waitFor(() => expect(screen.getByRole('spinbutton', { name: 'عدد السلايدات لكل سيشن شامل الغلاف' })).toHaveValue(3));
  expect(preview).toBeEnabled();
});
