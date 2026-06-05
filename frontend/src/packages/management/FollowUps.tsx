'use client';

import React, { useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
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
  ChevronRight
} from 'lucide-react';
import styles from './management.module.css';

interface FollowUp {
  id: string;
  customerId: string;
  dueDate: string;
  status: 'Pending' | 'Completed' | 'Missed';
  notes: string;
  type?: 'Nurturing' | 'AppointmentReminder';
  appointmentTime?: string;
}

const statusMapAr: Record<string, string> = {
  'Pending': 'معلقة',
  'Completed': 'مكتملة',
  'Missed': 'فائتة',
  'All': 'الكل'
};

export default function FollowUps() {
  const { activeProject } = useAuth();
  
  const [followUps, setFollowUps] = useState<FollowUp[]>([]);
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [loading, setLoading] = useState(true);

  // Pagination State
  const [currentPage, setCurrentPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const [actionLoadingId, setActionLoadingId] = useState<string | null>(null);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [filter, setFilter] = useState<'All' | 'Pending' | 'Completed' | 'Missed'>('Pending');

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

  useEffect(() => {
    fetchData();
    setCurrentPage(1);
  }, [activeProject]);

  useEffect(() => {
    setCurrentPage(1);
  }, [filter]);

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
    } catch (e: any) {
      console.error('Failed to send follow-up', e);
      setMessage({ type: 'error', text: 'فشل إرسال رسالة المتابعة.' });
    } finally {
      setActionLoadingId(null);
    }
  };

  const handleDelete = async (id: string) => {
    if (!window.confirm('هل أنت متأكد من مسح هذه المتابعة؟')) return;
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

  const filteredFollowUps = followUps.filter(f => {
    if (filter === 'All') return true;
    return f.status === filter;
  });

  const totalPages = Math.ceil(filteredFollowUps.length / pageSize) || 1;
  const paginatedFollowUps = filteredFollowUps.slice(
    (currentPage - 1) * pageSize,
    currentPage * pageSize
  );

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <div>
          <h1 className={styles.pageTitle}>جدول المتابعات</h1>
          <p className={styles.pageSubtitle}>إدارة مواعيد المتابعات المعلقة والمتأخرة للاتصال بالعملاء أو مراسلتهم</p>
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

      {/* Tabs */}
      <div style={{ display: 'flex', gap: 'var(--space-sm)', borderBottom: '1px solid var(--border-subtle)', paddingBottom: '2px' }}>
        {(['Pending', 'Completed', 'Missed', 'All'] as const).map(fOpt => (
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
                          fu.status === 'Missed' ? styles.statusFailed :
                          styles.statusPending
                        }`}>
                          {statusMapAr[fu.status]}
                        </span>
                      </td>
                      <td className={styles.td} style={{ textAlign: 'center' }}>
                        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '6px' }}>
                          {fu.status === 'Pending' ? (
                            <button
                              onClick={() => handleSend(fu.id)}
                              disabled={actionLoadingId !== null}
                              className={`${styles.btn} ${styles.btnSuccess}`}
                              style={{ padding: '4px 10px', fontSize: '0.75rem', display: 'inline-flex', alignItems: 'center', gap: '4px' }}
                            >
                              <Send size={12} />
                              إرسال
                            </button>
                          ) : (
                            <span style={{ color: 'hsl(var(--text-muted))', fontSize: '0.8rem' }}>مغلقة</span>
                          )}

                          <button
                            onClick={() => handleDelete(fu.id)}
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
    </div>
  );
}
