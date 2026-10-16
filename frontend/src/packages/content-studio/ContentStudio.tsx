'use client';

import PageHeader from '../../components/ui/PageHeader';
import Tabs from '../../components/ui/Tabs';
import Image from 'next/image';
import { useCallback, useEffect, useRef, useState } from 'react';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import {
  AlertTriangle,
  BookOpen,
  FileText,
  CalendarClock,
  Check,
  CheckCircle2,
  Clock3,
  ChevronLeft,
  ChevronRight,
  Film,
  Gamepad2,
  ImagePlus,
  LoaderCircle,
  Palette,
  RefreshCw,
  Send,
  Share2,
  ShieldCheck,
  Sparkles,
  Trash2,
  Upload,
  X,
  ZoomIn,
} from 'lucide-react';
import { useAuth } from '../../context/auth-context';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import Dialog from '../../components/shared/Dialog';
import { useUnsavedNavigationGuard } from '../../hooks/use-unsaved-navigation-guard';
import { contentApi } from './content-api';
import ContentVideos from './ContentVideos';
import ContentDocuments from './ContentDocuments';
import ImageModelSelect from './ImageModelSelect';
import SingleImageTrial from './SingleImageTrial';
import ContentCardGames from './ContentCardGames';
import type { ContentPost, ContentPostStatus, ContentStudioData, ContentWeekPlan, ContentWeekPlanItem, UpdateContentSettings } from './types';
import styles from './ContentStudio.module.css';

const statusLabels: Record<ContentPostStatus, string> = {
  Generating: 'بيتجهّز دلوقتي',
  AwaitingApproval: 'مستني رأيك',
  Approved: 'جاهز للنشر',
  Publishing: 'بيتنشر على Facebook',
  Published: 'اتنشر',
  GenerationFailed: 'التوليد وقف',
  PublishFailed: 'النشر محتاج إعادة',
  Rejected: 'مرفوض',
  PublishUnknown: 'نتيجة النشر غير معروفة',
};

const activeStatuses: ContentPostStatus[] = ['Generating', 'Publishing'];
const contentTabs = [
  { key: 'posts', label: 'الصور والمنشورات', icon: <ImagePlus size={17} aria-hidden="true" /> },
  { key: 'videos', label: 'الفيديوهات', icon: <Film size={17} aria-hidden="true" /> },
  { key: 'paper', label: 'ورق', icon: <FileText size={17} aria-hidden="true" /> },
  { key: 'documents', label: 'العروض والمستندات', icon: <BookOpen size={17} aria-hidden="true" /> },
  { key: 'games', label: 'الألعاب والكروت', icon: <Gamepad2 size={17} aria-hidden="true" /> },
] as const;
type ContentView = (typeof contentTabs)[number]['key'];
const postTasks = [
  { key: 'review', label: 'المراجعة والمكتبة', icon: <CheckCircle2 size={17} aria-hidden="true" /> },
  { key: 'create', label: 'إنشاء محتوى', icon: <Sparkles size={17} aria-hidden="true" /> },
  { key: 'schedule', label: 'جدول النشر', icon: <CalendarClock size={17} aria-hidden="true" /> },
  { key: 'brand', label: 'هوية المحتوى', icon: <Palette size={17} aria-hidden="true" /> },
] as const;
type PostTask = (typeof postTasks)[number]['key'];

interface ConfirmationState {
  title: string;
  message: string;
  confirmLabel: string;
  onConfirm: () => void;
}

export default function ContentStudio() {
  const { activeProject } = useAuth();
  return <ContentStudioProjectView key={activeProject?.id ?? 'no-active-project'} />;
}

function ContentStudioProjectView() {
  const { activeProject, user } = useAuth();
  const pathname = usePathname();
  const router = useRouter();
  const searchParams = useSearchParams();
  const requestedView = searchParams.get('view');
  const activeView: ContentView = requestedView === 'paper' || requestedView === 'videos' || requestedView === 'documents' || requestedView === 'games' ? requestedView : 'posts';
  const requestedTask = searchParams.get('task');
  const activeTask: PostTask = requestedTask === 'create' || requestedTask === 'schedule' || requestedTask === 'brand' ? requestedTask : 'review';
  const [trialDirty, setTrialDirty] = useState(false);
  const [studioData, setStudioData] = useState<ContentStudioData | null>(null);
  const [form, setForm] = useState<UpdateContentSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<string | null>(null);
  const [polling, setPolling] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [formDirty, setFormDirty] = useState(false);
  const [videoDraftDirty, setVideoDraftDirty] = useState(false);
  const [documentDraftDirty, setDocumentDraftDirty] = useState(false);
  const [gameDraftDirty, setGameDraftDirty] = useState(false);
  const [confirmation, setConfirmation] = useState<ConfirmationState | null>(null);
  const formDirtyRef = useRef(false);
  const logoInput = useRef<HTMLInputElement>(null);
  const paletteReferenceInput = useRef<HTMLInputElement>(null);
  const postReferenceInput = useRef<HTMLInputElement>(null);
  const hasActivePost = Boolean(studioData?.posts.some((post) => activeStatuses.includes(post.status)));
  const polledWeeklyPlans = studioData?.weeklyPlans?.length
    ? studioData.weeklyPlans
    : studioData?.weeklyPlan ? [studioData.weeklyPlan] : [];
  const hasPublishingPlan = polledWeeklyPlans.some((plan) => plan.items.some((item) => item.postStatus === 'Publishing'));
  const hasGeneratingPlan = polledWeeklyPlans.some((plan) => plan.status === 'Generating'
    || plan.items.some((item) => !item.contentPostId || item.postStatus === 'Generating'));
  const navigationGuard = useUnsavedNavigationGuard(formDirty || videoDraftDirty || documentDraftDirty || gameDraftDirty || trialDirty);
  const canManage = user?.role === 'Owner' || user?.role === 'Admin';

  const hydrateForm = useCallback((response: ContentStudioData) => {
    setForm({
      facebookPageId: response.settings.facebookPageId,
      dailyPublishTimeLocal: response.settings.dailyPublishTimeLocal,
      postsPerDay: response.settings.postsPerDay ?? 1,
      stylePrompt: response.settings.stylePrompt,
      isEnabled: response.settings.isEnabled,
      designSource: response.settings.designSource,
      matchReferenceTone: response.settings.matchReferenceTone,
      referenceColors: response.settings.referenceColors,
      imageProvider: response.settings.imageProvider ?? 'Gemini',
      openAiImageModel: response.settings.openAiImageModel ?? 'gpt-image-2.5-sunburst',
    });
    formDirtyRef.current = false;
    setFormDirty(false);
  }, []);

  const updateForm = (next: UpdateContentSettings) => {
    setForm(next);
    formDirtyRef.current = true;
    setFormDirty(true);
  };

  const refresh = useCallback(async (mode: 'foreground' | 'background' = 'foreground') => {
    if (!activeProject?.id) {
      setStudioData(null);
      setForm(null);
      setError(null);
      setLoading(false);
      return;
    }
    if (mode === 'foreground') setLoading(true);
    try {
      const response = await contentApi.get();
      setStudioData(response);
      if (mode === 'foreground' || !formDirtyRef.current) hydrateForm(response);
      setError(null);
    } catch (requestError) {
      setError(errorMessage(requestError));
    } finally {
      if (mode === 'foreground') setLoading(false);
    }
  }, [activeProject?.id, hydrateForm]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      formDirtyRef.current = false;
      setFormDirty(false);
      setLoading(true);
      void refresh();
    }, 0);
    return () => window.clearTimeout(timer);
  }, [activeProject?.id, refresh]);

  useEffect(() => {
    if (!polling && !hasActivePost && !hasPublishingPlan && !hasGeneratingPlan) return;
    const interval = window.setInterval(() => void refresh('background'), 4_000);
    const timeout = window.setTimeout(() => setPolling(false), 60_000);
    return () => { window.clearInterval(interval); window.clearTimeout(timeout); };
  }, [hasActivePost, hasPublishingPlan, hasGeneratingPlan, polling, refresh]);

  const run = async (key: string, action: () => Promise<{ message?: string } | ContentStudioData>) => {
    setBusy(key);
    setNotice(null);
    setError(null);
    try {
      const actionResponse = await action();
      if ('settings' in actionResponse) {
        setStudioData(actionResponse);
        if (key === 'save' || key === 'toggle' || !formDirtyRef.current) hydrateForm(actionResponse);
      } else {
        setNotice(actionResponse.message ?? 'تم تنفيذ الإجراء.');
        setPolling(true);
        await refresh('background');
      }
    } catch (requestError) {
      setError(errorMessage(requestError));
    } finally {
      setBusy(null);
    }
  };

  const uploadLogo = async (file?: File) => {
    if (!file) return;
    await run('logo', () => contentApi.uploadLogo(file));
    if (logoInput.current) logoInput.current.value = '';
  };

  const uploadReference = async (file: File | undefined, kind: 'palette' | 'post') => {
    if (!file) return;
    await run(`reference-${kind}`, async () => {
      const response = await contentApi.uploadBrandReference(file, kind);
      if (kind === 'palette') {
        setForm((current) => current ? { ...current, referenceColors: response.settings.referenceColors } : current);
      }
      return response;
    });
    const input = kind === 'palette' ? paletteReferenceInput.current : postReferenceInput.current;
    if (input) input.value = '';
  };

  const commitContentView = (nextView: ContentView) => {
    const params = new URLSearchParams(searchParams.toString());
    params.set('view', nextView);
    router.replace(`${pathname}?${params.toString()}`, { scroll: false });
  };

  const selectPostTask = (nextTask: PostTask) => {
    const params = new URLSearchParams(searchParams.toString());
    params.set('task', nextTask);
    router.replace(`${pathname}?${params.toString()}`, { scroll: false });
  };

  const selectContentView = (nextView: ContentView) => {
    if (nextView === activeView) return true;
    if (activeView === 'posts' && trialDirty) {
      setConfirmation({
        title: 'مغادرة تجربة الصورة؟',
        message: 'وصف الصورة والمعاينة الحالية لم يتم حفظهما. هل تريد المغادرة؟',
        confirmLabel: 'مغادرة دون حفظ',
        onConfirm: () => { setTrialDirty(false); commitContentView(nextView); },
      });
      return false;
    }
    if (activeView === 'videos' && videoDraftDirty) {
      setConfirmation({
        title: 'مغادرة فكرة الفيديو؟',
        message: 'ستفقد التوجيه والمقاسات التي لم ترسلها بعد. خطط الفيديو المحفوظة لن تتأثر.',
        confirmLabel: 'مغادرة دون حفظ',
        onConfirm: () => { setVideoDraftDirty(false); commitContentView(nextView); },
      });
      return false;
    }
    if ((activeView === 'documents' || activeView === 'paper') && documentDraftDirty) {
      setConfirmation({
        title: 'مغادرة الملف قبل الحفظ؟',
        message: 'ستفقد النص الجديد أو تعديلات الصفحات غير المحفوظة. الملفات المحفوظة لن تتأثر.',
        confirmLabel: 'مغادرة دون حفظ',
        onConfirm: () => { setDocumentDraftDirty(false); commitContentView(nextView); },
      });
      return false;
    }
    if (activeView === 'games' && gameDraftDirty) {
      setConfirmation({
        title: 'مغادرة اللعبة قبل تنفيذها؟',
        message: 'ستفقد وصف اللعبة والتعديلات التي لم تنفذها بعد. الألعاب المحفوظة لن تتأثر.',
        confirmLabel: 'مغادرة دون حفظ',
        onConfirm: () => { setGameDraftDirty(false); commitContentView(nextView); },
      });
      return false;
    }
    commitContentView(nextView);
    return true;
  };

  if (loading && activeView === 'posts') return <ContentSkeleton />;
  if (!activeProject?.id) {
    return (
      <section className={styles.noProject} aria-labelledby="content-no-project-title">
        <Share2 size={30} aria-hidden="true" />
        <h1 id="content-no-project-title">تعذر تحميل مساحة العمل</h1>
        <p>لا توجد مساحة مرتبطة متاحة الآن. أعد تحميل الصفحة أو تواصل مع المدير.</p>
      </section>
    );
  }
  if (activeView === 'videos') {
    return (
      <div className={styles.studio} dir="rtl">
        <div className={styles.pageHeader}>
          <PageHeader icon={<Film size={22} aria-hidden="true" />} title="استوديو المحتوى" description="إعداد مشاهد الفيديو ومراجعتها وتوليدها" />

        </div>
        <ContentTabs activeView={activeView} onChange={selectContentView} />
        <div id="content-panel-videos" role="tabpanel" aria-labelledby="content-tab-videos">
          <ContentVideos key={activeProject.id} projectId={activeProject.id} canManage={canManage} onDraftDirtyChange={setVideoDraftDirty} />
        </div>
        <StudioDialogs confirmation={confirmation} onCloseConfirmation={() => setConfirmation(null)} navigationGuard={navigationGuard} />
      </div>
    );
  }
  if (activeView === 'documents' || activeView === 'paper') {
    if (loading || !studioData) return <ContentSkeleton />;
    return (
      <div className={styles.studio} dir="rtl">
        <div className={styles.pageHeader}>
          <PageHeader icon={<BookOpen size={22} aria-hidden="true" />} title="استوديو المحتوى" description={activeView === 'paper' ? 'تصميم أوراق الشرح بفريم وهوية موحّدة' : 'إنشاء المستندات والعروض ومراجعتها وتصديرها'} />

        </div>
        <ContentTabs activeView={activeView} onChange={selectContentView} />
        <ContentDocuments key={activeView} paperMode={activeView === 'paper'} canManage={canManage} aiReady={studioData.aiConfigured} brandReady={Boolean(studioData.settings.logoUrl)} onDraftDirtyChange={setDocumentDraftDirty} />
        <StudioDialogs confirmation={confirmation} onCloseConfirmation={() => setConfirmation(null)} navigationGuard={navigationGuard} />
      </div>
    );
  }
  if (activeView === 'games') {
    if (loading || !studioData) return <ContentSkeleton />;
    return (
      <div className={styles.studio} dir="rtl">
        <div className={styles.pageHeader}>
          <PageHeader icon={<Gamepad2 size={22} aria-hidden="true" />} title="استوديو المحتوى" description="تصميم الألعاب والكروت بهوية مشروعك" />

        </div>
        <ContentTabs activeView={activeView} onChange={selectContentView} />
        <ContentCardGames canManage={canManage} aiReady={studioData.aiConfigured} brandReady={Boolean(studioData.settings.logoUrl)} onDraftDirtyChange={setGameDraftDirty} />
        <StudioDialogs confirmation={confirmation} onCloseConfirmation={() => setConfirmation(null)} navigationGuard={navigationGuard} />
      </div>
    );
  }
  if (!studioData || !form) {
    return <div className={styles.blockingError} role="alert"><AlertTriangle size={20} aria-hidden="true" /><span>{error ?? 'تعذر تحميل استوديو المحتوى.'}</span><button type="button" onClick={() => void refresh()}>إعادة المحاولة</button></div>;
  }

  const sample = studioData.settings.hasApprovedStyle
    ? studioData.posts.find((post) => post.id === studioData.settings.approvedSamplePostId)
    : studioData.posts.find((post) => post.isStyleSample
      && ['Generating', 'AwaitingApproval', 'GenerationFailed'].includes(post.status));
  const missingGenerationRequirements = [
    !studioData.aiConfigured && 'مفتاح Gemini من إعدادات المشروع',
    studioData.settings.imageProvider === 'OpenAI' && !studioData.settings.openAiImageApiKeyConfigured && 'مفتاح OpenAI للصور',
    !studioData.settings.logoUrl && 'اللوجو',
    studioData.knowledgeDocumentCount === 0 && 'مستند معتمد في قاعدة المعرفة',
    studioData.settings.designSource === 'references' && studioData.settings.referenceImages.every((image) => image.kind !== 'post') && 'ريفرنس بوست واحد على الأقل',
    formDirty && 'حفظ تعديلات الهوية',
  ].filter(Boolean);
  const canGenerate = missingGenerationRequirements.length === 0;
  const weeklyPlans = studioData.weeklyPlans?.length
    ? studioData.weeklyPlans
    : studioData.weeklyPlan ? [studioData.weeklyPlan] : [];
  const requiredDailySlots = Math.max(form.postsPerDay, ...weeklyPlans
    .filter((plan) => plan.status === 'Approved' || plan.status === 'AwaitingApproval')
    .map((plan) => plan.items.length / 7));
  const latestFirstPostTime = requiredDailySlots >= 3 ? '15:59' : requiredDailySlots >= 2 ? '19:59' : '23:59';
  const pageSaved = !formDirty && form.facebookPageId === studioData.settings.facebookPageId && Boolean(studioData.settings.facebookPageId);
  const isConfigured = Boolean(studioData.settings.logoUrl && studioData.settings.facebookPageId);
  const canControlSchedule = form.isEnabled || weeklyPlans.some((plan) => plan.status === 'Approved');
  const plannedPostIds = new Set(weeklyPlans.flatMap((plan) => plan.items
    .flatMap((item) => item.contentPostId ? [item.contentPostId] : [])));
  const historyPosts = studioData.posts.filter((post) => post.status !== 'Rejected'
    && ((post.isStyleSample && post.status === 'Published')
      || (!post.isStyleSample && (post.status === 'Published' || !plannedPostIds.has(post.id)))));

  const sampleReview = sample && <details className={styles.sampleReviewSection} open={!studioData.settings.hasApprovedStyle || sample.status === 'PublishFailed' || sample.status === 'PublishUnknown'}>
            <summary>{studioData.settings.hasApprovedStyle ? 'التصميم المعتمد' : 'مراجعة أول تصميم'}<span>{statusLabels[sample.status]}</span></summary>
          {sample?.status === 'Generating' && <GeneratingPreview />}

          {sample && sample.status !== 'Generating' && sample.status !== 'GenerationFailed' && (
            <SampleReview
              sample={sample}
              pageSaved={pageSaved}
              busy={busy}
              approved={studioData.settings.hasApprovedStyle}
              canManage={canManage}
              onApprove={() => setConfirmation({
                title: 'اعتماد التصميم وتجهيز خطة الأسبوع؟',
                message: 'سيُنشر أول بوست على صفحة Facebook، ثم يجهّز النظام 7 أفكار وكابشنات من قاعدة المعرفة لتراجعها قبل تشغيل النشر اليومي.',
                confirmLabel: 'اعتماد ونشر',
                onConfirm: () => void run('approve', () => contentApi.approve(sample.id)),
              })}
              onRegenerate={() => void run('regenerate', () => contentApi.regenerate(sample.id))}
              onPublish={() => setConfirmation({
                title: 'إعادة محاولة نشر أول بوست؟',
                message: 'راجع صفحة Facebook أولًا لتجنب نشر نسخة مكررة، ثم أكد إعادة المحاولة.',
                confirmLabel: 'إعادة محاولة النشر',
                onConfirm: () => void run('publish-sample', () => contentApi.publish(sample.id)),
              })}
            />
          )}

          {sample?.status === 'GenerationFailed' && (
            <div className={styles.failedPreview}><AlertTriangle size={28} aria-hidden="true" /><h3>التصميم ماكملش</h3><p>{sample.error}</p><button type="button" className={styles.btnSecondary} disabled={Boolean(busy) || !canManage} onClick={() => void run('regenerate', () => contentApi.regenerate(sample.id))}><RefreshCw size={17} aria-hidden="true" /> جرّب تصميم جديد</button></div>
          )}

            {!pageSaved && <button type="button" className={styles.textButton} onClick={() => selectPostTask('schedule')}>اختيار صفحة النشر وحفظها</button>}
          </details>;

  return (
    <div className={styles.studio} dir="rtl">
      <div className={styles.pageHeader}>
        <PageHeader icon={<ImagePlus size={22} aria-hidden="true" />} title="استوديو المحتوى" actions={<button type="button" className={styles.btnPrimary} onClick={() => selectPostTask('create')}><Sparkles size={17} /> إنشاء محتوى</button>} />
      </div>

      <ContentTabs activeView={activeView} onChange={selectContentView} />

      <div id="content-panel-posts" role="tabpanel" aria-labelledby="content-tab-posts">

      {(notice || error || studioData.settings.lastError) && (
        <div className={error || studioData.settings.lastError ? styles.alertError : styles.alertSuccess} role={error || studioData.settings.lastError ? 'alert' : 'status'} aria-live={error || studioData.settings.lastError ? 'assertive' : 'polite'}>
          {error || studioData.settings.lastError ? <AlertTriangle size={18} aria-hidden="true" /> : <CheckCircle2 size={18} aria-hidden="true" />}
          <span>{error ?? notice ?? studioData.settings.lastError}</span>
        </div>
      )}

      <div className={styles.taskNavigation}>
        <Tabs idPrefix="content-task" label="مهام الصور والمنشورات" tabs={postTasks} selected={activeTask} onChange={selectPostTask} />
      </div>

      {formDirty && <div className={styles.draftBar} role="status">
        <span>تعديلات الهوية أو النشر لم تُحفظ بعد.</span>
        <button type="button" className={styles.btnPrimary} disabled={Boolean(busy) || !canManage} onClick={() => void run('save', () => contentApi.updateSettings(form))}>
          {busy === 'save' ? <LoaderCircle className={styles.spin} size={17} /> : <Check size={17} />} حفظ الإعدادات
        </button>
      </div>}

      <section className={styles.taskPanel} id="content-task-panel-create" role="tabpanel" aria-labelledby="content-task-tab-create" hidden={activeTask !== 'create'}>
        <h2 className={styles.srOnly}>إنشاء محتوى جديد</h2>
        <div className={styles.createLayout}>
          <div className={styles.creationEditor}>
          <SingleImageTrial title="جرب صورة واحدة" promptLabel="وصف الصورة التجريبية" disabled={!canManage || Boolean(busy)}
            ready={form.imageProvider === 'OpenAI' ? Boolean(form.openAiImageApiKey?.trim() || studioData.settings.openAiImageApiKeyConfigured) : studioData.aiConfigured}
            onDirtyChange={setTrialDirty} onBusyChange={running => setBusy(running ? 'single-trial' : null)}
            generate={(prompt, signal) => contentApi.generateSingleImage({ prompt, imageProvider: form.imageProvider ?? 'Gemini',
              openAiImageModel: form.openAiImageModel ?? 'gpt-image-2.5-sunburst', openAiImageApiKey: form.openAiImageApiKey }, signal)} />

            {activeTask === 'create' && <details className={styles.configurationSection}>
              <summary><Sparkles size={17} aria-hidden="true" /> إعدادات توليد الصورة</summary>
              <fieldset className={styles.editableFields} disabled={!canManage || Boolean(busy)}>
              <ImageGenerationSettings form={form} settings={studioData.settings} onChange={updateForm}
                disabled={Boolean(busy) || !canManage || hasActivePost || hasGeneratingPlan} />

              </fieldset>
            </details>}
          </div>
          <aside className={styles.productionStart} aria-labelledby="content-first-design-heading">
            <h3 id="content-first-design-heading"><Send size={18} aria-hidden="true" /> من الصورة إلى النشر</h3>
          {!sample && !studioData.settings.hasApprovedStyle && (
            <div className={styles.emptyPreview}>
              <div className={styles.posterGhost}><span>1:1</span><Sparkles size={42} /></div>
              <h3>جاهز نطلع أول شكل؟</h3>
              <p>{studioData.settings.designSource === 'references' ? 'هنقرأ قاعدة المعرفة، ونبني التصميم بألوانك وريفرنسات البوستات.' : 'هنقرأ قاعدة المعرفة كلها، نختار فكرة حقيقية، ونبني الصورة بألوان اللوجو.'}</p>
              <button type="button" className={styles.btnPrimary} disabled={!canGenerate || Boolean(busy) || !canManage} onClick={() => void run('sample', contentApi.generateSample)}>
                {busy === 'sample' ? <LoaderCircle className={styles.spin} size={18} /> : <Sparkles size={18} />} ولّد أول تصميم
              </button>
              <small>معاينة فقط، لن يُنشر التصميم تلقائيًا ولا تحتاج صفحة Facebook.</small>
              {!canGenerate && <small>المتبقي للتوليد: {missingGenerationRequirements.join('، ')}.</small>}
            </div>
          )}

            {(sample || studioData.settings.hasApprovedStyle) && <div className={styles.productionStatus}>
              <CheckCircle2 size={24} aria-hidden="true" />
              <strong>{studioData.settings.hasApprovedStyle ? 'هوية المنشورات معتمدة' : 'أول تصميم جاهز للمراجعة أو قيد التجهيز'}</strong>
              <p>راجع التصميم وخطط الأسابيع قبل اعتماد النشر.</p>
              <button type="button" className={styles.btnSecondary} onClick={() => selectPostTask('review')}>فتح المراجعة</button>
            </div>}
            {!canGenerate && <button type="button" className={styles.textButton} onClick={() => selectPostTask('brand')}>استكمال هوية المحتوى</button>}
          </aside>
        </div>
      </section>

      {activeTask === 'brand' && <section className={styles.taskPanel} id="content-task-panel-brand" role="tabpanel" aria-labelledby="content-task-tab-brand">
        <div className={styles.taskHeading}><div><h2><Palette size={20} aria-hidden="true" /> هوية المحتوى</h2></div>{studioData.settings.hasApprovedStyle && <span className={styles.approvedMark}><Check size={14} /> معتمدة</span>}</div>
      <details className={styles.readinessDisclosure}><summary><ShieldCheck size={17} aria-hidden="true" /> جاهزية التوليد والنشر</summary>
      <section className={styles.readinessRail} aria-label="جاهزية التوليد والنشر">
        <Readiness icon={<Sparkles size={17} />} label="Gemini API" ready={studioData.aiConfigured} detail={studioData.aiConfigured ? 'مفتاح المشروع جاهز' : 'أضف المفتاح من الإعدادات'} />
        <Readiness icon={<ShieldCheck size={17} />} label="قاعدة المعرفة" ready={studioData.knowledgeDocumentCount > 0} detail={`${studioData.knowledgeDocumentCount} مستند معتمد`} />
        <Readiness icon={<Palette size={17} />} label="هوية البراند" ready={Boolean(studioData.settings.logoUrl)} detail={studioData.settings.logoFileName ?? 'ارفع اللوجو الأصلي'} />
        <div className={styles.readinessItem}><span><Share2 size={17} aria-hidden="true" /></span><div><strong>Facebook للنشر فقط</strong><small>{studioData.settings.facebookPageName ?? 'اختياري لتجربة التوليد'}</small></div></div>
      </section>
      </details>

        <fieldset className={styles.editableFields} disabled={!canManage || Boolean(busy)}>
          <div className={styles.brandLayout}>
            <div className={styles.brandIdentity}>
          <div className={styles.logoSection}>
            <button type="button" className={styles.logoPreview} aria-label="رفع لوجو المشروع من جهازك" disabled={Boolean(busy)} onClick={() => logoInput.current?.click()}>
              {studioData.settings.logoUrl ? (
                <AuthenticatedImage assetUrl={studioData.settings.logoUrl} alt="اللوجو الأصلي للمشروع" sizes="180px" />
              ) : <ImagePlus size={30} aria-hidden="true" />}
            </button>
            <div>
              <strong>{studioData.settings.logoFileName ?? 'ارفع اللوجو'}</strong>
              <p>هنبعت الملف الأصلي مع كل تصميم، نعزل خلفيته، ونظبط لون أجزائه الفاتحة أو الغامقة للوضوح من غير تغيير شكله.</p>
              <input ref={logoInput} className={styles.hiddenInput} type="file" accept="image/png,image/jpeg,image/webp" aria-label="اختيار ملف شعار المشروع" onChange={(event) => void uploadLogo(event.target.files?.[0])} />
              <button type="button" className={styles.btnPrimary} disabled={Boolean(busy)} onClick={() => logoInput.current?.click()}>
                {busy === 'logo' ? <LoaderCircle className={styles.spin} size={16} /> : <Upload size={16} />}
                {studioData.settings.logoUrl ? 'تغيير اللوجو' : 'رفع اللوجو من جهازك'}
              </button>
              <p>PNG أو JPG أو WebP، حتى 10 ميجابايت.</p>
            </div>
          </div>

          <fieldset className={styles.designSource}>
            <legend>مصدر شكل البوستات</legend>
            <label className={form.designSource === 'logo' ? styles.sourceSelected : styles.sourceOption}>
              <input type="radio" name="design-source" checked={form.designSource === 'logo'} onChange={() => updateForm({ ...form, designSource: 'logo', isEnabled: false })} />
              <span><strong>من اللوجو تلقائيًا</strong><small>الألوان المستخرجة والشكل الحالي.</small></span>
            </label>
            <label className={form.designSource === 'references' ? styles.sourceSelected : styles.sourceOption}>
              <input type="radio" name="design-source" checked={form.designSource === 'references'} onChange={() => updateForm({
                ...form,
                designSource: 'references',
                referenceColors: form.referenceColors.length ? form.referenceColors : (studioData.settings.brandColors.length ? studioData.settings.brandColors.slice(0, 6) : ['#0B0B0B']),
                isEnabled: false,
              })} />
              <span><strong>من البرزنتيشن وريفرنسات البوستات</strong><small>ألوان تحددها أنت وصور يستلهم منها التصميم.</small></span>
            </label>
          </fieldset>

          {form.designSource === 'logo' && studioData.settings.brandColors.length > 0 && (
            <div className={styles.paletteRow} aria-label="ألوان مستخرجة من اللوجو">
              <span>ألوان اللوجو</span>
              <div>{studioData.settings.brandColors.map((color) => <i key={color} style={{ backgroundColor: color }} title={color} />)}</div>
            </div>
          )}

          {form.designSource === 'references' && <div className={styles.referenceSetup}>
            <fieldset className={styles.designSource}>
              <legend>درجة الالتزام بالريفرنس</legend>
              <label className={!form.matchReferenceTone ? styles.sourceSelected : styles.sourceOption}>
                <input type="radio" name="reference-tone" checked={!form.matchReferenceTone} onChange={() => updateForm({ ...form, matchReferenceTone: false, isEnabled: false })} />
                <span><strong>استلهام حر</strong><small>يستخدم ألوان وريفرنسات البراند مع مساحة لفكرة بصرية جديدة.</small></span>
              </label>
              <label className={form.matchReferenceTone ? styles.sourceSelected : styles.sourceOption}>
                <input type="radio" name="reference-tone" checked={form.matchReferenceTone} onChange={() => updateForm({ ...form, matchReferenceTone: true, isEnabled: false })} />
                <span><strong>مطابقة تون الريفرنس</strong><small>يتبع نوع الصور والخلفيات والتباين وتوزيع الألوان والتايبوجرافي عن قرب.</small></span>
              </label>
            </fieldset>
            <div className={styles.referenceHeading}><strong>لوحة ألوان البراند</strong><small>ارفع صورة اللوحة لاستخراج الألوان تلقائيًا، أو اكتب أكواد HEX وعدّلها بنفسك.</small></div>
            <div className={styles.referenceColors}>
              {form.referenceColors.map((color, index) => <div className={styles.referenceColor} key={index}>
                <input type="color" aria-label={`اختيار اللون ${index + 1}`} value={/^#[0-9a-fA-F]{6}$/.test(color) ? color : '#000000'} onChange={(event) => updateForm({ ...form, referenceColors: form.referenceColors.map((item, itemIndex) => itemIndex === index ? event.target.value.toUpperCase() : item), isEnabled: false })} />
                <input type="text" dir="ltr" aria-label={`كود اللون ${index + 1}`} maxLength={7} value={color} onChange={(event) => updateForm({ ...form, referenceColors: form.referenceColors.map((item, itemIndex) => itemIndex === index ? event.target.value : item), isEnabled: false })} />
                <button type="button" aria-label={`حذف اللون ${index + 1}`} onClick={() => updateForm({ ...form, referenceColors: form.referenceColors.filter((_, itemIndex) => itemIndex !== index), isEnabled: false })}><Trash2 size={15} /></button>
              </div>)}
            </div>
            {form.referenceColors.length < 8 && <button type="button" className={styles.textButton} onClick={() => updateForm({ ...form, referenceColors: [...form.referenceColors, '#FFFFFF'], isEnabled: false })}>+ أضف لونًا</button>}

            <div className={styles.referenceHeading}><strong>صورة لوحة الألوان</strong><small>هنقرأ أكواد الألوان الظاهرة في الصورة ونملأ الخانات فوق.</small></div>
            <input ref={paletteReferenceInput} className={styles.hiddenInput} type="file" accept="image/png,image/jpeg,image/webp" aria-label="اختيار صورة لوحة الألوان" onChange={(event) => void uploadReference(event.target.files?.[0], 'palette')} />
            <button type="button" className={styles.btnSecondary} disabled={Boolean(busy)} onClick={() => paletteReferenceInput.current?.click()}><Upload size={15} /> {busy === 'reference-palette' ? 'جارٍ الرفع...' : 'ارفع صورة الألوان'}</button>

            <div className={styles.referenceHeading}><strong>ريفرنسات البوستات</strong><small>ارفع من 1 إلى 4 صور. سنأخذ منها أسلوب التكوين والخطوط، بدون نسخ كلامها.</small></div>
            <input ref={postReferenceInput} className={styles.hiddenInput} type="file" accept="image/png,image/jpeg,image/webp" aria-label="اختيار صورة بوست مرجعي" onChange={(event) => void uploadReference(event.target.files?.[0], 'post')} />
            <button type="button" className={styles.btnSecondary} disabled={Boolean(busy) || studioData.settings.referenceImages.filter((image) => image.kind === 'post').length >= 4} onClick={() => postReferenceInput.current?.click()}><ImagePlus size={15} /> {busy === 'reference-post' ? 'جارٍ الرفع...' : 'أضف ريفرنس بوست'}</button>
            <div className={styles.referenceGrid}>
              {studioData.settings.referenceImages.map((reference) => <div className={styles.referenceCard} key={reference.id}>
                <div className={styles.referenceThumb}><AuthenticatedImage assetUrl={reference.url} alt={reference.kind === 'palette' ? 'لوحة الألوان المرجعية' : 'بوست مرجعي'} sizes="96px" /></div>
                <span title={reference.fileName}>{reference.kind === 'palette' ? 'لوحة الألوان' : reference.fileName}</span>
                <button type="button" aria-label={`حذف ${reference.fileName}`} disabled={Boolean(busy)} onClick={() => void run(`delete-reference-${reference.id}`, () => contentApi.deleteBrandReference(reference.id))}><Trash2 size={15} /></button>
              </div>)}
            </div>
            <small>PNG أو JPG أو WebP، حتى 5 ميجابايت للصورة. تغيير الريفرنسات بعد الاعتماد يحتاج معاينة جديدة.</small>
          </div>}

            </div>
            <div className={styles.brandDirection}>
          <label className={styles.field}>
            <span>شكل التصميم</span>
            <textarea rows={5} maxLength={1500} value={form.stylePrompt} onChange={(event) => updateForm({ ...form, stylePrompt: event.target.value, isEnabled: false })} />
            {form.designSource === 'references' && form.matchReferenceTone && <small>في وضع مطابقة التون، صور البوستات المرجعية هي اللي بتحدد شكل التصميم حتى لو الوصف المكتوب مختلف.</small>}
            {studioData.settings.hasApprovedStyle && <small>تغيير الشكل يوقف الجدول لحد ما تعتمد معاينة جديدة.</small>}
            <small>{form.stylePrompt.length} من 1500 حرف</small>
          </label>

              <details className={styles.configurationSection}>
                <summary><Sparkles size={17} aria-hidden="true" /> مزود الصور ومفتاح التوليد</summary>
              <ImageGenerationSettings form={form} settings={studioData.settings} onChange={updateForm}
                disabled={Boolean(busy) || !canManage || hasActivePost || hasGeneratingPlan} />

                <div className={styles.modelDetails}>{studioData.imageModel} · {studioData.imageSize} · {studioData.aspectRatio}</div>
              </details>
            </div>
          </div>
        </fieldset>
        {!canManage && <p className={styles.contextNote}>تعديل الهوية متاح لمالك المشروع والمدير.</p>}
      </section>}

      {(activeTask === 'review' || activeTask === 'schedule') && <section className={styles.taskPanel} id={`content-task-panel-${activeTask}`} role="tabpanel" aria-labelledby={`content-task-tab-${activeTask}`}>
        <h2 className={styles.srOnly}>{activeTask === 'review' ? 'المحتوى المطلوب مراجعته' : 'جدول النشر'}</h2>

        {activeTask === 'schedule' && <>
          <NextPublishStatus settings={studioData.settings} weeklyPlan={weeklyPlans.find((plan) => plan.status === 'Approved') ?? studioData.weeklyPlan} />
          <div className={styles.scheduleSummary}>
            <span>{studioData.settings.facebookPageName ?? 'لم تُحدد صفحة النشر'} · {studioData.settings.isEnabled ? 'النشر اليومي مفعّل' : 'النشر اليومي متوقف'}</span>
            {studioData.settings.hasApprovedStyle && canControlSchedule && (
              <button type="button" className={form.isEnabled ? styles.btnPause : styles.btnPrimary} disabled={Boolean(busy) || !canManage || !isConfigured || formDirty} onClick={() => setConfirmation({
                title: form.isEnabled ? 'إيقاف النشر اليومي؟' : 'تشغيل النشر اليومي؟',
                message: form.isEnabled ? 'سيتوقف نشر البوستات المجدولة حتى تعيد تشغيل الجدول.' : `سيعود نشر الخطط المعتمدة في مواعيدها الظاهرة. الخطط الجديدة ستكون ${form.postsPerDay} بوست في اليوم، بدءًا من ${form.dailyPublishTimeLocal} حسب توقيت ${studioData.settings.timezone}.`,
                confirmLabel: form.isEnabled ? 'إيقاف الجدول' : 'تشغيل الجدول',
                onConfirm: () => void run('toggle', () => contentApi.updateSettings({ ...form, isEnabled: !form.isEnabled })),
              })}>
                {form.isEnabled ? 'وقّف النشر اليومي' : 'شغّل النشر اليومي'}
              </button>
            )}

          </div>
          <details className={styles.configurationSection} open={!studioData.settings.facebookPageId}>
            <summary><CalendarClock size={17} aria-hidden="true" /> إعدادات صفحة النشر والمواعيد</summary>
            <fieldset className={`${styles.editableFields} ${styles.scheduleFields}`} disabled={!canManage || Boolean(busy)}>
          <label className={styles.field}>
            <span>صفحة النشر (اختيارية للتوليد)</span>
            <select value={form.facebookPageId ?? ''} onChange={(event) => updateForm({ ...form, facebookPageId: event.target.value || undefined })}>
              <option value="">بدون صفحة، توليد وتجربة فقط</option>
              {studioData.connectedPages.map((page) => <option value={page.pageId} key={page.pageId}>{page.pageName}</option>)}
            </select>
            <small>تقدر تولّد وتراجع التصميم بدون صفحة. اختار صفحة لاحقًا عند النشر أو الجدولة.</small>
          </label>

          <label className={styles.field}>
            <span>ميعاد أول بوست في اليوم</span>
            <div className={styles.timeField}><Clock3 size={17} aria-hidden="true" /><input type="time" value={form.dailyPublishTimeLocal} onChange={(event) => updateForm({ ...form, dailyPublishTimeLocal: event.target.value, isEnabled: false })} /></div>
            <small>حسب توقيت {studioData.settings.timezone}</small>
          </label>

          <label className={styles.field}>
            <span>عدد البوستات في اليوم</span>
            <select value={form.postsPerDay} onChange={(event) => updateForm({ ...form, postsPerDay: Number(event.target.value) })}>
              <option value={1}>بوست واحد</option>
              <option value={2}>بوستين</option>
              <option value={3}>3 بوستات</option>
            </select>
            <small>لو أكتر من بوست، بينهم 4 ساعات. آخر موعد لأول بوست: {latestFirstPostTime}. العدد الجديد يطبق على الخطط اللي تجهزها بعد الحفظ.</small>
          </label>

            </fieldset>
          </details>
        </>}

        {activeTask === 'review' && <>
          {!sample && !studioData.settings.hasApprovedStyle && <div className={styles.emptyPlan}>
            <ImagePlus size={28} aria-hidden="true" /><div><h3>ابدأ بأول تصميم</h3><p>جهّز الهوية وأنشئ أول صورة لتراجعها هنا قبل النشر.</p></div>
            <button type="button" className={styles.btnSecondary} onClick={() => selectPostTask('create')}>إنشاء أول تصميم</button>
          </div>}
          {!studioData.settings.hasApprovedStyle && sampleReview}

        </>}
      {(activeTask === 'schedule' || studioData.settings.lastPublishedAtUtc || weeklyPlans.length > 0) && <WeeklyPlan
        key={activeTask}
        view={activeTask === 'schedule' ? 'schedule' : 'review'}
        canManage={canManage}
        plans={weeklyPlans}
        canPublish={canManage && pageSaved}
        onPublish={(id) => void run(`publish-${id}`, () => contentApi.publish(id))}
        postsPerDay={studioData.settings.postsPerDay}
        firstPostPublished={Boolean(studioData.settings.lastPublishedAtUtc)}
        busy={busy}
        onGenerate={() => void run('weekly-plan', contentApi.generateWeeklyPlan)}
        onApprove={(planId) => setConfirmation({
          title: 'اعتماد خطة الأسبوع؟',
          message: 'سيبدأ النشر التلقائي للصور والأفكار التي وافقت عليها في المواعيد الظاهرة أمامك.',
          confirmLabel: 'اعتمد وابدأ النشر',
          onConfirm: () => void run('approve-weekly-plan', () => contentApi.approveWeeklyPlan(planId)),
        })}
        onApproveItem={(planId, itemId) => void run(`approve-weekly-item-${itemId}`, () => contentApi.approveWeeklyPlanItem(planId, itemId))}
        onRegenerateItem={(planId, itemId) => void run(`regenerate-weekly-item-${itemId}`, () => contentApi.regenerateWeeklyPlanItem(planId, itemId))}
        onRegenerate={(planId) => setConfirmation({
          title: 'تجهيز خطة أسبوع بديلة؟',
          message: `سيتم رفض الخطة الحالية وإنشاء ${7 * studioData.settings.postsPerDay} أفكار وكابشنات جديدة من قاعدة المعرفة.`,
          confirmLabel: 'جهّز خطة بديلة',
          onConfirm: () => void run('regenerate-weekly-plan', () => contentApi.regenerateWeeklyPlan(planId)),
        })}
      />}

        {activeTask === 'review' && <>
          {studioData.settings.hasApprovedStyle && sampleReview}
      <ContentHistory key={activeProject.id} posts={historyPosts} busy={busy} canPublish={canManage && pageSaved} onPublish={(id) => setConfirmation({
        title: 'نشر هذا البوست الآن؟',
        message: 'سيُرسل التصميم والكابشن إلى صفحة Facebook المحفوظة. راجع المحتوى قبل المتابعة.',
        confirmLabel: 'نشر الآن',
        onConfirm: () => void run(`publish-${id}`, () => contentApi.publish(id)),
      })} />

        </>}
      </section>}
      </div>
      <StudioDialogs confirmation={confirmation} onCloseConfirmation={() => setConfirmation(null)} navigationGuard={navigationGuard} />
    </div>
  );
}

function ImageGenerationSettings({ form, settings, disabled, onChange }: {
  form: UpdateContentSettings;
  settings: ContentStudioData['settings'];
  disabled: boolean;
  onChange: (next: UpdateContentSettings) => void;
}) {
  return <>
          <ImageModelSelect label="موديل صور البوستات" provider={form.imageProvider ?? 'Gemini'} model={form.openAiImageModel}
            disabled={disabled} className={styles.field}
            onChange={selection => onChange({ ...form, ...selection })} />
          <small>التجربة تستخدم الاختيار الحالي فورًا. احفظ الإعدادات لتطبيقه على البوستات الجديدة.</small>
          {form.imageProvider === 'OpenAI' && <>
            <label className={styles.field}>
              <span>مفتاح OpenAI للصور</span>
              <input type="password" autoComplete="new-password" dir="ltr" value={form.openAiImageApiKey ?? ''}
                disabled={disabled}
                placeholder={settings.openAiImageApiKeyConfigured ? 'المفتاح محفوظ. اكتب مفتاحًا جديدًا لاستبداله' : 'أدخل مفتاح OpenAI API'}
                onChange={(event) => onChange({ ...form, openAiImageApiKey: event.target.value, clearOpenAiImageApiKey: false })} />
              <small>{form.clearOpenAiImageApiKey ? 'سيتم مسح المفتاح عند الحفظ. اختر Gemini لاستكمال الحفظ.'
                : settings.openAiImageApiKeyConfigured ? 'المفتاح محفوظ ومشفّر.' : 'يحتاج مفتاح API برصيد وصلاحية لموديل الصور. Gemini يجهّز الأفكار والكابشنات.'}</small>
            </label>
            {settings.openAiImageApiKeyConfigured && <button type="button" className={styles.textButton}
              disabled={disabled || Boolean(form.openAiImageApiKey)}
              onClick={() => onChange({ ...form, clearOpenAiImageApiKey: !form.clearOpenAiImageApiKey })}>
              {form.clearOpenAiImageApiKey ? 'إلغاء مسح المفتاح' : 'مسح مفتاح OpenAI عند الحفظ'}
            </button>}
          </>}

  </>;
}

function ContentTabs({ activeView, onChange }: { activeView: ContentView; onChange: (view: ContentView) => boolean }) {
  return <div className={styles.mediaNavigation}><Tabs idPrefix="content" label="أنواع المحتوى" compactOnMobile tabs={contentTabs} selected={activeView} onChange={onChange} /></div>;
}

function StudioDialogs({ confirmation, onCloseConfirmation, navigationGuard }: {
  confirmation: ConfirmationState | null;
  onCloseConfirmation: () => void;
  navigationGuard: ReturnType<typeof useUnsavedNavigationGuard>;
}) {
  const confirmAction = () => {
    const action = confirmation?.onConfirm;
    onCloseConfirmation();
    action?.();
  };
  return <>
    <ConfirmDialog
      isOpen={Boolean(confirmation)}
      title={confirmation?.title ?? ''}
      message={confirmation?.message ?? ''}
      confirmLabel={confirmation?.confirmLabel}
      onCancel={onCloseConfirmation}
      onConfirm={confirmAction}
    />
    <ConfirmDialog
      isOpen={navigationGuard.navigationBlocked}
      title="مغادرة الاستوديو دون حفظ؟"
      message="ستفقد تغييرات المحتوى التي لم تحفظها أو ترسلها بعد."
      confirmLabel="مغادرة دون حفظ"
      onCancel={navigationGuard.cancelNavigation}
      onConfirm={navigationGuard.confirmNavigation}
    />
  </>;
}

function Readiness({ icon, label, ready, detail }: { icon: React.ReactNode; label: string; ready: boolean; detail: string }) {
  return <div className={styles.readinessItem}><span className={ready ? styles.readyIcon : styles.waitIcon}>{ready ? <CheckCircle2 size={17} /> : icon}</span><div><strong>{label}</strong><small>{detail}</small></div></div>;
}

function AuthenticatedImage({ assetUrl, alt, sizes, priority = false }: { assetUrl: string; alt: string; sizes: string; priority?: boolean }) {
  const [loadedAsset, setLoadedAsset] = useState({
    assetUrl,
    imageSource: null as string | null,
    failed: false,
  });

  useEffect(() => {
    const abortController = new AbortController();
    let objectUrl: string | null = null;
    void contentApi.downloadAsset(assetUrl, abortController.signal)
      .then((imageBlob) => {
        if (abortController.signal.aborted) return;
        objectUrl = URL.createObjectURL(imageBlob);
        setLoadedAsset({ assetUrl, imageSource: objectUrl, failed: false });
      })
      .catch(() => {
        if (!abortController.signal.aborted) {
          setLoadedAsset({ assetUrl, imageSource: null, failed: true });
        }
      });

    return () => {
      abortController.abort();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [assetUrl]);

  if (loadedAsset.assetUrl !== assetUrl || !loadedAsset.imageSource) {
    if (loadedAsset.assetUrl === assetUrl && loadedAsset.failed) {
      return <span className={styles.assetState} role="img" aria-label="تعذر تحميل الصورة"><AlertTriangle size={24} /></span>;
    }
    return <span className={styles.assetState} aria-hidden="true"><LoaderCircle className={styles.spin} size={22} /></span>;
  }
  return <Image src={loadedAsset.imageSource} alt={alt} fill sizes={sizes} unoptimized priority={priority} />;
}

function SampleReview({ sample, pageSaved, busy, approved, canManage, onApprove, onRegenerate, onPublish }: { sample: ContentPost; pageSaved: boolean; busy: string | null; approved: boolean; canManage: boolean; onApprove: () => void; onRegenerate: () => void; onPublish: () => void }) {
  return <div className={styles.reviewLayout}>
    <div className={styles.posterFrame}>
      {sample.imageUrl && <AuthenticatedImage assetUrl={sample.imageUrl} alt={`تصميم ${sample.visualHeadline}`} sizes="(max-width: 900px) 90vw, 430px" priority />}
      <span className={styles.resolutionBadge}>{sample.imageSize}</span>
    </div>
    <div className={styles.copyReview}>
      <span className={styles.statusBadge}>{statusLabels[sample.status]}</span>
      <p className={styles.topic}>{sample.topic}</p>
      <h3>{sample.visualHeadline}</h3>
      <p className={styles.caption}>{sample.caption}</p>
      <div className={styles.sourceNote}><ShieldCheck size={17} /><span>اتعمل من {sample.knowledgeDocumentCount} مستند معتمد، واللوجو مأخوذ من الملف الأصلي من غير خلفية وبلون واضح على التصميم.</span></div>
      {!approved && <div className={styles.reviewActions}>
        <button type="button" className={styles.btnPrimary} disabled={Boolean(busy) || !canManage || !pageSaved} onClick={onApprove}>{busy === 'approve' ? <LoaderCircle className={styles.spin} size={18} /> : <Send size={18} />} اعتمد وانشر وجهّز الخطة</button>
        <button type="button" className={styles.btnSecondary} disabled={Boolean(busy) || !canManage} onClick={onRegenerate}>{busy === 'regenerate' ? <LoaderCircle className={styles.spin} size={17} /> : <RefreshCw size={17} />} جرّب شكل تاني</button>
        {!pageSaved && <small>اختار صفحة Facebook واضغط حفظ الإعدادات قبل الاعتماد.</small>}
      </div>}
      {approved && sample.status === 'PublishFailed' && <div className={styles.reviewActions}>
        <button type="button" className={styles.btnPrimary} disabled={Boolean(busy) || !canManage} onClick={onPublish}>{busy === 'publish-sample' ? <LoaderCircle className={styles.spin} size={18} /> : <Send size={18} />} حاول تنشر أول بوست تاني</button>
      </div>}
      {approved && sample.status === 'PublishUnknown' && <div className={styles.sourceNote}><AlertTriangle size={17} /><span>وقفنا الجدول لأن Facebook ما أكدش نتيجة النشر. راجع الصفحة قبل أي محاولة جديدة.</span></div>}
    </div>
  </div>;
}

function NextPublishStatus({ settings, weeklyPlan }: { settings: ContentStudioData['settings']; weeklyPlan?: ContentWeekPlan }) {
  const schedule = nextPublishSchedule(settings, weeklyPlan);
  const isActive = Boolean(settings.isEnabled && settings.nextPublishAtUtc);
  return <div className={`${styles.nextPublishStatus} ${isActive ? styles.nextPublishActive : ''}`} role="status">
    <span className={styles.nextPublishIcon}><CalendarClock size={20} aria-hidden="true" /></span>
    <div className={styles.nextPublishCopy}>
      <span>النشر الجاي</span>
      {isActive ? <time dateTime={settings.nextPublishAtUtc}>{schedule.title}</time> : <strong>{schedule.title}</strong>}
      <small>{schedule.detail}</small>
    </div>
  </div>;
}

function nextPublishSchedule(settings: ContentStudioData['settings'], weeklyPlan?: ContentWeekPlan) {
  const scheduledCount = weeklyPlan?.items.length && (weeklyPlan.status === 'Approved' || weeklyPlan.status === 'AwaitingApproval')
    ? weeklyPlan.items.length / 7
    : settings.postsPerDay;
  const dailyTime = `${scheduledCount} بوست يوميًا، بداية من ${settings.dailyPublishTimeLocal}`;
  const timezone = settings.timezone === 'Africa/Cairo' ? 'القاهرة' : settings.timezone;
  if (settings.isEnabled && settings.nextPublishAtUtc) {
    return { title: formatNextPublishDate(settings.nextPublishAtUtc, settings.timezone), detail: `حسب توقيت ${timezone}` };
  }
  if (!settings.hasApprovedStyle) return { title: 'لسه ما اتحددش', detail: `اعتمد أول تصميم، وبعدها ${dailyTime}` };
  if (!settings.lastPublishedAtUtc) return { title: 'بعد نشر أول بوست', detail: `وبعدها ${dailyTime}` };
  if (weeklyPlan?.status === 'Generating') return { title: 'بنجهّز خطة الأسبوع', detail: `${7 * settings.postsPerDay} أفكار وكابشنات من قاعدة المعرفة` };
  if (weeklyPlan?.status === 'AwaitingApproval') return { title: 'مستني اعتماد الخطة', detail: 'اعتمد الصور من المراجعة قبل بدء النشر' };
  if (weeklyPlan?.status === 'GenerationFailed') return { title: 'الخطة محتاجة إعادة', detail: 'جرّب تجهيز خطة بديلة من المراجعة' };
  return { title: 'مستني خطة معتمدة', detail: `${dailyTime} بعد اعتماد الأيام السبعة` };
}

function WeeklyPlan({ plans, view, canManage, postsPerDay, firstPostPublished, busy, canPublish, onPublish, onGenerate, onApprove, onRegenerate, onApproveItem, onRegenerateItem }: {
  plans: ContentWeekPlan[];
  view: 'review' | 'schedule';
  canManage: boolean;
  canPublish: boolean;
  onPublish: (postId: string) => void;
  postsPerDay: number;
  firstPostPublished: boolean;
  busy: string | null;
  onGenerate: () => void;
  onApprove: (planId: string) => void;
  onRegenerate: (planId: string) => void;
  onApproveItem: (planId: string, itemId: string) => void;
  onRegenerateItem: (planId: string, itemId: string) => void;
}) {
  const knownPlanIds = useRef<Set<string>>(new Set());
  const [selectedPlanId, setSelectedPlanId] = useState<string>();
  useEffect(() => {
    const newDraft = plans.find((candidate) => !knownPlanIds.current.has(candidate.id)
      && ['Generating', 'AwaitingApproval', 'GenerationFailed'].includes(candidate.status));
    const selectedStillExists = plans.some((candidate) => candidate.id === selectedPlanId);
    if (newDraft) setSelectedPlanId(newDraft.id);
    else if (!selectedStillExists) setSelectedPlanId(plans[0]?.id);
    knownPlanIds.current = new Set(plans.map((candidate) => candidate.id));
  }, [plans, selectedPlanId]);

  const visiblePlans = plans.filter((candidate) => view === 'schedule'
    ? candidate.status === 'Approved' || candidate.status === 'Completed' || candidate.status === 'Rejected'
    : candidate.status !== 'Approved' && candidate.status !== 'Completed' && candidate.status !== 'Rejected');
  const plan = visiblePlans.find((candidate) => candidate.id === selectedPlanId) ?? visiblePlans[0];
  const unresolvedPlan = plans.find((candidate) => ['Generating', 'AwaitingApproval', 'GenerationFailed'].includes(candidate.status));
  const canAddWeek = firstPostPublished && !unresolvedPlan;
  const isGenerating = plan?.status === 'Generating';
  const awaitingApproval = plan?.status === 'AwaitingApproval';
  const approved = plan?.status === 'Approved';
  const reviewedCount = plan?.items.filter((item) => item.postStatus === 'Approved' || item.postStatus === 'Published').length ?? 0;
  const allReviewed = Boolean(plan?.items.length && plan.items.length % 7 === 0 && reviewedCount === plan.items.length);
  const plannedCount = plan?.items.length || 7 * postsPerDay;

  return <section className={styles.weeklyPlan} aria-labelledby="weekly-plan-heading">
    <div className={styles.weeklyPlanHeading}>
      <div className={styles.weekTitle}>
        <h2 id="weekly-plan-heading">{view === 'schedule' ? <CalendarClock size={19} aria-hidden="true" /> : <CheckCircle2 size={19} aria-hidden="true" />}{view === 'schedule' ? 'خطط النشر' : 'خطط للمراجعة'}</h2>
        {plan && <span className={`${styles.planStatus} ${approved ? styles.planApproved : plan.status === 'GenerationFailed' ? styles.itemFailed : ''}`}>{awaitingApproval && <Clock3 size={12} aria-hidden="true" />}{weekPlanStatusLabel(plan.status)}</span>}
        {awaitingApproval && <span className={styles.reviewCount}>{reviewedCount} / {plan.items.length} تمت مراجعتها</span>}
        {visiblePlans.length === 1 && <span className={styles.weekPeriod}>{formatWeekRange(plan)}</span>}
      </div>
      <div className={styles.weekPlanHeadingActions}>
        {view === 'review' && firstPostPublished && canAddWeek && <button type="button" className={styles.weekPlanAdd} disabled={!canManage || Boolean(busy)} onClick={onGenerate}>
          {busy === 'weekly-plan' ? <LoaderCircle className={styles.spin} size={16} /> : <Sparkles size={16} />}
          {plans.length > 0 ? 'ضيف أسبوع جديد' : 'جهّز أول أسبوع'}
        </button>}
      </div>
    </div>

    {visiblePlans.length > 1 && <div className={styles.weekTabs} role="group" aria-label="الأسابيع المجهزة">
      {visiblePlans.map((candidate) => <button
        type="button"
        aria-pressed={candidate.id === plan?.id}
        className={candidate.id === plan?.id ? styles.weekTabActive : styles.weekTab}
        key={candidate.id}
        onClick={() => setSelectedPlanId(candidate.id)}
      >
        <span>الأسبوع {plans.indexOf(candidate) + 1}</span>
        <small>{formatWeekRange(candidate)}</small>
      </button>)}
    </div>}

    {!plan && <div className={styles.emptyPlan}>
      <CalendarClock size={30} aria-hidden="true" />
      <div><h3>{view === 'schedule' ? 'لا توجد خطط معتمدة بعد' : firstPostPublished ? 'لا توجد خطط تنتظر المراجعة' : 'الخطة بعد اعتماد أول تصميم'}</h3><p>{view === 'schedule' ? 'اعتمد الصور وخطة الأسبوع من المراجعة لتظهر مواعيدها هنا.' : firstPostPublished ? `يمكنك تجهيز ${plannedCount} أفكار مختلفة من قاعدة المعرفة، ${postsPerDay} في اليوم.` : 'أول بوست يتنشر بعد موافقتك، وبعدها هتظهر هنا الخطة كاملة.'}</p></div>
    </div>}

    {isGenerating && <div className={styles.planGenerating} aria-busy="true">
      <div><LoaderCircle className={styles.spin} size={22} /><strong>{plan.items.length > 0 ? `بنجهّز ${plannedCount} صورة للمراجعة` : `بنقرأ قاعدة المعرفة ونرتب ${plannedCount} بوست على 7 أيام`}</strong></div>
      {plan.items.length === 0 && Array.from({ length: 7 }, (_, index) => <span key={index} />)}
    </div>}

    {plan?.status === 'GenerationFailed' && <div className={styles.planFailed} role="alert">
      <AlertTriangle size={24} /><div><h3>الخطة ماكملتش</h3><p>{plan.error ?? 'تعذر تجهيز خطة الأسبوع.'}</p></div>
      <button type="button" className={styles.btnSecondary} disabled={!canManage || Boolean(busy)} onClick={() => onRegenerate(plan.id)}>{busy === 'regenerate-weekly-plan' ? <LoaderCircle className={styles.spin} size={17} /> : <RefreshCw size={17} />} جرّب خطة جديدة</button>
    </div>}

    {plan?.status === 'Completed' && <div className={styles.emptyPlan}>
      <CheckCircle2 size={28} aria-hidden="true" />
      <div><h3>بوستات الأسبوع اتنشرت</h3><p>تقدر تجهّز أسبوعًا جديدًا من المراجعة والمكتبة.</p></div>
    </div>}

    {plan?.status === 'Rejected' && <div className={styles.emptyPlan}>
      <AlertTriangle size={28} aria-hidden="true" />
      <div><h3>الأسبوع ده اتبدّل</h3><p>جهّز أسبوعًا جديدًا من المراجعة والمكتبة. النظام هيبعد عن الأفكار والعناوين السابقة.</p></div>
    </div>}

    {plan && plan.items.length > 0 && (view === 'review'
      ? <WeeklyPlanReview key={plan.id} plan={plan} busy={busy} canManage={canManage} canPublish={canPublish}
          onPublish={onPublish} onApproveItem={onApproveItem} onRegenerateItem={onRegenerateItem} />
      : <div className={styles.weekList}>{plan.items.map(item => <WeeklyPost key={item.id} item={item} plan={plan} presentation="row"
          busy={busy} canManage={canManage} canPublish={canPublish} onPublish={onPublish}
          onApproveItem={onApproveItem} onRegenerateItem={onRegenerateItem} />)}</div>)}

    {awaitingApproval && <div className={styles.planActions}>
      <div><ShieldCheck size={18} /><span>تمت مراجعة {reviewedCount} من {plan.items.length}. لن يبدأ النشر قبل موافقتك على كل الصور.</span></div>
      <button type="button" className={styles.btnPrimary} disabled={!canManage || Boolean(busy) || !allReviewed} onClick={() => onApprove(plan.id)}>{busy === 'approve-weekly-plan' ? <LoaderCircle className={styles.spin} size={18} /> : <CheckCircle2 size={18} />} {allReviewed ? 'اعتمد بوستات الأسبوع' : 'وافق على الصور الأول'}</button>
      <button type="button" className={styles.btnSecondary} disabled={!canManage || Boolean(busy)} onClick={() => onRegenerate(plan.id)}>{busy === 'regenerate-weekly-plan' ? <LoaderCircle className={styles.spin} size={17} /> : <RefreshCw size={17} />} اعمل خطة غيرها</button>
    </div>}
  </section>;
}

interface WeeklyPostActions {
  plan: ContentWeekPlan;
  busy: string | null;
  canManage: boolean;
  canPublish: boolean;
  onPublish: (postId: string) => void;
  onApproveItem: (planId: string, itemId: string) => void;
  onRegenerateItem: (planId: string, itemId: string) => void;
}

function WeeklyPlanReview(props: WeeklyPostActions) {
  const { plan } = props;
  const [selectedId, setSelectedId] = useState(() => plan.items.find(item => item.postStatus === 'AwaitingApproval')?.id ?? plan.items[0].id);
  const selected = plan.items.find(item => item.id === selectedId)
    ?? plan.items.find(item => item.postStatus === 'AwaitingApproval') ?? plan.items[0];
  const selectedIndex = plan.items.indexOf(selected);
  const selectPost = (id: string) => setSelectedId(id);
  return <div className={styles.postReviewWorkspace}>
    <nav className={styles.postReviewList} aria-label="منشورات الخطة">
      {plan.items.map((item, index) => <button type="button" key={item.id} aria-pressed={selected.id === item.id}
        aria-label={`مراجعة المنشور ${index + 1}: ${item.visualHeadline}`} onClick={() => selectPost(item.id)}>
        <span className={styles.postListThumb}>{item.imageUrl ? <AuthenticatedImage assetUrl={item.imageUrl} alt="" sizes="48px" /> : <ImagePlus size={20} aria-hidden="true" />}</span>
        <span className={styles.postListCopy}><strong>{item.visualHeadline}</strong><small>{formatWeekday(item.scheduledForUtc, plan.timezone)} · {weekItemStatus(item.postStatus, false)}</small></span>
        {item.postStatus === 'Approved' || item.postStatus === 'Published' ? <CheckCircle2 size={16} aria-label="تمت الموافقة" /> : <span className={styles.postListNumber}>{index + 1}</span>}
      </button>)}
    </nav>
    <div className={styles.postReviewDetail}>
      <div className={styles.postReviewControls}>
        <label className={styles.mobilePostSelect}>المنشور<select aria-label="اختيار منشور للمراجعة" value={selected.id} onChange={event => selectPost(event.target.value)}>
          {plan.items.map((item, index) => <option key={item.id} value={item.id}>{index + 1}. {item.visualHeadline}</option>)}
        </select></label>
        <span className={styles.postPosition}>منشور {selectedIndex + 1} من {plan.items.length}</span>
        <div><button type="button" className={styles.iconButton} aria-label="المنشور السابق" disabled={selectedIndex === 0} onClick={() => selectPost(plan.items[selectedIndex - 1].id)}><ChevronRight size={18} aria-hidden="true" /></button>
          <button type="button" className={styles.iconButton} aria-label="المنشور التالي" disabled={selectedIndex === plan.items.length - 1} onClick={() => selectPost(plan.items[selectedIndex + 1].id)}><ChevronLeft size={18} aria-hidden="true" /></button></div>
      </div>
      <section aria-label="معاينة المنشور المختار"><WeeklyPost {...props} item={selected} presentation="detail" /></section>
    </div>
  </div>;
}

function WeeklyPost({ item, plan, presentation, busy, canManage, canPublish, onPublish, onApproveItem, onRegenerateItem }: WeeklyPostActions & { item: ContentWeekPlanItem; presentation: 'detail' | 'row' }) {
  const imageReady = item.postStatus === 'AwaitingApproval';
  const imageApproved = item.postStatus === 'Approved' || item.postStatus === 'Published';
  const imageFailed = item.postStatus === 'GenerationFailed';
  const publishable = plan.status === 'Approved' && Boolean(item.contentPostId)
    && (item.postStatus === 'Approved' || item.postStatus === 'PublishFailed');
  const publishing = busy === `publish-${item.contentPostId}` || item.postStatus === 'Publishing';
  const canRegenerate = plan.status === 'AwaitingApproval' && (imageReady || imageApproved || imageFailed);
  return <article className={presentation === 'detail' ? styles.selectedPost : styles.weekItem} key={item.id}>
    {presentation === 'detail' && item.imageUrl ? <ExpandablePostImage assetUrl={item.imageUrl} title={item.visualHeadline} imageSize={item.imageSize} /> : <div className={styles.weekImage}>
      {item.imageUrl
        ? <AuthenticatedImage assetUrl={item.imageUrl} alt={`تصميم يوم ${formatWeekday(item.scheduledForUtc, plan.timezone)}: ${item.visualHeadline}`} sizes="(max-width: 560px) 96px, 128px" />
        : <span className={styles.weekImageState}>{imageFailed ? <AlertTriangle size={26} /> : <LoaderCircle className={styles.spin} size={24} />}</span>}
      {item.imageSize && <span className={styles.weekResolution}>{item.imageSize}</span>}
    </div>}
    <div className={styles.weekCopy}>
      <div className={styles.weekMeta}>
        <time dateTime={item.scheduledForUtc}><strong>{formatWeekday(item.scheduledForUtc, plan.timezone)}</strong><span>{formatDayAndTime(item.scheduledForUtc, plan.timezone)}</span></time>
        <span className={`${styles.itemState} ${imageApproved ? styles.itemPublished : imageFailed || item.postStatus === 'PublishFailed' || item.postStatus === 'PublishUnknown' ? styles.itemFailed : ''}`}>{imageReady && <Clock3 size={12} aria-hidden="true" />}{publishing ? statusLabels.Publishing : weekItemStatus(item.postStatus, plan.status === 'Approved')}</span>
      </div>
      <span className={styles.postTopic}>{item.topic}</span><h3>{item.visualHeadline}</h3><p>{item.caption}</p>
      {(item.postStatus === 'PublishFailed' || item.postStatus === 'PublishUnknown') && <small className={styles.weekImageError}>{item.postError ?? statusLabels[item.postStatus]}</small>}
      {imageFailed && <small className={styles.weekImageError}>{item.postError ?? 'الصورة ماكملتش. جرّب توليدها مرة ثانية.'}</small>}
      <div className={styles.weekActions}>
      {(publishable || item.postStatus === 'Publishing') && <div className={styles.itemReviewActions}>
        <button type="button" className={styles.btnSecondary} disabled={!canPublish || Boolean(busy) || publishing} onClick={() => onPublish(item.contentPostId!)} aria-label={`نزّل دلوقتي: ${item.visualHeadline}`} title={!canPublish ? 'اختار صفحة Facebook واحفظ الإعدادات بحساب مدير قبل النشر' : undefined}>
          {publishing ? <LoaderCircle className={styles.spin} size={17} /> : <Send size={17} />} {publishing ? 'بيتنشر دلوقتي…' : 'نزّل دلوقتي'}
        </button>
      </div>}
      {plan.status === 'AwaitingApproval' && <div className={styles.itemReviewActions}>
        {imageReady && <button type="button" className={styles.btnPrimary} disabled={!canManage || Boolean(busy)} onClick={() => onApproveItem(plan.id, item.id)}>{busy === `approve-weekly-item-${item.id}` ? <LoaderCircle className={styles.spin} size={17} /> : <CheckCircle2 size={17} />} موافق</button>}
        {canRegenerate && <button type="button" className={styles.btnSecondary} disabled={!canManage || Boolean(busy)} onClick={() => onRegenerateItem(plan.id, item.id)}>{busy === `regenerate-weekly-item-${item.id}` ? <LoaderCircle className={styles.spin} size={17} /> : <RefreshCw size={17} />} إعادة الصورة</button>}
      </div>}
      </div>
    </div>
  </article>;
}

function ExpandablePostImage({ assetUrl, title, imageSize }: { assetUrl: string; title: string; imageSize: string | undefined }) {
  const [expanded, setExpanded] = useState(false);
  const [zoomed, setZoomed] = useState(false);
  return <>
    <button type="button" className={styles.selectedPostImage} aria-label={`تكبير التصميم: ${title}`} onClick={() => { setZoomed(false); setExpanded(true); }}>
      <AuthenticatedImage assetUrl={assetUrl} alt={`تصميم ${title}`} sizes="(max-width: 820px) 90vw, 480px" />
      {imageSize && <small className={styles.weekResolution}>{imageSize}</small>}
      <span><ZoomIn size={16} aria-hidden="true" /> تكبير التصميم</span>
    </button>
    {expanded && <Dialog label={`معاينة التصميم: ${title}`} onClose={() => setExpanded(false)}>
      <div className={styles.imageZoomViewer}>
        <div className={styles.imageZoomHeader}><h2>{title}</h2><button type="button" className={styles.iconButton} aria-label="إغلاق معاينة التصميم" onClick={() => setExpanded(false)}><X size={20} aria-hidden="true" /></button></div>
        <button type="button" className={styles.btnSecondary} aria-pressed={zoomed} onClick={() => setZoomed(!zoomed)}><ZoomIn size={16} aria-hidden="true" />{zoomed ? 'عرض الصورة كاملة' : 'تكبير 200٪'}</button>
        <div className={styles.imageZoomViewport} tabIndex={0} role="region" aria-label="منطقة الصورة المكبرة"><div className={`${styles.imageZoomCanvas} ${zoomed ? styles.imageZoomed : ''}`}>
          <AuthenticatedImage assetUrl={assetUrl} alt={`التصميم مكبر: ${title}`} sizes="90vw" />
        </div></div>
      </div>
    </Dialog>}
  </>;
}

function weekItemStatus(status: ContentPostStatus | undefined, planApproved: boolean) {
  if (status === 'Published') return 'اتنشر';
  if (planApproved && status === 'Approved') return 'مجدول';
  if (status === 'Approved') return 'تمت الموافقة';
  if (status === 'AwaitingApproval') return 'مستنية رأيك';
  if (status === 'GenerationFailed') return 'محتاجة إعادة';
  return status ? statusLabels[status] : 'بتتجهّز';
}

function weekPlanStatusLabel(status: ContentWeekPlan['status']) {
  if (status === 'Generating') return 'بتتجهّز';
  if (status === 'AwaitingApproval') return 'مستنية موافقتك';
  if (status === 'Approved') return 'معتمدة وبتتنفذ';
  if (status === 'Completed') return 'اكتملت';
  if (status === 'GenerationFailed') return 'محتاجة إعادة';
  return 'اتبدلت';
}

function formatWeekRange(plan: ContentWeekPlan) {
  const firstDay = plan.items[0]?.scheduledForUtc;
  const lastDay = plan.items.at(-1)?.scheduledForUtc;
  if (!firstDay || !lastDay) return `يبدأ ${formatPlanDate(plan.startDateLocal)}`;
  const formatter = new Intl.DateTimeFormat('ar-EG', {
    day: 'numeric',
    month: 'short',
    timeZone: plan.timezone,
  });
  return `${formatter.format(new Date(firstDay))} – ${formatter.format(new Date(lastDay))}`;
}

function formatPlanDate(value: string) {
  return new Intl.DateTimeFormat('ar-EG', { day: 'numeric', month: 'short' })
    .format(new Date(`${value}T12:00:00`));
}

function ContentHistory({ posts, busy, canPublish, onPublish }: { posts: ContentPost[]; busy: string | null; canPublish: boolean; onPublish: (id: string) => void }) {
  const [visibleCount, setVisibleCount] = useState(12);
  const visiblePosts = posts.slice(0, visibleCount);

  return <section className={styles.history}>
    <div className={styles.historyHeading}><div><div><h2><BookOpen size={19} aria-hidden="true" /> سجل المحتوى</h2><p>أحدث 40 بوستًا بصورها والكابشن وحالة النشر.</p></div></div><strong>{posts.length} بوست في القائمة</strong></div>
    {posts.length === 0 ? <div className={styles.emptyHistory}>أول بوست معتمد هيظهر هنا.</div> : (
      <div className={styles.historyList}>
        {visiblePosts.map((post) => <article className={styles.historyRow} key={post.id}>
          <div className={styles.historyThumb}>{post.imageUrl ? <AuthenticatedImage assetUrl={post.imageUrl} alt="" sizes="84px" /> : <Sparkles size={22} />}</div>
          <div className={styles.historyCopy}><span>{post.topic || 'محتوى جديد'}</span><strong>{post.visualHeadline || 'جاري تجهيز النص'}</strong><small>{post.publishedAtUtc ? `اتنشر ${formatDate(post.publishedAtUtc)}` : post.generatedAtUtc ? `اتولد ${formatDate(post.generatedAtUtc)}` : 'قيد التنفيذ'}</small></div>
          <span className={`${styles.rowStatus} ${statusClass(post.status)}`}>{statusLabels[post.status]}</span>
          {(post.status === 'Approved' || post.status === 'PublishFailed') && <button type="button" className={styles.iconButton} disabled={Boolean(busy) || !canPublish} onClick={() => onPublish(post.id)} aria-label={`نشر البوست الآن: ${post.visualHeadline || post.topic}`}>{busy === `publish-${post.id}` ? <LoaderCircle className={styles.spin} size={18} /> : <Send size={18} />}</button>}
        </article>)}
        {visibleCount < posts.length && <button type="button" className={styles.loadMoreButton} onClick={() => setVisibleCount((count) => count + 12)}>عرض 12 بوستًا إضافيًا</button>}
      </div>
    )}
  </section>;
}

function GeneratingPreview() {
  return <div className={styles.generating}><div className={styles.generatingPoster}><span /><span /><span /></div><div><LoaderCircle className={styles.spin} size={26} /><h3>بنقرأ المعرفة ونبني التصميم</h3><p>الصورة 4K ممكن تاخد شوية وقت. الصفحة هتتحدّث لوحدها.</p></div></div>;
}

function ContentSkeleton() {
  return <div className={styles.skeleton} aria-busy="true"><span /><span /><div><span /><span /></div></div>;
}

function statusClass(status: ContentPostStatus) {
  if (status === 'Published') return styles.statusPublished;
  if (status === 'GenerationFailed' || status === 'PublishFailed' || status === 'PublishUnknown') return styles.statusFailed;
  if (status === 'Generating' || status === 'Publishing') return styles.statusWorking;
  return styles.statusPending;
}

function formatDate(value?: string, timezone = 'Africa/Cairo') {
  if (!value) return 'لسه ما اتحددش';
  return new Intl.DateTimeFormat('ar-EG', { dateStyle: 'medium', timeStyle: 'short', timeZone: timezone }).format(new Date(value));
}

function formatNextPublishDate(value: string, timezone: string) {
  return new Intl.DateTimeFormat('ar-EG', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    hour: 'numeric',
    minute: '2-digit',
    timeZone: timezone,
  }).format(new Date(value));
}

function formatWeekday(value: string, timezone: string) {
  return new Intl.DateTimeFormat('ar-EG', { weekday: 'long', timeZone: timezone }).format(new Date(value));
}

function formatDayAndTime(value: string, timezone: string) {
  return new Intl.DateTimeFormat('ar-EG', { day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit', timeZone: timezone }).format(new Date(value));
}

function errorMessage(error: unknown) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { error?: string } } }).response;
    if (response?.data?.error) return response.data.error;
  }
  return 'حصلت مشكلة غير متوقعة. جرّب مرة تانية.';
}
