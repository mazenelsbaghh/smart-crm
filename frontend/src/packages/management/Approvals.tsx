'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import { handleDialogKeyDown } from './dialog-accessibility';
import { 
  ShieldCheck, 
  Check, 
  X, 
  AlertTriangle, 
  User, 
  ChevronLeft,
  ChevronRight
} from 'lucide-react';
import styles from './management.module.css';

interface ApprovalRequest {
  id: string;
  projectId: string;
  actionType: string;
  payloadJson: string;
  riskLevel: 'Low' | 'Medium' | 'High' | 'Critical';
  status: 'Pending' | 'Approved' | 'Rejected';
  requestedBy: string;
  notes?: string;
  createdAt?: string;
}

const statusMapAr: Record<string, string> = {
  'Pending': 'معلقة',
  'Approved': 'معتمدة',
  'Rejected': 'مرفوضة'
};

const riskMapAr: Record<string, string> = {
  'Low': 'منخفض',
  'Medium': 'متوسط',
  'High': 'مرتفع',
  'Critical': 'حرِج'
};

const sensitivePayloadKey = /token|secret|password|credential|api.?key|رمز|كلمة.?مرور|سر/i;

function sanitizePayloadValue(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(sanitizePayloadValue);
  if (value !== null && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value as Record<string, unknown>)
        .filter(([key]) => !sensitivePayloadKey.test(key))
        .map(([key, nestedValue]) => [key, sanitizePayloadValue(nestedValue)]),
    );
  }
  return value;
}

function formatPayloadValue(value: unknown): string {
  const serialized = typeof value === 'string' ? value : JSON.stringify(value);
  if (!serialized) return 'غير متاح';
  return serialized.length > 500 ? `${serialized.slice(0, 500)}…` : serialized;
}

function formatCairoDate(value?: string): string {
  if (!value) return 'وقت الطلب غير متاح من المصدر';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'وقت الطلب غير صالح في المصدر';
  return `${date.toLocaleString('ar-EG', { timeZone: 'Africa/Cairo', dateStyle: 'medium', timeStyle: 'short' })} بتوقيت القاهرة`;
}

export default function Approvals() {
  const { activeProject } = useAuth();
  const [requests, setRequests] = useState<ApprovalRequest[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  // Pagination State
  const [currentPage, setCurrentPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const [actionLoadingId, setActionLoadingId] = useState<string | null>(null);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [activeTab, setActiveTab] = useState<'Pending' | 'Approved' | 'Rejected'>('Pending');
  const [approvalToConfirm, setApprovalToConfirm] = useState<ApprovalRequest | null>(null);
  const [rejectionToConfirm, setRejectionToConfirm] = useState<ApprovalRequest | null>(null);
  const [rejectionReason, setRejectionReason] = useState('');
  const rejectionReturnFocusRef = React.useRef<HTMLButtonElement | null>(null);
  const loadRequestIdRef = React.useRef(0);

  const fetchApprovals = useCallback(async () => {
    const requestId = ++loadRequestIdRef.current;
    if (!activeProject) {
      setRequests([]);
      setLoading(false);
      setLoadError('تعذر تحميل مساحة العمل. أعد المحاولة أو تواصل مع المدير.');
      return;
    }
    try {
      setLoading(true);
      setLoadError(null);
      setRequests([]);
      const response = await api.get<ApprovalRequest[]>(`/api/projects/${activeProject.id}/approvals`, {
        params: { status: activeTab }
      });
      if (requestId !== loadRequestIdRef.current) return;
      setRequests(response.data);
    } catch (e) {
      if (requestId !== loadRequestIdRef.current) return;
      console.error('Failed to load approval queue', e);
      setLoadError('فشل تحميل قائمة طلبات الاعتماد. لم يتم عرض قائمة فارغة بديلًا عنها.');
    } finally {
      if (requestId === loadRequestIdRef.current) setLoading(false);
    }
  }, [activeProject, activeTab]);

  useEffect(() => {
    const loadTimer = window.setTimeout(() => {
      setCurrentPage(1);
      void fetchApprovals();
    }, 0);
    return () => window.clearTimeout(loadTimer);
  }, [fetchApprovals]);

  const handleApprove = async (id: string) => {
    try {
      setActionLoadingId(id);
      setMessage(null);
      await api.post(`/api/approvals/${id}/approve`);
      setMessage({ type: 'success', text: 'تمت الموافقة على الطلب وتنفيذه بنجاح.' });
      setRequests(prev => prev.filter(r => r.id !== id));
    } catch (e) {
      console.error('Failed to approve request', e);
      setMessage({ type: 'error', text: 'فشل اعتماد الطلب.' });
    } finally {
      setActionLoadingId(null);
    }
  };

  const handleReject = async (id: string, notes: string) => {
    try {
      setActionLoadingId(id);
      setMessage(null);
      await api.post(`/api/approvals/${id}/reject`, { notes });
      setMessage({ type: 'success', text: 'تم رفض الطلب واستبعاده.' });
      setRequests(prev => prev.filter(r => r.id !== id));
    } catch (e) {
      console.error('Failed to reject request', e);
      setMessage({ type: 'error', text: 'فشل رفض الطلب.' });
    } finally {
      setActionLoadingId(null);
    }
  };

  const closeRejectionDialog = () => {
    setRejectionToConfirm(null);
    setRejectionReason('');
    window.setTimeout(() => rejectionReturnFocusRef.current?.focus(), 0);
  };

  const renderPayload = (payloadJson: string) => {
    try {
      const parsed: unknown = JSON.parse(payloadJson);
      if (parsed === null || Array.isArray(parsed) || typeof parsed !== 'object') {
        return <span>لا توجد تفاصيل منظّمة وآمنة للعرض.</span>;
      }
      const safePayload = sanitizePayloadValue(parsed) as Record<string, unknown>;
      const safeEntries = Object.entries(safePayload);
      return (
        <dl style={{
          fontSize: '0.8rem', 
          background: 'rgba(0, 0, 0, 0.2)', 
          padding: 'var(--space-sm)', 
          borderRadius: 'var(--radius-sm)',
          border: '1px solid rgba(255, 255, 255, 0.05)',
          fontFamily: 'monospace',
          color: 'hsl(var(--text-secondary))',
          wordBreak: 'break-word'
        }}>
          {safeEntries.length === 0 ? <span>لا توجد تفاصيل آمنة للعرض.</span> : safeEntries.map(([key, value]) => (
            <div key={key} style={{ display: 'grid', gridTemplateColumns: 'minmax(100px, 0.4fr) 1fr', gap: '8px' }}>
              <dt>{key}</dt>
              <dd>{formatPayloadValue(value)}</dd>
            </div>
          ))}
        </dl>
      );
    } catch {
      return <span>تعذر قراءة تفاصيل الإجراء بأمان.</span>;
    }
  };

  const totalPages = Math.ceil(requests.length / pageSize) || 1;
  const paginatedRequests = requests.slice(
    (currentPage - 1) * pageSize,
    currentPage * pageSize
  );

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <div>
          <h1 className={styles.pageTitle}>إدارة الموافقات</h1>
          <p className={styles.pageSubtitle}>مراجعة وإدارة الإجراءات والرسائل المقترحة من قبل الذكاء الاصطناعي والتي تتطلب موافقة بشرية</p>
        </div>
      </div>

      {message && (
        <div className={`glass-panel`} role={message.type === 'error' ? 'alert' : 'status'} style={{
          padding: 'var(--space-md)', 
          borderRight: `4px solid ${message.type === 'success' ? 'hsl(var(--accent-success))' : 'hsl(var(--accent-danger))'}`,
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--space-sm)'
        }}>
          <ShieldCheck size={18} style={{ color: message.type === 'success' ? 'hsl(var(--accent-success))' : 'hsl(var(--accent-danger))' }} />
          <span style={{ fontSize: '0.9rem', fontWeight: 600 }}>{message.text}</span>
        </div>
      )}

      {/* Tabs */}
      <div role="group" aria-label="تصفية طلبات الاعتماد حسب الحالة" style={{ display: 'flex', gap: 'var(--space-sm)', borderBottom: '1px solid var(--border-subtle)', paddingBottom: '2px' }}>
        {(['Pending', 'Approved', 'Rejected'] as const).map(tab => (
          <button
            key={tab}
            type="button"
            onClick={() => setActiveTab(tab)}
            aria-pressed={activeTab === tab}
            style={{
              padding: '0.5rem 1rem',
              background: activeTab === tab ? 'var(--accent-soft)' : 'transparent',
              border: 'none',
              borderBottom: activeTab === tab ? '2px solid var(--accent)' : '2px solid transparent',
              color: activeTab === tab ? 'var(--accent)' : 'var(--text-soft)',
              cursor: 'pointer',
              fontWeight: 600,
              fontSize: '0.85rem',
              transition: 'background-color 0.15s ease, border-color 0.15s ease, color 0.15s ease'
            }}
          >
            طلب {statusMapAr[tab]}
          </button>
        ))}
      </div>

      <div className={`glass-panel ${styles.panel}`}>
        {loading ? (
          <div className={styles.emptyState}>
            <div className={styles.spinner}></div>
            <p style={{ marginTop: 'var(--space-md)' }}>جاري تحميل طلبات الاعتماد...</p>
          </div>
        ) : loadError ? (
          <div className={styles.emptyState} role="alert">
            <h3 className={styles.emptyStateTitle}>تعذر تحميل طلبات الاعتماد</h3>
            <p className={styles.emptyStateDesc}>{loadError}</p>
            {activeProject && <button type="button" onClick={() => void fetchApprovals()} className={`${styles.btn} ${styles.btnPrimary}`}>إعادة المحاولة</button>}
          </div>
        ) : requests.length === 0 ? (
          <div className={styles.emptyState}>
            <ShieldCheck size={48} style={{ color: 'hsl(var(--text-muted))' }} />
            <h3 className={styles.emptyStateTitle}>لا توجد طلبات {statusMapAr[activeTab]}</h3>
            <p className={styles.emptyStateDesc}>لم يُرجع المصدر أي طلبات بهذه الحالة.</p>
          </div>
        ) : (
          <div className={styles.tableWrapper}>
            <table className={styles.table}>
              <caption className="sr-only">طلبات الاعتماد وحالة المخاطر</caption>
              <thead>
                <tr>
                  <th className={styles.th}>التفاصيل</th>
                  <th className={styles.th}>مستوى الخطورة</th>
                  <th className={styles.th}>بطلب من</th>
                  <th className={styles.th}>محتوى الإجراء المقترح</th>
                  {activeTab === 'Pending' && <th className={styles.th} style={{ textAlign: 'center' }}>التحقق والاعتماد</th>}
                </tr>
              </thead>
              <tbody>
                {paginatedRequests.map(req => (
                  <tr key={req.id} className={styles.tr}>
                    <td className={styles.td}>
                      <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                        <span style={{ fontWeight: 600, color: 'var(--text-strong)' }}>{req.actionType}</span>
                        <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))' }}>معرّف: {req.id.substring(0, 8)}</span>
                        <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))' }}>{formatCairoDate(req.createdAt)}</span>
                      </div>
                    </td>
                    <td className={styles.td}>
                      <span className={`${styles.statusBadge} ${
                        req.riskLevel === 'Critical' ? styles.statusFailed :
                        req.riskLevel === 'High' ? styles.statusPending :
                        styles.statusActive
                      }`} style={{ display: 'inline-flex', gap: '4px', alignItems: 'center' }}>
                        <AlertTriangle size={12} />
                        {riskMapAr[req.riskLevel] || req.riskLevel}
                      </span>
                    </td>
                    <td className={styles.td}>
                      <span style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                        <User size={14} style={{ color: 'hsl(var(--accent-secondary))' }} />
                        {req.requestedBy}
                      </span>
                    </td>
                    <td className={styles.td} style={{ maxWidth: '400px' }}>
                      {renderPayload(req.payloadJson)}
                      {req.notes && (
                        <div style={{ fontSize: '0.8rem', color: 'hsl(var(--accent-warning))', marginTop: '4px' }}>
                          <strong>ملاحظات: </strong>{req.notes}
                        </div>
                      )}
                    </td>
                    {activeTab === 'Pending' && (
                      <td className={styles.td} style={{ textAlign: 'center' }}>
                        <div style={{ display: 'flex', gap: 'var(--space-sm)', justifyContent: 'center' }}>
                          <button
                            type="button"
                            onClick={() => setApprovalToConfirm(req)}
                            disabled={actionLoadingId !== null}
                            className={`${styles.btnIcon} ${styles.btnSuccess}`}
                            title="موافقة وتنفيذ"
                            aria-label={`مراجعة اعتماد وتنفيذ الإجراء ${req.actionType}`}
                            style={{ padding: '6px' }}
                          >
                            <Check size={16} />
                          </button>
                          <button
                            type="button"
                            onClick={(event) => {
                              rejectionReturnFocusRef.current = event.currentTarget;
                              setRejectionReason('');
                              setRejectionToConfirm(req);
                            }}
                            disabled={actionLoadingId !== null}
                            className={`${styles.btnIcon} ${styles.btnDanger}`}
                            title="رفض واستبعاد"
                            aria-label={`رفض الإجراء ${req.actionType}`}
                            style={{ padding: '6px' }}
                          >
                            <X size={16} />
                          </button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>

            {/* Pagination Controls */}
            {requests.length > 0 && (
              <div className={styles.pagination}>
                <div className={styles.paginationInfo}>
                  <span>عرض السطور:</span>
                  <select
                    aria-label="عدد طلبات الاعتماد في الصفحة"
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
                    عرض {Math.min((currentPage - 1) * pageSize + 1, requests.length)} - {Math.min(currentPage * pageSize, requests.length)} من {requests.length}
                  </span>
                </div>

                <div className={styles.paginationControls}>
                  <button
                    onClick={() => setCurrentPage(prev => Math.min(prev + 1, totalPages))}
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
                    onClick={() => setCurrentPage(prev => Math.max(prev - 1, 1))}
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
      <ConfirmDialog
        isOpen={approvalToConfirm !== null}
        title="تأكيد الموافقة والتنفيذ"
        message={approvalToConfirm ? `سيتم اعتماد وتنفيذ الإجراء «${approvalToConfirm.actionType}» بمستوى خطورة ${riskMapAr[approvalToConfirm.riskLevel]}.` : ''}
        confirmLabel="اعتماد وتنفيذ"
        onConfirm={() => {
          const request = approvalToConfirm;
          setApprovalToConfirm(null);
          if (request) void handleApprove(request.id);
        }}
        onCancel={() => setApprovalToConfirm(null)}
      />
      {rejectionToConfirm && (
        <div className={styles.overlay}>
          <div className={`glass-panel ${styles.modal}`} role="dialog" aria-modal="true" aria-labelledby="reject-approval-title" aria-describedby="reject-approval-description" onKeyDown={(event) => handleDialogKeyDown(event, closeRejectionDialog)}>
            <div className={styles.modalHeader}>
              <h2 id="reject-approval-title" className={styles.modalTitle}>رفض الطلب</h2>
              <button type="button" className={styles.closeBtn} onClick={closeRejectionDialog} aria-label="إغلاق">×</button>
            </div>
            <div className={styles.form}>
              <p id="reject-approval-description">اكتب سببًا واضحًا لرفض الإجراء «{rejectionToConfirm.actionType}».</p>
              <div className={styles.formGroup}>
                <label className={styles.label} htmlFor="approval-rejection-reason">سبب الرفض</label>
                <textarea id="approval-rejection-reason" className={styles.textarea} value={rejectionReason} onChange={(event) => setRejectionReason(event.target.value)} required autoFocus />
              </div>
              <div className={styles.formActions}>
                <button type="button" className={`${styles.btn} ${styles.btnSecondary}`} onClick={closeRejectionDialog}>إلغاء</button>
                <button
                  type="button"
                  className={`${styles.btn} ${styles.btnDanger}`}
                  disabled={!rejectionReason.trim() || actionLoadingId !== null}
                  onClick={() => {
                    const request = rejectionToConfirm;
                    const reason = rejectionReason.trim();
                    closeRejectionDialog();
                    void handleReject(request.id, reason);
                  }}
                >
                  رفض الطلب
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
