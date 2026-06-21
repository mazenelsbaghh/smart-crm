'use client';

import React, { useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { useToast } from '../../context/toast-context';
import { api } from '../../services/api';
import { 
  Megaphone, 
  Plus, 
  Calendar, 
  Send, 
  BarChart3, 
  FileText, 
  Users,
  CheckCircle2,
  AlertCircle,
  ChevronLeft,
  ChevronRight
} from 'lucide-react';
import styles from './management.module.css';

interface Segment {
  id: string;
  name: string;
  projectId: string;
}

interface Campaign {
  id: string;
  name: string;
  segmentId: string;
  messageTemplateA: string;
  messageTemplateB?: string;
  status: number; // 0=Draft, 1=Scheduled, 2=Running, 3=Paused, 4=Completed, 5=Cancelled
  scheduledAt?: string;
  sentCount: number;
  deliveredCount: number;
  readCount: number;
  responseCount: number;
}

const statusMap = [
  { name: 'Draft', color: 'statusPending' },
  { name: 'Scheduled', color: 'statusActive' },
  { name: 'Running', color: 'statusActive' },
  { name: 'Paused', color: 'statusPending' },
  { name: 'Completed', color: 'statusCompleted' },
  { name: 'Cancelled', color: 'statusFailed' }
];

const statusNamesAr: Record<string, string> = {
  'Draft': 'مسودة',
  'Scheduled': 'مجدولة',
  'Running': 'قيد التشغيل',
  'Paused': 'متوقفة مؤقتاً',
  'Completed': 'مكتملة',
  'Cancelled': 'ملغاة',
  'Unknown': 'غير معروف'
};

export default function Campaigns() {
  const { activeProject } = useAuth();
  const { showToast } = useToast();
  
  const [campaigns, setCampaigns] = useState<Campaign[]>([]);
  const [segments, setSegments] = useState<Segment[]>([]);
  const [loading, setLoading] = useState(true);

  // Pagination State
  const [currentPage, setCurrentPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const [isModalOpen, setIsModalOpen] = useState(false);
  const [actionLoading, setActionLoading] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  // Form Fields
  const [formName, setFormName] = useState('');
  const [formSegmentId, setFormSegmentId] = useState('');
  const [formTemplateA, setFormTemplateA] = useState('');
  const [formTemplateB, setFormTemplateB] = useState('');
  const [formScheduleDate, setFormScheduleDate] = useState('');

  const fetchData = async () => {
    if (!activeProject) return;
    try {
      setLoading(true);
      const campRes = await api.get<Campaign[]>(`/api/projects/${activeProject.id}/campaigns`);
      const segRes = await api.get<Segment[]>(`/api/projects/${activeProject.id}/segments`);
      
      setCampaigns(campRes.data);
      setSegments(segRes.data);
    } catch (e) {
      console.error('Failed to load campaigns data', e);
      setMessage({ type: 'error', text: 'فشل تحميل بيانات الحملات.' });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchData();
    setCurrentPage(1);
  }, [activeProject]);

  const handleCreateCampaign = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!activeProject) return;
    if (!formName || !formSegmentId || !formTemplateA) {
      showToast('الاسم والمجموعة والقالب (أ) حقول مطلوبة.', 'error');
      return;
    }

    try {
      setActionLoading(true);
      setMessage(null);
      
      // 1. Create campaign as Draft
      const res = await api.post(`/api/projects/${activeProject.id}/campaigns`, {
        name: formName,
        segmentId: formSegmentId,
        messageTemplateA: formTemplateA,
        messageTemplateB: formTemplateB || '',
      });

      const campaignId = res.data.id;

      // 2. If scheduled date is provided, schedule it
      if (formScheduleDate) {
        await api.post(`/api/campaigns/${campaignId}/schedule`, JSON.stringify(new Date(formScheduleDate).toISOString()), {
          headers: { 'Content-Type': 'application/json' }
        });
      }

      setMessage({ type: 'success', text: 'تم إنشاء الحملة بنجاح.' });
      setIsModalOpen(false);
      
      // Reset form
      setFormName('');
      setFormSegmentId('');
      setFormTemplateA('');
      setFormTemplateB('');
      setFormScheduleDate('');
      
      fetchData();
    } catch (e: any) {
      console.error('Failed to create campaign', e);
      setMessage({ type: 'error', text: e.response?.data || 'فشل إنشاء الحملة.' });
    } finally {
      setActionLoading(false);
    }
  };

  const handleScheduleNow = async (campaignId: string) => {
    try {
      setMessage(null);
      await api.post(`/api/campaigns/${campaignId}/schedule`, JSON.stringify(new Date().toISOString()), {
        headers: { 'Content-Type': 'application/json' }
      });
      setMessage({ type: 'success', text: 'تم جدولة الحملة للإرسال الفوري.' });
      fetchData();
    } catch (e) {
      console.error('Failed to schedule campaign', e);
      setMessage({ type: 'error', text: 'فشل جدولة الحملة.' });
    }
  };

  const getSegmentName = (segId: string) => {
    return segments.find(s => s.id === segId)?.name || 'مجموعة غير معروفة';
  };

  const totalPages = Math.ceil(campaigns.length / pageSize) || 1;
  const paginatedCampaigns = campaigns.slice(
    (currentPage - 1) * pageSize,
    currentPage * pageSize
  );

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <div>
          <h1 className={styles.pageTitle}>الحملات الصادرة</h1>
          <p className={styles.pageSubtitle}>جدولة البث التسويقي وتشغيل قوالب اختبار A/B على مجموعات العملاء</p>
        </div>
        <button 
          onClick={() => setIsModalOpen(true)}
          className={`${styles.btn} ${styles.btnPrimary}`}
        >
          <Plus size={16} />
          إنشاء حملة
        </button>
      </div>

      {message && (
        <div className={`glass-panel`} style={{ 
          padding: 'var(--space-md)', 
          borderRight: `4px solid ${message.type === 'success' ? 'hsl(var(--accent-success))' : 'hsl(var(--accent-danger))'}`,
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--space-sm)'
        }}>
          <CheckCircle2 size={18} style={{ color: message.type === 'success' ? 'hsl(var(--accent-success))' : 'hsl(var(--accent-danger))' }} />
          <span style={{ fontSize: '0.9rem', fontWeight: 600 }}>{message.text}</span>
        </div>
      )}

      {/* Campaigns list */}
      <div className={`glass-panel ${styles.panel}`}>
        {loading ? (
          <div className={styles.emptyState}>
            <div className={styles.spinner}></div>
            <p style={{ marginTop: 'var(--space-md)' }}>جاري تحميل الحملات...</p>
          </div>
        ) : campaigns.length === 0 ? (
          <div className={styles.emptyState}>
            <Megaphone size={48} style={{ color: 'hsl(var(--text-muted))' }} />
            <h3 className={styles.emptyStateTitle}>لا توجد حملات مجدولة</h3>
            <p className={styles.emptyStateDesc}>أنشئ أول حملة تسويقية أو إعلامية للوصول إلى عملائك المستهدفين عبر واتساب.</p>
            <button onClick={() => setIsModalOpen(true)} className={`${styles.btn} ${styles.btnPrimary}`}>
              إنشاء حملة
            </button>
          </div>
        ) : (
          <div className={styles.tableWrapper}>
            <table className={styles.table}>
              <thead>
                <tr>
                  <th className={styles.th}>الحملة</th>
                  <th className={styles.th}>المجموعة المستهدفة</th>
                  <th className={styles.th}>الحالة</th>
                  <th className={styles.th}>المؤشرات (مرسل / مسلّم / مقروء / متفاعل)</th>
                  <th className={styles.th} style={{ textAlign: 'center' }}>الإجراءات</th>
                </tr>
              </thead>
              <tbody>
                {paginatedCampaigns.map(camp => {
                  const statusInfo = statusMap[camp.status] || { name: 'Unknown', color: 'statusPending' };
                  return (
                    <tr key={camp.id} className={styles.tr}>
                      <td className={styles.td}>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                          <span style={{ fontWeight: 600, color: 'var(--text-strong)' }}>{camp.name}</span>
                          {camp.scheduledAt && (
                            <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))', display: 'flex', alignItems: 'center', gap: '4px' }}>
                              <Calendar size={12} />
                              {new Date(camp.scheduledAt).toLocaleString('ar-EG')}
                            </span>
                          )}
                        </div>
                      </td>
                      <td className={styles.td}>
                        <span style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                          <Users size={14} style={{ color: 'hsl(var(--accent-secondary))' }} />
                          {getSegmentName(camp.segmentId)}
                        </span>
                      </td>
                      <td className={styles.td}>
                        <span className={`${styles.statusBadge} ${styles[statusInfo.color]}`}>
                          {statusNamesAr[statusInfo.name] || statusInfo.name}
                        </span>
                      </td>
                      <td className={styles.td}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                          <BarChart3 size={14} style={{ color: 'hsl(var(--accent-primary))' }} />
                          <span style={{ fontSize: '0.85rem', fontWeight: 600, color: 'var(--text-strong)' }}>
                            {camp.sentCount} / {camp.deliveredCount} / {camp.readCount} / {camp.responseCount}
                          </span>
                        </div>
                      </td>
                      <td className={styles.td} style={{ textAlign: 'center' }}>
                        {camp.status === 0 ? ( // Draft
                          <button
                            onClick={() => handleScheduleNow(camp.id)}
                            className={`${styles.btn} ${styles.btnPrimary}`}
                            style={{ padding: '4px 10px', fontSize: '0.75rem', display: 'inline-flex', alignItems: 'center', gap: '4px' }}
                          >
                            <Send size={12} />
                            إطلاق الآن
                          </button>
                        ) : (
                          <span style={{ color: 'hsl(var(--text-muted))', fontSize: '0.8rem' }}>مغلقة</span>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>

            {/* Pagination Controls */}
            {campaigns.length > 0 && (
              <div className={styles.pagination}>
                <div className={styles.paginationInfo}>
                  <span>عرض السطور:</span>
                  <select
                    value={pageSize}
                    onChange={(e) => {
                      setPageSize(Number(e.target.value));
                      setCurrentPage(1);
                    }}
                    className={styles.paginationSelect}
                  >
                    {[5, 10, 25, 50].map((size) => (
                      <option key={size} value={size}>
                        {size}
                      </option>
                    ))}
                  </select>
                  <span style={{ marginRight: '12px', marginLeft: '12px' }}>
                    عرض {Math.min((currentPage - 1) * pageSize + 1, campaigns.length)} - {Math.min(currentPage * pageSize, campaigns.length)} من {campaigns.length}
                  </span>
                </div>

                <div className={styles.paginationControls}>
                  <button
                    onClick={() => setCurrentPage(prev => Math.min(prev + 1, totalPages))}
                    disabled={currentPage === totalPages}
                    className={styles.paginationBtn}
                    title="الصفحة التالية"
                  >
                    <ChevronLeft size={16} />
                  </button>

                  {Array.from({ length: totalPages }, (_, i) => i + 1)
                    .filter(page => page === 1 || page === totalPages || Math.abs(page - currentPage) <= 1)
                    .map((page, idx, arr) => {
                      const elements = [];
                      if (idx > 0 && page - arr[idx - 1] > 1) {
                        elements.push(<span key={`ellipsis-${page}`} style={{ color: 'var(--text-soft)', padding: '0 4px' }}>...</span>);
                      }
                      elements.push(
                        <button
                          key={page}
                          onClick={() => setCurrentPage(page)}
                          className={`${styles.paginationBtn} ${currentPage === page ? styles.paginationBtnActive : ''}`}
                        >
                          {page}
                        </button>
                      );
                      return elements;
                    })}

                  <button
                    onClick={() => setCurrentPage(prev => Math.max(prev - 1, 1))}
                    disabled={currentPage === 1}
                    className={styles.paginationBtn}
                    title="الصفحة السابقة"
                  >
                    <ChevronRight size={16} />
                  </button>
                </div>
              </div>
            )}
          </div>
        )}
      </div>

      {/* Creation Modal Overlay */}
      {isModalOpen && (
        <div className={styles.overlay}>
          <div className={`glass-panel ${styles.modal}`}>
            <div className={styles.modalHeader}>
              <h3 className={styles.modalTitle}>جدولة حملة جديدة</h3>
              <button 
                type="button"
                onClick={() => setIsModalOpen(false)} 
                className={styles.closeBtn}
                aria-label="إغلاق"
                style={{ background: 'none', border: 'none', fontSize: '1.5rem', padding: 0 }}
              >
                &times;
              </button>
            </div>

            <form onSubmit={handleCreateCampaign} className={styles.form}>
              <div className={styles.formGroup}>
                <label className={styles.label}>اسم الحملة</label>
                <input 
                  type="text" 
                  value={formName} 
                  onChange={(e) => setFormName(e.target.value)} 
                  placeholder="مثال: عرض كود الخصم الصيفي" 
                  className={styles.input} 
                  required 
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>المجموعة المستهدفة</label>
                <select 
                  value={formSegmentId} 
                  onChange={(e) => setFormSegmentId(e.target.value)} 
                  className={styles.select} 
                  required
                >
                  <option value="">-- اختر مجموعة --</option>
                  {segments.map(seg => (
                    <option key={seg.id} value={seg.id}>{seg.name}</option>
                  ))}
                </select>
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>قالب الرسالة أ</label>
                <textarea 
                  value={formTemplateA} 
                  onChange={(e) => setFormTemplateA(e.target.value)} 
                  placeholder="مرحباً {{name}}، إليك الخصم الخاص بك..." 
                  className={styles.textarea} 
                  required 
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>قالب الرسالة ب (اختياري لاختبار A/B)</label>
                <textarea 
                  value={formTemplateB} 
                  onChange={(e) => setFormTemplateB(e.target.value)} 
                  placeholder="أهلاً {{name}}، احصل على كود الخصم اليوم!" 
                  className={styles.textarea} 
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>تاريخ ووقت الجدولة (اتركه فارغاً للحفظ كمسودة)</label>
                <input 
                  type="datetime-local" 
                  value={formScheduleDate} 
                  onChange={(e) => setFormScheduleDate(e.target.value)} 
                  className={styles.input} 
                />
              </div>

              <div className={styles.formActions}>
                <button 
                  type="button" 
                  onClick={() => setIsModalOpen(false)} 
                  className={`${styles.btn} ${styles.btnSecondary}`}
                  disabled={actionLoading}
                >
                  إلغاء
                </button>
                <button 
                  type="submit" 
                  className={`${styles.btn} ${styles.btnPrimary}`}
                  disabled={actionLoading}
                >
                  {actionLoading ? 'جاري الإنشاء...' : 'حفظ وجدولة'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
