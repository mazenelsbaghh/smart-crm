'use client';

import React, { useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import Dialog from '../../components/shared/Dialog';
import { 
  Calendar, 
  AlertCircle, 
  Clock, 
  MessageSquare,
  Send,
  Trash2,
  ChevronLeft,
  ChevronRight,
  Pencil,
  X,
  Sparkles,
  CheckCircle2,
  ListChecks
} from 'lucide-react';
import PageHeader from '../../components/ui/PageHeader';
import StatusMessage from '../../components/ui/StatusMessage';
import FilterTabs from '../../components/ui/FilterTabs';
import SearchField from '../../components/ui/SearchField';
import styles from './management.module.css';
import { useListFilters } from './hooks/use-list-filters';

interface FollowUp {
  id: string;
  customerId: string;
  customerName?: string;
  customerPhoneNumber: string;
  dueDate: string;
  status: 'Pending' | 'Completed' | 'Missed' | 'Bypassed' | 'Cancelled' | 'Processing' | 'DeliveryUnknown';
  notes: string;
  type?: 'Nurturing' | 'AppointmentReminder';
  appointmentTime?: string;
  tone?: string;
}

interface FollowUpPage {
  items: FollowUp[];
  filteredCount: number;
  totalCount: number;
  pendingCount: number;
  completedCount: number;
  missedCount: number;
  bypassedCount: number;
  cancelledCount: number;
  processingCount: number;
  deliveryUnknownCount: number;
}

const statusMapAr: Record<string, string> = {
  'Pending': 'معلقة',
  'Completed': 'مكتملة',
  'Bypassed': 'تم تجاوزها',
  'Cancelled': 'ملغية',
  'Processing': 'قيد التنفيذ',
  'DeliveryUnknown': 'الإرسال غير محسوم',
  'Missed': 'فائتة',
  'All': 'الكل'
};

const followUpTypeMapAr: Record<string, string> = {
  Nurturing: 'متابعة عميل',
  AppointmentReminder: 'تذكير بموعد',
};

const toneMapAr: Record<string, string> = {
  Creative: 'إبداعي',
  Salesy: 'بيعي مباشر',
};

function formatCairoDate(value?: string): string {
  if (!value) return 'غير متاح من المصدر';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'تاريخ غير صالح في المصدر';
  return `${date.toLocaleString('ar-EG', { timeZone: 'Africa/Cairo', dateStyle: 'medium', timeStyle: 'short' })} بتوقيت القاهرة`;
}

const listFilterConfig = { statuses: ['All', 'Pending', 'Completed', 'Bypassed', 'Missed', 'Cancelled', 'Processing', 'DeliveryUnknown'] as const, defaultStatus: 'Pending' as const };

export default function FollowUps() {
  return <React.Suspense fallback={<p role="status">جاري تحميل القائمة…</p>}><FollowUpsList /></React.Suspense>;
}

function FollowUpsList() {
  const { activeProject } = useAuth();
  
  const [followUps, setFollowUps] = useState<FollowUp[]>([]);
  const [counts, setCounts] = useState<(Omit<FollowUpPage, 'items' | 'filteredCount'> & { projectId: string }) | null>(null);
  const [filteredCount, setFilteredCount] = useState(0);
  const [loadedFilterKey, setLoadedFilterKey] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [reEvaluating, setReEvaluating] = useState(false);

  // Confirmation States
  const [confirmReEvaluateOpen, setConfirmReEvaluateOpen] = useState(false);
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);
  const [followUpToDelete, setFollowUpToDelete] = useState<string | null>(null);
  const [followUpToSend, setFollowUpToSend] = useState<FollowUp | null>(null);

  const [actionLoadingId, setActionLoadingId] = useState<string | null>(null);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  // Search & Editing State
  const { search: searchQuery, setSearch: setSearchQuery, status: filter, setStatus: setFilter,
    page: currentPage, setPage: setCurrentPage, pageSize, setPageSize, clearFilters, hasFilters } = useListFilters(listFilterConfig);
  const [editingFollowUp, setEditingFollowUp] = useState<FollowUp | null>(null);
  const [editNotes, setEditNotes] = useState('');
  const [editDueDate, setEditDueDate] = useState('');
  const [editType, setEditType] = useState<'Nurturing' | 'AppointmentReminder' | ''>('');
  const [editAppointmentTime, setEditAppointmentTime] = useState('');
  const [editTone, setEditTone] = useState<string>('');
  const loadRequestIdRef = React.useRef(0);
  const [referenceNow] = useState(() => Date.now());

  const filterKey = JSON.stringify([activeProject?.id, filter, searchQuery.trim(), pageSize]);

  const fetchData = async () => {
    const requestId = ++loadRequestIdRef.current;
    if (!activeProject) {
      setFollowUps([]);
      setLoading(false);
      setLoadError('تعذر تحميل مساحة العمل. أعد المحاولة أو تواصل مع المدير.');
      return;
    }
    try {
      setLoading(true);
      setLoadError(null);
      const response = await api.get<FollowUpPage>(`/api/projects/${activeProject.id}/follow-ups/page`, {
        params: { status: filter, search: searchQuery.trim() || undefined, page: currentPage, pageSize },
      });
      if (requestId !== loadRequestIdRef.current) return;
      
      setFollowUps(response.data.items);
      setFilteredCount(response.data.filteredCount);
      setLoadedFilterKey(filterKey);
      setCounts({
        projectId: activeProject.id,
        totalCount: response.data.totalCount,
        pendingCount: response.data.pendingCount,
        completedCount: response.data.completedCount,
        missedCount: response.data.missedCount,
        bypassedCount: response.data.bypassedCount,
        cancelledCount: response.data.cancelledCount,
        processingCount: response.data.processingCount,
        deliveryUnknownCount: response.data.deliveryUnknownCount,
      });
    } catch (e) {
      if (requestId !== loadRequestIdRef.current) return;
      console.error('Failed to load follow-ups', e);
      setLoadError('فشل تحميل مواعيد المتابعات. لم يتم عرض قائمة فارغة بديلًا عنها.');
    } finally {
      if (requestId === loadRequestIdRef.current) setLoading(false);
    }
  };

  const triggerReEvaluateAll = () => {
    if (!activeProject) return;
    setConfirmReEvaluateOpen(true);
  };

  const handleConfirmReEvaluateAll = async () => {
    setConfirmReEvaluateOpen(false);
    if (!activeProject) return;
    try {
      setReEvaluating(true);
      setMessage(null);
      
      const response = await api.post<{ message: string; count: number }>(`/api/projects/${activeProject.id}/follow-ups/re-evaluate-all`);
      
      setMessage({ type: 'success', text: `تم إعادة ضبط وتخصيص ${response.data.count} من المتابعات المعلقة بنجاح!` });
      void fetchData();
    } catch (e) {
      console.error(e);
      setMessage({ type: 'error', text: 'حدث خطأ أثناء إعادة ضبط المتابعات.' });
    } finally {
      setReEvaluating(false);
    }
  };

  useEffect(() => {
    const timeoutId = window.setTimeout(() => void fetchData(), searchQuery ? 250 : 0);
    return () => window.clearTimeout(timeoutId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeProject, filter, currentPage, pageSize, searchQuery]);

  const getCustomerName = (followUp: FollowUp) =>
    followUp.customerName || followUp.customerPhoneNumber || 'عميل غير مسجل';

  const handleSend = async (id: string) => {
    try {
      setActionLoadingId(id);
      setMessage(null);
      await api.post(`/api/follow-ups/${id}/send`);
      setMessage({ type: 'success', text: 'تم إرسال رسالة المتابعة بنجاح.' });
      await fetchData();
    } catch (e) {
      console.error('Failed to send follow-up', e);
      setMessage({ type: 'error', text: 'فشل إرسال رسالة المتابعة.' });
    } finally {
      setActionLoadingId(null);
    }
  };

  const triggerDelete = (id: string) => {
    setFollowUpToDelete(id);
    setConfirmDeleteOpen(true);
  };

  const handleConfirmDelete = async () => {
    if (!followUpToDelete) return;
    const id = followUpToDelete;
    setConfirmDeleteOpen(false);
    setFollowUpToDelete(null);
    try {
      setActionLoadingId(id);
      setMessage(null);
      await api.delete(`/api/follow-ups/${id}`);
      setMessage({ type: 'success', text: 'تم مسح المتابعة بنجاح.' });
      await fetchData();
    } catch (e) {
      console.error('Failed to delete follow-up', e);
      setMessage({ type: 'error', text: 'فشل مسح المتابعة.' });
    } finally {
      setActionLoadingId(null);
    }
  };

  const handleEditClick = (fu: FollowUp) => {
    setEditingFollowUp(fu);
    setEditNotes(fu.notes || '');
    
    // Format due date for datetime-local input (YYYY-MM-DDTHH:mm)
    if (fu.dueDate) {
      const d = new Date(fu.dueDate);
      const localDateTime = new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
      setEditDueDate(localDateTime);
    } else {
      setEditDueDate('');
    }
    
    setEditType(fu.type || '');
    setEditTone(fu.tone || '');
    
    if (fu.appointmentTime) {
      const d = new Date(fu.appointmentTime);
      const localDateTime = new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
      setEditAppointmentTime(localDateTime);
    } else {
      setEditAppointmentTime('');
    }
  };

  const handleSaveEdit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingFollowUp || !editType || !editTone) {
      setMessage({ type: 'error', text: 'اختر نوع المتابعة ونبرتها قبل الحفظ.' });
      return;
    }
    
    try {
      setActionLoadingId(editingFollowUp.id);
      setMessage(null);
      
      const payload = {
        notes: editNotes,
        type: editType,
        dueDate: editType === 'Nurturing'
          ? new Date(editDueDate).toISOString()
          : new Date(editAppointmentTime).toISOString(),
        appointmentTime: editType === 'AppointmentReminder'
          ? new Date(editAppointmentTime).toISOString()
          : null,
        tone: editTone
      };
      
      await api.put(`/api/follow-ups/${editingFollowUp.id}`, payload);
      setMessage({ type: 'success', text: 'تم تعديل المتابعة بنجاح.' });
      setEditingFollowUp(null);
      await fetchData();
    } catch (err) {
      console.error('Failed to update follow-up', err);
      setMessage({ type: 'error', text: 'فشل تعديل المتابعة.' });
    } finally {
      setActionLoadingId(null);
    }
  };

  const { totalCount, pendingCount, completedCount, missedCount, bypassedCount, cancelledCount, processingCount, deliveryUnknownCount }
    = counts && counts.projectId === activeProject?.id ? counts : {};
  const totalPages = Math.ceil(filteredCount / pageSize) || 1;
  useEffect(() => {
    if (!loading && !loadError && loadedFilterKey === filterKey && currentPage > totalPages) setCurrentPage(totalPages, 'replace');
  }, [loading, loadError, loadedFilterKey, filterKey, currentPage, totalPages, setCurrentPage]);

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <PageHeader icon={<Calendar size={22} aria-hidden="true" />} title="المتابعات" description="المواعيد والرسائل التي تحتاج متابعة فريقك." />
        <button
          type="button"
          onClick={triggerReEvaluateAll}
          disabled={!activeProject || reEvaluating || !pendingCount}
          title={!activeProject ? 'مساحة العمل غير متاحة' : pendingCount === 0 ? 'لا توجد متابعات معلقة لإعادة ضبطها' : undefined}
          className={`${styles.btn} ${styles.btnIntelligence}`}
        >
          <Sparkles size={16} aria-hidden="true" />
          {reEvaluating ? 'جاري إعادة الضبط بالـ AI...' : 'ضبط المتابعات تلقائياً بالـ AI'}
        </button>
      </div>

      <FilterTabs compactOnMobile label="تصفية المتابعات حسب الحالة" selected={filter} onChange={setFilter} options={[
        { key: 'All', icon: <ListChecks size={16} aria-hidden="true" />, label: 'كل المتابعات', count: totalCount, description: 'كل المهام المجدولة' },
        { key: 'Pending', icon: <Clock size={16} aria-hidden="true" />, tone: 'warning', label: 'معلقة', count: pendingCount, description: 'متابعات في انتظار الإرسال' },
        { key: 'Processing', icon: <Send size={16} aria-hidden="true" />, tone: 'info', label: 'قيد التنفيذ', count: processingCount, description: 'يعالجها نظام الإرسال حاليًا' },
        { key: 'Completed', icon: <CheckCircle2 size={16} aria-hidden="true" />, tone: 'success', label: 'مكتملة', count: completedCount, description: 'معلّمة كمكتملة' },
        { key: 'Missed', icon: <AlertCircle size={16} aria-hidden="true" />, tone: 'danger', label: 'فائتة', count: missedCount, description: 'فشل الإرسال أو تجاوز الموعد' },
        { key: 'DeliveryUnknown', icon: <AlertCircle size={16} aria-hidden="true" />, tone: 'warning', label: 'الإرسال غير محسوم', count: deliveryUnknownCount, description: 'لم يؤكد المصدر نتيجة الإرسال' },
        { key: 'Cancelled', icon: <X size={16} aria-hidden="true" />, label: 'ملغية', count: cancelledCount, description: 'أوقفتها قواعد الحجز أو الدفع أو الإلغاء' },
        { key: 'Bypassed', icon: <ChevronRight size={16} aria-hidden="true" />, label: 'تم تجاوزها', count: bypassedCount, description: 'لم تعد تحتاج الإرسال وفق حالة العميل' },
      ]} />

      {message && <StatusMessage tone={message.type === 'error' ? 'danger' : 'success'}>{message.text}</StatusMessage>}

      <div className={styles.listSearchTools}>
      <SearchField label="بحث في المتابعات باسم العميل أو رقم الهاتف" placeholder="ابحث باسم العميل أو رقم الهاتف…"
        value={searchQuery} onChange={event => setSearchQuery(event.target.value)} />
        {hasFilters && <button type="button" className={`${styles.btn} ${styles.btnSecondary}`} onClick={clearFilters}>إعادة ضبط التصفية</button>}
      </div>

      <div className={`${styles.panel} ${styles.operationsPanel}`}>
        {loading ? (
          <div className={styles.emptyState}>
            <div className={styles.spinner}></div>
            <p style={{ marginTop: 'var(--space-md)' }}>جاري تحميل المتابعات...</p>
          </div>
        ) : loadError ? (
          <div className={styles.emptyState} role="alert">
            <h3 className={styles.emptyStateTitle}>تعذر تحميل المتابعات</h3>
            <p className={styles.emptyStateDesc}>{loadError}{totalCount !== undefined && ' العدادات المعروضة من آخر تحميل ناجح.'}</p>
            {activeProject && <button type="button" onClick={() => void fetchData()} className={`${styles.btn} ${styles.btnPrimary}`}>إعادة المحاولة</button>}
          </div>
        ) : followUps.length === 0 ? (
          <div className={styles.emptyState}>
            <Calendar size={48} style={{ color: 'hsl(var(--text-muted))' }} />
            <h3 className={styles.emptyStateTitle}>لم يتم العثور على متابعات</h3>
            <p className={styles.emptyStateDesc}>ليس لديك أي مهام متابعة تطابق خيار التصفية هذا.</p>
          </div>
        ) : (
          <div className={styles.tableWrapper}>
            <table role="table" className={`${styles.table} ${styles.followUpTable}`}>
              <caption className="sr-only">مهام المتابعة وحالة الإرسال</caption>
              <thead>
                <tr>
                  <th className={styles.th}>العميل</th>
                  <th className={styles.th}>الموعد والحالة</th>

                  <th className={styles.th}>رسالة المتابعة</th>

                  <th className={styles.th} style={{ textAlign: 'center' }}>الإجراء</th>
                </tr>
              </thead>
              <tbody>
                {followUps.map(fu => {
                  const dueDate = new Date(fu.dueDate);
                  const isOverdue = !Number.isNaN(dueDate.getTime()) && dueDate.getTime() < referenceNow && fu.status === 'Pending';
                  return (
                    <tr key={fu.id} className={styles.tr}>
                      <td data-label="العميل" className={styles.td}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                          <div style={{
                            width: '28px',
                            height: '28px',
                            borderRadius: '50%',
                            background: 'var(--accent-soft)',
                            border: '1px solid var(--border-strong)',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            fontSize: '0.8rem',
                            color: 'var(--accent)',
                            fontWeight: 700
                          }}>
                            {getCustomerName(fu).charAt(0).toUpperCase()}
                          </div>
                          <span style={{ fontWeight: 600, color: 'var(--text-strong)' }}>
                            {getCustomerName(fu)}
                          </span>
                        </div>
                        <div className={styles.recordMeta}><MessageSquare size={14} aria-hidden="true" />{fu.type ? followUpTypeMapAr[fu.type] ?? fu.type : 'نوع المتابعة غير متاح'}</div>
                        {fu.tone && fu.tone !== 'Default' && <span className={`${styles.statusBadge} ${styles.statusIntelligence}`}>{toneMapAr[fu.tone] ?? `نبرة المصدر: ${fu.tone}`}</span>}
                      </td>
                      <td data-label="الموعد والحالة" className={styles.td}>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                          <div className={styles.dueDateLine} data-overdue={isOverdue || undefined}>
                            {isOverdue ? <AlertCircle size={14} /> : <Clock size={14} />}
                            <span>{formatCairoDate(fu.dueDate)}</span>
                            {isOverdue && <span style={{ fontSize: '0.7rem', fontWeight: 700, marginRight: '4px', color: 'hsl(var(--accent-danger))' }}>متأخرة</span>}
                          </div>
                          {fu.type === 'AppointmentReminder' && fu.appointmentTime && (
                            <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))', paddingRight: '20px' }}>
                              الموعد: {formatCairoDate(fu.appointmentTime)}
                            </span>
                          )}
                        </div>
                        <span className={`${styles.statusBadge} ${
                          fu.status === 'Completed' ? styles.statusCompleted :
                          fu.status === 'Bypassed' || fu.status === 'Cancelled' ? styles.statusBypassed :
                          fu.status === 'Missed' ? styles.statusFailed :
                          styles.statusPending
                        }`}>
                          {fu.status === 'Completed' ? <CheckCircle2 size={14} aria-hidden="true" /> : fu.status === 'Missed' || fu.status === 'DeliveryUnknown' ? <AlertCircle size={14} aria-hidden="true" /> : <Clock size={14} aria-hidden="true" />}{statusMapAr[fu.status] ?? fu.status}
                        </span>
                      </td>
                      <td data-label="رسالة المتابعة" className={styles.td}>
                        <div style={{ display: 'flex', alignItems: 'flex-start', gap: '6px' }}>
                          <MessageSquare size={14} style={{ color: 'hsl(var(--text-muted))', marginTop: '2px' }} />
                          <span>{fu.notes || 'لا يوجد وصف.'}</span>
                        </div>
                      </td>
                      <td data-label="الإجراء" className={styles.td} style={{ textAlign: 'center' }}>
                        <div className={styles.recordActions}>
                          {fu.status === 'Pending' ? (
                            <>
                              <button
                                onClick={() => setFollowUpToSend(fu)}
                                disabled={actionLoadingId !== null}
                                className={`${styles.btn} ${styles.btnSuccess}`}
                                style={{ padding: '4px 10px', fontSize: '0.75rem', display: 'inline-flex', alignItems: 'center', gap: '4px' }}
                              >
                                <Send size={12} />
                                إرسال
                              </button>
                              <button
                                onClick={() => handleEditClick(fu)}
                                disabled={actionLoadingId !== null}
                                className={`${styles.btn} ${styles.btnSecondary}`}
                                style={{ padding: '4px 10px', fontSize: '0.75rem', display: 'inline-flex', alignItems: 'center', gap: '4px' }}
                                title="تعديل المتابعة"
                              >
                                <Pencil size={12} />
                                تعديل
                              </button>
                            </>
                          ) : (
                            <span style={{ color: 'hsl(var(--text-muted))', fontSize: '0.8rem' }}>مغلقة</span>
                          )}

                          <details className={styles.recordDisclosure}><summary>المزيد</summary>
                          <button
                            type="button"
                            onClick={() => triggerDelete(fu.id)}
                            disabled={actionLoadingId !== null}
                            className={`${styles.btn} ${styles.btnDanger}`}
                            style={{ padding: '4px 10px', fontSize: '0.75rem', display: 'inline-flex', alignItems: 'center', gap: '4px' }}
                            title="مسح المتابعة"
                          >
                            <Trash2 size={12} />
                            مسح
                          </button>
                          </details>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>

            {/* Pagination Controls */}
            {followUps.length > 0 && (
              <div className={styles.pagination}>
                <div className={styles.paginationInfo}>
                  <span>عرض السطور:</span>
                  <select
                    aria-label="عدد المتابعات في الصفحة"
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
                    عرض {Math.min((currentPage - 1) * pageSize + 1, filteredCount)} - {Math.min(currentPage * pageSize, filteredCount)} من {filteredCount}
                  </span>
                </div>

                <div className={styles.paginationControls}>
                  <button
                    type="button"
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
                          type="button"
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
                    type="button"
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

      {editingFollowUp && (
        <Dialog label="تعديل متابعة" onClose={() => setEditingFollowUp(null)}>
          <div className={styles.dialogContent}>
            <div className={styles.modalHeader}>
              <h3 id="follow-up-edit-title" className={styles.modalTitle}>تعديل تفاصيل المتابعة</h3>
              <button 
                type="button"
                onClick={() => setEditingFollowUp(null)} 
                className={styles.closeBtn}
                aria-label="إغلاق"
                style={{ background: 'none', border: 'none', padding: 0 }}
              >
                <X size={20} />
              </button>
            </div>
            
            <form onSubmit={handleSaveEdit} style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
              <div className={styles.formGroup}>
                <label htmlFor="edit-follow-up-type" className={styles.label}>نوع المتابعة</label>
                <select
                  id="edit-follow-up-type"
                  autoFocus
                  value={editType}
                  onChange={(e) => setEditType(e.target.value as 'Nurturing' | 'AppointmentReminder' | '')}
                  className={styles.select}
                  required
                >
                  <option value="">اختر النوع</option>
                  <option value="Nurturing">متابعة لتنشيط العميل (Nurturing)</option>
                  <option value="AppointmentReminder">تذكير بموعد / كورس (Reminder)</option>
                </select>
              </div>

              {editType === 'Nurturing' ? (
                <div className={styles.formGroup}>
                  <label htmlFor="edit-follow-up-date" className={styles.label}>تاريخ ووقت المتابعة بتوقيت جهازك</label>
                  <input 
                    id="edit-follow-up-date"
                    type="datetime-local" 
                    value={editDueDate}
                    onChange={(e) => setEditDueDate(e.target.value)}
                    className={styles.input} 
                    required
                  />
                </div>
              ) : editType === 'AppointmentReminder' ? (
                <div className={styles.formGroup}>
                  <label htmlFor="edit-follow-up-appointment" className={styles.label}>تاريخ ووقت الموعد بتوقيت جهازك</label>
                  <input 
                    id="edit-follow-up-appointment"
                    type="datetime-local" 
                    value={editAppointmentTime}
                    onChange={(e) => setEditAppointmentTime(e.target.value)}
                    className={styles.input} 
                    required
                  />
                  <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))', marginTop: '2px', textAlign: 'right', display: 'block' }}>
                    سيتم إرسال رسالة التذكير تلقائياً قبل هذا الموعد بـ 24 ساعة.
                  </span>
                </div>
              ) : (
                <p className={styles.emptyStateDesc}>اختر نوع المتابعة لإظهار حقل الموعد المناسب.</p>
              )}

              <div className={styles.formGroup}>
                <label htmlFor="edit-follow-up-tone" className={styles.label}>نبرة المتابعة</label>
                <select
                  id="edit-follow-up-tone"
                  value={editTone}
                  onChange={(e) => setEditTone(e.target.value)}
                  className={styles.select}
                  required
                >
                  <option value="">اختر النبرة</option>
                  {editTone && !['Default', 'Creative', 'Salesy'].includes(editTone) && (
                    <option value={editTone}>القيمة الحالية: {editTone}</option>
                  )}
                  <option value="Default">الوضع الافتراضي (Default)</option>
                  <option value="Creative">إبداعي (Creative)</option>
                  <option value="Salesy">بيعي مباشر</option>
                </select>
              </div>

              <div className={styles.formGroup}>
                <label htmlFor="edit-follow-up-notes" className={styles.label}>نص الرسالة أو الملاحظات</label>
                <textarea
                  id="edit-follow-up-notes"
                  value={editNotes}
                  onChange={(e) => setEditNotes(e.target.value)}
                  className={styles.textarea}
                  rows={4}
                  placeholder="اكتب رسالة المتابعة هنا..."
                />
              </div>

              <div className={styles.formActions}>
                <button 
                  type="button" 
                  onClick={() => setEditingFollowUp(null)} 
                  className={`${styles.btn} ${styles.btnSecondary}`}
                >
                  إلغاء
                </button>
                <button 
                  type="submit" 
                  disabled={actionLoadingId !== null} 
                  className={`${styles.btn} ${styles.btnPrimary}`}
                >
                  {actionLoadingId === editingFollowUp.id ? 'جاري الحفظ...' : 'حفظ التعديلات'}
                </button>
              </div>
            </form>
          </div>
        </Dialog>
      )}

      <ConfirmDialog 
        isOpen={confirmReEvaluateOpen}
        title="تأكيد إعادة الضبط بالـ AI"
        message="هل أنت متأكد من رغبتك في إعادة تقييم وضبط جميع المتابعات المعلقة بالذكاء الاصطناعي بناءً على محادثات الطلاب؟ قد يستغرق هذا بضع ثوانٍ."
        confirmLabel="تأكيد الضبط"
        cancelLabel="إلغاء"
        onConfirm={handleConfirmReEvaluateAll}
        onCancel={() => setConfirmReEvaluateOpen(false)}
      />
      <ConfirmDialog
        isOpen={followUpToSend !== null}
        title="إرسال المتابعة الآن"
        message={followUpToSend ? `سيتم إرسال متابعة العميل «${getCustomerName(followUpToSend)}» الآن بالنص المسجل.` : ''}
        confirmLabel="إرسال الآن"
        onConfirm={() => {
          const followUp = followUpToSend;
          setFollowUpToSend(null);
          if (followUp) void handleSend(followUp.id);
        }}
        onCancel={() => setFollowUpToSend(null)}
      />

      <ConfirmDialog 
        isOpen={confirmDeleteOpen}
        title="تأكيد الحذف"
        message="هل أنت متأكد من مسح هذه المتابعة؟ لا يمكن التراجع عن هذا الإجراء."
        confirmLabel="مسح"
        cancelLabel="إلغاء"
        onConfirm={handleConfirmDelete}
        onCancel={() => { setConfirmDeleteOpen(false); setFollowUpToDelete(null); }}
      />
    </div>
  );
}
