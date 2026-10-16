'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { useToast } from '../../context/toast-context';
import { api } from '../../services/api';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import Dialog from '../../components/shared/Dialog';
import {
  Megaphone,
  Plus,
  Calendar,
  Send,
  CheckCheck,
  Eye,
  MessageSquare,
  Clock,
  FileText,
  CheckCircle2,
  PauseCircle,
  CircleX,
  Users,
  ChevronLeft,
  ChevronRight
} from 'lucide-react';
import PageHeader from '../../components/ui/PageHeader';
import FilterTabs from '../../components/ui/FilterTabs';
import SearchField from '../../components/ui/SearchField';
import StatusMessage from '../../components/ui/StatusMessage';
import styles from './management.module.css';
import dialogStyles from './ManagementDialogs.module.css';
import { useListFilters } from './hooks/use-list-filters';

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
  status: number; // 0=Draft, 1=Scheduled, 2=Running, 3=Paused, 4=Completed, 5=Cancelled, 6=Preparing
  scheduledAt?: string;
  sentCount: number;
  deliveredCount: number | null;
  readCount: number | null;
  responseCount: number | null;
}

const statusMap = [
  { name: 'Draft', color: 'statusPending' },
  { name: 'Scheduled', color: 'statusActive' },
  { name: 'Running', color: 'statusActive' },
  { name: 'Paused', color: 'statusPending' },
  { name: 'Completed', color: 'statusCompleted' },
  { name: 'Cancelled', color: 'statusFailed' },
  { name: 'Preparing', color: 'statusPending' }
];

const statusNamesAr: Record<string, string> = {
  'Draft': 'مسودة',
  'Scheduled': 'مجدولة',
  'Running': 'قيد التشغيل',
  'Paused': 'متوقفة مؤقتاً',
  'Completed': 'مكتملة',
  'Cancelled': 'ملغاة',
  'Preparing': 'قيد التحضير',
  'Unknown': 'غير معروف'
};

const formatMetric = (value: number | null) => value !== null && Number.isFinite(value) ? value.toLocaleString('ar-EG') : 'غير متاح';

function formatCairoDate(value?: string): string {
  if (!value) return 'موعد الجدولة غير متاح من المصدر';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'موعد غير صالح في المصدر';
  return `${date.toLocaleString('ar-EG', { timeZone: 'Africa/Cairo', dateStyle: 'medium', timeStyle: 'short' })} بتوقيت القاهرة`;
}

const listFilterConfig = { statuses: ['all', '0', '1', '2', '3', '4', '5', '6'] as const, defaultStatus: 'all' as const };

export default function Campaigns() {
  return <React.Suspense fallback={<p role="status">جاري تحميل القائمة…</p>}><CampaignsList /></React.Suspense>;
}

function CampaignsList() {
  const { activeProject } = useAuth();
  const { showToast } = useToast();

  const { search: searchQuery, setSearch: setSearchQuery, status: statusFilter, setStatus: setStatusFilter,
    page: currentPage, setPage: setCurrentPage, pageSize, setPageSize, clearFilters, hasFilters } = useListFilters(listFilterConfig);
  const [campaigns, setCampaigns] = useState<Campaign[]>([]);
  const [segments, setSegments] = useState<Segment[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadedProjectId, setLoadedProjectId] = useState<string | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [isModalOpen, setIsModalOpen] = useState(false);
  const [actionLoading, setActionLoading] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  // Form Fields
  const [formName, setFormName] = useState('');
  const [formSegmentId, setFormSegmentId] = useState('');
  const [formTemplateA, setFormTemplateA] = useState('');
  const [formTemplateB, setFormTemplateB] = useState('');
  const [formScheduleDate, setFormScheduleDate] = useState('');
  const [campaignToLaunch, setCampaignToLaunch] = useState<Campaign | null>(null);
  const [confirmScheduledCreation, setConfirmScheduledCreation] = useState(false);
  const loadRequestIdRef = React.useRef(0);

  const fetchData = useCallback(async () => {
    const requestId = ++loadRequestIdRef.current;
    if (!activeProject) {
      setCampaigns([]);
      setSegments([]);
      setLoading(false);
      setLoadError('تعذر تحميل مساحة العمل. أعد المحاولة أو تواصل مع المدير.');
      return;
    }
    try {
      setLoading(true);
      setLoadError(null);
      setCampaigns([]);
      setSegments([]);
      const [campRes, segRes] = await Promise.all([
        api.get<Campaign[]>(`/api/projects/${activeProject.id}/campaigns`),
        api.get<Segment[]>(`/api/projects/${activeProject.id}/segments`),
      ]);
      if (requestId !== loadRequestIdRef.current) return;

      setCampaigns(campRes.data);
      setLoadedProjectId(activeProject.id);
      setSegments(segRes.data);
    } catch (e) {
      if (requestId !== loadRequestIdRef.current) return;
      console.error('Failed to load campaigns data', e);
      setLoadError('فشل تحميل بيانات الحملات. لم يتم عرض قائمة فارغة بديلًا عنها.');
    } finally {
      if (requestId === loadRequestIdRef.current) setLoading(false);
    }
  }, [activeProject]);

  useEffect(() => {
    const loadTimer = window.setTimeout(() => {
      void fetchData();
    }, 0);
    return () => window.clearTimeout(loadTimer);
  }, [fetchData]);

  const createCampaign = async () => {
    if (!activeProject) return;
    if (!formName || !formSegmentId || !formTemplateA) {
      showToast('الاسم والمجموعة والقالب (أ) حقول مطلوبة.', 'error');
      return;
    }

    let createdCampaignId: string | null = null;
    try {
      setActionLoading(true);
      setMessage(null);

      // 1. Create campaign as Draft
      const res = await api.post<{ id: string }>(`/api/projects/${activeProject.id}/campaigns`, {
        name: formName,
        segmentId: formSegmentId,
        messageTemplateA: formTemplateA,
        messageTemplateB: formTemplateB || '',
      });

      createdCampaignId = res.data.id;

      // 2. If scheduled date is provided, schedule it
      if (formScheduleDate) {
        await api.post(`/api/campaigns/${createdCampaignId}/schedule`, JSON.stringify(new Date(formScheduleDate).toISOString()), {
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

      void fetchData();
    } catch (e) {
      console.error('Failed to create campaign', e);
      if (createdCampaignId) {
        setMessage({ type: 'error', text: 'تم حفظ الحملة كمسودة، لكن تعذرت جدولتها. افتح المسودة الحالية لإعادة المحاولة ولا تنشئ نسخة جديدة.' });
        setIsModalOpen(false);
        setFormName('');
        setFormSegmentId('');
        setFormTemplateA('');
        setFormTemplateB('');
        setFormScheduleDate('');
        void fetchData();
      } else {
        setMessage({ type: 'error', text: 'فشل إنشاء الحملة.' });
      }
    } finally {
      setActionLoading(false);
    }
  };

  const submitCampaign = (event: React.FormEvent) => {
    event.preventDefault();
    if (formScheduleDate) {
      const scheduledAt = new Date(formScheduleDate);
      if (Number.isNaN(scheduledAt.getTime()) || scheduledAt.getTime() <= Date.now()) {
        showToast('اختر موعدًا صحيحًا في المستقبل، أو اترك الموعد فارغًا لحفظ مسودة.', 'error');
        return;
      }
      setConfirmScheduledCreation(true);
      return;
    }
    void createCampaign();
  };

  const handleScheduleNow = async (campaignId: string) => {
    try {
      setActionLoading(true);
      setMessage(null);
      await api.post(`/api/campaigns/${campaignId}/schedule`, JSON.stringify(new Date().toISOString()), {
        headers: { 'Content-Type': 'application/json' }
      });
      setMessage({ type: 'success', text: 'تم جدولة الحملة للإرسال الفوري.' });
      void fetchData();
    } catch (e) {
      console.error('Failed to schedule campaign', e);
      setMessage({ type: 'error', text: 'فشل جدولة الحملة.' });
    } finally {
      setActionLoading(false);
    }
  };

  const getSegmentName = (segId: string) => {
    return segments.find(s => s.id === segId)?.name || 'مجموعة غير معروفة';
  };

  const filteredCampaigns = campaigns.filter(campaign =>
    (statusFilter === 'all' || String(campaign.status) === statusFilter) &&
    `${campaign.name} ${getSegmentName(campaign.segmentId)}`.toLocaleLowerCase('ar').includes(searchQuery.trim().toLocaleLowerCase('ar')));
  const totalPages = Math.ceil(filteredCampaigns.length / pageSize) || 1;
  const countsAvailable = !loading && !loadError && loadedProjectId === activeProject?.id;
  useEffect(() => {
    if (!loading && !loadError && loadedProjectId === activeProject?.id && currentPage > totalPages) setCurrentPage(totalPages, 'replace');
  }, [loading, loadError, loadedProjectId, activeProject?.id, currentPage, totalPages, setCurrentPage]);
  const paginatedCampaigns = filteredCampaigns.slice(
    (currentPage - 1) * pageSize,
    currentPage * pageSize
  );

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <PageHeader icon={<Megaphone size={22} aria-hidden="true" />} title="حملات واتساب" description="جهّز الرسالة والجمهور، ثم راجع موعد الإرسال." />
        {activeProject && (
          <button
            type="button"
            onClick={() => { setMessage(null); setIsModalOpen(true); }}
            className={`${styles.btn} ${styles.btnPrimary}`}
          >
            <Plus size={16} />
            إنشاء حملة
          </button>
        )}
      </div>

      {message && <StatusMessage tone={message.type === 'error' ? 'danger' : 'success'}>{message.text}</StatusMessage>}

      <div className={styles.operationsToolbar}>
        <FilterTabs compactOnMobile label="حالة الحملات" selected={statusFilter} onChange={setStatusFilter} options={[
          { key: 'all', label: 'الكل', count: countsAvailable ? campaigns.length : undefined, icon: <Megaphone size={16} aria-hidden="true" /> },
          { key: '0', label: 'مسودات', count: countsAvailable ? campaigns.filter(c => c.status === 0).length : undefined, icon: <FileText size={16} aria-hidden="true" /> },
          { key: '1', label: 'مجدولة', count: countsAvailable ? campaigns.filter(c => c.status === 1).length : undefined, icon: <Clock size={16} aria-hidden="true" />, tone: 'info' },
          { key: '6', label: 'قيد التحضير', count: countsAvailable ? campaigns.filter(c => c.status === 6).length : undefined, icon: <Clock size={16} aria-hidden="true" />, tone: 'warning' },
          { key: '2', label: 'قيد التشغيل', count: countsAvailable ? campaigns.filter(c => c.status === 2).length : undefined, icon: <Send size={16} aria-hidden="true" />, tone: 'info' },
          { key: '3', label: 'متوقفة مؤقتاً', count: countsAvailable ? campaigns.filter(c => c.status === 3).length : undefined, icon: <PauseCircle size={16} aria-hidden="true" />, tone: 'warning' },
          { key: '4', label: 'مكتملة', count: countsAvailable ? campaigns.filter(c => c.status === 4).length : undefined, icon: <CheckCircle2 size={16} aria-hidden="true" />, tone: 'success' },
          { key: '5', label: 'ملغاة', count: countsAvailable ? campaigns.filter(c => c.status === 5).length : undefined, icon: <CircleX size={16} aria-hidden="true" />, tone: 'danger' },
        ]} />
        <div className={styles.listSearchTools}>
        <SearchField label="بحث في الحملات" placeholder="اسم الحملة أو المجموعة…" value={searchQuery} onChange={event => setSearchQuery(event.target.value)} />
          {hasFilters && <button type="button" className={`${styles.btn} ${styles.btnSecondary}`} onClick={clearFilters}>إعادة ضبط التصفية</button>}
        </div>
      </div>
      <div className={`${styles.panel} ${styles.operationsPanel}`}>
        {loading ? (
          <div className={styles.emptyState}>
            <div className={styles.spinner}></div>
            <p style={{ marginTop: 'var(--space-md)' }}>جاري تحميل الحملات...</p>
          </div>
        ) : loadError ? (
          <div className={styles.emptyState} role="alert">
            <h3 className={styles.emptyStateTitle}>تعذر تحميل الحملات</h3>
            <p className={styles.emptyStateDesc}>{loadError}</p>
            {activeProject && <button type="button" onClick={() => void fetchData()} className={`${styles.btn} ${styles.btnPrimary}`}>إعادة المحاولة</button>}
          </div>
        ) : filteredCampaigns.length === 0 ? (
          <div className={styles.emptyState}>
            <Megaphone size={48} style={{ color: 'hsl(var(--text-muted))' }} />
            <h3 className={styles.emptyStateTitle}>{campaigns.length ? 'لا توجد حملات مطابقة' : 'لا توجد حملات'}</h3>
            <p className={styles.emptyStateDesc}>{campaigns.length ? 'غيّر البحث أو الحالة لعرض حملات أخرى.' : 'أنشئ أول حملة واختر الجمهور والرسالة قبل الإرسال.'}</p>
            <button onClick={() => { setMessage(null); setIsModalOpen(true); }} className={`${styles.btn} ${styles.btnPrimary}`}>
              إنشاء حملة
            </button>
          </div>
        ) : (
          <div className={styles.tableWrapper}>
            <table role="table" className={styles.table}>
              <caption className="sr-only">الحملات الصادرة وحالتها ومؤشرات التسليم</caption>
              <thead>
                <tr>
                  <th className={styles.th}>الحملة</th>

                  <th className={styles.th}>الحالة</th>
                  <th className={styles.th}>نتائج الإرسال</th>
                  <th className={styles.th} style={{ textAlign: 'center' }}>الإجراءات</th>
                </tr>
              </thead>
              <tbody>
                {paginatedCampaigns.map(camp => {
                  const statusInfo = statusMap[camp.status] || { name: 'Unknown', color: 'statusPending' };
                  return (
                    <tr key={camp.id} className={styles.tr}>
                      <td data-label="الحملة" className={styles.td}>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                          <span style={{ fontWeight: 600, color: 'var(--text-strong)' }}>{camp.name}</span>
                          <span className={styles.recordMeta}><Users size={14} aria-hidden="true" />{getSegmentName(camp.segmentId)}</span>
                          <details className={styles.recordDisclosure}><summary>عرض الرسالة</summary><p>{camp.messageTemplateA}</p>{camp.messageTemplateB && <p><strong>النسخة B: </strong>{camp.messageTemplateB}</p>}</details>
                          {(camp.scheduledAt || camp.status === 1) && (
                            <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))', display: 'flex', alignItems: 'center', gap: '4px' }}>
                              <Calendar size={12} />
                              {formatCairoDate(camp.scheduledAt)}
                            </span>
                          )}
                        </div>
                      </td>
                      <td data-label="الحالة" className={styles.td}>
                        <span className={`${styles.statusBadge} ${styles[statusInfo.color]}`}>
                          {camp.status === 4 ? <CheckCircle2 size={14} aria-hidden="true" /> : camp.status === 3 ? <PauseCircle size={14} aria-hidden="true" /> : camp.status === 5 ? <CircleX size={14} aria-hidden="true" /> : <Clock size={14} aria-hidden="true" />}
                          {statusNamesAr[statusInfo.name] || statusInfo.name}
                        </span>
                      </td>
                      <td data-label="نتائج الإرسال" className={styles.td}>
                        <dl className={styles.deliveryMetrics}>
                          <div><dt><Send size={13} aria-hidden="true" /> مرسل</dt><dd>{formatMetric(camp.sentCount)}</dd></div>
                          <div><dt><CheckCheck size={13} aria-hidden="true" /> مسلّم</dt><dd>{formatMetric(camp.deliveredCount)}</dd></div>
                          <div><dt><Eye size={13} aria-hidden="true" /> مقروء</dt><dd>{formatMetric(camp.readCount)}</dd></div>
                          <div><dt><MessageSquare size={13} aria-hidden="true" /> متفاعل</dt><dd>{formatMetric(camp.responseCount)}</dd></div>
                        </dl>
                        {(camp.deliveredCount === null || camp.readCount === null || camp.responseCount === null) && (
                          <p className={styles.recordMeta}>مرسل يعني قبول مزود الرسائل للإرسال. تتبّع التسليم والقراءة والتفاعل غير متاح حاليًا.</p>
                        )}
                      </td>
                      <td data-label="الإجراءات" className={styles.td} style={{ textAlign: 'center' }}>
                        {camp.status === 0 ? ( // Draft
                          <button
                            onClick={() => setCampaignToLaunch(camp)}
                            disabled={actionLoading}
                            className={`${styles.btn} ${styles.btnPrimary}`}
                            style={{ padding: '4px 10px', fontSize: '0.75rem', display: 'inline-flex', alignItems: 'center', gap: '4px' }}
                          >
                            <Send size={12} />
                            إطلاق الآن
                          </button>
                        ) : (
                          <span style={{ color: 'hsl(var(--text-muted))', fontSize: '0.8rem' }}>لا يلزم إجراء</span>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>

            {/* Pagination Controls */}
            {filteredCampaigns.length > 0 && (
              <div className={styles.pagination}>
                <div className={styles.paginationInfo}>
                  <span>عرض السطور:</span>
                  <select
                    aria-label="عدد الحملات في الصفحة"
                    value={pageSize}
                    onChange={(e) => {
                      setPageSize(Number(e.target.value));
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
                    عرض {Math.min((currentPage - 1) * pageSize + 1, filteredCampaigns.length)} - {Math.min(currentPage * pageSize, filteredCampaigns.length)} من {filteredCampaigns.length}
                  </span>
                </div>

                <div className={styles.paginationControls}>
                  <button
                    onClick={() => setCurrentPage(Math.min(currentPage + 1, totalPages))}
                    disabled={currentPage === totalPages}
                    className={styles.paginationBtn}
                    title="الصفحة التالية"
                    aria-label="الصفحة التالية"
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
                          aria-label={`الصفحة ${page}`}
                          aria-current={currentPage === page ? 'page' : undefined}
                        >
                          {page}
                        </button>
                      );
                      return elements;
                    })}

                  <button
                    onClick={() => setCurrentPage(Math.max(currentPage - 1, 1))}
                    disabled={currentPage === 1}
                    className={styles.paginationBtn}
                    title="الصفحة السابقة"
                    aria-label="الصفحة السابقة"
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
        <Dialog
          label="إنشاء حملة" onClose={() => setIsModalOpen(false)}
          title="إنشاء حملة جديدة"
          footer={<>
            {message?.type === 'error' && <div className={dialogStyles.feedback}>
              <StatusMessage tone="danger">{message.text}</StatusMessage>
            </div>}
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
              form="campaign-create-form"
              className={`${styles.btn} ${styles.btnPrimary}`}
              disabled={actionLoading}
            >
              {actionLoading ? 'جاري الإنشاء...' : formScheduleDate ? 'حفظ وجدولة' : 'حفظ كمسودة'}
            </button>
          </>}
        >
          <div className={styles.dialogContent}>
            <form id="campaign-create-form" onSubmit={submitCampaign} className={dialogStyles.form}>
              <div className={styles.formGroup}>
                <label htmlFor="campaign-name" className={styles.label}>اسم الحملة</label>
                <input
                  id="campaign-name"
                  autoFocus
                  type="text"
                  value={formName}
                  onChange={(e) => setFormName(e.target.value)}
                  placeholder="مثال: عرض كود الخصم الصيفي"
                  className={styles.input}
                  required
                />
              </div>

              <div className={styles.formGroup}>
                <label htmlFor="campaign-segment" className={styles.label}>المجموعة المستهدفة</label>
                <select
                  id="campaign-segment"
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
                <label htmlFor="campaign-template-a" className={styles.label}>قالب الرسالة أ</label>
                <textarea
                  id="campaign-template-a"
                  value={formTemplateA}
                  onChange={(e) => setFormTemplateA(e.target.value)}
                  placeholder="مرحباً {{name}}، إليك الخصم الخاص بك..."
                  className={styles.textarea}
                  required
                />
              </div>

              <div className={styles.formGroup}>
                <label htmlFor="campaign-template-b" className={styles.label}>قالب الرسالة ب (اختياري لاختبار A/B)</label>
                <textarea
                  id="campaign-template-b"
                  value={formTemplateB}
                  onChange={(e) => setFormTemplateB(e.target.value)}
                  placeholder="أهلاً {{name}}، احصل على كود الخصم اليوم!"
                  className={styles.textarea}
                />
              </div>

              <div className={styles.formGroup}>
                <label htmlFor="campaign-schedule-at" className={styles.label}>تاريخ ووقت الجدولة بتوقيت جهازك (اتركه فارغًا للحفظ كمسودة)</label>
                <input
                  id="campaign-schedule-at"
                  type="datetime-local"
                  value={formScheduleDate}
                  onChange={(e) => setFormScheduleDate(e.target.value)}
                  className={styles.input}
                />
              </div>
            </form>
          </div>
        </Dialog>
      )}
      <ConfirmDialog
        isOpen={campaignToLaunch !== null}
        title="إطلاق الحملة الآن"
        message={campaignToLaunch ? `سيتم جدولة الحملة «${campaignToLaunch.name}» فورًا للمجموعة «${getSegmentName(campaignToLaunch.segmentId)}».` : ''}
        confirmLabel="إطلاق الآن"
        onConfirm={() => {
          const campaign = campaignToLaunch;
          setCampaignToLaunch(null);
          if (campaign) void handleScheduleNow(campaign.id);
        }}
        onCancel={() => setCampaignToLaunch(null)}
      />
      <ConfirmDialog
        isOpen={confirmScheduledCreation}
        title="إنشاء وجدولة الحملة"
        message={`سيتم إنشاء الحملة «${formName}» وجدولتها للمجموعة «${getSegmentName(formSegmentId)}» في الموعد المحدد بتوقيت جهازك.`}
        confirmLabel="إنشاء وجدولة"
        onConfirm={() => {
          setConfirmScheduledCreation(false);
          void createCampaign();
        }}
        onCancel={() => setConfirmScheduledCreation(false)}
      />
    </div>
  );
}
