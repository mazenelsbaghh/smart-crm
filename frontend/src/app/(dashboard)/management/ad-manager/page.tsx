'use client';

import { useState, type ReactNode } from 'react';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { Activity, AlertTriangle, Bot, CheckCircle2, Clock3, DatabaseZap, Megaphone, MessageCircleMore, MousePointerClick, Sparkles, Target } from 'lucide-react';
import { useAuth } from '../../../../context/auth-context';
import { adManagerApi } from '../../../../packages/ad-manager/api/ad-manager-api';
import { useAdManager } from '../../../../packages/ad-manager/hooks/use-ad-manager';
import { useAdManagerActions } from '../../../../packages/ad-manager/hooks/use-ad-manager-actions';
import { SettingsPanel } from '../../../../packages/ad-manager/components/SettingsPanel';
import { ExistingCampaignImport } from '../../../../packages/ad-manager/components/ExistingCampaignImport';
import { AdManagerShell } from '../../../../packages/ad-manager/components/AdManagerShell';
import { OverviewView } from '../../../../packages/ad-manager/components/OverviewView';
import { StrategyView } from '../../../../packages/ad-manager/components/StrategyView';
import { AudiencesView } from '../../../../packages/ad-manager/components/AudiencesView';
import { ExperimentsView } from '../../../../packages/ad-manager/components/ExperimentsView';
import { WhatsAppOutcomesView } from '../../../../packages/ad-manager/components/WhatsAppOutcomesView';
import { DecisionsView } from '../../../../packages/ad-manager/components/DecisionsView';
import { CampaignHierarchyView } from '../../../../packages/ad-manager/components/CampaignHierarchyView';
import { CreativesView } from '../../../../packages/ad-manager/components/CreativesView';
import { DailyReportsView } from '../../../../packages/ad-manager/components/DailyReportsView';
import ConfirmDialog from '../../../../components/shared/ConfirmDialog';
import styles from '../../../../packages/ad-manager/AdManager.module.css';

const tabs = [
  ['overview', 'نظرة عامة'], ['strategy', 'الاستراتيجية'], ['campaigns', 'الحملات'],
  ['audiences', 'الجمهور'], ['creatives', 'المحتوى'], ['experiments', 'الاختبارات'],
  ['conversions', 'نتائج واتساب'], ['decisions', 'قرارات AI'], ['settings', 'الإعدادات'],
] as const;

type ManagedAction = 'enable' | 'disable' | 'stop' | 'resume';
type PendingManagedAction = { projectId?: string; action: ManagedAction };
const number = (value: number) => new Intl.NumberFormat('ar-EG', { maximumFractionDigits: 2 }).format(value);
const money = (value: number, currency: string | undefined) => {
  if (!currency) return `${number(value)} (العملة غير متاحة)`;
  try { return new Intl.NumberFormat('ar-EG', { style: 'currency', currency, maximumFractionDigits: 2 }).format(value); }
  catch { return `${number(value)} (${currency})`; }
};
const dateTime = (value: string | undefined, timezone: string | undefined) => {
  if (!value) return 'لم يُسجّل بعد';
  try { return new Intl.DateTimeFormat('ar-EG', { dateStyle: 'short', timeStyle: 'short', timeZone: timezone || 'UTC' }).format(new Date(value)); }
  catch { return `${new Intl.DateTimeFormat('ar-EG', { dateStyle: 'short', timeStyle: 'short', timeZone: 'UTC' }).format(new Date(value))} UTC`; }
};

const jobLabels: Record<string, { label: string; schedule: string }> = {
  spend: { label: 'مراقبة الصرف والسقف', schedule: 'كل 5 دقائق' },
  sync: { label: 'مزامنة حالة الحملة مع Meta', schedule: 'كل 10 دقائق' },
  insights: { label: 'سحب الأداء من Meta', schedule: 'مرتين كل ساعة' },
  tracking: { label: 'فحص التتبع', schedule: 'كل 15 دقيقة' },
  decision: { label: 'قرار AI ومراجعة الأمان', schedule: 'كل ساعة' },
  fatigue: { label: 'فحص إرهاق المحتوى', schedule: 'كل 6 ساعات' },
  impact: { label: 'مراجعة أثر القرارات', schedule: 'كل ساعتين' },
  'conversion-delivery': { label: 'إرسال التحويلات', schedule: 'كل دقيقة' },
  rebalance: { label: 'إعادة توزيع الميزانية', schedule: 'يوميًا 04:00' },
  tests: { label: 'فحص محتوى واختبارات جديدة', schedule: 'كل 6 ساعات' },
  strategy: { label: 'مراجعة الاستراتيجية', schedule: 'كل يوم اثنين' },
};

export default function AdManagerPage() {
  const { activeProject, user, loading: authLoading } = useAuth();
  const projectId = activeProject?.id;
  const searchParams = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const facebookConnected = searchParams.get('facebook') === 'connected';
  const requestedView = searchParams.get('view');
  const selectedView = tabs.some(([key]) => key === requestedView) ? requestedView as (typeof tabs)[number][0] : 'overview';
  const actions = useAdManagerActions(projectId);
  const activeTab = facebookConnected && !actions.connectionFinished ? 'settings' : selectedView;
  const data = useAdManager(projectId, activeTab);
  const [pendingActionState, setPendingActionState] = useState<PendingManagedAction | null>(null);
  const pendingAction = pendingActionState && pendingActionState.projectId === projectId
    ? pendingActionState.action
    : null;
  const canManage = user?.role === 'Owner' || user?.role === 'Admin';
  const setPendingAction = (action: ManagedAction | null) => {
    setPendingActionState(action ? { projectId, action } : null);
  };
  const setTab = (next: (typeof tabs)[number][0]) => {
    const params = new URLSearchParams(searchParams.toString());
    params.set('view', next);
    router.replace(`${pathname}?${params.toString()}`, { scroll: false });
  };

  const confirmAction = () => {
    if (!projectId || !pendingAction || !canManage) return;
    const action = pendingAction;
    setPendingAction(null);
    if (action === 'enable') void actions.runAction(() => adManagerApi.enable(projectId), 'تم تشغيل Autopilot داخل السقف المفوّض.', data.refreshAfterMutation);
    if (action === 'disable') void actions.runAction(() => adManagerApi.disable(projectId), 'توقفت القرارات الجديدة وبدأ إيقاف الإعلانات التي يديرها النظام.', data.refreshAfterMutation);
    if (action === 'stop') void actions.runAction(() => adManagerApi.emergencyStop(projectId, 'Manual dashboard emergency stop'), 'بدأ الإيقاف الطارئ وسيظهر تقدم كل عنصر هنا.', data.refreshAfterMutation);
    if (action === 'resume') void actions.runAction(() => adManagerApi.resume(projectId), 'تم رفع قفل الطوارئ فقط. راجع الجاهزية ثم فعّل النظام يدويًا.', data.refreshAfterMutation);
  };

  const confirmation = pendingAction ? {
    enable: {
      title: 'تشغيل الإدارة الآلية؟',
      message: `سيُسمح بقرارات وصرف مؤهلين على مشروع ${activeProject?.name ?? ''} داخل سقف ${money(data.overview?.dailyCap ?? 0, data.overview?.currency)} يوميًا. لن يتجاوز النظام التفويض النشط.`,
      label: 'شغّل داخل السقف',
    },
    disable: {
      title: 'إيقاف الإدارة الآلية؟',
      message: 'ستتوقف القرارات الجديدة، وسيبدأ إيقاف الإعلانات التي يملكها النظام بدون حذفها. قد يستغرق تأكيد Meta بعض الوقت وسيظهر التقدم هنا.',
      label: 'ابدأ الإيقاف العادي',
    },
    stop: {
      title: 'تنفيذ إيقاف طارئ؟',
      message: 'سيتم إرسال أوامر إيقاف فورية للحملة ومجموعات الإعلانات والإعلانات التي يملكها النظام فقط، بدون حذفها. راقب التقدم لأن بعض العناصر قد تحتاج مراجعة يدوية.',
      label: 'نفّذ الإيقاف الطارئ',
    },
    resume: {
      title: 'رفع قفل الطوارئ؟',
      message: 'سيُرفع قفل الطوارئ فقط؛ لن يعود الصرف تلقائيًا. بعد فحص الجاهزية ستحتاج إلى تشغيل الإدارة الآلية يدويًا.',
      label: 'ارفع القفل',
    },
  }[pendingAction] : null;

  if (authLoading || (data.loading && !data.overview)) return <div className={styles.loading} aria-busy="true" aria-label="جارٍ تحميل مدير الإعلانات"><span /><span /><span /></div>;
  if (!projectId) return <section className={styles.empty} role="status"><Target aria-hidden="true" /><h1>تعذر تحميل مساحة العمل</h1><p>لا توجد مساحة مرتبطة متاحة الآن. أعد تحميل الصفحة أو تواصل مع المدير.</p></section>;

  return (
    <AdManagerShell tabs={tabs.map(([key, label]) => ({ key, label }))} activeTab={activeTab} busy={actions.busy}
      refreshing={data.refreshing}
      ready={data.overview?.readiness.ready ?? false} autopilot={data.overview?.autopilot ?? false}
      emergencyStop={data.overview?.emergencyStop ?? false} continuingSpend={data.overview?.continuingSpend}
      stopState={data.stopState}
      updatedAt={data.overview?.asOfUtc} reportingTimezone={data.overview?.reportingTimezone}
      canManage={canManage} notice={actions.notice} error={actions.actionError ?? data.error}
      onTabChange={(key) => setTab(key as (typeof tabs)[number][0])} onRefresh={() => void data.refresh()}
      onResume={() => setPendingAction('resume')}
      onStop={() => setPendingAction('stop')}>

      {activeTab === 'overview' && <>
        {data.overview && <OverviewView overview={data.overview} onConfigure={() => setTab('settings')} canManage={canManage} />}
        <OperationalReport
          overview={data.overview}
          busy={actions.busy}
          canManage={canManage}
          onSync={() => projectId && void actions.runAction(() => adManagerApi.syncNow(projectId), 'بدأ سحب حالة الحملة والأداء. ستظهر أحدث الأرقام خلال لحظات.', data.refreshAfterMutation)}
        />
        {data.dailyReport && <DailyReportsView key={projectId} projectId={projectId} initial={data.dailyReport} />}
      </>}

      {activeTab === 'strategy' && <StrategyView strategy={data.strategy} />}

      {activeTab === 'campaigns' && <>{canManage && <ExistingCampaignImport key={projectId} projectId={projectId} dailyCap={data.overview?.dailyCap ?? 0} onImported={data.refreshAfterMutation} />}<CampaignHierarchyView rows={data.campaigns} /></>}
      {activeTab === 'creatives' && <CreativesView projectId={projectId} creatives={data.creatives} comparisons={data.creativeComparison} decisions={data.decisions} onChanged={data.refreshAfterMutation} canManage={canManage} />}
      {activeTab === 'audiences' && <AudiencesView rows={data.audiences} />}
      {activeTab === 'experiments' && <ExperimentsView rows={data.experiments} ads={data.campaigns} />}
      {activeTab === 'conversions' && <WhatsAppOutcomesView rows={data.conversions} touches={data.attributionTouches} deliveries={data.conversionDeliveries} tracking={data.trackingHealth} />}
      {activeTab === 'decisions' && <DecisionsView projectId={projectId} rows={data.decisions} />}
      {activeTab === 'settings' && <div className={styles.settings}>
        {canManage ? <SettingsPanel key={projectId} projectId={projectId} dailyCap={data.overview?.dailyCap ?? 0}
          onSaved={(message) => actions.finishFacebookConnection(message, data.refreshAfterMutation)} />
          : <div className={styles.readOnlyPanel} role="status"><h2>إعدادات للعرض فقط</h2><p>ربط Meta وتفويض الميزانية يحتاجان دور مالك أو مدير. يمكنك متابعة الحالة والتقارير بدون تغيير.</p></div>}
        <div><Megaphone size={20} /><h2>Meta Advantage+ وواتساب فقط</h2><p>مِتا تختار المواضع الديناميكية المؤهلة بين Facebook وInstagram، لكن كل إعلان يفتح رقم واتساب المصرّح به فقط.</p></div>
        <div><Sparkles size={20} /><h2>المحتوى المسموح</h2><p>بوستات الصفحة وصور وفيديوهات المشروع، مع نصوص وCTA وقص ومقاسات وThumbnail؛ بدون توليد صورة أو فيديو من الصفر.</p></div>
        <div><MousePointerClick size={20} /><h2>التحويل الحقيقي</h2><p>الدفع والاشتراك والحضور أولوية، ثم الحجز والـTrial والـLead المؤهل، حسب جودة البيانات.</p></div>
        {canManage && <div className={styles.controlRow}>
          <button className={styles.primaryButton} disabled={actions.busy || !data.overview?.readiness.ready || !data.overview?.currency || !data.overview?.reportingTimezone || data.overview?.autopilot} onClick={() => setPendingAction('enable')}>{data.overview?.autopilot ? 'Autopilot يعمل الآن' : 'تشغيل Autopilot'}</button>
          <button className={styles.secondaryButton} disabled={actions.busy || !data.overview?.autopilot} onClick={() => setPendingAction('disable')}>إيقاف عادي</button>
        </div>}
      </div>}

      <ConfirmDialog isOpen={Boolean(confirmation)} title={confirmation?.title ?? ''} message={confirmation?.message ?? ''}
        confirmLabel={confirmation?.label} onCancel={() => setPendingAction(null)} onConfirm={confirmAction} />
    </AdManagerShell>
  );
}

function OperationalReport({ overview, busy, canManage, onSync }: { overview: import('../../../../packages/ad-manager/types').AdvertisingOverview | null; busy: boolean; canManage: boolean; onSync: () => void }) {
  const operations = overview?.operations;
  if (!operations) return null;
  const campaign = operations.campaign;
  const latestDecision = operations.ai.latestDecision;
  const timezone = overview.reportingTimezone;
  const currency = overview.currency;
  return <section className={styles.operationalReport} aria-label="تقرير تشغيل مدير الإعلانات">
    <div className={styles.reportHeading}>
      <div>
        <p className={styles.sectionEyebrow}>التشغيل المباشر</p>
        <h2>حالة التشغيل الآن</h2>
        <p>ملخص مباشر للربط، السحب، التتبع، قرارات AI، والحملة التي يديرها النظام.</p>
      </div>
      {canManage && <button className={styles.secondaryButton} onClick={onSync} disabled={busy || !overview.readiness.ready}><DatabaseZap size={17} aria-hidden="true" /> سحب الآن</button>}
    </div>

    <div className={styles.statusRail}>
      <StatusItem icon={<Activity size={18} />} title="ربط Meta" good={operations.connection?.connected ?? false} value={operations.connection?.connected ? 'متصل وجاهز' : 'يحتاج مراجعة'} detail={`آخر مزامنة: ${dateTime(operations.connection?.lastSyncAtUtc, timezone)}`} />
      <StatusItem icon={<Target size={18} />} title="الحملة المدارة" good={campaign?.effectiveStatus === 'ACTIVE'} value={campaign ? campaign.effectiveStatus || 'تحت المراجعة' : 'لا توجد حملة'} detail={campaign ? `${money(campaign.dailyBudget, currency)} يوميًا · آخر قراءة ${dateTime(campaign.lastSyncedAtUtc, timezone)}` : 'اختر حملة من تبويب الحملات'} />
      <StatusItem icon={<CheckCircle2 size={18} />} title="تتبع النتائج" good={operations.tracking.healthy} value={operations.tracking.healthy ? 'سليم' : `متوقف للحماية · ${operations.tracking.state}`} detail={operations.tracking.healthy ? `Cloud API وDataset وCRM متصلة · ${dateTime(operations.tracking.evaluatedAtUtc, timezone)}` : operations.tracking.mode === 'UNSAFE_NO_DATASET' ? 'اختر WABA والرقم وDataset؛ الصرف يظل ممنوعًا حتى اكتمال الجاهزية' : `آخر فحص · ${dateTime(operations.tracking.evaluatedAtUtc, timezone)}`} />
      <StatusItem icon={<Bot size={18} />} title="AI الإعلاني" good={Boolean(latestDecision)} value={latestDecision ? latestDecision.actionType : 'ينتظر أول دورة'} detail={latestDecision ? `آخر قرار: ${dateTime(latestDecision.createdAt, timezone)}` : `الموديل: ${operations.ai.model}`} />
    </div>

    <div className={styles.reportGrid}>
      <div className={styles.reportBlock}>
        <div className={styles.reportBlockTitle}><DatabaseZap size={18} /><h3>آخر سحب من Meta</h3></div>
        <div className={styles.performanceLine}><strong>{money(overview.spend, currency)}</strong><span>صرف اليوم</span></div>
        <dl className={styles.compactFacts}>
          <div><dt>آخر سحب</dt><dd>{dateTime(operations.performance.lastPulledAtUtc, timezone)}</dd></div>
          <div><dt>الظهور اليوم</dt><dd>{number(operations.performance.impressions)}</dd></div>
          <div><dt>الضغطات اليوم</dt><dd>{number(operations.performance.clicks)}</dd></div>
          <div><dt>سجل الأداء</dt><dd>{operations.performance.snapshots} قراءة عبر {operations.performance.daysLoaded} أيام</dd></div>
        </dl>
      </div>
      <div className={styles.reportBlock}>
        <div className={styles.reportBlockTitle}><MessageCircleMore size={18} /><h3>AI وWhatsApp</h3></div>
        <dl className={styles.compactFacts}>
          <div><dt>الموديل</dt><dd>{operations.ai.model}</dd></div>
          <div><dt>مفتاح AI</dt><dd>{operations.ai.usesProjectApiKey ? 'مفتاح المشروع' : 'مفتاح النظام'}</dd></div>
          <div><dt>التحويلات المؤكدة</dt><dd>{overview?.bookings ?? 0} حجز، {overview?.purchases ?? 0} شراء، {overview?.leads ?? 0} محادثة جديدة، {overview?.qualifiedLeads ?? 0} Lead مؤهل</dd></div>
          <div><dt>حالة القرارات</dt><dd>{latestDecision ? `${latestDecision.actionType} · ${latestDecision.state}` : 'لا توجد بيانات كافية بعد'}</dd></div>
        </dl>
      </div>
    </div>

    {operations.connection?.lastErrorSummary && <div className={styles.operationWarning} role="status"><AlertTriangle size={17} /><span>آخر ملاحظة من Meta: {operations.connection.lastErrorSummary}</span></div>}
    {operations.lastFailure && <div className={styles.operationWarning} role="status"><AlertTriangle size={17} /><span>آخر محاولة غير مكتملة: {jobLabels[operations.lastFailure.jobName]?.label ?? operations.lastFailure.jobName}، وستُعاد تلقائيًا في موعدها التالي.</span></div>}
    {operations.tracking.openIncidents.length > 0 && <div className={styles.operationWarning} role="alert"><AlertTriangle size={17} /><span>{operations.tracking.openIncidents[0].summary}</span></div>}

    <div className={styles.schedulerHeader}><div><Clock3 size={18} /><h3>مهام النظام المجدولة</h3></div><span>يتم التنفيذ تلقائيًا، ولا يحتاج منك تشغيل يدوي.</span></div>
    <div className={styles.schedulerList}>
      {Object.entries(jobLabels).map(([key, job]) => {
        const run = operations.jobs.find(item => item.jobName === key);
        const ok = run?.state === 'Completed';
        return <div className={styles.schedulerRow} key={key}>
          <span className={ok ? styles.runGood : styles.runWait}>{ok ? <CheckCircle2 size={16} /> : <Clock3 size={16} />}</span>
          <strong>{job.label}</strong>
          <small>{job.schedule}</small>
          <time>{run ? `${ok ? 'آخر تشغيل' : 'الحالة'}: ${dateTime(run.completedAtUtc ?? run.startedAtUtc, timezone)}` : 'لم يبدأ بعد'}</time>
        </div>;
      })}
    </div>
  </section>;
}

function StatusItem({ icon, title, good, value, detail }: { icon: ReactNode; title: string; good: boolean; value: string; detail: string }) {
  return <div className={styles.statusItem}>
    <span className={good ? styles.runGood : styles.runWait}>{icon}</span>
    <div><small>{title}</small><strong>{value}</strong><p>{detail}</p></div>
  </div>;
}
