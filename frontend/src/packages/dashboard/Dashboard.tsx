'use client';

import React, { useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { crmService, Customer, Deal } from '../../services/crm';
import { useRouter } from 'next/navigation';
import { 
  Users, 
  DollarSign, 
  Target, 
  TrendingUp, 
  Sparkles, 
  MessageSquare, 
  RefreshCw, 
  ArrowRight,
  UserCheck
} from 'lucide-react';
import styles from './dashboard.module.css';

export default function Dashboard() {
  const { activeProject } = useAuth();
  const router = useRouter();
  
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [deals, setDeals] = useState<Deal[]>([]);
  const [loading, setLoading] = useState(true);
  const [recalculating, setRecalculating] = useState(false);

  const fetchDashboardData = async () => {
    if (!activeProject) return;
    try {
      setLoading(true);
      const custData = await crmService.getCustomers(activeProject.id);
      const dealData = await crmService.getDeals(activeProject.id);
      setCustomers(custData);
      setDeals(dealData);
    } catch (err) {
      console.error('Failed to load dashboard data', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchDashboardData();
  }, [activeProject]);

  const handleRecalculate = async () => {
    if (!activeProject || recalculating) return;
    setRecalculating(true);
    try {
      await crmService.recalculateAnalytics(activeProject.id);
      await fetchDashboardData();
    } catch (err) {
      console.error('Failed to recalculate metrics', err);
    } finally {
      setRecalculating(false);
    }
  };

  if (loading) {
    return (
      <div className={styles.loadingBox}>
        <div className={styles.spinner}></div>
        <p>جاري تحميل مؤشرات التشغيل...</p>
      </div>
    );
  }

  // Calculate metrics
  const totalCustomers = customers.length;
  const activeDeals = deals.filter(d => d.status === 0).length;
  const closedWonDeals = deals.filter(d => d.status === 1);
  const revenue = closedWonDeals.reduce((sum, d) => sum + d.amount, 0);
  
  // Average Lead Score
  const avgLeadScore = totalCustomers > 0 
    ? Math.round(customers.reduce((sum, c) => sum + (c.leadScore || 0), 0) / totalCustomers) 
    : 0;

  // Recent 5 customers
  const recentCustomers = [...customers]
    .sort((a, b) => (b.leadScore || 0) - (a.leadScore || 0))
    .slice(0, 5);

  return (
    <div className={styles.container}>
      {/* Title Header */}
      <div className={styles.dashboardHeader}>
        <div>
          <h1 className={styles.pageTitle}>نظرة عامة على الأداء</h1>
          <p className={styles.pageSubtitle}>مؤشرات التشغيل والمبيعات للمشروع {activeProject?.name}</p>
        </div>
        <button 
          onClick={handleRecalculate} 
          disabled={recalculating} 
          className={styles.refreshButton}
        >
          <RefreshCw size={16} className={recalculating ? styles.spinIcon : ''} />
          <span>{recalculating ? 'جاري التحديث...' : 'تحديث المؤشرات'}</span>
        </button>
      </div>

      {/* KPI Stats Cards */}
      <div className={styles.statsGrid}>
        <div className={`glass-panel ${styles.statCard}`}>
          <div className={styles.statIconContainer}>
            <Users size={24} style={{ color: 'hsl(var(--accent-primary))' }} />
          </div>
          <div>
            <span className={styles.statLabel}>إجمالي العملاء</span>
            <h2 className={styles.statValue}>{totalCustomers}</h2>
          </div>
        </div>

        <div className={`glass-panel ${styles.statCard}`}>
          <div className={styles.statIconContainer}>
            <Target size={24} style={{ color: 'hsl(var(--accent-secondary))' }} />
          </div>
          <div>
            <span className={styles.statLabel}>الصفقات المفتوحة</span>
            <h2 className={styles.statValue}>{activeDeals}</h2>
          </div>
        </div>

        <div className={`glass-panel ${styles.statCard}`}>
          <div className={styles.statIconContainer}>
            <DollarSign size={24} style={{ color: 'hsl(140, 100%, 65%)' }} />
          </div>
          <div>
            <span className={styles.statLabel}>الإيراد المغلق</span>
            <h2 className={styles.statValue}>${revenue.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</h2>
          </div>
        </div>

        <div className={`glass-panel ${styles.statCard}`}>
          <div className={styles.statIconContainer}>
            <TrendingUp size={24} style={{ color: 'hsl(200, 100%, 60%)' }} />
          </div>
          <div>
            <span className={styles.statLabel}>متوسط تقييم العملاء</span>
            <h2 className={styles.statValue}>{avgLeadScore}/100</h2>
          </div>
        </div>
      </div>

      {/* Two Column Grid */}
      <div className={styles.contentGrid}>
        {/* Left Column: Recent Hot Leads */}
        <div className={`glass-panel ${styles.leadsPanel}`}>
          <div className={styles.panelHeader}>
            <h3 className={styles.panelTitle}>أهم العملاء المحتملين</h3>
            <span className={styles.badge}>الأعلى تقييما أولا</span>
          </div>

          <div className={styles.leadsList}>
            {recentCustomers.length === 0 ? (
              <div className={styles.emptyLeads}>لا توجد عملاء في قاعدة البيانات</div>
            ) : (
              recentCustomers.map(c => (
                <div key={c.id} className={styles.leadItem} onClick={() => router.push('/crm')}>
                  <div className={styles.leadDetails}>
                    <span className={styles.leadName}>{c.name || 'عميل بدون اسم'}</span>
                    <span className={styles.leadPhone}>{c.phoneNumber}</span>
                  </div>
                  <div className={styles.leadRight}>
                    <span className={styles.leadStage}>{c.pipelineStage || 'جديد'}</span>
                    <div className={styles.scorePill}>
                      <Sparkles size={10} style={{ marginRight: '4px', color: 'hsl(var(--accent-secondary))' }} />
                      التقييم: {c.leadScore}
                    </div>
                  </div>
                </div>
              ))
            )}
          </div>
        </div>

        {/* Right Column: Quick actions / workflow shortcuts */}
        <div className={styles.actionsColumn}>
          <div className={`glass-panel ${styles.actionCard}`}>
            <h3 className={styles.panelTitle}>إجراءات سريعة</h3>
            <div className={styles.actionsGrid}>
              <div 
                className={styles.actionShortcut}
                onClick={() => router.push('/inbox')}
              >
                <div className={styles.shortcutIconBg}>
                  <MessageSquare size={20} />
                </div>
                <div>
                  <h4 className={styles.shortcutTitle}>إدارة المحادثات</h4>
                  <p className={styles.shortcutDesc}>رد على العملاء في الوقت الفعلي</p>
                </div>
                <ArrowRight size={16} className={styles.shortcutArrow} />
              </div>

              <div 
                className={styles.actionShortcut}
                onClick={() => router.push('/crm/pipeline')}
              >
                <div className={styles.shortcutIconBg}>
                  <UserCheck size={20} />
                </div>
                <div>
                  <h4 className={styles.shortcutTitle}>مسار الصفقات</h4>
                  <p className={styles.shortcutDesc}>تابع مراحل البيع والفرص</p>
                </div>
                <ArrowRight size={16} className={styles.shortcutArrow} />
              </div>
            </div>
          </div>

          {/* AI suggestion panel info */}
          <div className={`glass-panel ${styles.aiPanel}`}>
            <div className={styles.aiHeader}>
              <Sparkles size={20} style={{ color: 'hsl(var(--accent-secondary))' }} />
              <h3 className={styles.panelTitle}>مساعد الذكاء الاصطناعي</h3>
            </div>
            <p className={styles.aiText}>
              مساعد Gemini يعمل على تحليل رسائل العملاء الواردة لاستخراج الميزانية والاهتمامات والمدينة ونية الشراء. المقترحات الواثقة يتم حفظها تلقائيا، والعناصر الحساسة تنتظر موافقتك.
            </p>
            <div className={styles.aiIndicator}>
              <div className={styles.pulseDot}></div>
              <span>في انتظار رسائل واتساب الجديدة...</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
