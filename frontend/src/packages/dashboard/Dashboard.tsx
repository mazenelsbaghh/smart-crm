'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import {
  Activity,
  LayoutDashboard,
  CalendarClock,
  ArrowLeft,
  MessageSquare,
  RefreshCw,
  Sparkles,
  Star,
  Users,
  Zap,
} from 'lucide-react';
import { useAuth } from '../../context/auth-context';
import { crmService, DashboardSummary } from '../../services/crm';
import PageHeader from '../../components/ui/PageHeader';
import Button from '../../components/ui/Button';
import styles from './dashboard.module.css';

const CAIRO_TIME_ZONE = 'Africa/Cairo';

function formatCount(value: number | undefined) {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0
    ? value.toLocaleString('ar-EG') : 'غير متاح';
}

function DashboardSkeleton() {
  return (
    <div className={styles.container} aria-busy="true" aria-label="جاري تحميل لوحة التحكم">
      <div className={styles.dashboardHeader}>
        <div>
          <div className={styles.skeleton} style={{ width: '220px', height: '28px' }} />
          <div className={styles.skeleton} style={{ width: '300px', height: '14px', marginTop: '10px' }} />
        </div>
      </div>
      <div className={styles.statsGrid}>
        {[1, 2, 3].map((metric) => (
          <div key={metric} className={`glass-panel ${styles.statCard} ${styles.skeletonCard}`}>
            <div className={styles.skeleton} style={{ width: '42px', height: '42px', borderRadius: 'var(--radius-md)' }} />
            <div className={styles.skeleton} style={{ width: '90px', height: '14px', marginTop: '16px' }} />
            <div className={styles.skeleton} style={{ width: '70px', height: '24px', marginTop: '8px' }} />
          </div>
        ))}
      </div>
    </div>
  );
}

export default function Dashboard() {
  const { activeProject } = useAuth();
  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [lastUpdatedAt, setLastUpdatedAt] = useState<Date | null>(null);
  const [lastLoadedProjectId, setLastLoadedProjectId] = useState<string | null>(null);
  const [stageSelection, setStageSelection] = useState({ projectId: '', stage: 'الكل' });
  const stageFilter = stageSelection.projectId === activeProject?.id ? stageSelection.stage : 'الكل';
  const [lastLoadedStage, setLastLoadedStage] = useState<string | null>(null);
  const loadRequestIdRef = React.useRef(0);

  const fetchDashboardData = useCallback(async () => {
    const requestId = ++loadRequestIdRef.current;
    if (!activeProject) {
      setLoading(false);
      setLoadError('تعذر تحميل مساحة العمل. أعد المحاولة أو تواصل مع المدير.');
      return;
    }

    setLoading(true);
    setLoadError(null);
    try {
      const response = await crmService.getDashboardSummary(activeProject.id, stageFilter === 'الكل' ? 'All' : stageFilter);
      if (requestId !== loadRequestIdRef.current) return;
      setSummary(response);
      setLastLoadedStage(stageFilter);
      setLastUpdatedAt(new Date());
      setLastLoadedProjectId(activeProject.id);
    } catch (error) {
      if (requestId !== loadRequestIdRef.current) return;
      console.error('Failed to load dashboard data', error);
      setLoadError('تعذر تحميل بيانات لوحة التحكم. لم يتم استبدالها بأرقام تقديرية.');
    } finally {
      if (requestId === loadRequestIdRef.current) setLoading(false);
    }
  }, [activeProject, stageFilter]);

  useEffect(() => {
    const requestTimer = window.setTimeout(() => void fetchDashboardData(), 0);
    return () => {
      window.clearTimeout(requestTimer);
      loadRequestIdRef.current += 1;
    };
  }, [fetchDashboardData]);

  const metrics = useMemo(() => {
    return {
      customers: summary?.customers,
      chats: summary?.chats,
      conversionRate: summary && Number.isFinite(summary.totalDeals) && summary.totalDeals > 0 &&
        Number.isFinite(summary.wonDeals) && summary.wonDeals >= 0 && summary.wonDeals <= summary.totalDeals ? Math.round((summary.wonDeals / summary.totalDeals) * 1000) / 10 : null,
    };
  }, [summary]);

  const stages = useMemo(
    () => ['الكل', ...(summary?.stages ?? [])],
    [summary],
  );

  const visibleLeads = useMemo(
    () => summary?.topLeads ?? [],
    [summary],
  );

  if (loading && !lastUpdatedAt) return <DashboardSkeleton />;

  const formattedUpdatedAt = lastUpdatedAt?.toLocaleString('ar-EG', {
    timeZone: CAIRO_TIME_ZONE,
    dateStyle: 'medium',
    timeStyle: 'short',
  });
  const hasLoadedData = lastUpdatedAt !== null && lastLoadedProjectId === activeProject?.id;
  const hasLoadedStage = hasLoadedData && lastLoadedStage === stageFilter;
  const displayedLeads = hasLoadedStage ? visibleLeads : [];

  return (
    <section className={styles.container} aria-labelledby="dashboard-title">
      <PageHeader title="نظرة عامة" icon={<LayoutDashboard size={24} aria-hidden="true" />} titleId="dashboard-title" description={`العملاء والمحادثات في ${activeProject?.name ?? 'مشروعك'}`}
        eyebrow={<span role="status">{hasLoadedData && formattedUpdatedAt ? `آخر تحديث: ${formattedUpdatedAt} بتوقيت القاهرة` : 'بانتظار تحميل بيانات المشروع'}</span>}
        actions={<><Link href="/inbox" className={styles.primaryLink}><MessageSquare size={17} aria-hidden="true" />افتح المحادثات</Link><Button onClick={() => void fetchDashboardData()} disabled={!activeProject} loading={loading}>
          <RefreshCw size={16} aria-hidden="true" />تحديث المؤشرات
        </Button></>} />

      {loadError && (
        <div className="glass-panel" role="alert" style={{ padding: 'var(--space-md)', marginBottom: 'var(--space-md)' }}>
          <p>{loadError}</p>
          {activeProject && (
            <button type="button" className={styles.refreshButton} onClick={() => void fetchDashboardData()}>
              إعادة المحاولة
            </button>
          )}
        </div>
      )}

      <section className={styles.statsGrid} aria-label="مؤشرات المشروع">
        <article className={`glass-panel ${styles.statCard}`}>
          <div className={styles.statIconBox}><MessageSquare size={22} aria-hidden="true" /></div>
          <span className={styles.statLabel}>إجمالي المحادثات</span>
          <strong className={styles.statValue}>{hasLoadedData ? formatCount(metrics.chats) : 'غير متاح'}</strong>
        </article>
        <article className={`glass-panel ${styles.statCard}`}>
          <div className={styles.statIconBox}><Users size={22} aria-hidden="true" /></div>
          <span className={styles.statLabel}>إجمالي العملاء</span>
          <strong className={styles.statValue}>{hasLoadedData ? formatCount(metrics.customers) : 'غير متاح'}</strong>
        </article>
        <article className={`glass-panel ${styles.statCard}`}>
          <div className={styles.statIconBox}><Star size={22} aria-hidden="true" /></div>
          <span className={styles.statLabel}>نسبة الصفقات المسجلة كرابحة</span>
          <strong className={styles.statValue}>
            {!hasLoadedData || metrics.conversionRate === null ? 'غير متاح' : `${metrics.conversionRate.toLocaleString('ar-EG')}٪`}
          </strong>
        </article>
      </section>

      <div className={styles.contentGrid}>
        <section className={`glass-panel ${styles.leadsPanel}`} aria-labelledby="top-leads-heading">
          <div className={styles.panelHeader}>
            <div>
              <h2 id="top-leads-heading" className={styles.panelTitle}><Users size={20} aria-hidden="true" />أعلى العملاء تقييمًا</h2>
              <p className={styles.panelSubtitle}>أول 10 نتائج من بيانات CRM الحالية</p>
            </div>
            <label className={styles.selectWrapper}>
              <span>المرحلة</span>
              <select value={hasLoadedData ? stageFilter : 'الكل'} onChange={(event) => setStageSelection({ projectId: activeProject!.id, stage: event.target.value })} className={styles.stageSelect} disabled={!hasLoadedData || loading}>
                {(hasLoadedData ? stages : ['الكل']).map((stage) => <option key={stage} value={stage}>{stage}</option>)}
              </select>
            </label>
          </div>
          <div className={styles.tableContainer}>
            <table className={styles.leadsTable}>
              <caption className="sr-only">العملاء الأعلى تقييمًا في CRM</caption>
              <thead><tr><th>العميل</th><th>التقييم</th><th>المرحلة</th><th>الإجراء</th></tr></thead>
              <tbody>
                {displayedLeads.length === 0 ? (
                  <tr><td colSpan={4} className={styles.emptyTable}>{hasLoadedStage ? 'لا توجد نتائج. غيّر المرحلة أو أضف بيانات CRM.' : loading ? 'جاري تحميل نتائج المرحلة…' : 'بيانات هذه المرحلة غير متاحة؛ أعد المحاولة.'}</td></tr>
                ) : displayedLeads.map((customer) => (
                  <tr key={customer.id}>
                    <td data-label="العميل">
                      <div className={styles.customerProfileCell}>
                        <div className={styles.avatarLetter}>{(customer.name || '؟')[0]}</div>
                        <div><div className={styles.customerName}>{customer.name || 'اسم غير مسجل'}</div><div className={styles.customerPhone}>{customer.phoneNumber || 'هاتف غير مسجل'}</div></div>
                      </div>
                    </td>
                    <td data-label="التقييم"><div className={styles.scoreCell}><Zap size={14} aria-hidden="true" /><span>{Number.isFinite(customer.leadScore) ? customer.leadScore : 'غير متاح'}</span></div></td>
                    <td data-label="المرحلة"><span className={styles.stageBadge}>{customer.pipelineStage || 'غير محددة'}</span></td>
                    <td data-label="الإجراء"><Link className={styles.actionBtn} href={`/inbox?customerId=${encodeURIComponent(customer.id)}&startConversation=1`} aria-label={`بدء محادثة مع ${customer.name || customer.phoneNumber}`}><MessageSquare size={16} aria-hidden="true" />محادثة</Link></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        <aside className={styles.actionsColumn} aria-label="أدوات المتابعة وحالة المشروع">
          <nav className={styles.quickLinks} aria-label="متابعة العمل">
            <h2>متابعة العمل</h2>
            <Link href="/management/follow-ups"><CalendarClock size={19} aria-hidden="true" /><span>المتابعات والتذكيرات<small>مواعيد التواصل مع العملاء</small></span><ArrowLeft size={16} aria-hidden="true" /></Link>
            <Link href="/crm"><Users size={19} aria-hidden="true" /><span>سجل العملاء<small>البيانات والوسوم وملف العميل</small></span><ArrowLeft size={16} aria-hidden="true" /></Link>
            <Link href="/management/reports"><Activity size={19} aria-hidden="true" /><span>تقارير المبيعات<small>نتائج التواصل والحجز</small></span><ArrowLeft size={16} aria-hidden="true" /></Link>
          </nav>
          <section className={styles.healthPanel}>
            <h2 className={styles.healthTitle}><Sparkles size={18} aria-hidden="true" />الرد الآلي</h2>
            <strong className={styles.aiKpiTitle}>{!activeProject ? 'غير متاح' : activeProject.settings?.aiAutoReplyEnabled ? 'مفعّل في إعدادات المشروع' : 'غير مفعّل'}</strong>
            <p className={styles.aiKpiDesc}>هذه حالة إعداد فقط، وليست إثباتًا على اتصال مزود الذكاء الاصطناعي.</p>
          </section>
          <details className={styles.healthPanel}>
            <summary className={styles.healthTitle}><Activity size={18} aria-hidden="true" />حالة البيانات وتحديثها</summary>
            <p className={styles.aiKpiDesc}>تُعرض الأرقام فقط بعد نجاح تحميل CRM والمحادثات. صحة الخادم واستهلاك API غير متاحين من هذه الواجهة.</p>
            <button
              type="button"
              className={styles.refreshButton}
              onClick={() => void fetchDashboardData()}
              disabled={!activeProject || loading}
              title={!activeProject ? 'مساحة العمل غير متاحة' : undefined}
            >
              <RefreshCw size={16} aria-hidden="true" /> {loading ? 'جاري التحميل...' : 'إعادة تحميل المصادر'}
            </button>
          </details>
        </aside>
      </div>
    </section>
  );
}
