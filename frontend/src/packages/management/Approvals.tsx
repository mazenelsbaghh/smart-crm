'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import Dialog from '../../components/shared/Dialog';
import {
  ShieldCheck,
  Check,
  X,
  AlertTriangle,
  User,
  Clock,
  ChevronLeft,
  ChevronRight
} from 'lucide-react';
import PageHeader from '../../components/ui/PageHeader';
import StatusMessage from '../../components/ui/StatusMessage';
import FilterTabs from '../../components/ui/FilterTabs';
import styles from './management.module.css';
import dialogStyles from './ManagementDialogs.module.css';
import { useListFilters } from './hooks/use-list-filters';

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

const listFilterConfig = { statuses: ['Pending', 'Approved', 'Rejected'] as const, defaultStatus: 'Pending' as const, search: false };

export default function Approvals() {
  return <React.Suspense fallback={<p role="status">جاري تحميل القائمة…</p>}><ApprovalsList /></React.Suspense>;
}

function ApprovalsList() {
  const { activeProject } = useAuth();
  const [requests, setRequests] = useState<ApprovalRequest[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [loadedFilterKey, setLoadedFilterKey] = useState<string | null>(null);

  const { status: activeTab, setStatus: setActiveTab, page: currentPage, setPage: setCurrentPage,
    pageSize, setPageSize, clearFilters, hasFilters } = useListFilters(listFilterConfig);

  const [actionLoadingId, setActionLoadingId] = useState<string | null>(null);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [approvalToConfirm, setApprovalToConfirm] = useState<ApprovalRequest | null>(null);
  const [rejectionToConfirm, setRejectionToConfirm] = useState<ApprovalRequest | null>(null);
  const [rejectionReason, setRejectionReason] = useState('');
  const rejectionReturnFocusRef = React.useRef<HTMLButtonElement | null>(null);
  const loadRequestIdRef = React.useRef(0);
  const filterKey = JSON.stringify([activeProject?.id, activeTab]);

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
      setLoadedFilterKey(filterKey);
    } catch (e) {
      if (requestId !== loadRequestIdRef.current) return;
      console.error('Failed to load approval queue', e);
      setLoadError('فشل تحميل قائمة طلبات الاعتماد. لم يتم عرض قائمة فارغة بديلًا عنها.');
    } finally {
      if (requestId === loadRequestIdRef.current) setLoading(false);
    }
  }, [activeProject, activeTab, filterKey]);

  useEffect(() => {
    const loadTimer = window.setTimeout(() => {
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
        <dl className={styles.payloadDetails}>
          {safeEntries.length === 0 ? <span>لا توجد تفاصيل آمنة للعرض.</span> : safeEntries.map(([key, value]) => (
            <div key={key}>
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
  useEffect(() => {
    if (!loading && !loadError && loadedFilterKey === filterKey && currentPage > totalPages) setCurrentPage(totalPages, 'replace');
  }, [loading, loadError, loadedFilterKey, filterKey, currentPage, totalPages, setCurrentPage]);
  const paginatedRequests = requests.slice(
    (currentPage - 1) * pageSize,
    currentPage * pageSize
  );

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <PageHeader icon={<ShieldCheck size={22} aria-hidden="true" />} title="الموافقات" description="راجع الإجراء المقترح قبل الموافقة على تنفيذه." />
      </div>

      {message && <StatusMessage tone={message.type === 'error' ? 'danger' : 'success'}>{message.text}</StatusMessage>}

      <div className={styles.listSearchTools}>
        <FilterTabs compactOnMobile label="تصفية طلبات الاعتماد حسب الحالة" selected={activeTab} onChange={setActiveTab}
          options={[{ key: 'Pending', label: 'طلبات معلقة', icon: <Clock size={16} aria-hidden="true" />, tone: 'warning' }, { key: 'Approved', label: 'طلبات معتمدة', icon: <Check size={16} aria-hidden="true" />, tone: 'success' }, { key: 'Rejected', label: 'طلبات مرفوضة', icon: <X size={16} aria-hidden="true" />, tone: 'danger' }]} />
        {hasFilters && <button type="button" className={`${styles.btn} ${styles.btnSecondary}`} onClick={clearFilters}>إعادة ضبط التصفية</button>}
      </div>

      <div className={`${styles.panel} ${styles.operationsPanel}`}>
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
            <div className={styles.approvalList} aria-label="طلبات الاعتماد وحالة المخاطر">
              {paginatedRequests.map(req => (
                <article key={req.id} className={styles.approvalRecord}>
                  <div className={styles.approvalContext}>
                    <span className={`${styles.statusBadge} ${req.riskLevel === 'Critical' ? styles.statusFailed : req.riskLevel === 'High' ? styles.statusPending : styles.statusInfo}`}>
                      <AlertTriangle size={14} aria-hidden="true" />خطورة {riskMapAr[req.riskLevel] || req.riskLevel}
                    </span>
                    <h2>{req.actionType}</h2>
                    <span className={styles.recordMeta}><User size={15} aria-hidden="true" />{req.requestedBy}</span>
                    <span className={styles.recordMeta}><Clock size={15} aria-hidden="true" />{formatCairoDate(req.createdAt)}</span>
                    <small className={styles.recordMeta}>طلب {req.id.substring(0, 8)}</small>
                  </div>
                  <div className={styles.approvalContent}>
                    <h3 className={styles.recordLabel}>الإجراء المقترح</h3>
                    {renderPayload(req.payloadJson)}
                    {req.notes && <p className={styles.recordNote}><strong>ملاحظات: </strong>{req.notes}</p>}
                    {activeTab === 'Pending' && <div className={styles.recordActions}>
                      <button type="button" onClick={() => setApprovalToConfirm(req)} disabled={actionLoadingId !== null}
                        className={`${styles.btn} ${styles.btnPrimary}`} aria-label={`مراجعة اعتماد وتنفيذ الإجراء ${req.actionType}`}>
                        <Check size={16} aria-hidden="true" /> مراجعة وموافقة
                      </button>
                      <button type="button" onClick={event => { rejectionReturnFocusRef.current = event.currentTarget; setRejectionReason(''); setRejectionToConfirm(req); }}
                        disabled={actionLoadingId !== null} className={`${styles.btn} ${styles.btnDanger}`} aria-label={`رفض الإجراء ${req.actionType}`}>
                        <X size={16} aria-hidden="true" /> رفض
                      </button>
                    </div>}
                  </div>
                </article>
              ))}
            </div>

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
        <Dialog
          label="رفض طلب الموافقة" onClose={closeRejectionDialog}
          title="رفض الطلب"
          footer={<>
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
          </>}
        >
          <div className={styles.dialogContent}>
            <div className={dialogStyles.form}>
              <p id="reject-approval-description">اكتب سببًا واضحًا لرفض الإجراء «{rejectionToConfirm.actionType}».</p>
              <div className={styles.formGroup}>
                <label className={styles.label} htmlFor="approval-rejection-reason">سبب الرفض</label>
                <textarea id="approval-rejection-reason" className={styles.textarea} value={rejectionReason} onChange={(event) => setRejectionReason(event.target.value)} required autoFocus />
              </div>
            </div>
          </div>
        </Dialog>
      )}
    </div>
  );
}
