'use client';

import React, { useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import { 
  GitFork, 
  Plus, 
  Play, 
  Pause, 
  Trash2, 
  Settings, 
  FileCode,
  CheckCircle2,
  AlertCircle
} from 'lucide-react';
import styles from './management.module.css';

interface AutomationWorkflow {
  id: string;
  name: string;
  triggerType: string;
  filtersJson: string;
  actionsJson: string;
  isActive: boolean;
  version: number;
}

const triggerMapAr: Record<string, string> = {
  'MessageReceived': 'استقبال رسالة واتساب',
  'CustomerTagAdded': 'إضافة وسم للعميل',
  'LeadStageChanged': 'تغيير مرحلة العميل في CRM'
};

const actionMapAr: Record<string, string> = {
  'SendWhatsAppMessage': 'إرسال رسالة واتساب',
  'CRMUpdate': 'تحديث بيانات CRM',
  'CreateFollowUp': 'جدولة مهمة متابعة'
};

export default function Workflows() {
  const { activeProject } = useAuth();
  
  const [workflows, setWorkflows] = useState<AutomationWorkflow[]>([]);
  const [loading, setLoading] = useState(true);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [actionLoading, setActionLoading] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  // Form Fields
  const [formName, setFormName] = useState('');
  const [formTriggerType, setFormTriggerType] = useState('MessageReceived');
  const [formFilterKey, setFormFilterKey] = useState('');
  const [formFilterValue, setFormFilterValue] = useState('');
  const [formActionType, setFormActionType] = useState('SendWhatsAppMessage');
  const [formActionPayload, setFormActionPayload] = useState('');

  const fetchWorkflows = async () => {
    if (!activeProject) return;
    try {
      setLoading(true);
      const response = await api.get<AutomationWorkflow[]>(`/api/projects/${activeProject.id}/workflows`);
      setWorkflows(response.data);
    } catch (e) {
      console.error('Failed to fetch workflows', e);
      setMessage({ type: 'error', text: 'فشل تحميل قواعد الأتمتة.' });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchWorkflows();
  }, [activeProject]);

  const handleToggleActive = async (workflow: AutomationWorkflow) => {
    try {
      setMessage(null);
      const updated = { ...workflow, isActive: !workflow.isActive };
      await api.put(`/api/workflows/${workflow.id}`, updated);
      setWorkflows(prev => prev.map(w => w.id === workflow.id ? updated : w));
      setMessage({ type: 'success', text: `تم ${updated.isActive ? 'تفعيل' : 'إيقاف'} سير العمل بنجاح.` });
    } catch (e) {
      console.error('Failed to toggle workflow status', e);
      setMessage({ type: 'error', text: 'فشل تغيير حالة سير العمل.' });
    }
  };

  const [confirmOpen, setConfirmOpen] = useState(false);
  const [workflowToDelete, setWorkflowToDelete] = useState<string | null>(null);

  const handleDelete = (id: string) => {
    setWorkflowToDelete(id);
    setConfirmOpen(true);
  };

  const handleConfirmDelete = async () => {
    if (!workflowToDelete) return;
    setConfirmOpen(false);
    try {
      setMessage(null);
      await api.delete(`/api/workflows/${workflowToDelete}`);
      setWorkflows(prev => prev.filter(w => w.id !== workflowToDelete));
      setMessage({ type: 'success', text: 'تم حذف قاعدة سير العمل بنجاح.' });
    } catch (e) {
      console.error('Failed to delete workflow', e);
      setMessage({ type: 'error', text: 'فشل حذف قاعدة سير العمل.' });
    } finally {
      setWorkflowToDelete(null);
    }
  };

  const handleCreateWorkflow = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!activeProject) return;
    if (!formName) return;

    // Build filter JSON and action JSON objects
    const filters = formFilterKey ? { [formFilterKey]: formFilterValue } : {};
    const actions = [{ type: formActionType, payload: formActionPayload }];

    try {
      setActionLoading(true);
      setMessage(null);

      await api.post(`/api/projects/${activeProject.id}/workflows`, {
        name: formName,
        triggerType: formTriggerType,
        filtersJson: JSON.stringify(filters),
        actionsJson: JSON.stringify(actions),
        isActive: true
      });

      setMessage({ type: 'success', text: 'تم إنشاء قاعدة الأتمتة بنجاح.' });
      setIsModalOpen(false);
      
      // Reset form
      setFormName('');
      setFormTriggerType('MessageReceived');
      setFormFilterKey('');
      setFormFilterValue('');
      setFormActionType('SendWhatsAppMessage');
      setFormActionPayload('');
      
      fetchWorkflows();
    } catch (e: any) {
      console.error('Failed to create workflow', e);
      setMessage({ type: 'error', text: e.response?.data || 'فشل إنشاء قاعدة الأتمتة.' });
    } finally {
      setActionLoading(false);
    }
  };

  const parseJsonSafe = (json: string) => {
    try {
      return JSON.parse(json);
    } catch {
      return null;
    }
  };

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <div>
          <h1 className={styles.pageTitle}>أتمتة العمليات</h1>
          <p className={styles.pageSubtitle}>تهيئة قواعد التشغيل التلقائي لمراسلة العملاء، وتوجيه المحادثات، وتحديث بيانات CRM تلقائياً</p>
        </div>
        <button 
          onClick={() => setIsModalOpen(true)}
          className={`${styles.btn} ${styles.btnPrimary}`}
        >
          <Plus size={16} />
          إنشاء قاعدة
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

      {/* Rules list */}
      <div className={`glass-panel ${styles.panel}`}>
        {loading ? (
          <div className={styles.emptyState}>
            <div className={styles.spinner}></div>
            <p style={{ marginTop: 'var(--space-md)' }}>جاري تحميل العمليات...</p>
          </div>
        ) : workflows.length === 0 ? (
          <div className={styles.emptyState}>
            <GitFork size={48} style={{ color: 'hsl(var(--text-muted))' }} />
            <h3 className={styles.emptyStateTitle}>لا توجد قواعد أتمتة نشطة</h3>
            <p className={styles.emptyStateDesc}>أنشئ قواعد تلقائية مثل تصنيف جهات الاتصال تلقائياً أو جدولة مهام عند استقبال الرسائل.</p>
            <button onClick={() => setIsModalOpen(true)} className={`${styles.btn} ${styles.btnPrimary}`}>
              إنشاء قاعدة
            </button>
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
            {workflows.map(wf => {
              const filters = parseJsonSafe(wf.filtersJson) || {};
              const actions = parseJsonSafe(wf.actionsJson) || [];
              return (
                <div 
                  key={wf.id} 
                  className={`glass-panel-interactive`}
                  style={{
                    padding: 'var(--space-lg)',
                    borderRadius: 'var(--radius-md)',
                    borderRight: wf.isActive ? '4px solid hsl(var(--accent-primary))' : '4px solid rgba(255,255,255,0.1)',
                    display: 'flex',
                    flexDirection: 'column',
                    gap: 'var(--space-md)'
                  }}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <div>
                      <h4 style={{ fontSize: '1rem', fontWeight: 700, color: 'var(--text-strong)' }}>{wf.name}</h4>
                      <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))' }}>معرّف: {wf.id} | إصدار: {wf.version}</span>
                    </div>
                    
                    <div style={{ display: 'flex', gap: 'var(--space-sm)' }}>
                      <button
                        onClick={() => handleToggleActive(wf)}
                        className={styles.btnIcon}
                        title={wf.isActive ? 'إيقاف مؤقت' : 'تفعيل'}
                        style={{ color: wf.isActive ? 'hsl(var(--accent-warning))' : 'hsl(var(--accent-success))' }}
                      >
                        {wf.isActive ? <Pause size={14} /> : <Play size={14} />}
                      </button>
                      <button
                        onClick={() => handleDelete(wf.id)}
                        className={styles.btnIcon}
                        style={{ color: 'hsl(0, 100%, 65%)' }}
                        title="حذف القاعدة"
                      >
                        <Trash2 size={14} />
                      </button>
                    </div>
                  </div>

                  {/* Flow Steps View */}
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: '15px', alignItems: 'center', fontSize: '0.85rem' }}>
                    {/* Step 1: Trigger */}
                    <div style={{ background: 'rgba(255, 255, 255, 0.04)', padding: '8px 12px', borderRadius: '4px', border: '1px solid rgba(255,255,255,0.05)' }}>
                      <span style={{ color: 'hsl(var(--text-muted))', fontWeight: 600, display: 'block', fontSize: '0.7rem', textTransform: 'uppercase' }}>عند حدوث الحدث</span>
                      <span style={{ color: 'hsl(var(--accent-secondary))', fontWeight: 700 }}>{triggerMapAr[wf.triggerType] || wf.triggerType}</span>
                    </div>

                    <div style={{ color: 'hsl(var(--text-muted))', fontWeight: 700 }}>&larr;</div>

                    {/* Step 2: Filters */}
                    {Object.keys(filters).length > 0 && (
                      <>
                        <div style={{ background: 'rgba(255, 255, 255, 0.04)', padding: '8px 12px', borderRadius: '4px', border: '1px solid rgba(255,255,255,0.05)' }}>
                          <span style={{ color: 'hsl(var(--text-muted))', fontWeight: 600, display: 'block', fontSize: '0.7rem' }}>إذا تطابقت الشروط</span>
                          <span style={{ color: 'var(--text-strong)' }}>
                            {Object.entries(filters).map(([k, v]) => `${k} = ${v}`).join(', ')}
                          </span>
                        </div>
                        <div style={{ color: 'hsl(var(--text-muted))', fontWeight: 700 }}>&larr;</div>
                      </>
                    )}

                    {/* Step 3: Actions */}
                    <div style={{ background: 'rgba(203, 184, 255, 0.12)', padding: '8px 12px', borderRadius: '4px', border: '1px solid rgba(203, 184, 255, 0.25)' }}>
                      <span style={{ color: 'hsl(var(--text-muted))', fontWeight: 600, display: 'block', fontSize: '0.7rem' }}>نفذ الإجراءات التالية</span>
                      {actions.map((act: any, idx: number) => (
                        <div key={idx} style={{ color: 'var(--text-strong)', fontWeight: 600 }}>
                          {actionMapAr[act.type] || act.type}: <span style={{ color: 'hsl(var(--text-secondary))', fontFamily: 'monospace', fontSize: '0.8rem' }}>{act.payload}</span>
                        </div>
                      ))}
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Creation Modal Overlay */}
      {isModalOpen && (
        <div className={styles.overlay}>
          <div className={`glass-panel ${styles.modal}`}>
            <div className={styles.modalHeader}>
              <h3 className={styles.modalTitle}>تهيئة قاعدة الأتمتة</h3>
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

            <form onSubmit={handleCreateWorkflow} className={styles.form}>
              <div className={styles.formGroup}>
                <label className={styles.label}>اسم سير العمل</label>
                <input 
                  type="text" 
                  value={formName} 
                  onChange={(e) => setFormName(e.target.value)} 
                  placeholder="مثال: الرد التلقائي الترحيبي" 
                  className={styles.input} 
                  required 
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>نوع الحدث المشغل</label>
                <select 
                  value={formTriggerType} 
                  onChange={(e) => setFormTriggerType(e.target.value)} 
                  className={styles.select}
                >
                  <option value="MessageReceived">استقبال رسالة واتساب</option>
                  <option value="CustomerTagAdded">إضافة وسم للعميل</option>
                  <option value="LeadStageChanged">تغيير مرحلة العميل في CRM</option>
                </select>
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--space-md)' }}>
                <div className={styles.formGroup}>
                  <label className={styles.label}>مفتاح شرط التصفية</label>
                  <input 
                    type="text" 
                    value={formFilterKey} 
                    onChange={(e) => setFormFilterKey(e.target.value)} 
                    placeholder="مثال: city" 
                    className={styles.input} 
                  />
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label}>القيمة المستهدفة للشرط</label>
                  <input 
                    type="text" 
                    value={formFilterValue} 
                    onChange={(e) => setFormFilterValue(e.target.value)} 
                    placeholder="مثال: Cairo" 
                    className={styles.input} 
                  />
                </div>
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>نوع الإجراء المستهدف</label>
                <select 
                  value={formActionType} 
                  onChange={(e) => setFormActionType(e.target.value)} 
                  className={styles.select}
                >
                  <option value="SendWhatsAppMessage">إرسال قالب رسالة واتساب</option>
                  <option value="CRMUpdate">تحديث بيانات CRM للعميل</option>
                  <option value="CreateFollowUp">جدولة مهمة متابعة</option>
                </select>
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>محتوى الإجراء / نص الرسالة</label>
                <textarea 
                  value={formActionPayload} 
                  onChange={(e) => setFormActionPayload(e.target.value)} 
                  placeholder="مرحباً بك! أهلاً بك في متجرنا..." 
                  className={styles.textarea} 
                  required
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
                  {actionLoading ? 'جاري الإنشاء...' : 'إنشاء قاعدة'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      <ConfirmDialog 
        isOpen={confirmOpen}
        title="تأكيد الحذف"
        message="هل أنت متأكد من رغبتك في حذف قاعدة الأتمتة هذه؟"
        confirmLabel="حذف"
        cancelLabel="إلغاء"
        onConfirm={handleConfirmDelete}
        onCancel={() => { setConfirmOpen(false); setWorkflowToDelete(null); }}
      />
    </div>
  );
}
