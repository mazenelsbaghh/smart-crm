'use client';

import React, { useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import { 
  BarChart3, 
  TrendingUp, 
  MessageSquare, 
  UserCheck, 
  Clock, 
  Target,
  Zap,
  HelpCircle
} from 'lucide-react';
import styles from './management.module.css';

interface DailyOperationsReport {
  totalConversations: number;
  activeConversations: number;
  completedConversations: number;
  missedFollowUps: number;
  aiAutoRepliesSent: number;
}

interface FollowUpsReport {
  pendingCount: number;
  missedCount: number;
  completedCount: number;
}

interface AiPerformanceReport {
  averageResponseTimeMs: number;
  accuracyScore: number;
  totalTokenUsage: number;
}

export default function Reports() {
  const { activeProject } = useAuth();
  
  const [dailyOps, setDailyOps] = useState<DailyOperationsReport | null>(null);
  const [followUps, setFollowUps] = useState<FollowUpsReport | null>(null);
  const [aiPerf, setAiPerf] = useState<AiPerformanceReport | null>(null);
  
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  const fetchReports = async () => {
    if (!activeProject) return;
    try {
      setLoading(true);
      setError(false);
      
      const [opsRes, fuRes, aiRes] = await Promise.all([
        api.get<DailyOperationsReport>(`/api/projects/${activeProject.id}/reports/daily-operations`),
        api.get<FollowUpsReport>(`/api/projects/${activeProject.id}/reports/follow-ups`),
        api.get<AiPerformanceReport>(`/api/projects/${activeProject.id}/reports/ai-performance`)
      ]);
      
      setDailyOps(opsRes.data);
      setFollowUps(fuRes.data);
      setAiPerf(aiRes.data);
    } catch (e) {
      console.error('Failed to load analytical reports', e);
      setError(true);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchReports();
  }, [activeProject]);

  if (loading) {
    return (
      <div className={styles.emptyState}>
        <div className={styles.spinner}></div>
        <p style={{ marginTop: 'var(--space-md)' }}>جاري تجميع لوحات البيانات الإحصائية...</p>
      </div>
    );
  }

  if (error || !dailyOps || !followUps || !aiPerf) {
    return (
      <div className={styles.emptyState}>
        <BarChart3 size={48} style={{ color: 'hsl(var(--accent-danger))' }} />
        <h3 className={styles.emptyStateTitle}>فشل تحميل التحليلات الإحصائية</h3>
        <p className={styles.emptyStateDesc}>تعذر الاتصال بمحرك تقارير العمليات. تحقق من اتصال الخادم والشبكة.</p>
        <button onClick={fetchReports} className={`${styles.btn} ${styles.btnPrimary}`}>
          إعادة المحاولة
        </button>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <div>
          <h1 className={styles.pageTitle}>التقارير والإحصائيات</h1>
          <p className={styles.pageSubtitle}>مراقبة معدلات تدفق الرسائل، ونسبة إنجاز المتابعات، وأداء نموذج الذكاء الاصطناعي</p>
        </div>
      </div>

      {/* Metrics Row 1: Daily Operations */}
      <h3 style={{ fontSize: '1rem', fontWeight: 700, color: 'hsl(var(--text-muted))', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: 'var(--space-md)' }}>
        العمليات اليومية للرسائل والمحادثات
      </h3>
      <div className={styles.statsGrid}>
        <div className={`glass-panel ${styles.statCard}`}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span className={styles.statLabel}>إجمالي المحادثات</span>
            <MessageSquare size={16} style={{ color: 'hsl(var(--accent-primary))' }} />
          </div>
          <div className={styles.statValue}>{dailyOps.totalConversations}</div>
          <div className={styles.statDesc}>عدد المحادثات المسجلة التراكمية</div>
        </div>

        <div className={`glass-panel ${styles.statCard}`}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span className={styles.statLabel}>المحادثات النشطة</span>
            <TrendingUp size={16} style={{ color: 'hsl(var(--accent-warning))' }} />
          </div>
          <div className={styles.statValue}>{dailyOps.activeConversations}</div>
          <div className={styles.statDesc}>المحادثات المفتوحة أو قيد الانتظار للمراجعة</div>
        </div>

        <div className={`glass-panel ${styles.statCard}`}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span className={styles.statLabel}>المحادثات المكتملة</span>
            <UserCheck size={16} style={{ color: 'hsl(var(--accent-success))' }} />
          </div>
          <div className={styles.statValue}>{dailyOps.completedConversations}</div>
          <div className={styles.statDesc}>محادثات العملاء التي تم حلها وإغلاقها</div>
        </div>

        <div className={`glass-panel ${styles.statCard}`}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span className={styles.statLabel}>الردود التلقائية للذكاء الاصطناعي</span>
            <Zap size={16} style={{ color: 'hsl(var(--accent-secondary))' }} />
          </div>
          <div className={styles.statValue}>{dailyOps.aiAutoRepliesSent}</div>
          <div className={styles.statDesc}>الردود التلقائية التي أرسلها البوت للعملاء</div>
        </div>
      </div>

      {/* Row 2: Follow-ups and AI Accuracy */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr', gap: 'var(--space-lg)', marginTop: 'var(--space-md)' }}>
        {/* Left Side: CRM Follow-ups Status */}
        <div className={`glass-panel ${styles.panel}`}>
          <div className={styles.panelHeader}>
            <h4 className={styles.panelTitle}>معدل الالتزام بجدول المتابعات للعملاء</h4>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 'var(--space-md)' }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', padding: 'var(--space-md)', background: 'rgba(255,255,255,0.02)', borderRadius: 'var(--radius-sm)' }}>
              <span style={{ fontSize: '0.75rem', fontWeight: 600, color: 'hsl(var(--text-muted))' }}>متابعات معلقة</span>
              <span style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--text-strong)' }}>{followUps.pendingCount}</span>
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', padding: 'var(--space-md)', background: 'rgba(255,255,255,0.02)', borderRadius: 'var(--radius-sm)' }}>
              <span style={{ fontSize: '0.75rem', fontWeight: 600, color: 'hsl(var(--text-muted))' }}>متابعات مكتملة</span>
              <span style={{ fontSize: '1.5rem', fontWeight: 700, color: 'hsl(var(--accent-success))' }}>{followUps.completedCount}</span>
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', padding: 'var(--space-md)', background: 'rgba(239, 68, 68, 0.05)', borderRadius: 'var(--radius-sm)', border: '1px solid rgba(239, 68, 68, 0.1)' }}>
              <span style={{ fontSize: '0.75rem', fontWeight: 600, color: 'hsl(var(--accent-danger))' }}>متابعات فائتة</span>
              <span style={{ fontSize: '1.5rem', fontWeight: 700, color: 'hsl(0, 100%, 65%)' }}>{followUps.missedCount}</span>
            </div>
          </div>
        </div>

        {/* Right Side: AI Engine Metrics */}
        <div className={`glass-panel ${styles.panel}`}>
          <div className={styles.panelHeader}>
            <h4 className={styles.panelTitle}>تحليلات أداء ذكاء AI Gemini</h4>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 'var(--space-md)' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-md)', padding: 'var(--space-md)', background: 'rgba(255,255,255,0.02)', borderRadius: 'var(--radius-sm)' }}>
              <div style={{ width: '40px', height: '40px', borderRadius: '50%', background: 'var(--accent-soft)', display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'hsl(var(--accent-primary))', padding: '10px' }}>
                <Clock size={20} />
              </div>
              <div>
                <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))', display: 'block' }}>متوسط زمن الاستجابة</span>
                <span style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--text-strong)' }}>{aiPerf.averageResponseTimeMs} مللي ثانية</span>
              </div>
            </div>

            <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-md)', padding: 'var(--space-md)', background: 'rgba(255,255,255,0.02)', borderRadius: 'var(--radius-sm)' }}>
              <div style={{ width: '40px', height: '40px', borderRadius: '50%', background: 'rgba(236, 72, 153, 0.1)', display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'hsl(var(--accent-secondary))', padding: '10px' }}>
                <Target size={20} />
              </div>
              <div>
                <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))', display: 'block' }}>دقة الاقتراحات والردود</span>
                <span style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--text-strong)' }}>{Math.round(aiPerf.accuracyScore * 100)}%</span>
              </div>
            </div>

            <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-md)', padding: 'var(--space-md)', background: 'rgba(255, 255, 255, 0.02)', borderRadius: 'var(--radius-sm)' }}>
              <div style={{ width: '40px', height: '40px', borderRadius: '50%', background: 'rgba(245, 158, 11, 0.1)', display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'hsl(var(--accent-warning))', padding: '10px' }}>
                <Zap size={20} />
              </div>
              <div>
                <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))', display: 'block' }}>إجمالي استهلاك الرموز (Tokens)</span>
                <span style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--text-strong)' }}>{aiPerf.totalTokenUsage.toLocaleString('ar-EG')} رمز</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
