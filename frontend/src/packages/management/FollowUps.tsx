'use client';

import React, { useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import { crmService, Customer } from '../../services/crm';
import { 
  Calendar, 
  CheckCircle2, 
  AlertCircle, 
  Clock, 
  MessageSquare,
  Send,
  Trash2,
  ChevronLeft,
  ChevronRight,
  Search,
  Pencil,
  X
} from 'lucide-react';
import styles from './management.module.css';

interface FollowUp {
  id: string;
  customerId: string;
  dueDate: string;
  status: 'Pending' | 'Completed' | 'Missed' | 'Bypassed';
  notes: string;
  type?: 'Nurturing' | 'AppointmentReminder';
  appointmentTime?: string;
  tone?: string;
}

const statusMapAr: Record<string, string> = {
  'Pending': 'معلقة',
  'Completed': 'مكتملة',
  'Bypassed': 'ملغية',
  'Missed': 'فائتة',
  'All': 'الكل'
};

export default function FollowUps() {
  const { activeProject } = useAuth();
  
  const [followUps, setFollowUps] = useState<FollowUp[]>([]);
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [loading, setLoading] = useState(true);
  const [reEvaluating, setReEvaluating] = useState(false);

  // Confirmation States
  const [confirmReEvaluateOpen, setConfirmReEvaluateOpen] = useState(false);
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);
  const [followUpToDelete, setFollowUpToDelete] = useState<string | null>(null);

  // Pagination State
  const [currentPage, setCurrentPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const [actionLoadingId, setActionLoadingId] = useState<string | null>(null);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [filter, setFilter] = useState<'All' | 'Pending' | 'Completed' | 'Bypassed' | 'Missed'>('Pending');

  // Search & Editing State
  const [searchQuery, setSearchQuery] = useState('');
  const [editingFollowUp, setEditingFollowUp] = useState<FollowUp | null>(null);
  const [editNotes, setEditNotes] = useState('');
  const [editDueDate, setEditDueDate] = useState('');
  const [editType, setEditType] = useState<'Nurturing' | 'AppointmentReminder'>('Nurturing');
  const [editAppointmentTime, setEditAppointmentTime] = useState('');
  const [editTone, setEditTone] = useState<string>('Default');

  const fetchData = async () => {
    if (!activeProject) return;
    try {
      setLoading(true);
      // Fetch follow-ups
      const fuRes = await api.get<FollowUp[]>(`/api/projects/${activeProject.id}/follow-ups`);
      // Fetch customers to map names
      const custData = await crmService.getCustomers(activeProject.id);
      
      setFollowUps(fuRes.data);
      setCustomers(custData);
    } catch (e) {
      console.error('Failed to load follow-ups', e);
      setMessage({ type: 'error', text: 'فشل تحميل مواعيد المتابعات.' });
    } finally {
      setLoading(false);
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
    // eslint-disable-next-line react-hooks/set-state-in-effect
    fetchData();
    setCurrentPage(1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeProject]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setCurrentPage(1);
  }, [filter, searchQuery]);

  const getCustomerName = (customerId: string) => {
    const customer = customers.find(c => c.id === customerId);
    return customer ? customer.name || customer.phoneNumber : `عميل (${customerId.substring(0, 8)})`;
  };

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
    
    setEditType(fu.type || 'Nurturing');
    setEditTone(fu.tone || 'Default');
    
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
    if (!editingFollowUp) return;
    
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

  const filteredFollowUps = followUps.filter(f => {
    // 1. Filter by status tab
    if (filter !== 'All' && f.status !== filter) return false;
    
    // 2. Filter by search query (customer name or phone number)
    if (searchQuery.trim() !== '') {
      const q = searchQuery.toLowerCase();
      const customer = customers.find(c => c.id === f.customerId);
      const name = customer?.name?.toLowerCase() || '';
      const phone = customer?.phoneNumber || '';
      if (!name.includes(q) && !phone.includes(q)) {
        return false;
      }
    }
    
    return true;
  });

  const totalCount = followUps.length;
  const pendingCount = followUps.filter(f => f.status === 'Pending').length;
  const completedCount = followUps.filter(f => f.status === 'Completed').length;
  const missedCount = followUps.filter(f => f.status === 'Missed').length;
  const bypassedCount = followUps.filter(f => f.status === 'Bypassed').length;

  const totalPages = Math.ceil(filteredFollowUps.length / pageSize) || 1;
  const paginatedFollowUps = filteredFollowUps.slice(
    (currentPage - 1) * pageSize,
    currentPage * pageSize
  );

  return (
    <div className={styles.container}>
      <div className={styles.header} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 'var(--space-md)' }}>
        <div>
          <h1 className={styles.pageTitle}>جدول المتابعات</h1>
          <p className={styles.pageSubtitle}>إدارة مواعيد المتابعات المعلقة والمتأخرة للاتصال بالعملاء أو مراسلتهم</p>
        </div>
        <button
          type="button"
          onClick={triggerReEvaluateAll}
          disabled={reEvaluating || pendingCount === 0}
          className={`${styles.btn} ${styles.btnPrimary}`}
          style={{ 
            padding: '10px 20px', 
            fontSize: '0.85rem', 
            backgroundColor: 'rgba(0, 243, 255, 0.1)', 
            color: 'hsl(var(--accent-primary))',
            borderColor: 'hsla(var(--accent-primary-hsl), 0.3)',
            borderRadius: 'var(--radius-sm)',
            fontWeight: 700,
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            gap: '8px'
          }}
        >
          <Clock size={16} />
          {reEvaluating ? 'جاري إعادة الضبط بالـ AI...' : 'ضبط المتابعات تلقائياً بالـ AI'}
        </button>
      </div>

      {/* KPI Stats Cards */}
      <div className={styles.statsGrid}>
        <div 
          className={`glass-panel ${styles.statCard} ${styles.statCardClickable} ${filter === 'All' ? styles.statCardActive : ''}`}
          onClick={() => setFilter('All')}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span className={styles.statLabel}>إجمالي المتابعات</span>
            <Calendar size={18} style={{ color: 'hsl(var(--accent-primary))' }} />
          </div>
          <h2 className={styles.statValue}>{totalCount}</h2>
          <span className={styles.statDesc}>كل المهام المجدولة</span>
        </div>

        <div 
          className={`glass-panel ${styles.statCard} ${styles.statCardClickable} ${filter === 'Pending' ? styles.statCardActive : ''}`}
          onClick={() => setFilter('Pending')}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span className={styles.statLabel}>المتابعات المعلقة</span>
            <Clock size={18} style={{ color: 'hsl(38, 92%, 60%)' }} />
          </div>
          <h2 className={styles.statValue}>{pendingCount}</h2>
          <span className={styles.statDesc}>في انتظار الإرسال</span>
        </div>

        <div 
          className={`glass-panel ${styles.statCard} ${styles.statCardClickable} ${filter === 'Completed' ? styles.statCardActive : ''}`}
          onClick={() => setFilter('Completed')}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span className={styles.statLabel}>المتابعات المكتملة</span>
            <CheckCircle2 size={18} style={{ color: 'hsl(140, 100%, 65%)' }} />
          </div>
          <h2 className={styles.statValue}>{completedCount}</h2>
          <span className={styles.statDesc}>تم إرسالها بنجاح</span>
        </div>

        <div 
          className={`glass-panel ${styles.statCard} ${styles.statCardClickable} ${filter === 'Missed' ? styles.statCardActive : ''}`}
          onClick={() => setFilter('Missed')}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span className={styles.statLabel}>المتابعات الفائتة</span>
            <AlertCircle size={18} style={{ color: 'hsl(0, 100%, 65%)' }} />
          </div>
          <h2 className={styles.statValue}>{missedCount}</h2>
          <span className={styles.statDesc}>فشل الإرسال أو تجاوز الموعد</span>
        </div>

        <div 
          className={`glass-panel ${styles.statCard} ${styles.statCardClickable} ${filter === 'Bypassed' ? styles.statCardActive : ''}`}
          onClick={() => setFilter('Bypassed')}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span className={styles.statLabel}>المتابعات الملغية</span>
            <AlertCircle size={18} style={{ color: 'hsl(215, 20%, 72%)' }} />
          </div>
          <h2 className={styles.statValue}>{bypassedCount}</h2>
          <span className={styles.statDesc}>تجاوزها الرد اليدوي للعميل</span>
        </div>
      </div>

      {message && (
        <div className={`glass-panel`} style={{ 
          padding: 'var(--space-md)', 
          background: message.type === 'success' ? 'var(--success-soft)' : 'var(--danger-soft)',
          border: '1px solid var(--border-subtle)',
          color: 'var(--text-strong)',
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--space-sm)'
        }}>
          <CheckCircle2 size={18} style={{ color: message.type === 'success' ? 'hsl(var(--accent-success))' : 'hsl(var(--accent-danger))' }} />
          <span style={{ fontSize: '0.9rem', fontWeight: 600 }}>{message.text}</span>
        </div>
      )}

      {/* Tabs and Search */}
      <div style={{ 
        display: 'flex', 
        justifyContent: 'space-between', 
        alignItems: 'center', 
        gap: 'var(--space-md)', 
        borderBottom: '1px solid var(--border-subtle)', 
        paddingBottom: '8px',
        flexWrap: 'wrap'
      }}>
        {/* Tabs */}
        <div style={{ display: 'flex', gap: 'var(--space-sm)' }}>
          {(['Pending', 'Completed', 'Bypassed', 'Missed', 'All'] as const).map(fOpt => (
            <button
              key={fOpt}
              onClick={() => setFilter(fOpt)}
              style={{
                padding: '0.5rem 1rem',
                background: filter === fOpt ? 'var(--accent-soft)' : 'transparent',
                border: 'none',
                borderBottom: filter === fOpt ? '2px solid var(--accent)' : '2px solid transparent',
                color: filter === fOpt ? 'var(--accent)' : 'var(--text-soft)',
                cursor: 'pointer',
                fontWeight: 600,
                fontSize: '0.85rem',
                transition: 'all 0.15s ease'
              }}
            >
              {statusMapAr[fOpt]}
            </button>
          ))}
        </div>

        {/* Search input */}
        <div style={{ position: 'relative', minWidth: '280px' }}>
          <input
            type="text"
            placeholder="بحث باسم العميل أو رقم الهاتف..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            style={{
              width: '100%',
              padding: '8px 36px 8px 12px',
              borderRadius: 'var(--radius-sm)',
              border: '1px solid var(--border-subtle)',
              background: 'var(--surface-muted)',
              color: 'var(--text-strong)',
              fontSize: '0.85rem',
              outline: 'none',
              transition: 'all 0.15s ease',
              textAlign: 'right'
            }}
          />
          <Search 
            size={16} 
            style={{ 
              position: 'absolute', 
              right: '12px', 
              top: '50%', 
              transform: 'translateY(-50%)', 
              color: 'hsl(var(--text-muted))',
              pointerEvents: 'none'
            }} 
          />
        </div>
      </div>

      <div className={`glass-panel ${styles.panel}`}>
        {loading ? (
          <div className={styles.emptyState}>
            <div className={styles.spinner}></div>
            <p style={{ marginTop: 'var(--space-md)' }}>جاري تحميل المتابعات...</p>
          </div>
        ) : filteredFollowUps.length === 0 ? (
          <div className={styles.emptyState}>
            <Calendar size={48} style={{ color: 'hsl(var(--text-muted))' }} />
            <h3 className={styles.emptyStateTitle}>لم يتم العثور على متابعات</h3>
            <p className={styles.emptyStateDesc}>ليس لديك أي مهام متابعة تطابق خيار التصفية هذا.</p>
          </div>
        ) : (
          <div className={styles.tableWrapper}>
            <table className={styles.table}>
              <thead>
                <tr>
                  <th className={styles.th}>العميل</th>
                  <th className={styles.th}>تاريخ الاستحقاق</th>
                  <th className={styles.th}>نوع المتابعة</th>
                  <th className={styles.th}>ملاحظات</th>
                  <th className={styles.th}>الحالة</th>
                  <th className={styles.th} style={{ textAlign: 'center' }}>الإجراء</th>
                </tr>
              </thead>
              <tbody>
                {paginatedFollowUps.map(fu => {
                  const isOverdue = new Date(fu.dueDate) < new Date() && fu.status === 'Pending';
                  return (
                    <tr key={fu.id} className={styles.tr}>
                      <td className={styles.td}>
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
                            {getCustomerName(fu.customerId).charAt(0).toUpperCase()}
                          </div>
                          <span style={{ fontWeight: 600, color: 'var(--text-strong)' }}>
                            {getCustomerName(fu.customerId)}
                          </span>
                        </div>
                      </td>
                      <td className={styles.td}>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                          <div style={{ display: 'flex', alignItems: 'center', gap: '6px', color: isOverdue ? 'hsl(var(--accent-danger))' : 'var(--text-base)' }}>
                            {isOverdue ? <AlertCircle size={14} /> : <Clock size={14} />}
                            <span>{new Date(fu.dueDate).toLocaleString('ar-EG')}</span>
                            {isOverdue && <span style={{ fontSize: '0.7rem', fontWeight: 700, marginRight: '4px', color: 'hsl(var(--accent-danger))' }}>متأخرة</span>}
                          </div>
                          {fu.type === 'AppointmentReminder' && fu.appointmentTime && (
                            <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))', paddingRight: '20px' }}>
                              الموعد: {new Date(fu.appointmentTime).toLocaleString('ar-EG')}
                            </span>
                          )}
                        </div>
                      </td>
                      <td className={styles.td}>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', alignItems: 'flex-start' }}>
                          {fu.type === 'AppointmentReminder' ? (
                            <span className={styles.statusBadge} style={{
                              backgroundColor: 'rgba(16, 185, 129, 0.12)',
                              color: 'hsl(140, 100%, 65%)',
                              border: '1px solid rgba(16, 185, 129, 0.2)'
                            }}>
                              تذكير بموعد
                            </span>
                          ) : (
                            <span className={styles.statusBadge} style={{
                              backgroundColor: 'rgba(99, 102, 241, 0.12)',
                              color: 'hsl(239, 84%, 75%)',
                              border: '1px solid rgba(99, 102, 241, 0.2)'
                            }}>
                              متابعة عميل
                            </span>
                          )}
                          {fu.tone && fu.tone !== 'Default' && (
                            <span className={styles.statusBadge} style={{
                              backgroundColor: 'rgba(168, 85, 247, 0.12)',
                              color: 'hsl(270, 84%, 75%)',
                              border: '1px solid rgba(168, 85, 247, 0.2)',
                              fontSize: '0.7rem'
                            }}>
                              {fu.tone === 'Creative' ? 'إبداعي' : 'سلزجي صايع'}
                            </span>
                          )}
                        </div>
                      </td>
                      <td className={styles.td}>
                        <div style={{ display: 'flex', alignItems: 'flex-start', gap: '6px' }}>
                          <MessageSquare size={14} style={{ color: 'hsl(var(--text-muted))', marginTop: '2px' }} />
                          <span>{fu.notes || 'لا يوجد وصف.'}</span>
                        </div>
                      </td>
                      <td className={styles.td}>
                        <span className={`${styles.statusBadge} ${
                          fu.status === 'Completed' ? styles.statusCompleted :
                          fu.status === 'Bypassed' ? styles.statusBypassed :
                          fu.status === 'Missed' ? styles.statusFailed :
                          styles.statusPending
                        }`}>
                          {statusMapAr[fu.status]}
                        </span>
                      </td>
                      <td className={styles.td} style={{ textAlign: 'center' }}>
                        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '6px' }}>
                          {fu.status === 'Pending' ? (
                            <>
                              <button
                                onClick={() => handleSend(fu.id)}
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
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>

            {/* Pagination Controls */}
            {filteredFollowUps.length > 0 && (
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
                    عرض {Math.min((currentPage - 1) * pageSize + 1, filteredFollowUps.length)} - {Math.min(currentPage * pageSize, filteredFollowUps.length)} من {filteredFollowUps.length}
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

      {editingFollowUp && (
        <div className={styles.overlay}>
          <div className={`glass-panel ${styles.modal}`}>
            <div className={styles.modalHeader}>
              <h3 className={styles.modalTitle}>تعديل تفاصيل المتابعة</h3>
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
                <label className={styles.label}>نوع المتابعة</label>
                <select
                  value={editType}
                  onChange={(e) => setEditType(e.target.value as 'Nurturing' | 'AppointmentReminder')}
                  className={styles.select}
                >
                  <option value="Nurturing">متابعة لتنشيط العميل (Nurturing)</option>
                  <option value="AppointmentReminder">تذكير بموعد / كورس (Reminder)</option>
                </select>
              </div>

              {editType === 'Nurturing' ? (
                <div className={styles.formGroup}>
                  <label className={styles.label}>تاريخ ووقت المتابعة</label>
                  <input 
                    type="datetime-local" 
                    value={editDueDate}
                    onChange={(e) => setEditDueDate(e.target.value)}
                    className={styles.input} 
                    required
                  />
                </div>
              ) : (
                <div className={styles.formGroup}>
                  <label className={styles.label}>تاريخ ووقت الكورس / الموعد</label>
                  <input 
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
              )}

              <div className={styles.formGroup}>
                <label className={styles.label}>نبرة المتابعة (Tone)</label>
                <select
                  value={editTone}
                  onChange={(e) => setEditTone(e.target.value)}
                  className={styles.select}
                >
                  <option value="Default">الوضع الافتراضي (Default)</option>
                  <option value="Creative">إبداعي (Creative)</option>
                  <option value="Salesy">سلزجي صايع (Salesy)</option>
                </select>
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>نص الرسالة / ملاحظات</label>
                <textarea
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
        </div>
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
