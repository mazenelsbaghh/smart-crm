'use client';

import React, { useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import { 
  BookOpen, 
  Upload, 
  RefreshCw, 
  FileText, 
  ExternalLink,
  CheckCircle2,
  AlertCircle,
  Plus,
  ChevronLeft,
  ChevronRight
} from 'lucide-react';
import styles from './management.module.css';

interface KnowledgeDocument {
  id: string;
  projectId: string;
  title: string;
  content: string;
  sourceUrl?: string;
  status: 'Draft' | 'PendingApproval' | 'Approved' | 'Rejected' | 'Published';
  version: number;
}

const statusMapAr: Record<string, string> = {
  'Draft': 'مسودة',
  'PendingApproval': 'قيد الاعتماد',
  'Approved': 'معتمد',
  'Published': 'معتمد',
  'Rejected': 'مرفوض'
};

export default function KnowledgeBase() {
  const { activeProject } = useAuth();
  
  const [documents, setDocuments] = useState<KnowledgeDocument[]>([]);
  const [activeTab, setActiveTab] = useState<'all' | 'approved' | 'pending'>('all');
  const [loading, setLoading] = useState(true);

  // Pagination State
  const [currentPage, setCurrentPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const [actionLoading, setActionLoading] = useState(false);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingDocumentId, setEditingDocumentId] = useState<string | null>(null);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  // Form Fields
  const [formTitle, setFormTitle] = useState('');
  const [formContent, setFormContent] = useState('');
  const [formSourceUrl, setFormSourceUrl] = useState('');

  const fetchDocuments = async () => {
    if (!activeProject) return;
    try {
      setLoading(true);
      const response = await api.get<KnowledgeDocument[]>(`/api/projects/${activeProject.id}/knowledge`);
      setDocuments(response.data);
    } catch (e) {
      console.error('Failed to load knowledge base documents', e);
      setMessage({ type: 'error', text: 'فشل جلب مستندات قاعدة المعرفة.' });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchDocuments();
    setCurrentPage(1);
  }, [activeProject]);

  useEffect(() => {
    setCurrentPage(1);
  }, [activeTab]);

  const handleSyncBrain = async () => {
    if (!activeProject) return;
    try {
      setActionLoading(true);
      setMessage(null);
      await api.post(`/api/projects/${activeProject.id}/brain/sync`);
      setMessage({ type: 'success', text: 'تمت إعادة فهرسة دماغ الذكاء الاصطناعي بنجاح.' });
      fetchDocuments();
    } catch (e: any) {
      console.error('Failed to sync brain', e);
      setMessage({ type: 'error', text: e.response?.data?.message || 'فشلت عملية المزامنة.' });
    } finally {
      setActionLoading(false);
    }
  };

  const handleSaveDocument = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!activeProject || !formTitle || !formContent) return;

    try {
      setActionLoading(true);
      setMessage(null);
      
      if (editingDocumentId) {
        await api.put(`/api/knowledge/${editingDocumentId}`, {
          title: formTitle,
          content: formContent,
          sourceUrl: formSourceUrl || undefined
        });
        setMessage({ type: 'success', text: `تم تحديث المستند "${formTitle}" بنجاح.` });
      } else {
        await api.post(`/api/projects/${activeProject.id}/knowledge`, {
          title: formTitle,
          content: formContent,
          sourceUrl: formSourceUrl || undefined
        });
        setMessage({ type: 'success', text: `تم رفع المستند "${formTitle}" بنجاح.` });
      }

      setIsModalOpen(false);
      setEditingDocumentId(null);
      setFormTitle('');
      setFormContent('');
      setFormSourceUrl('');
      fetchDocuments();
    } catch (e: any) {
      console.error('Failed to save document', e);
      setMessage({ type: 'error', text: e.response?.data || 'فشل حفظ المستند المعرفي.' });
    } finally {
      setActionLoading(false);
    }
  };

  const handleStartEdit = (doc: KnowledgeDocument) => {
    setEditingDocumentId(doc.id);
    setFormTitle(doc.title);
    setFormContent(doc.content);
    setFormSourceUrl(doc.sourceUrl || '');
    setIsModalOpen(true);
  };

  const handleApproveDocument = async (id: string) => {
    try {
      setMessage(null);
      await api.put(`/api/knowledge/${id}/approve`);
      setMessage({ type: 'success', text: 'تم نشر المستند بنجاح.' });
      fetchDocuments();
    } catch (e) {
      console.error('Failed to approve document', e);
      setMessage({ type: 'error', text: 'فشل نشر المستند.' });
    }
  };

  const handleRejectDocument = async (id: string) => {
    try {
      setMessage(null);
      await api.put(`/api/knowledge/${id}/reject`);
      setMessage({ type: 'success', text: 'تم إرجاع المستند إلى حالة المسودة.' });
      fetchDocuments();
    } catch (e) {
      console.error('Failed to reject document', e);
      setMessage({ type: 'error', text: 'فشل تعديل حالة المستند.' });
    }
  };

  const handleDeleteDocument = async (id: string) => {
    if (!window.confirm('هل أنت متأكد من حذف هذا المستند؟ لا يمكن التراجع عن هذا الإجراء.')) return;
    try {
      setActionLoading(true);
      setMessage(null);
      await api.delete(`/api/knowledge/${id}`);
      setMessage({ type: 'success', text: 'تم حذف المستند بنجاح.' });
      fetchDocuments();
    } catch (e: any) {
      console.error('Failed to delete document', e);
      setMessage({ type: 'error', text: e.response?.data?.message || 'فشل حذف المستند.' });
    } finally {
      setActionLoading(false);
    }
  };

  const handleFileUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    const reader = new FileReader();
    reader.onload = (event) => {
      const text = event.target?.result as string;
      setFormTitle(file.name.replace(/\.[^/.]+$/, '')); // Remove file extension
      setFormContent(text);
      setIsModalOpen(true);
    };
    reader.readAsText(file);
  };

  const filteredDocuments = documents.filter(doc => {
    if (activeTab === 'approved') return doc.status === 'Approved' || doc.status as string === 'Published';
    if (activeTab === 'pending') return doc.status === 'PendingApproval' || doc.status === 'Draft' || doc.status === 'Rejected';
    return true;
  });

  const totalPages = Math.ceil(filteredDocuments.length / pageSize) || 1;
  const paginatedDocuments = filteredDocuments.slice(
    (currentPage - 1) * pageSize,
    currentPage * pageSize
  );

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <div>
          <h1 className={styles.pageTitle}>قاعدة المعرفة والتدريب</h1>
          <p className={styles.pageSubtitle}>توفير مستندات المصدر، والتعليمات، وأدلة البحث لتدريب نموذج الذكاء الاصطناعي Gemini الخاص بك</p>
        </div>
        
        <div style={{ display: 'flex', gap: 'var(--space-md)' }}>
          <label className={`${styles.btn} ${styles.btnSecondary}`} style={{ cursor: 'pointer', display: 'flex', alignItems: 'center' }}>
            <Upload size={16} />
            رفع ملف (.txt)
            <input 
              type="file" 
              accept=".txt" 
              onChange={handleFileUpload} 
              style={{ display: 'none' }} 
            />
          </label>

          <button 
            onClick={handleSyncBrain} 
            disabled={actionLoading}
            className={`${styles.btn} ${styles.btnPrimary}`}
          >
            <RefreshCw size={16} className={actionLoading ? 'animate-spin' : ''} />
            مزامنة ذكاء AI
          </button>
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
          <CheckCircle2 size={18} style={{ color: message.type === 'success' ? 'hsl(var(--accent-success))' : 'hsl(var(--accent-danger))' }} />
          <span style={{ fontSize: '0.9rem', fontWeight: 600 }}>{message.text}</span>
        </div>
      )}

      {/* Documents Grid / Table */}
      <div className={`glass-panel ${styles.panel}`}>
        <div className={styles.panelHeader}>
          <h3 className={styles.panelTitle}>المصادر والمستندات المدخلة</h3>
          <button 
            onClick={() => {
              setEditingDocumentId(null);
              setFormTitle('');
              setFormContent('');
              setFormSourceUrl('');
              setIsModalOpen(true);
            }}
            className={`${styles.btn} ${styles.btnSecondary}`}
            style={{ padding: '4px 10px', fontSize: '0.8rem' }}
          >
            <Plus size={12} />
            إضافة نص يدوياً
          </button>
        </div>

        {/* Tab selection */}
        <div style={{ display: 'flex', gap: 'var(--space-md)', marginBottom: 'var(--space-md)', borderBottom: '1px solid var(--border-subtle)', paddingBottom: 'var(--space-sm)' }}>
          <button 
            onClick={() => setActiveTab('all')}
            className={`${styles.btn} ${activeTab === 'all' ? styles.btnPrimary : styles.btnSecondary}`}
            style={{ padding: '6px 12px', fontSize: '0.8rem' }}
          >
            كل المستندات ({documents.length})
          </button>
          <button 
            onClick={() => setActiveTab('approved')}
            className={`${styles.btn} ${activeTab === 'approved' ? styles.btnPrimary : styles.btnSecondary}`}
            style={{ padding: '6px 12px', fontSize: '0.8rem' }}
          >
            المعتمدة ({documents.filter(d => d.status === 'Approved' || d.status as string === 'Published').length})
          </button>
          <button 
            onClick={() => setActiveTab('pending')}
            className={`${styles.btn} ${activeTab === 'pending' ? styles.btnPrimary : styles.btnSecondary}`}
            style={{ padding: '6px 12px', fontSize: '0.8rem' }}
          >
            قيد الاعتماد ({documents.filter(d => d.status === 'PendingApproval' || d.status === 'Draft' || d.status === 'Rejected').length})
          </button>
        </div>

        {loading ? (
          <div className={styles.emptyState}>
            <div className={styles.spinner}></div>
            <p style={{ marginTop: 'var(--space-md)' }}>جاري تحميل مستندات قاعدة المعرفة...</p>
          </div>
        ) : filteredDocuments.length === 0 ? (
          <div className={styles.emptyState}>
            <BookOpen size={48} style={{ color: 'hsl(var(--text-muted))' }} />
            <h3 className={styles.emptyStateTitle}>لا توجد مستندات</h3>
            <p className={styles.emptyStateDesc}>لا توجد مستندات تطابق تصنيف التصفية الحالي.</p>
          </div>
        ) : (
          <div className={styles.tableWrapper}>
            <table className={styles.table}>
              <thead>
                <tr>
                  <th className={styles.th}>العنوان</th>
                  <th className={styles.th}>المصدر</th>
                  <th className={styles.th}>الحالة</th>
                  <th className={styles.th}>مقتطف من المحتوى</th>
                  <th className={styles.th} style={{ textAlign: 'center' }}>إجراءات الاعتماد</th>
                </tr>
              </thead>
              <tbody>
                {paginatedDocuments.map(doc => (
                  <tr key={doc.id} className={styles.tr}>
                    <td className={styles.td}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <FileText size={16} style={{ color: 'hsl(var(--accent-primary))' }} />
                        <span style={{ fontWeight: 600, color: 'var(--text-strong)' }}>{doc.title}</span>
                      </div>
                    </td>
                    <td className={styles.td}>
                      {doc.sourceUrl ? (
                        <a 
                          href={doc.sourceUrl} 
                          target="_blank" 
                          rel="noreferrer" 
                          style={{ display: 'flex', alignItems: 'center', gap: '4px', color: 'hsl(var(--accent-secondary))', textDecoration: 'none' }}
                        >
                          <ExternalLink size={12} />
                          رابط المصدر
                        </a>
                      ) : (
                        <span style={{ color: 'hsl(var(--text-muted))' }}>نص مرفوع يدوياً</span>
                      )}
                    </td>
                    <td className={styles.td}>
                      <span className={`${styles.statusBadge} ${
                        (doc.status === 'Approved' || doc.status === 'Published') 
                          ? styles.statusCompleted 
                          : doc.status === 'PendingApproval'
                          ? styles.statusPending
                          : doc.status === 'Rejected'
                          ? styles.statusFailed
                          : styles.statusPending
                      }`}>
                        {statusMapAr[doc.status] || doc.status}
                      </span>
                    </td>
                    <td className={styles.td}>
                      <span style={{ fontSize: '0.8rem', color: 'hsl(var(--text-secondary))' }}>
                        {doc.content.length > 60 ? `${doc.content.substring(0, 60)}...` : doc.content}
                      </span>
                    </td>
                    <td className={styles.td} style={{ textAlign: 'center' }}>
                      <div style={{ display: 'flex', gap: 'var(--space-sm)', justifyContent: 'center' }}>
                        {(doc.status !== 'Approved' && doc.status as string !== 'Published') ? (
                          <button
                            onClick={() => handleApproveDocument(doc.id)}
                            className={`${styles.btn} ${styles.btnSuccess}`}
                            style={{ padding: '2px 8px', fontSize: '0.75rem' }}
                            disabled={actionLoading}
                          >
                            اعتماد ونشر
                          </button>
                        ) : (
                          <button
                            onClick={() => handleRejectDocument(doc.id)}
                            className={`${styles.btn} ${styles.btnSecondary}`}
                            style={{ padding: '2px 8px', fontSize: '0.75rem' }}
                            disabled={actionLoading}
                          >
                            سحب لمسودة
                          </button>
                        )}
                        {doc.status === 'PendingApproval' && (
                          <button
                            onClick={() => handleRejectDocument(doc.id)}
                            className={`${styles.btn} ${styles.btnDanger}`}
                            style={{ padding: '2px 8px', fontSize: '0.75rem' }}
                            disabled={actionLoading}
                          >
                            رفض
                          </button>
                        )}
                        <button
                          onClick={() => handleStartEdit(doc)}
                          className={`${styles.btn} ${styles.btnSecondary}`}
                          style={{ padding: '2px 8px', fontSize: '0.75rem', backgroundColor: 'rgba(59, 130, 246, 0.15)', color: 'hsl(210, 100%, 75%)' }}
                          disabled={actionLoading}
                        >
                          تعديل
                        </button>
                        <button
                          onClick={() => handleDeleteDocument(doc.id)}
                          className={`${styles.btn} ${styles.btnDanger}`}
                          style={{ padding: '2px 8px', fontSize: '0.75rem' }}
                          disabled={actionLoading}
                        >
                          حذف
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            {/* Pagination Controls */}
            {filteredDocuments.length > 0 && (
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
                    عرض {Math.min((currentPage - 1) * pageSize + 1, filteredDocuments.length)} - {Math.min(currentPage * pageSize, filteredDocuments.length)} من {filteredDocuments.length}
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

      {/* Manual document creation modal */}
      {isModalOpen && (
        <div className={styles.overlay}>
          <div className={`glass-panel ${styles.modal}`}>
            <div className={styles.modalHeader}>
              <h3 className={styles.modalTitle}>
                {editingDocumentId ? 'تعديل مستند معرفي' : 'إضافة مستند معرفي'}
              </h3>
              <div onClick={() => { setIsModalOpen(false); setEditingDocumentId(null); }} className={styles.closeBtn}>
                &times;
              </div>
            </div>

            <form onSubmit={handleSaveDocument} className={styles.form}>
              <div className={styles.formGroup}>
                <label className={styles.label}>عنوان المستند</label>
                <input 
                  type="text" 
                  value={formTitle} 
                  onChange={(e) => setFormTitle(e.target.value)} 
                  placeholder="مثال: دليل سياسة الاسترجاع والضمان" 
                  className={styles.input} 
                  required 
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>رابط المصدر المرجعي (اختياري)</label>
                <input 
                  type="text" 
                  value={formSourceUrl} 
                  onChange={(e) => setFormSourceUrl(e.target.value)} 
                  placeholder="https://mysite.com/policy" 
                  className={styles.input} 
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>نص / محتوى المستند</label>
                <textarea 
                  value={formContent} 
                  onChange={(e) => setFormContent(e.target.value)} 
                  placeholder="الصق الأسئلة الشائعة أو تفاصيل السياسة والمحتوى التعليمي هنا..." 
                  className={styles.textarea} 
                  required 
                />
              </div>

              <div className={styles.formActions}>
                <button 
                  type="button" 
                  onClick={() => { setIsModalOpen(false); setEditingDocumentId(null); }} 
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
                  {actionLoading ? 'جاري الحفظ...' : (editingDocumentId ? 'تحديث وتعديل' : 'حفظ وتقسيم')}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
