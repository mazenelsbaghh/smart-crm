'use client';

import Image from 'next/image';
import { useCallback, useEffect, useRef, useState } from 'react';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import {
  AlertTriangle,
  CalendarClock,
  Check,
  CheckCircle2,
  Clock3,
  Film,
  ImagePlus,
  LoaderCircle,
  Palette,
  RefreshCw,
  Send,
  Share2,
  ShieldCheck,
  Sparkles,
  Upload,
} from 'lucide-react';
import { useAuth } from '../../context/auth-context';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import { useUnsavedNavigationGuard } from '../../hooks/use-unsaved-navigation-guard';
import { contentApi } from './content-api';
import ContentVideos from './ContentVideos';
import type { ContentPost, ContentPostStatus, ContentStudioData, ContentWeekPlan, UpdateContentSettings } from './types';
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
  { key: 'posts', label: 'الصور والمنشورات' },
  { key: 'videos', label: 'الفيديوهات' },
] as const;
type ContentView = (typeof contentTabs)[number]['key'];

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
  const activeView: ContentView = searchParams.get('view') === 'videos' ? 'videos' : 'posts';
  const [studioData, setStudioData] = useState<ContentStudioData | null>(null);
  const [form, setForm] = useState<UpdateContentSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<string | null>(null);
  const [polling, setPolling] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [formDirty, setFormDirty] = useState(false);
  const [videoDraftDirty, setVideoDraftDirty] = useState(false);
  const [confirmation, setConfirmation] = useState<ConfirmationState | null>(null);
  const formDirtyRef = useRef(false);
  const logoInput = useRef<HTMLInputElement>(null);
  const hasActivePost = Boolean(studioData?.posts.some((post) => activeStatuses.includes(post.status)));
  const polledWeeklyPlans = studioData?.weeklyPlans?.length
    ? studioData.weeklyPlans
    : studioData?.weeklyPlan ? [studioData.weeklyPlan] : [];
  const hasGeneratingPlan = polledWeeklyPlans.some((plan) => plan.status === 'Generating'
    || plan.items.some((item) => !item.contentPostId || item.postStatus === 'Generating'));
  const navigationGuard = useUnsavedNavigationGuard(formDirty || videoDraftDirty);
  const canManage = user?.role === 'Owner' || user?.role === 'Admin';

  const hydrateForm = useCallback((response: ContentStudioData) => {
    setForm({
      facebookPageId: response.settings.facebookPageId,
      dailyPublishTimeLocal: response.settings.dailyPublishTimeLocal,
      stylePrompt: response.settings.stylePrompt,
      isEnabled: response.settings.isEnabled,
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
    if (!polling && !hasActivePost && !hasGeneratingPlan) return;
    const interval = window.setInterval(() => void refresh('background'), 4_000);
    const timeout = window.setTimeout(() => setPolling(false), 60_000);
    return () => { window.clearInterval(interval); window.clearTimeout(timeout); };
  }, [hasActivePost, hasGeneratingPlan, polling, refresh]);

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

  const commitContentView = (nextView: ContentView) => {
    const params = new URLSearchParams(searchParams.toString());
    params.set('view', nextView);
    router.replace(`${pathname}?${params.toString()}`, { scroll: false });
  };

  const selectContentView = (nextView: ContentView) => {
    if (nextView === activeView) return true;
    if (activeView === 'videos' && videoDraftDirty) {
      setConfirmation({
        title: 'مغادرة فكرة الفيديو؟',
        message: 'ستفقد التوجيه والمقاسات التي لم ترسلها بعد. خطط الفيديو المحفوظة لن تتأثر.',
        confirmLabel: 'مغادرة دون حفظ',
        onConfirm: () => { setVideoDraftDirty(false); commitContentView(nextView); },
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
        <header className={styles.pageHeader}>
          <div>
            <p className={styles.eyebrow}>VIDEO STORYBOARD</p>
            <h1>فيديو يبدأ من معرفة مشروعك</h1>
            <p>يقترح الفكرة والافتتاحية، يقسمها لمشاهد تراجعها، ثم يولّد كل مشهد ويحفظ المكتمل حتى لو احتاج غيره إعادة.</p>
          </div>
          <div className={styles.modelStamp} aria-label="توليد الفيديو يحتاج مفتاح Agent Platform مستقل لكل مشروع">
            <Film size={18} />
            <span><strong>Gemini Video</strong>مفتاح مستقل لكل مشروع</span>
          </div>
        </header>
        <ContentTabs activeView={activeView} onChange={selectContentView} />
        <div id="content-panel-videos" role="tabpanel" aria-labelledby="content-tab-videos">
          <ContentVideos key={activeProject.id} projectId={activeProject.id} canManage={canManage} onDraftDirtyChange={setVideoDraftDirty} />
        </div>
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
  const canGenerate = Boolean(studioData.settings.logoUrl && studioData.aiConfigured && studioData.knowledgeDocumentCount > 0);
  const weeklyPlans = studioData.weeklyPlans?.length
    ? studioData.weeklyPlans
    : studioData.weeklyPlan ? [studioData.weeklyPlan] : [];
  const pageSaved = !formDirty && form.facebookPageId === studioData.settings.facebookPageId && Boolean(studioData.settings.facebookPageId);
  const isConfigured = Boolean(studioData.settings.logoUrl && studioData.settings.facebookPageId);
  const canControlSchedule = form.isEnabled || weeklyPlans.some((plan) => plan.status === 'Approved');
  const plannedPostIds = new Set(weeklyPlans.flatMap((plan) => plan.items
    .flatMap((item) => item.contentPostId ? [item.contentPostId] : [])));
  const historyPosts = studioData.posts.filter((post) => post.status !== 'Rejected'
    && ((post.isStyleSample && post.status === 'Published')
      || (!post.isStyleSample && (post.status === 'Published' || !plannedPostIds.has(post.id)))));

  return (
    <div className={styles.studio} dir="rtl">
      <header className={styles.pageHeader}>
        <div>
          <p className={styles.eyebrow}>CONTENT AUTOPILOT</p>
          <h1>محتوى يطلع شبه براندك</h1>
          <p>من قاعدة معرفة المشروع، بهوية ولوجو واضح من غير خلفية، وبعد موافقتك على خطة الأسبوع ينزل كل بوست في يومه.</p>
        </div>
        <div className={styles.modelStamp} aria-label="موديل ودقة توليد الصور">
          <Sparkles size={18} />
          <span><strong>{studioData.imageModel}</strong>{studioData.imageSize} · {studioData.aspectRatio}</span>
        </div>
      </header>

      <ContentTabs activeView={activeView} onChange={selectContentView} />

      <div id="content-panel-posts" role="tabpanel" aria-labelledby="content-tab-posts">

      {(notice || error || studioData.settings.lastError) && (
        <div className={error || studioData.settings.lastError ? styles.alertError : styles.alertSuccess} role={error || studioData.settings.lastError ? 'alert' : 'status'} aria-live={error || studioData.settings.lastError ? 'assertive' : 'polite'}>
          {error || studioData.settings.lastError ? <AlertTriangle size={18} aria-hidden="true" /> : <CheckCircle2 size={18} aria-hidden="true" />}
          <span>{error ?? notice ?? studioData.settings.lastError}</span>
        </div>
      )}

      <section className={styles.readinessRail} aria-label="جاهزية التوليد والنشر">
        <Readiness icon={<Sparkles size={17} />} label="Gemini API" ready={studioData.aiConfigured} detail={studioData.aiConfigured ? 'مفتاح المشروع جاهز' : 'أضف المفتاح من الإعدادات'} />
        <Readiness icon={<ShieldCheck size={17} />} label="قاعدة المعرفة" ready={studioData.knowledgeDocumentCount > 0} detail={`${studioData.knowledgeDocumentCount} مستند معتمد`} />
        <Readiness icon={<Palette size={17} />} label="هوية البراند" ready={Boolean(studioData.settings.logoUrl)} detail={studioData.settings.logoFileName ?? 'ارفع اللوجو الأصلي'} />
        <Readiness icon={<Share2 size={17} />} label="صفحة Facebook" ready={Boolean(studioData.settings.facebookPageId)} detail={studioData.settings.facebookPageName ?? 'اختار الصفحة المتصلة'} />
      </section>

      <div className={styles.workspace}>
        <aside className={styles.settingsPanel} aria-label="إعدادات هوية وجدول المحتوى">
          <div className={styles.panelHeading}>
            <div><span>01</span><h2>الهوية والجدول</h2></div>
            {studioData.settings.hasApprovedStyle && <span className={styles.approvedMark}><Check size={14} /> معتمد</span>}
          </div>

          <div className={styles.logoSection}>
            <div className={styles.logoPreview}>
              {studioData.settings.logoUrl ? (
                <AuthenticatedImage assetUrl={studioData.settings.logoUrl} alt="اللوجو الأصلي للمشروع" sizes="180px" />
              ) : <ImagePlus size={30} />}
            </div>
            <div>
              <strong>{studioData.settings.logoFileName ?? 'ارفع اللوجو'}</strong>
              <p>هنبعت الملف الأصلي مع كل تصميم، نعزل خلفيته، ونظبط لون أجزائه الفاتحة أو الغامقة للوضوح من غير تغيير شكله.</p>
              <input ref={logoInput} className={styles.hiddenInput} type="file" accept="image/png,image/jpeg,image/webp" aria-label="اختيار ملف شعار المشروع" onChange={(event) => void uploadLogo(event.target.files?.[0])} />
              <button type="button" className={styles.textButton} disabled={Boolean(busy)} onClick={() => logoInput.current?.click()}>
                {busy === 'logo' ? <LoaderCircle className={styles.spin} size={16} /> : <Upload size={16} />}
                {studioData.settings.logoUrl ? 'غيّر اللوجو' : 'اختار ملف اللوجو'}
              </button>
            </div>
          </div>

          {studioData.settings.brandColors.length > 0 && (
            <div className={styles.paletteRow} aria-label="ألوان مستخرجة من اللوجو">
              <span>ألوانك</span>
              <div>{studioData.settings.brandColors.map((color) => <i key={color} style={{ backgroundColor: color }} title={color} />)}</div>
            </div>
          )}

          <label className={styles.field}>
            <span>صفحة النشر</span>
            <select value={form.facebookPageId ?? ''} onChange={(event) => updateForm({ ...form, facebookPageId: event.target.value || undefined })}>
              <option value="">اختار صفحة Facebook</option>
              {studioData.connectedPages.map((page) => <option value={page.pageId} key={page.pageId}>{page.pageName}</option>)}
            </select>
          </label>

          <label className={styles.field}>
            <span>ميعاد البوست اليومي</span>
            <div className={styles.timeField}><Clock3 size={17} aria-hidden="true" /><input type="time" value={form.dailyPublishTimeLocal} onChange={(event) => updateForm({ ...form, dailyPublishTimeLocal: event.target.value, isEnabled: false })} /></div>
            <small>حسب توقيت {studioData.settings.timezone}</small>
          </label>

          <label className={styles.field}>
            <span>شكل التصميم</span>
            <textarea rows={5} maxLength={1500} value={form.stylePrompt} onChange={(event) => updateForm({ ...form, stylePrompt: event.target.value, isEnabled: false })} />
            {studioData.settings.hasApprovedStyle && <small>تغيير الشكل يوقف الجدول لحد ما تعتمد معاينة جديدة.</small>}
            <small>{form.stylePrompt.length} من 1500 حرف</small>
          </label>

          {formDirty && <p className={styles.unsavedNotice} role="status">لديك تعديلات غير محفوظة. التحديث التلقائي لن يمسحها.</p>}

          <div className={styles.settingsActions}>
            <button type="button" className={styles.btnSecondary} disabled={Boolean(busy) || !formDirty} onClick={() => void run('save', () => contentApi.updateSettings(form))}>
              {busy === 'save' ? <LoaderCircle className={styles.spin} size={17} /> : <Check size={17} />} حفظ الإعدادات
            </button>
            {studioData.settings.hasApprovedStyle && canControlSchedule && (
              <button type="button" className={form.isEnabled ? styles.btnPause : styles.btnPrimary} disabled={Boolean(busy) || !isConfigured || formDirty} onClick={() => setConfirmation({
                title: form.isEnabled ? 'إيقاف النشر اليومي؟' : 'تشغيل النشر اليومي؟',
                message: form.isEnabled ? 'سيتوقف إنشاء ونشر البوستات اليومية الجديدة حتى تعيد تشغيل الجدول.' : `سيُنشر محتوى يومي على الصفحة المحفوظة في تمام ${form.dailyPublishTimeLocal} حسب توقيت ${studioData.settings.timezone}.`,
                confirmLabel: form.isEnabled ? 'إيقاف الجدول' : 'تشغيل الجدول',
                onConfirm: () => void run('toggle', () => contentApi.updateSettings({ ...form, isEnabled: !form.isEnabled })),
              })}>
                {form.isEnabled ? 'وقّف النشر اليومي' : 'شغّل النشر اليومي'}
              </button>
            )}
          </div>
        </aside>

        <section className={styles.previewStage} aria-labelledby="content-production-heading">
          <div className={styles.stageHeading}>
            <div><span>02</span><div><h2 id="content-production-heading">{studioData.settings.hasApprovedStyle ? 'خط الإنتاج شغّال' : 'اعتماد أول تصميم'}</h2><p>{studioData.settings.hasApprovedStyle ? 'كل بوست جديد يستخدم نفس الهوية واللوجو.' : 'أول صورة مش هتنزل غير لما تقول تمام.'}</p></div></div>
            {studioData.settings.isEnabled && <span className={styles.liveBadge}><i /> نشر يومي</span>}
          </div>
          <NextPublishStatus
            settings={studioData.settings}
            weeklyPlan={weeklyPlans.find((plan) => plan.status === 'Approved') ?? studioData.weeklyPlan}
          />

          {!sample && !studioData.settings.hasApprovedStyle && (
            <div className={styles.emptyPreview}>
              <div className={styles.posterGhost}><span>1:1</span><Sparkles size={42} /></div>
              <h3>جاهز نطلع أول شكل؟</h3>
              <p>هنقرأ قاعدة المعرفة كلها، نختار فكرة حقيقية، ونبني الصورة بألوان اللوجو.</p>
              <button type="button" className={styles.btnPrimary} disabled={!canGenerate || Boolean(busy)} onClick={() => void run('sample', contentApi.generateSample)}>
                {busy === 'sample' ? <LoaderCircle className={styles.spin} size={18} /> : <Sparkles size={18} />} ولّد أول تصميم
              </button>
              {!canGenerate && <small>كمّل مفتاح Gemini، قاعدة المعرفة، واللوجو الأول.</small>}
            </div>
          )}

          {sample?.status === 'Generating' && <GeneratingPreview />}

          {sample && sample.status !== 'Generating' && sample.status !== 'GenerationFailed' && (
            <SampleReview
              sample={sample}
              pageSaved={pageSaved}
              busy={busy}
              approved={studioData.settings.hasApprovedStyle}
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
            <div className={styles.failedPreview}><AlertTriangle size={28} aria-hidden="true" /><h3>التصميم ماكملش</h3><p>{sample.error}</p><button type="button" className={styles.btnSecondary} onClick={() => void run('regenerate', () => contentApi.regenerate(sample.id))}><RefreshCw size={17} aria-hidden="true" /> جرّب تصميم جديد</button></div>
          )}

          {studioData.settings.hasApprovedStyle && !sample && <AutomationSummary studioData={studioData} />}
        </section>
      </div>

      <WeeklyPlan
        plans={weeklyPlans}
        firstPostPublished={Boolean(studioData.settings.lastPublishedAtUtc)}
        busy={busy}
        onGenerate={() => void run('weekly-plan', contentApi.generateWeeklyPlan)}
        onApprove={(planId) => setConfirmation({
          title: 'اعتماد خطة الأسبوع؟',
          message: 'سيبدأ النشر التلقائي للصور والأفكار التي وافقت عليها، بوست واحد كل يوم في الموعد الظاهر أمامك.',
          confirmLabel: 'اعتمد وابدأ النشر',
          onConfirm: () => void run('approve-weekly-plan', () => contentApi.approveWeeklyPlan(planId)),
        })}
        onApproveItem={(planId, itemId) => void run(`approve-weekly-item-${itemId}`, () => contentApi.approveWeeklyPlanItem(planId, itemId))}
        onRegenerateItem={(planId, itemId) => void run(`regenerate-weekly-item-${itemId}`, () => contentApi.regenerateWeeklyPlanItem(planId, itemId))}
        onRegenerate={(planId) => setConfirmation({
          title: 'تجهيز خطة أسبوع بديلة؟',
          message: 'سيتم رفض الخطة الحالية وإنشاء 7 أفكار وكابشنات جديدة من قاعدة المعرفة.',
          confirmLabel: 'جهّز خطة بديلة',
          onConfirm: () => void run('regenerate-weekly-plan', () => contentApi.regenerateWeeklyPlan(planId)),
        })}
      />

      <ContentHistory key={activeProject.id} posts={historyPosts} busy={busy} onPublish={(id) => setConfirmation({
        title: 'نشر هذا البوست الآن؟',
        message: 'سيُرسل التصميم والكابشن إلى صفحة Facebook المحفوظة. راجع المحتوى قبل المتابعة.',
        confirmLabel: 'نشر الآن',
        onConfirm: () => void run(`publish-${id}`, () => contentApi.publish(id)),
      })} />
      </div>
      <StudioDialogs confirmation={confirmation} onCloseConfirmation={() => setConfirmation(null)} navigationGuard={navigationGuard} />
    </div>
  );
}

function ContentTabs({ activeView, onChange }: { activeView: ContentView; onChange: (view: ContentView) => boolean }) {
  const tabRefs = useRef<Record<ContentView, HTMLButtonElement | null>>({ posts: null, videos: null });
  const selectFromKeyboard = (event: React.KeyboardEvent, index: number) => {
    if (!['ArrowRight', 'ArrowLeft', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    const nextIndex = event.key === 'Home' ? 0 : event.key === 'End' ? contentTabs.length - 1
      : (index + (event.key === 'ArrowRight' ? -1 : 1) + contentTabs.length) % contentTabs.length;
    const nextView = contentTabs[nextIndex].key;
    if (onChange(nextView)) window.requestAnimationFrame(() => tabRefs.current[nextView]?.focus());
  };

  return <nav className={styles.contentTabs} role="tablist" aria-label="أنواع المحتوى">
    {contentTabs.map((tab, index) => <button
      type="button"
      role="tab"
      id={`content-tab-${tab.key}`}
      aria-selected={activeView === tab.key}
      aria-controls={`content-panel-${tab.key}`}
      tabIndex={activeView === tab.key ? 0 : -1}
      className={activeView === tab.key ? styles.contentTabActive : styles.contentTab}
      key={tab.key}
      ref={(node) => { tabRefs.current[tab.key] = node; }}
      onClick={() => onChange(tab.key)}
      onKeyDown={(event) => selectFromKeyboard(event, index)}
    >{tab.label}</button>)}
  </nav>;
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

function SampleReview({ sample, pageSaved, busy, approved, onApprove, onRegenerate, onPublish }: { sample: ContentPost; pageSaved: boolean; busy: string | null; approved: boolean; onApprove: () => void; onRegenerate: () => void; onPublish: () => void }) {
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
        <button type="button" className={styles.btnPrimary} disabled={Boolean(busy) || !pageSaved} onClick={onApprove}>{busy === 'approve' ? <LoaderCircle className={styles.spin} size={18} /> : <Send size={18} />} اعتمد وانشر وجهّز الخطة</button>
        <button type="button" className={styles.btnSecondary} disabled={Boolean(busy)} onClick={onRegenerate}>{busy === 'regenerate' ? <LoaderCircle className={styles.spin} size={17} /> : <RefreshCw size={17} />} جرّب شكل تاني</button>
        {!pageSaved && <small>اختار صفحة Facebook واضغط حفظ الإعدادات قبل الاعتماد.</small>}
      </div>}
      {approved && sample.status === 'PublishFailed' && <div className={styles.reviewActions}>
        <button type="button" className={styles.btnPrimary} disabled={Boolean(busy)} onClick={onPublish}>{busy === 'publish-sample' ? <LoaderCircle className={styles.spin} size={18} /> : <Send size={18} />} حاول تنشر أول بوست تاني</button>
      </div>}
      {approved && sample.status === 'PublishUnknown' && <div className={styles.sourceNote}><AlertTriangle size={17} /><span>وقفنا الجدول لأن Facebook ما أكدش نتيجة النشر. راجع الصفحة قبل أي محاولة جديدة.</span></div>}
    </div>
  </div>;
}

function AutomationSummary({ studioData }: { studioData: ContentStudioData }) {
  return <div className={styles.automationSummary}><div className={styles.orbit}><CalendarClock size={40} /></div><h3>{studioData.settings.isEnabled ? 'المحتوى بيتجهّز تلقائيًا' : 'الهوية معتمدة والجدول متوقف'}</h3><p>{studioData.settings.isEnabled ? `البوست الجاي ${formatDate(studioData.settings.nextPublishAtUtc, studioData.settings.timezone)}` : 'تقدر تشغّل النشر اليومي من الإعدادات.'}</p></div>;
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
  const dailyTime = `يوميًا الساعة ${settings.dailyPublishTimeLocal}`;
  const timezone = settings.timezone === 'Africa/Cairo' ? 'القاهرة' : settings.timezone;
  if (settings.isEnabled && settings.nextPublishAtUtc) {
    return { title: formatNextPublishDate(settings.nextPublishAtUtc, settings.timezone), detail: `حسب توقيت ${timezone}` };
  }
  if (!settings.hasApprovedStyle) return { title: 'لسه ما اتحددش', detail: `اعتمد أول تصميم، وبعدها ${dailyTime}` };
  if (!settings.lastPublishedAtUtc) return { title: 'بعد نشر أول بوست', detail: `وبعدها ${dailyTime}` };
  if (weeklyPlan?.status === 'Generating') return { title: 'بنجهّز خطة الأسبوع', detail: '7 أفكار وكابشنات من قاعدة المعرفة' };
  if (weeklyPlan?.status === 'AwaitingApproval') return { title: 'مستني اعتماد الخطة', detail: 'راجع الأيام الموجودة تحت قبل بدء النشر' };
  if (weeklyPlan?.status === 'GenerationFailed') return { title: 'الخطة محتاجة إعادة', detail: 'جرّب تجهيز خطة بديلة من القسم الموجود تحت' };
  return { title: 'مستني خطة معتمدة', detail: `${dailyTime} بعد اعتماد الأيام السبعة` };
}

function WeeklyPlan({ plans, firstPostPublished, busy, onGenerate, onApprove, onRegenerate, onApproveItem, onRegenerateItem }: {
  plans: ContentWeekPlan[];
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

  const plan = plans.find((candidate) => candidate.id === selectedPlanId) ?? plans[0];
  const unresolvedPlan = plans.find((candidate) => ['Generating', 'AwaitingApproval', 'GenerationFailed'].includes(candidate.status));
  const canAddWeek = firstPostPublished && !unresolvedPlan;
  const isGenerating = plan?.status === 'Generating';
  const awaitingApproval = plan?.status === 'AwaitingApproval';
  const approved = plan?.status === 'Approved';
  const reviewedCount = plan?.items.filter((item) => item.postStatus === 'Approved' || item.postStatus === 'Published').length ?? 0;
  const allReviewed = Boolean(plan?.items.length === 7 && reviewedCount === 7);

  return <section className={styles.weeklyPlan} aria-labelledby="weekly-plan-heading">
    <div className={styles.weeklyPlanHeading}>
      <div><span>03</span><div><h2 id="weekly-plan-heading">خطط الأسابيع</h2><p>جهّز أسبوعًا أو أكثر؛ كل أسبوع يبدأ بعد اللي قبله ومن غير تكرار القديم.</p></div></div>
      <div className={styles.weekPlanHeadingActions}>
        {plan && <strong className={`${styles.planStatus} ${approved ? styles.planApproved : ''}`}>{weekPlanStatusLabel(plan.status)}</strong>}
        {firstPostPublished && <button type="button" className={styles.weekPlanAdd} disabled={Boolean(busy) || !canAddWeek} onClick={onGenerate} title={unresolvedPlan ? 'راجع الأسبوع الجاري أو اعتمده قبل إضافة أسبوع جديد' : undefined}>
          {busy === 'weekly-plan' ? <LoaderCircle className={styles.spin} size={16} /> : <Sparkles size={16} />}
          {plans.length > 0 ? 'ضيف أسبوع جديد' : 'جهّز أول أسبوع'}
        </button>}
      </div>
    </div>

    {plans.length > 0 && <div className={styles.weekTabs} role="tablist" aria-label="الأسابيع المجهزة">
      {plans.map((candidate, index) => <button
        type="button"
        role="tab"
        aria-selected={candidate.id === plan?.id}
        className={candidate.id === plan?.id ? styles.weekTabActive : styles.weekTab}
        key={candidate.id}
        onClick={() => setSelectedPlanId(candidate.id)}
      >
        <span>الأسبوع {index + 1}</span>
        <small>{formatWeekRange(candidate)}</small>
      </button>)}
    </div>}

    {!plan && <div className={styles.emptyPlan}>
      <CalendarClock size={30} aria-hidden="true" />
      <div><h3>{firstPostPublished ? 'جاهز نرتب الأسبوع الجاي' : 'الخطة بعد اعتماد أول تصميم'}</h3><p>{firstPostPublished ? 'هنبني 7 أفكار مختلفة من قاعدة المعرفة، واحدة لكل يوم.' : 'أول بوست يتنشر بعد موافقتك، وبعدها هتظهر هنا الخطة كاملة.'}</p></div>
    </div>}

    {isGenerating && <div className={styles.planGenerating} aria-busy="true">
      <div><LoaderCircle className={styles.spin} size={22} /><strong>{plan.items.length > 0 ? 'بنجهّز صور الأيام السبعة للمراجعة' : 'بنقرأ قاعدة المعرفة ونرتب 7 أيام مختلفة'}</strong></div>
      {plan.items.length === 0 && Array.from({ length: 7 }, (_, index) => <span key={index} />)}
    </div>}

    {plan?.status === 'GenerationFailed' && <div className={styles.planFailed} role="alert">
      <AlertTriangle size={24} /><div><h3>الخطة ماكملتش</h3><p>{plan.error ?? 'تعذر تجهيز خطة الأسبوع.'}</p></div>
      <button type="button" className={styles.btnSecondary} disabled={Boolean(busy)} onClick={() => onRegenerate(plan.id)}>{busy === 'regenerate-weekly-plan' ? <LoaderCircle className={styles.spin} size={17} /> : <RefreshCw size={17} />} جرّب خطة جديدة</button>
    </div>}

    {plan?.status === 'Completed' && <div className={styles.emptyPlan}>
      <CheckCircle2 size={28} aria-hidden="true" />
      <div><h3>الأيام السبعة اتنشرت</h3><p>تقدر تضيف أسبوعًا جديدًا من الزر الموجود فوق.</p></div>
    </div>}

    {plan?.status === 'Rejected' && <div className={styles.emptyPlan}>
      <AlertTriangle size={28} aria-hidden="true" />
      <div><h3>الأسبوع ده اتبدّل</h3><p>ضيف أسبوعًا جديدًا، والنظام هيبعد عن كل الأفكار والعناوين السابقة.</p></div>
    </div>}

    {plan && plan.items.length > 0 && <div className={styles.weekList}>
      {plan.items.map((item) => {
        const imageReady = item.postStatus === 'AwaitingApproval';
        const imageApproved = item.postStatus === 'Approved' || item.postStatus === 'Published';
        const imageFailed = item.postStatus === 'GenerationFailed';
        const canRegenerate = awaitingApproval && (imageReady || imageApproved || imageFailed);
        return <article className={styles.weekItem} key={item.id}>
          <div className={styles.weekImage}>
            {item.imageUrl
              ? <AuthenticatedImage assetUrl={item.imageUrl} alt={`تصميم يوم ${formatWeekday(item.scheduledForUtc, plan.timezone)}: ${item.visualHeadline}`} sizes="(max-width: 560px) 92vw, 220px" />
              : <span className={styles.weekImageState}>{imageFailed ? <AlertTriangle size={26} /> : <LoaderCircle className={styles.spin} size={24} />}</span>}
            {item.imageSize && <span className={styles.weekResolution}>{item.imageSize}</span>}
          </div>
          <div className={styles.weekCopy}>
            <div className={styles.weekMeta}>
              <time dateTime={item.scheduledForUtc}><strong>{formatWeekday(item.scheduledForUtc, plan.timezone)}</strong><span>{formatDayAndTime(item.scheduledForUtc, plan.timezone)}</span></time>
              <span className={`${styles.itemState} ${imageApproved || item.postStatus === 'Published' ? styles.itemPublished : ''}`}>{weekItemStatus(item.postStatus, approved)}</span>
            </div>
            <span>الفكرة الكريتيف: {item.topic}</span><h3>{item.visualHeadline}</h3><p>{item.caption}</p>
            {imageFailed && <small className={styles.weekImageError}>{item.postError ?? 'الصورة ماكملتش. جرّب توليدها مرة ثانية.'}</small>}
            {awaitingApproval && <div className={styles.itemReviewActions}>
              {imageReady && <button type="button" className={styles.btnPrimary} disabled={Boolean(busy)} onClick={() => onApproveItem(plan.id, item.id)}>{busy === `approve-weekly-item-${item.id}` ? <LoaderCircle className={styles.spin} size={17} /> : <CheckCircle2 size={17} />} موافق</button>}
              {canRegenerate && <button type="button" className={styles.btnSecondary} disabled={Boolean(busy)} onClick={() => onRegenerateItem(plan.id, item.id)}>{busy === `regenerate-weekly-item-${item.id}` ? <LoaderCircle className={styles.spin} size={17} /> : <RefreshCw size={17} />} إعادة الصورة</button>}
            </div>}
          </div>
        </article>;
      })}
    </div>}

    {awaitingApproval && <div className={styles.planActions}>
      <div><ShieldCheck size={18} /><span>تمت مراجعة {reviewedCount} من 7. لن يبدأ النشر قبل موافقتك على كل الصور.</span></div>
      <button type="button" className={styles.btnPrimary} disabled={Boolean(busy) || !allReviewed} onClick={() => onApprove(plan.id)}>{busy === 'approve-weekly-plan' ? <LoaderCircle className={styles.spin} size={18} /> : <CheckCircle2 size={18} />} {allReviewed ? 'اعتمد الأيام السبعة' : 'وافق على الصور الأول'}</button>
      <button type="button" className={styles.btnSecondary} disabled={Boolean(busy)} onClick={() => onRegenerate(plan.id)}>{busy === 'regenerate-weekly-plan' ? <LoaderCircle className={styles.spin} size={17} /> : <RefreshCw size={17} />} اعمل خطة غيرها</button>
    </div>}
  </section>;
}

function weekItemStatus(status: ContentPostStatus | undefined, planApproved: boolean) {
  if (status === 'Published') return 'اتنشر';
  if (planApproved && status === 'Approved') return 'مجدول';
  if (status === 'Approved') return 'تمت الموافقة';
  if (status === 'AwaitingApproval') return 'مستنية رأيك';
  if (status === 'GenerationFailed') return 'محتاجة إعادة';
  return 'بتتجهّز';
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

function ContentHistory({ posts, busy, onPublish }: { posts: ContentPost[]; busy: string | null; onPublish: (id: string) => void }) {
  const [visibleCount, setVisibleCount] = useState(12);
  const visiblePosts = posts.slice(0, visibleCount);

  return <section className={styles.history}>
    <div className={styles.historyHeading}><div><span>04</span><div><h2>سجل المحتوى</h2><p>كل صورة وكابشن وحالة النشر في مكان واحد.</p></div></div><strong>{posts.length} بوست</strong></div>
    {posts.length === 0 ? <div className={styles.emptyHistory}>أول بوست معتمد هيظهر هنا.</div> : (
      <div className={styles.historyList}>
        {visiblePosts.map((post) => <article className={styles.historyRow} key={post.id}>
          <div className={styles.historyThumb}>{post.imageUrl ? <AuthenticatedImage assetUrl={post.imageUrl} alt="" sizes="84px" /> : <Sparkles size={22} />}</div>
          <div className={styles.historyCopy}><span>{post.topic || 'محتوى جديد'}</span><strong>{post.visualHeadline || 'جاري تجهيز النص'}</strong><small>{post.publishedAtUtc ? `اتنشر ${formatDate(post.publishedAtUtc)}` : post.generatedAtUtc ? `اتولد ${formatDate(post.generatedAtUtc)}` : 'قيد التنفيذ'}</small></div>
          <span className={`${styles.rowStatus} ${statusClass(post.status)}`}>{statusLabels[post.status]}</span>
          {(post.status === 'Approved' || post.status === 'PublishFailed') && <button type="button" className={styles.iconButton} disabled={Boolean(busy)} onClick={() => onPublish(post.id)} aria-label={`نشر البوست الآن: ${post.visualHeadline || post.topic}`}>{busy === `publish-${post.id}` ? <LoaderCircle className={styles.spin} size={18} /> : <Send size={18} />}</button>}
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
