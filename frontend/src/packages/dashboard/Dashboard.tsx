'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  Activity,
  ArrowLeft,
  MessageSquare,
  RefreshCw,
  Sparkles,
  Star,
  Users,
  Zap,
} from 'lucide-react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import { crmService, Customer, Deal } from '../../services/crm';
import styles from './dashboard.module.css';

const CAIRO_TIME_ZONE = 'Africa/Cairo';

function DashboardSkeleton() {
  return (
    <div className={styles.container} aria-busy="true" aria-label="جاري تحميل لوحة التحكم">
      <div className={styles.dashboardHeader}>
        <div>
          <div className={`${styles.skeleton} ${styles.skeletonTitle}`} style={{ width: '220px', height: '28px' }} />
          <div className={styles.skeleton} style={{ width: '300px', height: '14px', marginTop: '10px' }} />
        </div>
      </div>
      <div className={styles.statsGrid}>
        {[1, 2, 3, 4].map((metric) => (
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
  const router = useRouter();
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [deals, setDeals] = useState<Deal[]>([]);
  const [totalChats, setTotalChats] = useState(0);
  const [loading, setLoading] = useState(true);
  const [recalculating, setRecalculating] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [lastUpdatedAt, setLastUpdatedAt] = useState<Date | null>(null);
  const [lastLoadedProjectId, setLastLoadedProjectId] = useState<string | null>(null);
  const [stageFilter, setStageFilter] = useState('الكل');
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
      const [customerList, dealList, conversationsResponse] = await Promise.all([
        crmService.getCustomers(activeProject.id),
        crmService.getDeals(activeProject.id),
        api.get<unknown[]>(`/api/projects/${activeProject.id}/conversations`, {
          params: { channel: 'All', limit: 1000 },
        }),
      ]);
      if (requestId !== loadRequestIdRef.current) return;
      setCustomers(customerList);
      setDeals(dealList);
      setTotalChats(conversationsResponse.data.length);
      setLastUpdatedAt(new Date());
      setLastLoadedProjectId(activeProject.id);
    } catch (error) {
      if (requestId !== loadRequestIdRef.current) return;
      console.error('Failed to load dashboard data', error);
      setLoadError('تعذر تحميل بيانات لوحة التحكم. لم يتم استبدالها بأرقام تقديرية.');
    } finally {
      if (requestId === loadRequestIdRef.current) setLoading(false);
    }
  }, [activeProject]);

  useEffect(() => {
    const requestTimer = window.setTimeout(() => void fetchDashboardData(), 0);
    return () => window.clearTimeout(requestTimer);
  }, [fetchDashboardData]);

  const recalculateMetrics = async () => {
    if (!activeProject || recalculating) return;
    setRecalculating(true);
    try {
      await crmService.recalculateAnalytics(activeProject.id);
      await fetchDashboardData();
    } catch (error) {
      console.error('Failed to recalculate metrics', error);
      setLoadError('تعذر إعادة حساب المؤشرات. البيانات المعروضة هي آخر بيانات تم تحميلها بنجاح.');
    } finally {
      setRecalculating(false);
    }
  };

  const metrics = useMemo(() => {
    const wonDeals = deals.filter((deal) => deal.status === 1).length;
    return {
      customers: customers.length,
      chats: totalChats,
      conversionRate: deals.length > 0 ? Math.round((wonDeals / deals.length) * 1000) / 10 : null,
    };
  }, [customers.length, deals, totalChats]);

  const stages = useMemo(
    () => ['الكل', ...Array.from(new Set(customers.map((customer) => customer.pipelineStage).filter(Boolean)))],
    [customers],
  );

  const visibleLeads = useMemo(
    () => customers
      .filter((customer) => stageFilter === 'الكل' || customer.pipelineStage === stageFilter)
      .sort((first, second) => second.leadScore - first.leadScore)
      .slice(0, 10),
    [customers, stageFilter],
  );

  if (loading && !lastUpdatedAt) return <DashboardSkeleton />;

  const formattedUpdatedAt = lastUpdatedAt?.toLocaleString('ar-EG', {
    timeZone: CAIRO_TIME_ZONE,
    dateStyle: 'medium',
    timeStyle: 'short',
  });
  const hasLoadedData = lastUpdatedAt !== null && lastLoadedProjectId === activeProject?.id;
  const displayedLeads = hasLoadedData ? visibleLeads : [];

  return (
    <section className={styles.container} aria-labelledby="dashboard-title">
      <div className={styles.dashboardHeader}>
        <div>
          <div className={styles.breadcrumbs} aria-label="مسار الصفحة">
            <span>الرئيسية</span>
            <span className={styles.chevron} aria-hidden="true">/</span>
            <span className={styles.activeBreadcrumb}>لوحة التحكم</span>
          </div>
          <h1 id="dashboard-title" className={styles.pageTitle}>نظرة عامة على الأداء</h1>
          <p className={styles.pageSubtitle}>
            بيانات المشروع {activeProject?.name ?? 'غير محدد'}، دون تقديرات أو قيم تجريبية
          </p>
          <p className={styles.panelSubtitle} role="status">
            {hasLoadedData && formattedUpdatedAt ? `آخر تحميل ناجح: ${formattedUpdatedAt} بتوقيت القاهرة` : 'لم يتم تحميل مصدر بيانات لهذا المشروع بعد'}
          </p>
        </div>
        <div className={styles.headerActions}>
          <span className={styles.rangeButton}>كل البيانات المتاحة من المصدر</span>
          <button
            type="button"
            onClick={recalculateMetrics}
            disabled={!activeProject || recalculating}
            title={!activeProject ? 'مساحة العمل غير متاحة' : undefined}
            className={styles.refreshButton}
          >
            <RefreshCw size={16} aria-hidden="true" className={recalculating ? styles.spinIcon : ''} />
            {recalculating ? 'جاري التحديث...' : 'تحديث المؤشرات'}
          </button>
        </div>
      </div>

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
          <span className={styles.statLabel}>المحادثات المحمّلة</span>
          <strong className={styles.statValue}>{hasLoadedData ? metrics.chats.toLocaleString('ar-EG') : 'غير متاح'}</strong>
        </article>
        <article className={`glass-panel ${styles.statCard}`}>
          <div className={styles.statIconBox}><Users size={22} aria-hidden="true" /></div>
          <span className={styles.statLabel}>إجمالي العملاء</span>
          <strong className={styles.statValue}>{hasLoadedData ? metrics.customers.toLocaleString('ar-EG') : 'غير متاح'}</strong>
        </article>
        <article className={`glass-panel ${styles.statCard}`}>
          <div className={styles.statIconBox}><Star size={22} aria-hidden="true" /></div>
          <span className={styles.statLabel}>نسبة الصفقات المسجلة كرابحة</span>
          <strong className={styles.statValue}>
            {!hasLoadedData || metrics.conversionRate === null ? 'غير متاح' : `${metrics.conversionRate.toLocaleString('ar-EG')}٪`}
          </strong>
        </article>
        <article className={`glass-panel ${styles.statCard} ${styles.aiKpiCard}`}>
          <div className={styles.statIconBox}><Sparkles size={20} aria-hidden="true" /></div>
          <span className={styles.statLabel}>الرد الآلي</span>
          <strong className={styles.aiKpiTitle}>
            {!activeProject ? 'غير متاح' : activeProject.settings?.aiAutoReplyEnabled ? 'مفعّل في إعدادات المشروع' : 'غير مفعّل'}
          </strong>
          <p className={styles.aiKpiDesc}>هذه حالة إعداد فقط، وليست إثباتًا على اتصال مزود الذكاء الاصطناعي.</p>
        </article>
      </section>

      <div className={styles.contentGrid}>
        <section className={`glass-panel ${styles.leadsPanel}`} aria-labelledby="top-leads-heading">
          <div className={styles.panelHeader}>
            <div>
              <h2 id="top-leads-heading" className={styles.panelTitle}>أعلى العملاء تقييمًا</h2>
              <p className={styles.panelSubtitle}>أول 10 نتائج من بيانات CRM الحالية</p>
            </div>
            <label className={styles.selectWrapper}>
              <span className="sr-only">تصفية حسب المرحلة</span>
              <select value={hasLoadedData ? stageFilter : 'الكل'} onChange={(event) => setStageFilter(event.target.value)} className={styles.stageSelect} disabled={!hasLoadedData || loading}>
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
                  <tr><td colSpan={4} className={styles.emptyTable}>{hasLoadedData ? 'لا توجد نتائج. غيّر المرحلة أو أضف بيانات CRM.' : 'بيانات CRM غير متاحة قبل أول تحميل ناجح.'}</td></tr>
                ) : displayedLeads.map((customer) => (
                  <tr key={customer.id}>
                    <td>
                      <div className={styles.customerProfileCell}>
                        <div className={styles.avatarLetter}>{(customer.name || '؟')[0]}</div>
                        <div><div className={styles.customerName}>{customer.name || 'اسم غير مسجل'}</div><div className={styles.customerPhone}>{customer.phoneNumber || 'هاتف غير مسجل'}</div></div>
                      </div>
                    </td>
                    <td><div className={styles.scoreCell}><Zap size={14} aria-hidden="true" /><span>{Number.isFinite(customer.leadScore) ? customer.leadScore : 'غير متاح'}</span></div></td>
                    <td><span className={styles.stageBadge}>{customer.pipelineStage || 'غير محددة'}</span></td>
                    <td><button type="button" className={styles.actionBtn} onClick={() => router.push('/crm')} aria-label={`فتح سجل العميل ${customer.name || customer.phoneNumber}`}><ArrowLeft size={16} aria-hidden="true" /></button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        <aside className={styles.actionsColumn} aria-label="حالة المصادر">
          <section className={`glass-panel ${styles.healthPanel}`}>
            <h2 className={styles.healthTitle}><Activity size={18} aria-hidden="true" /> حالة البيانات</h2>
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
          </section>
        </aside>
      </div>
    </section>
  );
}
