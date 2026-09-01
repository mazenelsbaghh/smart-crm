'use client';

import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import axios from 'axios';
import { AlertCircle, CalendarDays, RefreshCw } from 'lucide-react';
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
import type { FollowUpPlanAction, OpportunityItem, ReportPreset, SalesIntelligenceDashboard } from './reports/types';
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

const inputDate = (date: Date) => {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
};

const customWindow = (fromDate: string, toDate: string): ReportWindow | null => {
  if (!fromDate || !toDate || fromDate > toDate) return null;
  const from = new Date(`${fromDate}T00:00:00`);
  const to = new Date(`${toDate}T00:00:00`);
  to.setDate(to.getDate() + 1);
  return { fromUtc: from.toISOString(), toUtc: to.toISOString() };
};

const reportRequestError = (error: unknown, fallback: string) => {
  if (!axios.isAxiosError<{ error?: string }>(error)) return fallback;
  return error.response?.data?.error?.trim() || fallback;
};

export default function Reports() {
  const { activeProject, user } = useAuth();
  const [preset, setPreset] = useState<ReportPreset>('7d');
  const [windowRange, setWindowRange] = useState<ReportWindow>(() => createWindow('7d'));
  const [customFromDate, setCustomFromDate] = useState(() => inputDate(new Date(Date.now() - 6 * 86_400_000)));
  const [customToDate, setCustomToDate] = useState(() => inputDate(new Date()));
  const [dashboard, setDashboard] = useState<SalesIntelligenceDashboard | null>(null);
  const [loadedReportKey, setLoadedReportKey] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const requestId = useRef(0);
  const canManage = user?.role === 'Owner' || user?.role === 'Admin';
  const reportRequestKey = `${activeProject?.id ?? 'none'}:${windowRange.fromUtc}:${windowRange.toUtc}`;
  const reportIntentKeyRef = useRef(reportRequestKey);

  useLayoutEffect(() => {
    reportIntentKeyRef.current = reportRequestKey;
  }, [reportRequestKey]);

  const load = useCallback(async () => {
    const nextReportKey = `${activeProject?.id ?? 'none'}:${windowRange.fromUtc}:${windowRange.toUtc}`;
    if (reportIntentKeyRef.current !== nextReportKey) return;
    const currentRequest = ++requestId.current;
    if (!activeProject) {
      setLoading(false);
      setDashboard(null);
      setLoadedReportKey(null);
      setError('تعذر تحديد المشروع النشط.');
      return;
    }
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
    const nextWindow = customWindow(fromDate, toDate);
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

  const refresh = useCallback(async () => {
    if (!activeProject || !canManage || refreshing) return;
    const refreshReportKey = `${activeProject.id}:${windowRange.fromUtc}:${windowRange.toUtc}`;
    setRefreshing(true);
    setError('');
    setNotice('');
    try {
      const refreshResult = await reportsApi.refresh(activeProject.id, windowRange);
      if (reportIntentKeyRef.current === refreshReportKey) {
        setNotice(refreshResult.pending > 0
          ? `بدأ تحليل كل الشاتات في الخلفية: ${refreshResult.pending.toLocaleString('ar-EG')} شات منتظر.`
          : 'كل شاتات الفترة محللة بالفعل.');
        await load();
      }
    } catch (requestError) {
      if (reportIntentKeyRef.current === refreshReportKey) {
        setError(reportRequestError(requestError, 'تعذر تشغيل التحليل الآن. حاول مرة أخرى.'));
      }
    } finally {
      setRefreshing(false);
    }
  }, [activeProject, canManage, load, refreshing, windowRange]);

  useEffect(() => {
    const handleShortcut = (event: KeyboardEvent) => {
      if (event.altKey && event.key.toLowerCase() === 'r' && canManage) {
        event.preventDefault();
        void refresh();
      }
    };
    window.addEventListener('keydown', handleShortcut);
    return () => window.removeEventListener('keydown', handleShortcut);
  }, [canManage, refresh]);

  if (loading && !dashboard) return <ReportsSkeleton />;

  if (!dashboard) return (
    <div className={styles.failure} role="alert">
      <AlertCircle size={28} aria-hidden="true" />
      <h1>تعذر فتح تحليلات المبيعات</h1><p>{error}</p>
      <button type="button" onClick={() => void load()}>إعادة المحاولة</button>
    </div>
  );

  const updateAnalysis = async (conversationId: string, reason?: string) => {
    if (!activeProject) return;
    if (reason) await reportsApi.correctReason(activeProject.id, conversationId, reason);
    else await reportsApi.analyze(activeProject.id, conversationId);
    await load();
  };

  const scheduleOpportunity = async (opportunity: OpportunityItem) => {
    if (!activeProject || opportunity.channel !== 'WhatsApp') {
      throw new Error('Automated follow-ups are currently limited to WhatsApp opportunities.');
    }
    const mutationReportKey = reportRequestKey;
    setError('');
    try {
      const result = await reportsApi.queueFollowUpPlan(
        activeProject.id,
        windowRange,
        'Schedule',
        opportunity.conversationId,
        opportunity.actionToken,
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
    if (!activeProject || opportunity.channel !== 'WhatsApp') {
      throw new Error('Immediate automated follow-ups are currently limited to WhatsApp opportunities.');
    }
    const mutationReportKey = reportRequestKey;
    setError('');
    try {
      const result = await reportsApi.queueFollowUpPlan(
        activeProject.id,
        windowRange,
        'SendNow',
        opportunity.conversationId,
        opportunity.actionToken,
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

  const queueFollowUpPlan = async (action: FollowUpPlanAction) => {
    if (!activeProject) return false;
    const mutationReportKey = reportRequestKey;
    const planToken = action === 'SendNow'
      ? dashboard.followUpPlan.sendNowToken
      : dashboard.followUpPlan.scheduleToken;
    setError('');
    try {
      const queued = await reportsApi.queueFollowUpPlan(
        activeProject.id,
        windowRange,
        action,
        undefined,
        planToken,
      );
      if (reportIntentKeyRef.current === mutationReportKey) {
        setNotice(action === 'SendNow'
          ? `بدأ إرسال المتابعة إلى ${queued.queued} عميل.`
          : `تمت جدولة ${queued.queued} عميل بعد 24 ساعة.`);
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
    <main className={styles.page}>
      <header className={styles.header}>
        <div>
          <h1>مدير المبيعات بالذكاء الاصطناعي</h1>
          <p>يفهم كل شات، يربطه بالحجز والدفع، ويحوّل أسباب التسرب إلى خطوات قابلة للتنفيذ.</p>
        </div>
        <div className={styles.headerActions}>
          <div className={styles.segmented} aria-label="فترة التقرير">{presets.map((presetOption) => <button type="button" aria-pressed={preset === presetOption.value} key={presetOption.value} onClick={() => changePreset(presetOption.value)}>{presetOption.label}</button>)}</div>
          {canManage && <button className={styles.refreshButton} type="button" disabled={refreshing} onClick={() => void refresh()}><RefreshCw size={16} className={refreshing ? styles.spinning : ''} aria-hidden="true" />{refreshing ? 'يجهّز التحليل…' : 'حلّل الكل'}<kbd>Alt R</kbd></button>}
        </div>
      </header>

      <div className={styles.customRangeBar}>
        <div className={styles.customRangeTitle}><CalendarDays size={17} aria-hidden="true" /><span>يوم محدد أو فترة مخصصة</span>{preset === 'custom' && <b>مفعّلة</b>}</div>
        <label>من<input type="date" value={customFromDate} max={customToDate} onChange={(event) => setCustomFromDate(event.target.value)} /></label>
        <label>إلى<input type="date" value={customToDate} min={customFromDate} onChange={(event) => setCustomToDate(event.target.value)} /></label>
        <button type="button" onClick={() => applyCustomRange()}>عرض الفترة</button>
      </div>

      {(error || notice) && <div className={error ? styles.errorBanner : styles.noticeBanner} role={error ? 'alert' : 'status'}>{error || notice}</div>}
      <div className={styles.reportContext}>مجموعة العملاء الذين بدأوا الشات بين {new Date(dashboard.windowStartUtc).toLocaleDateString('ar-EG')} و{new Date(dashboard.windowEndUtc).toLocaleDateString('ar-EG')}، النتائج تُتبع حتى 30 يومًا من بداية كل شات. آخر تحديث {new Date(dashboard.generatedAtUtc).toLocaleTimeString('ar-EG', { timeZone: 'Africa/Cairo', hour: 'numeric', minute: '2-digit' })} بتوقيت القاهرة.</div>

      <MetricStrip dashboard={dashboard} />
      <AiBrief digest={dashboard.aiDigest} coverage={dashboard.analysisCoverage} />
      <div className={styles.twoColumn}><FunnelView funnel={dashboard.funnel} daily={dashboard.daily} onSelectDay={selectReportDay} /><ReasonBreakdown reasons={dashboard.reasons} /></div>
      <FunnelTransitions transitions={dashboard.funnelTransitions} />
      <OpportunityQueue
        key={reportRequestKey}
        opportunities={dashboard.opportunities}
        plan={dashboard.followUpPlan}
        canManage={canManage}
        actionsDisabled={loading || loadedReportKey !== reportRequestKey || dashboard.projectId !== activeProject?.id}
        onSchedule={scheduleOpportunity}
        onSend={sendOpportunity}
        onQueuePlan={queueFollowUpPlan}
      />
      <AskAnalyst onAsk={(question) => {
        if (!activeProject) return Promise.reject(new Error('No project'));
        return reportsApi.ask(activeProject.id, windowRange, question);
      }} />
      <AnalysisTable
        rows={dashboard.analyses}
        canManage={canManage}
        onReanalyze={(id) => updateAnalysis(id)}
        onCorrect={(id, reason) => updateAnalysis(id, reason)}
      />
    </main>
  );
}

function ReportsSkeleton() {
  return <div className={styles.skeletonPage} role="status" aria-label="جاري تحميل تحليلات المبيعات"><div className={styles.skeletonHeader} /><div className={styles.skeletonMetrics}>{[0, 1, 2, 3].map((skeletonIndex) => <span key={skeletonIndex} />)}</div><div className={styles.skeletonPanel} /><div className={styles.skeletonPanel} /></div>;
}
