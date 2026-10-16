'use client';

import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import axios from 'axios';
import { AlertCircle, CalendarDays, RefreshCw, ChartNoAxesCombined, ListChecks, MessagesSquare } from 'lucide-react';
import { useAuth } from '../../context/auth-context';
import { AiBrief } from './reports/AiBrief';
import { AnalysisTable } from './reports/AnalysisTable';
import { AskAnalyst } from './reports/AskAnalyst';
import { FunnelView } from './reports/FunnelView';
import { FunnelTransitions } from './reports/FunnelTransitions';
import { MetricStrip } from './reports/MetricStrip';
import { OpportunityQueue } from './reports/OpportunityQueue';
import { ReasonBreakdown } from './reports/ReasonBreakdown';
import { reportsApi, type ReportWindow } from './reports/reports-api';
import { reportDateWindow, reportInputDate, reportTimezone } from './reports/report-window';
import type { Project } from '../../services/auth';
import type { FollowUpDispatchOptions, FollowUpPlanAction, OpportunityItem, ReportPreset, SalesIntelligenceDashboard } from './reports/types';
import PageHeader from '../../components/ui/PageHeader';
import Tabs from '../../components/ui/Tabs';
import styles from './reports/reports.module.css';

const presets: { value: ReportPreset; label: string; days: number }[] = [
  { value: 'today', label: 'آخر 24 ساعة', days: 1 },
  { value: '7d', label: '7 أيام', days: 7 },
  { value: '30d', label: '30 يومًا', days: 30 },
];

const createWindow = (preset: ReportPreset): ReportWindow => {
  const days = presets.find((item) => item.value === preset)?.days ?? 7;
  const to = new Date();
  return { fromUtc: new Date(to.getTime() - days * 86_400_000).toISOString(), toUtc: to.toISOString() };
};

const reportRequestError = (error: unknown, fallback: string) => {
  if (!axios.isAxiosError<{ error?: string }>(error)) return fallback;
  return error.response?.data?.error?.trim() || fallback;
};

export default function Reports() {
  const { activeProject, user, loading } = useAuth();
  if (!activeProject) return loading ? <ReportsSkeleton /> : <div className={styles.failure} role="alert">تعذر تحديد المشروع النشط.</div>;
  return <ReportWorkspace key={`${activeProject.id}:${activeProject.settings?.timezone}`} activeProject={activeProject} canManage={user?.role === 'Owner' || user?.role === 'Admin'} />;
}

function ReportWorkspace({ activeProject, canManage }: { activeProject: Project; canManage: boolean }) {
  const timezone = reportTimezone(activeProject.settings?.timezone);
  const [preset, setPreset] = useState<ReportPreset>('7d');
  const [windowRange, setWindowRange] = useState<ReportWindow>(() => createWindow('7d'));
  const [customFromDate, setCustomFromDate] = useState(() => reportInputDate(new Date(Date.now() - 6 * 86_400_000), timezone));
  const [customToDate, setCustomToDate] = useState(() => reportInputDate(new Date(), timezone));
  const [dashboard, setDashboard] = useState<SalesIntelligenceDashboard | null>(null);
  const [loadedReportKey, setLoadedReportKey] = useState<string | null>(null);
  const [reportTask, setReportTask] = useState<'overview' | 'opportunities' | 'conversations'>('overview');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const requestId = useRef(0);
  const reportRequestKey = `${activeProject.id}:${windowRange.fromUtc}:${windowRange.toUtc}`;
  const reportIntentKeyRef = useRef(reportRequestKey);

  useLayoutEffect(() => {
    reportIntentKeyRef.current = reportRequestKey;
  }, [reportRequestKey]);

  const load = useCallback(async () => {
    const nextReportKey = `${activeProject.id}:${windowRange.fromUtc}:${windowRange.toUtc}`;
    if (reportIntentKeyRef.current !== nextReportKey) return;
    const currentRequest = ++requestId.current;
    setLoading(true);
    setError('');
    try {
      const next = await reportsApi.dashboard(activeProject.id, windowRange);
      if (currentRequest === requestId.current && reportIntentKeyRef.current === nextReportKey) {
        setDashboard(next);
        setLoadedReportKey(nextReportKey);
      }
    } catch {
      if (currentRequest === requestId.current && reportIntentKeyRef.current === nextReportKey) {
        setError('تعذر تحميل تحليلات المبيعات. تحقق من اتصال الخادم ثم أعد المحاولة.');
      }
    } finally {
      if (currentRequest === requestId.current && reportIntentKeyRef.current === nextReportKey) setLoading(false);
    }
  }, [activeProject, windowRange]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const changePreset = (next: ReportPreset) => {
    if (next === 'custom') return;
    setLoading(true);
    setPreset(next);
    setWindowRange(createWindow(next));
    setNotice('');
  };

  const applyCustomRange = (fromDate = customFromDate, toDate = customToDate) => {
    const nextWindow = reportDateWindow(fromDate, toDate, dashboard?.timezone ?? timezone);
    if (!nextWindow) {
      setError('اختر تاريخ بداية ونهاية صحيحين؛ تاريخ النهاية لا يسبق البداية.');
      return;
    }
    setPreset('custom');
    setLoading(true);
    setWindowRange(nextWindow);
    setError('');
    setNotice('');
  };

  const selectReportDay = (date: string) => {
    setCustomFromDate(date);
    setCustomToDate(date);
    applyCustomRange(date, date);
  };

  useEffect(() => {
    const handleShortcut = (event: KeyboardEvent) => {
      if (event.altKey && event.key.toLowerCase() === 'r' && !loading) {
        event.preventDefault();
        void load();
      }
    };
    window.addEventListener('keydown', handleShortcut);
    return () => window.removeEventListener('keydown', handleShortcut);
  }, [load, loading]);

  if (loading && !dashboard) return <ReportsSkeleton />;

  if (!dashboard) return (
    <div className={styles.failure} role="alert">
      <AlertCircle size={28} aria-hidden="true" />
      <h1>تعذر فتح تحليلات المبيعات</h1><p>{error}</p>
      <button type="button" onClick={() => void load()}>إعادة المحاولة</button>
    </div>
  );

  const updateAnalysis = async (conversationId: string, reason?: string) => {
    if (reason) await reportsApi.correctReason(activeProject.id, conversationId, reason);
    else await reportsApi.analyze(activeProject.id, conversationId);
    await load();
  };

  const scheduleOpportunity = async (opportunity: OpportunityItem) => {
    if (opportunity.channel !== 'WhatsApp') {
      throw new Error('Automated follow-ups are currently limited to WhatsApp opportunities.');
    }
    const mutationReportKey = reportRequestKey;
    setError('');
    try {
      const result = await reportsApi.queueFollowUpPlan(
        activeProject.id,
        windowRange,
        { action: 'Schedule', conversationId: opportunity.conversationId, planToken: opportunity.actionToken },
      );
      if (reportIntentKeyRef.current === mutationReportKey) {
        setNotice(result.queued > 0
          ? `تمت جدولة متابعة ${opportunity.customerName} بعد 24 ساعة.`
          : `متابعة ${opportunity.customerName} مسجلة بالفعل.`);
        await load();
      }
    } catch (requestError) {
      if (reportIntentKeyRef.current === mutationReportKey) {
        setError(reportRequestError(requestError, 'تعذر جدولة المتابعة. حاول مرة أخرى.'));
      }
      throw requestError;
    }
  };

  const sendOpportunity = async (opportunity: OpportunityItem) => {
    if (opportunity.channel !== 'WhatsApp') {
      throw new Error('Immediate automated follow-ups are currently limited to WhatsApp opportunities.');
    }
    const mutationReportKey = reportRequestKey;
    setError('');
    try {
      const result = await reportsApi.queueFollowUpPlan(
        activeProject.id,
        windowRange,
        { action: 'SendNow', conversationId: opportunity.conversationId, planToken: opportunity.actionToken },
      );
      if (reportIntentKeyRef.current === mutationReportKey) {
        setNotice(result.queued > 0
          ? `بدأ إرسال متابعة ${opportunity.customerName}.`
          : `متابعة ${opportunity.customerName} مسجلة بالفعل.`);
        await load();
      }
    } catch (requestError) {
      if (reportIntentKeyRef.current === mutationReportKey) {
        setError(reportRequestError(requestError, 'تعذر إرسال المتابعة. راجع اتصال واتساب وحاول مرة أخرى.'));
      }
      throw requestError;
    }
  };

  const queueFollowUpPlan = async (action: FollowUpPlanAction, dispatchOptions: FollowUpDispatchOptions) => {
    const mutationReportKey = reportRequestKey;
    const planToken = action === 'SendNow'
      ? dashboard.followUpPlan.sendNowToken
      : dashboard.followUpPlan.scheduleToken;
    setError('');
    try {
      const queued = await reportsApi.queueFollowUpPlan(
        activeProject.id,
        windowRange,
        { action, planToken, dispatchOptions },
      );
      if (reportIntentKeyRef.current === mutationReportKey) {
        setNotice(action === 'SendNow'
          ? `بدأ إرسال المتابعة إلى ${queued.queued} عميل بفاصل ${dispatchOptions.minIntervalSeconds}–${dispatchOptions.maxIntervalSeconds} ثانية بين كل رسالة.`
          : `تمت جدولة ${queued.queued} عميل على ${dispatchOptions.scheduleDays} أيام، الأعلى أولوية أولًا، بداية من بكرة.`);
        await load();
      }
      return true;
    } catch (requestError) {
      if (reportIntentKeyRef.current === mutationReportKey) {
        setError(reportRequestError(requestError, 'تعذر تنفيذ خطة المتابعات. حاول مرة أخرى.'));
      }
      return false;
    }
  };

  return (
    <section className={styles.page}>
      <div className={styles.header}>
        <PageHeader icon={<ChartNoAxesCombined size={24} aria-hidden="true" />} title="تقارير المبيعات" description="المحادثات والحجوزات ونتائج المتابعة في فترة واحدة" />
        <div className={styles.headerActions}>
          <div className={styles.segmented} aria-label="فترة التقرير">{presets.map((presetOption) => <button type="button" aria-pressed={preset === presetOption.value} key={presetOption.value} onClick={() => changePreset(presetOption.value)}>{presetOption.label}</button>)}</div>
          <button className={styles.refreshButton} type="button" disabled={loading} onClick={() => void load()}><RefreshCw size={16} className={loading ? styles.spinning : ''} aria-hidden="true" />{loading ? 'يحدّث البيانات…' : 'تحديث البيانات'}<kbd>Alt R</kbd></button>
        </div>
      </div>

      <details className={styles.customRangeDisclosure}>
        <summary><CalendarDays size={17} aria-hidden="true" />يوم محدد أو فترة مخصصة{preset === 'custom' && <span>مفعّلة</span>}</summary>
      <div className={styles.customRangeBar}>
        <label>من<input type="date" value={customFromDate} max={customToDate} onChange={(event) => setCustomFromDate(event.target.value)} /></label>
        <label>إلى<input type="date" value={customToDate} min={customFromDate} onChange={(event) => setCustomToDate(event.target.value)} /></label>
        <button type="button" onClick={() => applyCustomRange()}>عرض الفترة</button>
      </div>
      </details>

      {(error || notice) && <div className={error ? styles.errorBanner : styles.noticeBanner} role={error ? 'alert' : 'status'}>{error || notice}</div>}
      {loadedReportKey !== reportRequestKey && <p role="status">المعروض هو آخر تقرير تم تحميله؛ أرقام الفترة المطلوبة لم تصل بعد.</p>}
      <div className={styles.reportContext}>مجموعة العملاء الذين بدأوا الشات بين {new Date(dashboard.windowStartUtc).toLocaleDateString('ar-EG', { timeZone: dashboard.timezone })} و{new Date(new Date(dashboard.windowEndUtc).getTime() - 1).toLocaleDateString('ar-EG', { timeZone: dashboard.timezone })}، الحجوزات تُنسب للشات إذا سُجلت خلال 30 يومًا من بدايته، والدفع والحضور حسب حالتها المسجلة الآن. آخر تحديث {new Date(dashboard.generatedAtUtc).toLocaleTimeString('ar-EG', { timeZone: dashboard.timezone, hour: 'numeric', minute: '2-digit' })} بتوقيت {dashboard.timezone}.</div>

      <Tabs idPrefix="sales-report" label="عرض التقرير" selected={reportTask} onChange={setReportTask} compactOnMobile
        tabs={[
          { key: 'overview', label: 'نظرة عامة', icon: <ChartNoAxesCombined size={17} aria-hidden="true" /> },
          { key: 'opportunities', label: 'فرص المتابعة', icon: <ListChecks size={17} aria-hidden="true" /> },
          { key: 'conversations', label: 'تفاصيل المحادثات', icon: <MessagesSquare size={17} aria-hidden="true" /> },
        ]} />
      <div id="sales-report-panel-overview" role="tabpanel" aria-labelledby="sales-report-tab-overview" hidden={reportTask !== 'overview'} className={styles.taskPanel}>
      <MetricStrip dashboard={dashboard} />
      <AiBrief digest={dashboard.aiDigest} coverage={dashboard.analysisCoverage} timezone={dashboard.timezone} />
      <div className={styles.twoColumn}><FunnelView funnel={dashboard.funnel} daily={dashboard.daily} onSelectDay={selectReportDay} /><ReasonBreakdown reasons={dashboard.reasons} /></div>
      <FunnelTransitions transitions={dashboard.funnelTransitions} />
      <AskAnalyst key={reportRequestKey} disabled={loading || loadedReportKey !== reportRequestKey} onAsk={(question) => reportsApi.ask(activeProject.id, windowRange, question)} />
      </div>
      <div id="sales-report-panel-opportunities" role="tabpanel" aria-labelledby="sales-report-tab-opportunities" hidden={reportTask !== 'opportunities'} className={styles.taskPanel}>
      <OpportunityQueue
        key={reportRequestKey}
        opportunities={dashboard.opportunities}
        timezone={dashboard.timezone}
        plan={dashboard.followUpPlan}
        canManage={canManage}
        actionsDisabled={loading || loadedReportKey !== reportRequestKey || dashboard.projectId !== activeProject?.id}
        onSchedule={scheduleOpportunity}
        onSend={sendOpportunity}
        onQueuePlan={queueFollowUpPlan}
      />
      </div>
      <div id="sales-report-panel-conversations" role="tabpanel" aria-labelledby="sales-report-tab-conversations" hidden={reportTask !== 'conversations'} className={styles.taskPanel}>
      <AnalysisTable
        key={reportRequestKey}
        rows={dashboard.analyses}
        totalCount={dashboard.analyzedConversations}
        timezone={dashboard.timezone}
        actionsDisabled={loading || loadedReportKey !== reportRequestKey}
        canManage={canManage}
        onReanalyze={(id) => updateAnalysis(id)}
        onCorrect={(id, reason) => updateAnalysis(id, reason)}
      />
      </div>
    </section>
  );
}

function ReportsSkeleton() {
  return <div className={styles.skeletonPage} role="status" aria-label="جاري تحميل تحليلات المبيعات"><div className={styles.skeletonHeader} /><div className={styles.skeletonMetrics}>{[0, 1, 2, 3].map((skeletonIndex) => <span key={skeletonIndex} />)}</div><div className={styles.skeletonPanel} /><div className={styles.skeletonPanel} /></div>;
}
