'use client';

import React, { useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import { 
  ShieldCheck, 
  Check, 
  X, 
  AlertTriangle, 
  User, 
  FileText, 
  HelpCircle,
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

export default function Approvals() {
  const { activeProject } = useAuth();
  const [requests, setRequests] = useState<ApprovalRequest[]>([]);
  const [loading, setLoading] = useState(true);

  // Pagination State
  const [currentPage, setCurrentPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const [actionLoadingId, setActionLoadingId] = useState<string | null>(null);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [activeTab, setActiveTab] = useState<'Pending' | 'Approved' | 'Rejected'>('Pending');

  const fetchApprovals = async () => {
    if (!activeProject) return;
    try {
      setLoading(true);
      const response = await api.get<ApprovalRequest[]>(`/api/projects/${activeProject.id}/approvals`, {
        params: { status: activeTab }
      });
      setRequests(response.data);
    } catch (e) {
      console.error('Failed to load approval queue', e);
      setMessage({ type: 'error', text: 'فشل تحميل قائمة طلبات الاعتماد.' });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchApprovals();
    setCurrentPage(1);
  }, [activeProject, activeTab]);

  const handleApprove = async (id: string) => {
    try {
      setActionLoadingId(id);
      setMessage(null);
      await api.post(`/api/approvals/${id}/approve`);
      setMessage({ type: 'success', text: 'تمت الموافقة على الطلب وتنفيذه بنجاح.' });
      setRequests(prev => prev.filter(r => r.id !== id));
    } catch (e: any) {
      console.error('Failed to approve request', e);
      setMessage({ type: 'error', text: e.response?.data || 'فشل اعتماد الطلب.' });
    } finally {
      setActionLoadingId(null);
    }
  };

  const handleReject = async (id: string) => {
    const notes = prompt('أدخل سبب الرفض (اختياري):') || undefined;
    try {
      setActionLoadingId(id);
      setMessage(null);
      await api.post(`/api/approvals/${id}/reject`, { notes });
      setMessage({ type: 'success', text: 'تم رفض الطلب واستبعاده.' });
      setRequests(prev => prev.filter(r => r.id !== id));
    } catch (e: any) {
      console.error('Failed to reject request', e);
      setMessage({ type: 'error', text: e.response?.data || 'فشل رفض الطلب.' });
    } finally {
      setActionLoadingId(null);
    }
  };

  const renderPayload = (payloadJson: string) => {
    try {
      const parsed = JSON.parse(payloadJson);
      return (
        <div style={{ 
          fontSize: '0.8rem', 
          background: 'rgba(0, 0, 0, 0.2)', 
          padding: 'var(--space-sm)', 
          borderRadius: 'var(--radius-sm)',
          border: '1px solid rgba(255, 255, 255, 0.05)',
          fontFamily: 'monospace',
          color: 'hsl(var(--text-secondary))',
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-all'
        }}>
          {JSON.stringify(parsed, null, 2)}
        </div>
      );
    } catch {
      return <span style={{ fontFamily: 'monospace' }}>{payloadJson}</span>;
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
        <div className={`glass-panel`} style={{ 
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
      <div style={{ display: 'flex', gap: 'var(--space-sm)', borderBottom: '1px solid var(--border-subtle)', paddingBottom: '2px' }}>
        {(['Pending', 'Approved', 'Rejected'] as const).map(tab => (
          <button
            key={tab}
            onClick={() => setActiveTab(tab)}
            style={{
              padding: '0.5rem 1rem',
              background: activeTab === tab ? 'var(--accent-soft)' : 'transparent',
              border: 'none',
              borderBottom: activeTab === tab ? '2px solid var(--accent)' : '2px solid transparent',
              color: activeTab === tab ? 'var(--accent)' : 'var(--text-soft)',
              cursor: 'pointer',
              fontWeight: 600,
              fontSize: '0.85rem',
              transition: 'all 0.15s ease'
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
        ) : requests.length === 0 ? (
          <div className={styles.emptyState}>
            <ShieldCheck size={48} style={{ color: 'hsl(var(--text-muted))' }} />
            <h3 className={styles.emptyStateTitle}>لا توجد طلبات معلقة!</h3>
            <p className={styles.emptyStateDesc}>لا توجد أي طلبات {statusMapAr[activeTab]} تتطلب اتخاذ إجراء حالياً.</p>
          </div>
        ) : (
          <div className={styles.tableWrapper}>
            <table className={styles.table}>
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
                            onClick={() => handleApprove(req.id)}
                            disabled={actionLoadingId !== null}
                            className={`${styles.btnIcon} ${styles.btnSuccess}`}
                            title="موافقة وتنفيذ"
                            style={{ padding: '6px' }}
                          >
                            <Check size={16} />
                          </button>
                          <button
                            onClick={() => handleReject(req.id)}
                            disabled={actionLoadingId !== null}
                            className={`${styles.btnIcon} ${styles.btnDanger}`}
                            title="رفض واستبعاد"
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
