'use client';

import React, { useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
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

  // AI Wizard State
  const [isWizardOpen, setIsWizardOpen] = useState(false);
  const [wizardStep, setWizardStep] = useState<1 | 2 | 3 | 4>(1); // 1: Text, 2: Questions, 3: Q&A, 4: Title/Save
  const [wizardText, setWizardText] = useState('');
  const [wizardTitle, setWizardTitle] = useState('');
  const [wizardQuestions, setWizardQuestions] = useState<{ question: string; options: string[] }[]>([]);
  const [currentQuestionIndex, setCurrentQuestionIndex] = useState(0);
  const [wizardAnswers, setWizardAnswers] = useState<Record<number, string>>({}); // { questionIndex: answer }
  const [customAnswer, setCustomAnswer] = useState('');
  const [generatedQas, setGeneratedQas] = useState<{ question: string; answer: string }[]>([]);
  const [wizardLoading, setWizardLoading] = useState(false);

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

  const handleStartWizard = () => {
    setEditingDocumentId(null);
    setIsWizardOpen(true);
    setWizardStep(1);
    setWizardText('');
    setWizardTitle('');
    setWizardQuestions([]);
    setCurrentQuestionIndex(0);
    setWizardAnswers({});
    setCustomAnswer('');
    setGeneratedQas([]);
  };

  const handleStartWizardFromEdit = () => {
    setIsModalOpen(false);
    setIsWizardOpen(true);
    setWizardStep(1);
    setWizardText(formContent);
    setWizardTitle(formTitle);
    setWizardQuestions([]);
    setCurrentQuestionIndex(0);
    setWizardAnswers({});
    setCustomAnswer('');
    setGeneratedQas([]);
  };

  const handleWizardAnalyze = async () => {
    if (!activeProject || !wizardText.trim()) return;
    try {
      setWizardLoading(true);
      const response = await api.post<{ question: string; options: string[] }[]>(
        `/api/projects/${activeProject.id}/knowledge/wizard/analyze`,
        { rawText: wizardText }
      );
      if (response.data && response.data.length > 0) {
        setWizardQuestions(response.data);
        setCurrentQuestionIndex(0);
        setWizardStep(2);
        setCustomAnswer('');
      } else {
        setGeneratedQas([{ question: 'النص الأصلي المدخل', answer: wizardText }]);
        setWizardStep(3);
      }
    } catch (e) {
      console.error('Failed to analyze raw text', e);
      setMessage({ type: 'error', text: 'فشل تحليل النص بالذكاء الاصطناعي.' });
    } finally {
      setWizardLoading(false);
    }
  };

  const handleNextQuestion = () => {
    const answer = customAnswer.trim() || wizardAnswers[currentQuestionIndex] || '';
    if (!answer) {
      alert('يرجى اختيار إجابة أو كتابة إجابة مخصصة');
      return;
    }

    const updatedAnswers = { ...wizardAnswers, [currentQuestionIndex]: answer };
    setWizardAnswers(updatedAnswers);

    if (currentQuestionIndex < wizardQuestions.length - 1) {
      const nextIndex = currentQuestionIndex + 1;
      setCurrentQuestionIndex(nextIndex);
      const nextAns = updatedAnswers[nextIndex] || '';
      const nextOpts = wizardQuestions[nextIndex]?.options || [];
      if (nextAns && !nextOpts.includes(nextAns)) {
        setCustomAnswer(nextAns);
      } else {
        setCustomAnswer('');
      }
    } else {
      handleWizardGenerate(updatedAnswers);
    }
  };

  const handleWizardGenerate = async (answers: Record<number, string>) => {
    if (!activeProject) return;
    try {
      setWizardLoading(true);
      const answersPayload = wizardQuestions.map((q, idx) => ({
        question: q.question,
        answer: answers[idx] || ''
      }));

      const response = await api.post<{ question: string; answer: string }[]>(
        `/api/projects/${activeProject.id}/knowledge/wizard/generate`,
        {
          rawText: wizardText,
          answers: answersPayload
        }
      );
      setGeneratedQas(response.data);
      setWizardTitle('معلومات قاعدة المعرفة المعالجة');
      setWizardStep(3);
    } catch (e) {
      console.error('Failed to generate Q&A pairs', e);
      setMessage({ type: 'error', text: 'فشل توليد الأسئلة والأجوبة بالذكاء الاصطناعي.' });
    } finally {
      setWizardLoading(false);
    }
  };

  const handleSaveWizardDocument = async () => {
    if (!activeProject || !wizardTitle || generatedQas.length === 0) return;
    try {
      setWizardLoading(true);
      const formattedContent = generatedQas
        .map(qa => `س: ${qa.question.trim()}\nج: ${qa.answer.trim()}`)
        .join('\n\n');

      if (editingDocumentId) {
        await api.put(`/api/knowledge/${editingDocumentId}`, {
          title: wizardTitle,
          content: formattedContent
        });
        setMessage({ type: 'success', text: `تم تحديث مستند الأسئلة والأجوبة "${wizardTitle}" بنجاح.` });
      } else {
        await api.post(`/api/projects/${activeProject.id}/knowledge`, {
          title: wizardTitle,
          content: formattedContent
        });
        setMessage({ type: 'success', text: `تم حفظ مستند الأسئلة والأجوبة "${wizardTitle}" بنجاح.` });
      }
      
      setIsWizardOpen(false);
      setEditingDocumentId(null);
      fetchDocuments();
    } catch (e) {
      console.error('Failed to save wizard document', e);
      setMessage({ type: 'error', text: 'فشل حفظ مستند قاعدة المعرفة.' });
    } finally {
      setWizardLoading(false);
    }
  };

  const handleEditQa = (index: number, field: 'question' | 'answer', value: string) => {
    const updated = [...generatedQas];
    updated[index] = { ...updated[index], [field]: value };
    setGeneratedQas(updated);
  };

  const handleDeleteQa = (index: number) => {
    const updated = generatedQas.filter((_, idx) => idx !== index);
    setGeneratedQas(updated);
  };

  const handleAddQa = () => {
    setGeneratedQas([...generatedQas, { question: '', answer: '' }]);
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

  const [confirmOpen, setConfirmOpen] = useState(false);
  const [docToDelete, setDocToDelete] = useState<string | null>(null);

  const handleDeleteDocument = (id: string) => {
    setDocToDelete(id);
    setConfirmOpen(true);
  };

  const handleConfirmDelete = async () => {
    if (!docToDelete) return;
    setConfirmOpen(false);
    try {
      setActionLoading(true);
      setMessage(null);
      await api.delete(`/api/knowledge/${docToDelete}`);
      setMessage({ type: 'success', text: 'تم حذف المستند بنجاح.' });
      fetchDocuments();
    } catch (e: any) {
      console.error('Failed to delete document', e);
      setMessage({ type: 'error', text: e.response?.data?.message || 'فشل حذف المستند.' });
    } finally {
      setActionLoading(false);
      setDocToDelete(null);
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
          <div style={{ display: 'flex', gap: 'var(--space-sm)' }}>
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
            <button 
              onClick={handleStartWizard}
              className={`${styles.btn} ${styles.btnSecondary}`}
              style={{ padding: '4px 10px', fontSize: '0.8rem', backgroundColor: 'rgba(59, 130, 246, 0.15)', color: 'hsl(210, 100%, 75%)', border: '1px solid rgba(59, 130, 246, 0.3)' }}
            >
              <Plus size={12} />
              معالج الذكاء الاصطناعي
            </button>
          </div>
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
              <button 
                type="button"
                onClick={() => { setIsModalOpen(false); setEditingDocumentId(null); }} 
                className={styles.closeBtn}
                aria-label="إغلاق"
                style={{ background: 'none', border: 'none', fontSize: '1.5rem', padding: 0 }}
              >
                &times;
              </button>
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
                {editingDocumentId && (
                  <button 
                    type="button"
                    onClick={handleStartWizardFromEdit}
                    className={`${styles.btn}`}
                    style={{ backgroundColor: 'rgba(59, 130, 246, 0.15)', color: 'hsl(210, 100%, 75%)', border: '1px solid rgba(59, 130, 246, 0.3)' }}
                    disabled={actionLoading || !formContent.trim()}
                  >
                    تعديل عبر معالج الذكاء الاصطناعي
                  </button>
                )}
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

      {/* AI Wizard Modal */}
      {isWizardOpen && (
        <div className={styles.overlay}>
          <div className={`glass-panel ${styles.modal}`} style={{ maxWidth: '640px', width: '90%' }}>
            <div className={styles.modalHeader}>
              <h3 className={styles.modalTitle} style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <BookOpen size={18} style={{ color: 'hsl(var(--accent-primary))' }} />
                معالج قاعدة المعرفة بالذكاء الاصطناعي
              </h3>
              <button 
                type="button"
                onClick={() => { setIsWizardOpen(false); setEditingDocumentId(null); }} 
                className={styles.closeBtn}
                aria-label="إغلاق"
                style={{ background: 'none', border: 'none', fontSize: '1.5rem', padding: 0 }}
              >
                &times;
              </button>
            </div>

            {/* Stepper Progress Bar */}
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 'var(--space-md)', borderBottom: '1px solid var(--border-subtle)', paddingBottom: 'var(--space-md)' }}>
              {[1, 2, 3, 4].map((step) => (
                <div key={step} style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                  <div style={{
                    width: '28px',
                    height: '28px',
                    borderRadius: '50%',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    fontWeight: 700,
                    fontSize: '0.8rem',
                    background: wizardStep === step ? 'var(--accent)' : wizardStep > step ? 'rgba(16, 185, 129, 0.2)' : 'var(--surface-muted)',
                    color: wizardStep === step ? 'var(--accent-ink)' : wizardStep > step ? 'hsl(140, 100%, 65%)' : 'var(--text-soft)',
                    border: wizardStep === step ? 'none' : '1px solid var(--border-subtle)'
                  }}>
                    {step}
                  </div>
                  <span style={{
                    fontSize: '0.8rem',
                    fontWeight: wizardStep === step ? 700 : 500,
                    color: wizardStep === step ? 'var(--text-strong)' : 'var(--text-soft)'
                  }}>
                    {step === 1 ? 'إدخال النص' : step === 2 ? 'أسئلة توضيحية' : step === 3 ? 'مراجعة الأسئلة' : 'حفظ ونشر'}
                  </span>
                </div>
              ))}
            </div>

            {/* Step 1: Raw Text Input */}
            {wizardStep === 1 && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
                <div className={styles.formGroup}>
                  <label className={styles.label}>أدخل النص الخام لقاعدة المعرفة</label>
                  <textarea 
                    value={wizardText} 
                    onChange={(e) => setWizardText(e.target.value)} 
                    placeholder="الصق هنا معلومات عن شركتك أو خدماتك أو سياساتك العامة..." 
                    className={styles.textarea} 
                    style={{ minHeight: '180px' }}
                    required 
                  />
                  <p style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))', marginTop: '4px' }}>
                    يقوم الذكاء الاصطناعي بتحليل النص وصياغة أسئلة لسد أي فجوات معلوماتية.
                  </p>
                </div>

                <div className={styles.formActions}>
                  <button 
                    type="button" 
                    onClick={() => { setIsWizardOpen(false); setEditingDocumentId(null); }} 
                    className={`${styles.btn} ${styles.btnSecondary}`}
                  >
                    إلغاء
                  </button>
                  <button 
                    type="button" 
                    onClick={handleWizardAnalyze} 
                    className={`${styles.btn} ${styles.btnPrimary}`}
                    disabled={wizardLoading || !wizardText.trim()}
                  >
                    {wizardLoading ? 'جاري التحليل...' : 'التالي (تحليل النص)'}
                  </button>
                </div>
              </div>
            )}

            {/* Step 2: Clarifying Questions */}
            {wizardStep === 2 && wizardQuestions.length > 0 && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <span style={{ fontSize: '0.8rem', color: 'var(--text-soft)' }}>
                    السؤال {currentQuestionIndex + 1} من {wizardQuestions.length}
                  </span>
                </div>

                <div style={{ padding: 'var(--space-md)', background: 'var(--surface-muted)', borderRadius: 'var(--radius-sm)', border: '1px solid var(--border-subtle)' }}>
                  <h4 style={{ fontSize: '1rem', fontWeight: 600, color: 'var(--text-strong)', marginBottom: 'var(--space-md)' }}>
                    {wizardQuestions[currentQuestionIndex].question}
                  </h4>

                  {/* Predefined Options */}
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginBottom: 'var(--space-md)' }}>
                    {wizardQuestions[currentQuestionIndex].options.map((option, idx) => (
                      <button
                        key={idx}
                        type="button"
                        onClick={() => {
                          setWizardAnswers({ ...wizardAnswers, [currentQuestionIndex]: option });
                          setCustomAnswer('');
                        }}
                        style={{
                          textAlign: 'right',
                          padding: '10px 14px',
                          borderRadius: 'var(--radius-sm)',
                          border: '1px solid ' + (wizardAnswers[currentQuestionIndex] === option && !customAnswer ? 'var(--accent)' : 'var(--border-subtle)'),
                          background: wizardAnswers[currentQuestionIndex] === option && !customAnswer ? 'var(--accent-soft)' : 'var(--surface)',
                          color: wizardAnswers[currentQuestionIndex] === option && !customAnswer ? 'var(--accent)' : 'var(--text-strong)',
                          fontSize: '0.85rem',
                          fontWeight: 500,
                          cursor: 'pointer',
                          transition: 'all 0.2s'
                        }}
                      >
                        {option}
                      </button>
                    ))}
                  </div>

                  {/* Custom Answer Input */}
                  <div className={styles.formGroup}>
                    <label className={styles.label}>أو اكتب إجابة مخصصة:</label>
                    <textarea
                      value={customAnswer}
                      onChange={(e) => {
                        setCustomAnswer(e.target.value);
                      }}
                      placeholder="اكتب تفاصيل إضافية مخصصة هنا..."
                      className={styles.textarea}
                      style={{ minHeight: '60px' }}
                    />
                  </div>
                </div>

                <div className={styles.formActions}>
                  <button 
                    type="button" 
                    onClick={() => {
                      if (currentQuestionIndex > 0) {
                        const prevIndex = currentQuestionIndex - 1;
                        setCurrentQuestionIndex(prevIndex);
                        const prevAns = wizardAnswers[prevIndex] || '';
                        const prevOpts = wizardQuestions[prevIndex]?.options || [];
                        if (prevAns && !prevOpts.includes(prevAns)) {
                          setCustomAnswer(prevAns);
                        } else {
                          setCustomAnswer('');
                        }
                      } else {
                        setWizardStep(1);
                      }
                    }} 
                    className={`${styles.btn} ${styles.btnSecondary}`}
                  >
                    السابق
                  </button>
                  <button 
                    type="button" 
                    onClick={handleNextQuestion} 
                    className={`${styles.btn} ${styles.btnPrimary}`}
                    disabled={wizardLoading || (!customAnswer.trim() && !wizardAnswers[currentQuestionIndex])}
                  >
                    {wizardLoading 
                      ? 'جاري التوليد...' 
                      : currentQuestionIndex === wizardQuestions.length - 1 
                        ? 'توليد الأسئلة والأجوبة' 
                        : 'السؤال التالي'}
                  </button>
                </div>
              </div>
            )}

            {/* Step 3: Review Q&A List */}
            {wizardStep === 3 && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <h4 style={{ fontSize: '0.95rem', fontWeight: 600 }}>مراجعة وتعديل الأسئلة والأجوبة الناتجة</h4>
                  <button
                    type="button"
                    onClick={handleAddQa}
                    className={`${styles.btn} ${styles.btnSecondary}`}
                    style={{ padding: '2px 8px', fontSize: '0.75rem' }}
                  >
                    <Plus size={12} />
                    سؤال وجواب جديد
                  </button>
                </div>

                <div style={{ maxHeight: '320px', overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: '12px', paddingRight: '4px' }}>
                  {generatedQas.map((qa, index) => (
                    <div key={index} style={{ padding: '12px', background: 'var(--surface-muted)', borderRadius: 'var(--radius-sm)', border: '1px solid var(--border-subtle)', position: 'relative' }}>
                      <button
                        type="button"
                        onClick={() => handleDeleteQa(index)}
                        style={{ position: 'absolute', top: '8px', left: '8px', background: 'none', border: 'none', color: 'hsl(var(--accent-danger))', cursor: 'pointer', fontSize: '1.25rem' }}
                        title="حذف"
                      >
                        &times;
                      </button>

                      <div className={styles.formGroup} style={{ marginBottom: '8px', marginTop: '12px' }}>
                        <label className={styles.label}>السؤال</label>
                        <input
                          type="text"
                          value={qa.question}
                          onChange={(e) => handleEditQa(index, 'question', e.target.value)}
                          className={styles.input}
                          style={{ padding: '6px 10px', fontSize: '0.85rem' }}
                        />
                      </div>

                      <div className={styles.formGroup}>
                        <label className={styles.label}>الإجابة</label>
                        <textarea
                          value={qa.answer}
                          onChange={(e) => handleEditQa(index, 'answer', e.target.value)}
                          className={styles.textarea}
                          style={{ minHeight: '60px', padding: '6px 10px', fontSize: '0.85rem' }}
                        />
                      </div>
                    </div>
                  ))}
                </div>

                <div className={styles.formActions}>
                  <button 
                    type="button" 
                    onClick={() => {
                      if (wizardQuestions.length > 0) {
                        setCurrentQuestionIndex(wizardQuestions.length - 1);
                        setWizardStep(2);
                      } else {
                        setWizardStep(1);
                      }
                    }} 
                    className={`${styles.btn} ${styles.btnSecondary}`}
                  >
                    السابق
                  </button>
                  <button 
                    type="button" 
                    onClick={() => setWizardStep(4)} 
                    className={`${styles.btn} ${styles.btnPrimary}`}
                    disabled={generatedQas.length === 0}
                  >
                    التالي (حفظ)
                  </button>
                </div>
              </div>
            )}

            {/* Step 4: Title & Save */}
            {wizardStep === 4 && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
                <div className={styles.formGroup}>
                  <label className={styles.label}>عنوان مستند قاعدة المعرفة</label>
                  <input 
                    type="text" 
                    value={wizardTitle} 
                    onChange={(e) => setWizardTitle(e.target.value)} 
                    placeholder="مثال: معلومات قاعدة المعرفة المعالجة" 
                    className={styles.input} 
                    required 
                  />
                </div>

                <div className={styles.formActions}>
                  <button 
                    type="button" 
                    onClick={() => setWizardStep(3)} 
                    className={`${styles.btn} ${styles.btnSecondary}`}
                  >
                    السابق
                  </button>
                  <button 
                    type="button" 
                    onClick={handleSaveWizardDocument} 
                    className={`${styles.btn} ${styles.btnPrimary}`}
                    disabled={wizardLoading || !wizardTitle.trim()}
                  >
                    {wizardLoading ? 'جاري الحفظ والتقسيم...' : 'حفظ ونشر'}
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>
      )}

      <ConfirmDialog 
        isOpen={confirmOpen}
        title="تأكيد الحذف"
        message="هل أنت متأكد من حذف هذا المستند؟ لا يمكن التراجع عن هذا الإجراء."
        confirmLabel="حذف"
        cancelLabel="إلغاء"
        onConfirm={handleConfirmDelete}
        onCancel={() => { setConfirmOpen(false); setDocToDelete(null); }}
      />
    </div>
  );
}
