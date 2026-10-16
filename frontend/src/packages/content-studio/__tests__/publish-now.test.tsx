import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterAll, beforeAll, beforeEach, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../../context/auth-context';
import { api } from '../../../services/api';
import ContentStudio from '../ContentStudio';
import type { ContentPostStatus, ContentStudioData } from '../types';

const routing = vi.hoisted(() => ({ search: '', listeners: new Set<() => void>(), replace: vi.fn() }));
vi.mock('next/navigation', async () => {
  const { useSyncExternalStore } = await import('react');
  return {
    usePathname: () => '/management/content',
    useRouter: () => routing,
    useSearchParams: () => new URLSearchParams(useSyncExternalStore(
      listener => { routing.listeners.add(listener); return () => { routing.listeners.delete(listener); }; },
      () => routing.search,
    )),
  };
});

let studio: ContentStudioData;
const dialogMethods = ['showModal', 'close'] as const;
const originalDialogMethods = dialogMethods.map(name => Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, name));
beforeAll(() => {
  // JSDOM omits these native browser methods; keep the real Dialog component under test.
  Object.defineProperties(HTMLDialogElement.prototype, {
    showModal: { configurable: true, value(this: HTMLDialogElement) { this.setAttribute('open', ''); } },
    close: { configurable: true, value(this: HTMLDialogElement) { this.removeAttribute('open'); } },
  });
});
afterAll(() => dialogMethods.forEach((name, index) => {
  const descriptor = originalDialogMethods[index];
  if (descriptor) Object.defineProperty(HTMLDialogElement.prototype, name, descriptor);
  else Reflect.deleteProperty(HTMLDialogElement.prototype, name);
}));
beforeEach(() => {
  routing.replace.mockClear();
  routing.search = '';
  routing.replace.mockImplementation((url: string) => {
    routing.search = new URL(url, 'http://localhost').search;
    routing.listeners.forEach(listener => listener());
  });
  localStorage.clear();
  localStorage.setItem('user', JSON.stringify({ id: 'owner', email: 'owner@example.test', role: 'Owner' }));
  studio = {
    imageModel: 'test', imageSize: '4K', aspectRatio: '1:1', aiConfigured: true, knowledgeDocumentCount: 1,
    connectedPages: [{ pageId: 'page', pageName: 'صفحتنا' }],
    settings: {
      facebookPageId: 'page', isEnabled: true, hasApprovedStyle: true,
      dailyPublishTimeLocal: '10:00', postsPerDay: 1, timezone: 'Africa/Cairo',
      lastPublishedAtUtc: '2026-09-27T07:00:00Z', stylePrompt: '', brandColors: [],
      designSource: 'logo', matchReferenceTone: false, referenceColors: [], referenceImages: [],
    },
    posts: [], weeklyPlans: [{
      id: 'week', status: 'Approved', startDateLocal: '2026-09-28', dailyPublishTimeLocal: '10:00',
      timezone: 'Africa/Cairo', knowledgeDocumentCount: 1,
      items: (['Approved', 'Approved', 'Published', 'PublishUnknown'] satisfies ContentPostStatus[]).map((status, index) => ({
        id: `item-${index}`, contentPostId: `post-${index}`, dayIndex: index,
        scheduledForUtc: `2026-09-${28 + index % 3}T07:00:00Z`, topic: `فكرة ${index}`,
        visualHeadline: `عنوان ${index}`, caption: 'الكابشن', postStatus: status,
      })),
    }],
  };
  const project = { id: 'project', name: 'مشروعنا', settings: null };
  vi.spyOn(api, 'get').mockImplementation(async url => {
    if (url === '/api/projects') return { data: [project] } as never;
    if (url === '/api/projects/project') return { data: project } as never;
    if (url === '/api/content') return { data: studio } as never;
    if (url.startsWith('/api/content/posts/') && url.endsWith('/image')) return { data: new Blob(['test-image'], { type: 'image/png' }) } as never;
    throw new Error(`Unexpected URL: ${url}`);
  });
});

function prepareReviewWeek() {
  const plan = studio.weeklyPlans![0];
  studio = { ...studio, weeklyPlans: [{ ...plan, status: 'AwaitingApproval', items: Array.from({ length: 7 }, (_, index) => ({
    ...plan.items[0], id: `item-${index}`, contentPostId: `post-${index}`, visualHeadline: `عنوان ${index}`,
    caption: `كابشن المنشور ${index}`, postStatus: 'AwaitingApproval', imageUrl: `/api/content/posts/post-${index}/image`,
  })) }] };
  vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test-content-image');
  vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
}

it('approves the selected preview and keeps that post selected after refreshing the week', async () => {
  prepareReviewWeek();
  const post = vi.spyOn(api, 'post').mockImplementation(async () => {
    studio = { ...studio, weeklyPlans: studio.weeklyPlans!.map(plan => ({ ...plan,
      items: plan.items.map(item => item.id === 'item-1' ? { ...item, postStatus: 'Approved' } : item),
    })) };
    return { data: { message: 'تمت الموافقة على صورة اليوم.' } } as never;
  });
  render(<AuthProvider><ContentStudio /></AuthProvider>);
  await userEvent.click(await screen.findByRole('button', { name: 'مراجعة المنشور 2: عنوان 1' }));
  const preview = screen.getByRole('region', { name: 'معاينة المنشور المختار' });
  expect(within(preview).getByRole('heading', { name: 'عنوان 1' })).toBeInTheDocument();
  expect(within(preview).getByText('كابشن المنشور 1')).toBeInTheDocument();
  expect(within(preview).queryByText('كابشن المنشور 0')).not.toBeInTheDocument();
  await userEvent.click(within(preview).getByRole('button', { name: 'موافق' }));
  expect(post).toHaveBeenCalledExactlyOnceWith('/api/content/weekly-plans/week/items/item-1/approve');
  expect(await within(preview).findByText('تمت الموافقة')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'مراجعة المنشور 2: عنوان 1' })).toHaveAttribute('aria-pressed', 'true');
  expect(screen.getByRole('button', { name: 'وافق على الصور الأول' })).toBeDisabled();
});

it('enlarges the selected post and returns to the same review after closing the image', async () => {
  prepareReviewWeek();
  render(<AuthProvider><ContentStudio /></AuthProvider>);
  await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'اختيار منشور للمراجعة' }), 'item-2');
  await userEvent.click(screen.getByRole('button', { name: 'تكبير التصميم: عنوان 2' }));
  const dialog = screen.getByRole('dialog', { name: 'معاينة التصميم: عنوان 2' });
  expect(await within(dialog).findByRole('img', { name: 'التصميم مكبر: عنوان 2' })).toBeInTheDocument();
  await userEvent.click(within(dialog).getByRole('button', { name: 'تكبير 200٪' }));
  expect(within(dialog).getByRole('button', { name: 'عرض الصورة كاملة' })).toHaveAttribute('aria-pressed', 'true');
  await userEvent.tab({ shift: true });
  await userEvent.tab({ shift: true });
  expect(within(dialog).getByRole('region', { name: 'منطقة الصورة المكبرة' })).toHaveFocus();
  await userEvent.click(within(dialog).getByRole('button', { name: 'إغلاق معاينة التصميم' }));
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  expect(within(screen.getByRole('region', { name: 'معاينة المنشور المختار' })).getByText('كابشن المنشور 2')).toBeInTheDocument();
});

it('keeps the selected review available when regenerating its image fails', async () => {
  prepareReviewWeek();
  const post = vi.spyOn(api, 'post').mockRejectedValue({ response: { data: { error: 'تعذر إعادة الصورة.' } } });
  render(<AuthProvider><ContentStudio /></AuthProvider>);
  await userEvent.click(await screen.findByRole('button', { name: 'المنشور التالي' }));
  const preview = screen.getByRole('region', { name: 'معاينة المنشور المختار' });
  await userEvent.click(within(preview).getByRole('button', { name: 'إعادة الصورة' }));
  expect(await screen.findByText('تعذر إعادة الصورة.')).toBeInTheDocument();
  expect(post).toHaveBeenCalledExactlyOnceWith('/api/content/weekly-plans/week/items/item-1/regenerate');
  expect(within(preview).getByText('كابشن المنشور 1')).toBeInTheDocument();
  expect(within(preview).getByRole('button', { name: 'إعادة الصورة' })).toBeEnabled();
});

it('preserves an image trial when tab navigation is cancelled and discards only after confirmation', async () => {
  render(<AuthProvider><ContentStudio /></AuthProvider>);
  await userEvent.click(await screen.findByRole('tab', { name: 'إنشاء محتوى' }));
  const trial = await screen.findByRole('region', { name: 'جرب صورة واحدة' });
  const prompt = within(trial).getByRole('textbox');
  await userEvent.type(prompt, 'وصف الصورة غير المحفوظ');
  await userEvent.click(screen.getByRole('tab', { name: 'هوية المحتوى' }));
  expect(screen.queryByRole('region', { name: 'جرب صورة واحدة' })).not.toBeInTheDocument();
  await userEvent.click(screen.getByRole('tab', { name: 'إنشاء محتوى' }));
  expect(prompt).toHaveValue('وصف الصورة غير المحفوظ');
  routing.replace.mockClear();
  await userEvent.click(screen.getByRole('tab', { name: 'الفيديوهات' }));
  const dialog = await screen.findByRole('dialog', { name: 'مغادرة تجربة الصورة؟' });
  expect(routing.replace).not.toHaveBeenCalled();
  await userEvent.click(within(dialog).getByRole('button', { name: 'إلغاء' }));
  expect(prompt).toHaveValue('وصف الصورة غير المحفوظ');
  await userEvent.click(screen.getByRole('tab', { name: 'الفيديوهات' }));
  await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'مغادرة دون حفظ' }));
  expect(routing.replace).toHaveBeenCalledExactlyOnceWith('/management/content?task=create&view=videos', { scroll: false });
});

it('publishes only the chosen weekly post and shows its publishing state', async () => {
  const post = vi.spyOn(api, 'post').mockImplementation(async () => {
    studio = { ...studio, weeklyPlans: studio.weeklyPlans!.map(plan => ({ ...plan,
      items: plan.items.map(item => item.contentPostId === 'post-1' ? { ...item, postStatus: 'Publishing' } : item),
    })) };
    return { data: { message: 'تمت إضافة المنشور إلى طابور Facebook.' } } as never;
  });
  render(<AuthProvider><ContentStudio /></AuthProvider>);
  await userEvent.click(await screen.findByRole('tab', { name: 'جدول النشر' }));
  await userEvent.click(await screen.findByRole('button', { name: 'نزّل دلوقتي: عنوان 1' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'نزّل دلوقتي: عنوان 1' })).toBeDisabled());
  expect(post).toHaveBeenCalledExactlyOnceWith('/api/content/posts/post-1/publish');
  expect(screen.getByRole('button', { name: 'نزّل دلوقتي: عنوان 1' })).toHaveTextContent('بيتنشر دلوقتي');
  expect(screen.getByRole('button', { name: 'نزّل دلوقتي: عنوان 0' })).toBeEnabled();
  expect(screen.queryByRole('button', { name: 'نزّل دلوقتي: عنوان 2' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'نزّل دلوقتي: عنوان 3' })).not.toBeInTheDocument();
});

it('shows a rejected publish request and leaves the selected post available to retry', async () => {
  vi.spyOn(api, 'post').mockRejectedValue({ response: { data: { error: 'المنشور غير جاهز للنشر.' } } });
  render(<AuthProvider><ContentStudio /></AuthProvider>);
  await userEvent.click(await screen.findByRole('tab', { name: 'جدول النشر' }));
  await userEvent.click(await screen.findByRole('button', { name: 'نزّل دلوقتي: عنوان 1' }));
  expect(await screen.findByText('المنشور غير جاهز للنشر.')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'نزّل دلوقتي: عنوان 1' })).toBeEnabled();
});

it.each(['gpt-image-2.5-sunburst', 'gpt-image-2.5-flare'] as const)('saves the selected model %s and clears the key input after saving', async model => {
  const put = vi.spyOn(api, 'put').mockImplementation(async (_url, payload) => {
    const settings = payload as { imageProvider: 'Gemini' | 'OpenAI'; openAiImageModel: typeof model };
    studio = { ...studio, imageModel: model, imageSize: '2880x2880',
      settings: { ...studio.settings, imageProvider: settings.imageProvider, openAiImageModel: settings.openAiImageModel, openAiImageApiKeyConfigured: true } };
    return { data: studio } as never;
  });
  render(<AuthProvider><ContentStudio /></AuthProvider>);
  await userEvent.click(await screen.findByRole('tab', { name: 'هوية المحتوى' }));
  await userEvent.click(screen.getByText('مزود الصور ومفتاح التوليد'));
  await userEvent.selectOptions(await screen.findByLabelText(/^موديل صور البوستات/), model);
  const keyInput = screen.getByLabelText(/^مفتاح OpenAI للصور/);
  await userEvent.type(keyInput, 'test-image-api-key');
  await userEvent.click(screen.getByRole('button', { name: 'حفظ الإعدادات' }));

  await waitFor(() => expect(keyInput).toHaveValue(''));
  expect(screen.getByLabelText(/^موديل صور البوستات/)).toHaveValue(model);
  expect(screen.getByText('المفتاح محفوظ ومشفّر.')).toBeInTheDocument();
  expect(put).toHaveBeenCalledWith('/api/content/settings', expect.objectContaining({
    imageProvider: 'OpenAI', openAiImageModel: model, openAiImageApiKey: 'test-image-api-key',
  }));
});

it('trials one Flare image without saving settings, generating a week or enabling publishing', async () => {
  studio = { ...studio, knowledgeDocumentCount: 0, weeklyPlans: [], settings: {
    ...studio.settings, isEnabled: false, openAiImageApiKeyConfigured: true,
  } };
  const post = vi.spyOn(api, 'post').mockResolvedValue({ data: {
    image: 'data:image/png;base64,CQgH', model: 'gpt-image-2.5-flare', size: '1024x1024',
  } });
  const put = vi.spyOn(api, 'put');
  render(<AuthProvider><ContentStudio /></AuthProvider>);
  await userEvent.click(await screen.findByRole('tab', { name: 'إنشاء محتوى' }));
  await userEvent.click(screen.getByText('إعدادات توليد الصورة'));
  await userEvent.selectOptions(await screen.findByLabelText(/^موديل صور البوستات/), 'gpt-image-2.5-flare');
  const trial = screen.getByRole('region', { name: 'جرب صورة واحدة' });
  await userEvent.type(within(trial).getByRole('textbox'), 'صورة تعريف بالبراند');
  await userEvent.click(within(trial).getByRole('button', { name: 'جرّب صورة واحدة' }));
  expect(await within(trial).findByRole('link', { name: 'تنزيل الصورة' })).toHaveAttribute('download', 'trial-gpt-image-2.5-flare.png');
  expect(post).toHaveBeenCalledExactlyOnceWith('/api/content/images/preview', expect.objectContaining({
    prompt: 'صورة تعريف بالبراند', imageProvider: 'OpenAI', openAiImageModel: 'gpt-image-2.5-flare',
  }), expect.objectContaining({ signal: expect.any(AbortSignal) }));
  expect(put).not.toHaveBeenCalled();
  expect(studio.settings.isEnabled).toBe(false);
});
