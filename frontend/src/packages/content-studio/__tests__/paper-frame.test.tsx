import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { api } from '../../../services/api';
import ContentDocuments from '../ContentDocuments';
import PaperFrameDesigner from '../PaperFrameDesigner';

it('requires an approved frame, sends its token with the lesson and invalidates approval when the design changes', async () => {
  const source = 'شرح الدرس كاملًا مع أمثلة وتطبيقات للطلاب. '.repeat(5);
  const created: unknown[] = [];
  vi.spyOn(api, 'get').mockImplementation(async url => {
    if (url === '/api/content/papers/options') return { data: {
      styles: [{ id: 'ribbons', name: 'شرائط', description: 'شرح وملاحظات' }],
      palettes: [{ id: 'mint', name: 'نعناع', colors: ['#347568', '#F2C34E', '#F8F5E9'] }],
    } };
    if (url === '/api/content/documents') return { data: { documents: [] } };
    if (url === '/api/content/documents/paper-new') return { data: {
      document: { id: 'paper-new', kind: 'Paper', status: 'Planning', title: 'درس', requestedPageCount: 2 }, pages: [],
    } };
    throw new Error(`Unexpected GET ${url}`);
  });
  vi.spyOn(api, 'post').mockImplementation(async (url, payload) => {
    if (url === '/api/content/papers/frame') {
      expect((payload as FormData).get('includeTeacher')).toBe('false');
      expect((payload as FormData).has('teacher')).toBe(false);
      return { data: { token: 'approved-frame', image: 'data:image/png;base64,CQgH' } };
    }
    if (url.endsWith('suggest-page-count')) return { data: { pageCount: 2, minPageCount: 2, maxPageCount: 5 } };
    if (url.endsWith('/preview')) return { data: { title: 'درس', pageCount: 2, fingerprint: 'outline', pages: [
      { pageIndex: 0, title: 'درس', body: '', blocks: [] },
      { pageIndex: 1, title: '', body: source, blocks: [{ type: 'paragraph', items: [source] }] },
    ] } };
    if (url === '/api/content/documents') { created.push(payload); return { data: { id: 'paper-new', message: 'بدأ التصميم' } }; }
    throw new Error(`Unexpected POST ${url}`);
  });
  render(<ContentDocuments paperMode canManage aiReady brandReady={false} onDraftDirtyChange={() => {}} />);
  fireEvent.change(screen.getByRole('textbox', { name: /^المحتوى الكامل/ }), { target: { value: source } });
  expect(screen.getByRole('button', { name: 'اعرض التقسيم' })).toBeDisabled();
  fireEvent.click(screen.getByLabelText('بدون مرجع جاهز'));
  await screen.findByRole('radio', { name: /شرائط.*شرح وملاحظات/ });
  fireEvent.click(screen.getByRole('button', { name: 'صمّم الفريم' }));
  fireEvent.click(await screen.findByRole('button', { name: 'اعتمد الفريم واستخدمه للورق' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'اعرض التقسيم' })).toBeEnabled());
  fireEvent.click(screen.getByLabelText('بصورة المدرّس'));
  expect(screen.getByRole('button', { name: 'اعرض التقسيم' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'صمّم الفريم' })).toBeDisabled();
  fireEvent.click(screen.getByLabelText('بدون صورة'));
  fireEvent.click(screen.getByRole('button', { name: 'صمّم الفريم' }));
  fireEvent.click(await screen.findByRole('button', { name: 'اعتمد الفريم واستخدمه للورق' }));
  fireEvent.click(screen.getByRole('button', { name: 'اعرض التقسيم' }));
  fireEvent.click(await screen.findByRole('button', { name: 'اعتمد التقسيم وابدأ التصميم' }));
  await screen.findByText('بدأ التصميم');
  expect(created).toEqual([expect.objectContaining({ kind: 'Paper', content: source, paperFrameToken: 'approved-frame', previewFingerprint: 'outline' })]);
});

it('uses ChatGPT for the frame, clears the submitted key and reuses the saved key for the next frame', async () => {
  vi.spyOn(api, 'get').mockResolvedValue({ data: {
    styles: [{ id: 'ribbons', name: 'شرائط', description: 'شرح' }],
    palettes: [{ id: 'mint', name: 'نعناع', colors: ['#347568'] }],
    openAiImageApiKeyConfigured: false,
  } });
  const requests: FormData[] = [];
  vi.spyOn(api, 'post').mockImplementation(async (url, payload) => {
    if (url === '/api/content/papers/preview') return { data: { image: 'data:image/png;base64,CQgH', model: 'gpt-image-2.5-flare', size: '768x1024' } };
    requests.push(payload as FormData);
    return { data: { token: 'openai-frame', image: 'data:image/png;base64,CQgH', imageProvider: 'OpenAI', openAiImageApiKeyConfigured: true } };
  });
  render(<PaperFrameDesigner canManage aiReady={false} locked={false} onFrameChange={() => {}} onDirtyChange={() => {}} />);
  fireEvent.change(screen.getByRole('combobox', { name: /^موديل صور الورق/ }), { target: { value: 'gpt-image-2.5-flare' } });
  fireEvent.click(screen.getByLabelText('بدون مرجع جاهز'));
  await screen.findByRole('radio', { name: /شرائط.*شرح/ });
  expect(screen.getByRole('button', { name: 'صمّم الفريم' })).toBeDisabled();
  fireEvent.change(screen.getByLabelText(/^مفتاح OpenAI للصور/), { target: { value: 'test-paper-key' } });
  fireEvent.click(screen.getByRole('button', { name: 'صمّم الفريم' }));
  await screen.findByRole('button', { name: 'اعتمد الفريم واستخدمه للورق' });
  expect(screen.getByLabelText(/^مفتاح OpenAI للصور/)).toHaveValue('');
  expect(requests[0].get('imageProvider')).toBe('OpenAI');
  expect(requests[0].get('openAiImageModel')).toBe('gpt-image-2.5-flare');
  expect(requests[0].get('openAiImageApiKey')).toBe('test-paper-key');
  fireEvent.click(screen.getByRole('button', { name: 'جرّب فريم تاني بنفس الاختيارات' }));
  await screen.findByRole('button', { name: 'اعتمد الفريم واستخدمه للورق' });
  expect(requests[1].get('imageProvider')).toBe('OpenAI');
  expect(requests[1].has('openAiImageApiKey')).toBe(false);
  fireEvent.click(screen.getByRole('button', { name: 'اعتمد الفريم واستخدمه للورق' }));
  const trial = screen.getByRole('region', { name: 'جرب ورقة واحدة' });
  fireEvent.change(within(trial).getByRole('textbox'), { target: { value: 'شرح المضارع البسيط مع مثالين' } });
  fireEvent.click(within(trial).getByRole('button', { name: 'جرّب صورة واحدة' }));
  expect(await within(trial).findByRole('link', { name: 'تنزيل الصورة' })).toHaveAttribute('download', 'trial-gpt-image-2.5-flare.png');
  expect(api.post).toHaveBeenCalledWith('/api/content/papers/preview', {
    frameToken: 'openai-frame', content: 'شرح المضارع البسيط مع مثالين',
  }, expect.objectContaining({ signal: expect.any(AbortSignal) }));
  expect(requests).toHaveLength(2);
});
