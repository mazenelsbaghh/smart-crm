'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import { handleDialogKeyDown } from './dialog-accessibility';
import { 
  GitFork, 
  Plus, 
  Play, 
  Pause, 
  Trash2, 
  CheckCircle2,
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

interface WorkflowAction {
  type: string;
  payload?: string;
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

function parseJsonSafe(json: string): unknown {
  try {
    return JSON.parse(json) as unknown;
  } catch {
    return null;
  }
}

export default function Workflows() {
  const { activeProject } = useAuth();
  
  const [workflows, setWorkflows] = useState<AutomationWorkflow[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
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
  const [workflowToToggle, setWorkflowToToggle] = useState<AutomationWorkflow | null>(null);
  const loadRequestIdRef = React.useRef(0);

  const fetchWorkflows = useCallback(async () => {
    const requestId = ++loadRequestIdRef.current;
    if (!activeProject) {
      setWorkflows([]);
      setLoading(false);
      setLoadError('تعذر تحميل مساحة العمل. أعد المحاولة أو تواصل مع المدير.');
      return;
    }
    try {
      setLoading(true);
      setLoadError(null);
      setWorkflows([]);
      const response = await api.get<AutomationWorkflow[]>(`/api/projects/${activeProject.id}/workflows`);
      if (requestId !== loadRequestIdRef.current) return;
      setWorkflows(response.data);
    } catch (e) {
      if (requestId !== loadRequestIdRef.current) return;
      console.error('Failed to fetch workflows', e);
      setLoadError('فشل تحميل قواعد الأتمتة. لم يتم عرض قائمة فارغة بديلًا عنها.');
    } finally {
      if (requestId === loadRequestIdRef.current) setLoading(false);
    }
  }, [activeProject]);

  useEffect(() => {
    const loadTimer = window.setTimeout(() => void fetchWorkflows(), 0);
    return () => window.clearTimeout(loadTimer);
  }, [fetchWorkflows]);

  const handleToggleActive = async (workflow: AutomationWorkflow) => {
    try {
      setActionLoading(true);
      setMessage(null);
      const updated = { ...workflow, isActive: !workflow.isActive };
      await api.put(`/api/workflows/${workflow.id}`, updated);
      setWorkflows(prev => prev.map(w => w.id === workflow.id ? updated : w));
      setMessage({ type: 'success', text: `تم ${updated.isActive ? 'تفعيل' : 'إيقاف'} سير العمل بنجاح.` });
    } catch (e) {
      console.error('Failed to toggle workflow status', e);
      setMessage({ type: 'error', text: 'فشل تغيير حالة سير العمل.' });
    } finally {
      setActionLoading(false);
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
      setActionLoading(true);
      setMessage(null);
      await api.delete(`/api/workflows/${workflowToDelete}`);
      setWorkflows(prev => prev.filter(w => w.id !== workflowToDelete));
      setMessage({ type: 'success', text: 'تم حذف قاعدة سير العمل بنجاح.' });
    } catch (e) {
      console.error('Failed to delete workflow', e);
      setMessage({ type: 'error', text: 'فشل حذف قاعدة سير العمل.' });
    } finally {
      setActionLoading(false);
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
        isActive: false
      });

      setMessage({ type: 'success', text: 'تم إنشاء قاعدة الأتمتة كمسودة متوقفة. راجعها ثم فعّلها بشكل منفصل.' });
      setIsModalOpen(false);
      
      // Reset form
      setFormName('');
      setFormTriggerType('MessageReceived');
      setFormFilterKey('');
      setFormFilterValue('');
      setFormActionType('SendWhatsAppMessage');
      setFormActionPayload('');
      
      void fetchWorkflows();
    } catch (e) {
      console.error('Failed to create workflow', e);
      setMessage({ type: 'error', text: 'فشل إنشاء قاعدة الأتمتة.' });
    } finally {
      setActionLoading(false);
    }
  };

  const workflowNameToDelete = workflows.find((workflow) => workflow.id === workflowToDelete)?.name;

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <div>
          <h1 className={styles.pageTitle}>أتمتة العمليات</h1>
          <p className={styles.pageSubtitle}>تهيئة قواعد التشغيل التلقائي لمراسلة العملاء، وتوجيه المحادثات، وتحديث بيانات CRM تلقائياً</p>
        </div>
        {activeProject && (
          <button
            type="button"
            onClick={() => setIsModalOpen(true)}
            className={`${styles.btn} ${styles.btnPrimary}`}
          >
            <Plus size={16} />
            إنشاء قاعدة
          </button>
        )}
      </div>

      {message && (
        <div className={`glass-panel`} role={message.type === 'error' ? 'alert' : 'status'} style={{
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
        ) : loadError ? (
          <div className={styles.emptyState} role="alert">
            <h3 className={styles.emptyStateTitle}>تعذر تحميل قواعد الأتمتة</h3>
            <p className={styles.emptyStateDesc}>{loadError}</p>
            {activeProject && <button type="button" onClick={() => void fetchWorkflows()} className={`${styles.btn} ${styles.btnPrimary}`}>إعادة المحاولة</button>}
          </div>
        ) : workflows.length === 0 ? (
          <div className={styles.emptyState}>
            <GitFork size={48} style={{ color: 'hsl(var(--text-muted))' }} />
            <h3 className={styles.emptyStateTitle}>لا توجد قواعد أتمتة</h3>
            <p className={styles.emptyStateDesc}>أنشئ قواعد تلقائية مثل تصنيف جهات الاتصال تلقائياً أو جدولة مهام عند استقبال الرسائل.</p>
            <button onClick={() => setIsModalOpen(true)} className={`${styles.btn} ${styles.btnPrimary}`}>
              إنشاء قاعدة
            </button>
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
            {workflows.map(wf => {
              const parsedFilters = parseJsonSafe(wf.filtersJson);
              const filters = parsedFilters !== null && typeof parsedFilters === 'object' && !Array.isArray(parsedFilters)
                ? parsedFilters as Record<string, unknown>
                : {};
              const parsedActions = parseJsonSafe(wf.actionsJson);
              const actions = Array.isArray(parsedActions)
                ? parsedActions.flatMap((action): WorkflowAction[] => {
                    if (action === null || typeof action !== 'object') return [];
                    const candidate = action as { type?: unknown; payload?: unknown };
                    if (typeof candidate.type !== 'string') return [];
                    const payload = typeof candidate.payload === 'string'
                      ? candidate.payload
                      : candidate.payload === undefined ? undefined : JSON.stringify(candidate.payload);
                    return [{ type: candidate.type, payload }];
                  })
                : [];
              return (
                <div 
                  key={wf.id} 
                  className="glass-panel"
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
                      <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))' }}>معرّف: {wf.id} | إصدار: {wf.version} | الحالة: {wf.isActive ? 'نشط' : 'متوقف'}</span>
                    </div>
                    
                    <div style={{ display: 'flex', gap: 'var(--space-sm)' }}>
                      <button
                        onClick={() => setWorkflowToToggle(wf)}
                        disabled={actionLoading}
                        className={styles.btnIcon}
                        title={wf.isActive ? 'إيقاف مؤقت' : 'تفعيل'}
                        aria-label={`${wf.isActive ? 'إيقاف' : 'تفعيل'} سير العمل ${wf.name}`}
                        style={{ color: wf.isActive ? 'hsl(var(--accent-warning))' : 'hsl(var(--accent-success))' }}
                      >
                        {wf.isActive ? <Pause size={14} /> : <Play size={14} />}
                      </button>
                      <button
                        onClick={() => handleDelete(wf.id)}
                        disabled={actionLoading}
                        className={styles.btnIcon}
                        style={{ color: 'hsl(0, 100%, 65%)' }}
                        title="حذف القاعدة"
                        aria-label={`حذف سير العمل ${wf.name}`}
                      >
                        <Trash2 size={14} />
                      </button>
                    </div>
                  </div>

                  {/* Flow Steps View */}
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: '15px', alignItems: 'center', fontSize: '0.85rem' }}>
                    <div style={{ background: 'rgba(255, 255, 255, 0.04)', padding: '8px 12px', borderRadius: '4px', border: '1px solid rgba(255,255,255,0.05)' }}>
                      <span style={{ color: 'hsl(var(--text-muted))', fontWeight: 600, display: 'block', fontSize: '0.7rem', textTransform: 'uppercase' }}>عند حدوث الحدث</span>
                      <span style={{ color: 'hsl(var(--accent-secondary))', fontWeight: 700 }}>{triggerMapAr[wf.triggerType] || wf.triggerType}</span>
                    </div>

                    <div style={{ color: 'hsl(var(--text-muted))', fontWeight: 700 }}>&larr;</div>

                    {Object.keys(filters).length > 0 && (
                      <>
                        <div style={{ background: 'rgba(255, 255, 255, 0.04)', padding: '8px 12px', borderRadius: '4px', border: '1px solid rgba(255,255,255,0.05)' }}>
                          <span style={{ color: 'hsl(var(--text-muted))', fontWeight: 600, display: 'block', fontSize: '0.7rem' }}>إذا تطابقت الشروط</span>
                          <span style={{ color: 'var(--text-strong)' }}>
                            {Object.entries(filters).map(([key, value]) => `${key} = ${typeof value === 'string' ? value : JSON.stringify(value)}`).join(', ')}
                          </span>
                        </div>
                        <div style={{ color: 'hsl(var(--text-muted))', fontWeight: 700 }}>&larr;</div>
                      </>
                    )}

                    <div style={{ background: 'rgba(203, 184, 255, 0.12)', padding: '8px 12px', borderRadius: '4px', border: '1px solid rgba(203, 184, 255, 0.25)' }}>
                      <span style={{ color: 'hsl(var(--text-muted))', fontWeight: 600, display: 'block', fontSize: '0.7rem' }}>نفذ الإجراءات التالية</span>
                      {actions.map((act, idx) => (
                        <div key={idx} style={{ color: 'var(--text-strong)', fontWeight: 600 }}>
                          {actionMapAr[act.type] || act.type}
                          {act.payload && <>: <span style={{ color: 'hsl(var(--text-secondary))', fontFamily: 'monospace', fontSize: '0.8rem' }}>{act.payload}</span></>}
                        </div>
                      ))}
                      {actions.length === 0 && <span style={{ color: 'var(--text-soft)' }}>لا توجد إجراءات صالحة في المصدر.</span>}
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
          <div className={`glass-panel ${styles.modal}`} role="dialog" aria-modal="true" aria-labelledby="workflow-dialog-title" onKeyDown={(event) => handleDialogKeyDown(event, () => setIsModalOpen(false))}>
            <div className={styles.modalHeader}>
              <h3 id="workflow-dialog-title" className={styles.modalTitle}>تهيئة قاعدة الأتمتة</h3>
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
                <label htmlFor="workflow-name" className={styles.label}>اسم سير العمل</label>
                <input 
                  id="workflow-name"
                  autoFocus
                  type="text" 
                  value={formName} 
                  onChange={(e) => setFormName(e.target.value)} 
                  placeholder="مثال: الرد التلقائي الترحيبي" 
                  className={styles.input} 
                  required 
                />
              </div>

              <div className={styles.formGroup}>
                <label htmlFor="workflow-trigger" className={styles.label}>نوع الحدث المشغل</label>
                <select 
                  id="workflow-trigger"
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
                  <label htmlFor="workflow-filter-key" className={styles.label}>مفتاح شرط التصفية</label>
                  <input 
                    id="workflow-filter-key"
                    type="text" 
                    value={formFilterKey} 
                    onChange={(e) => setFormFilterKey(e.target.value)} 
                    placeholder="مثال: city" 
                    className={styles.input} 
                  />
                </div>
                <div className={styles.formGroup}>
                  <label htmlFor="workflow-filter-value" className={styles.label}>القيمة المستهدفة للشرط</label>
                  <input 
                    id="workflow-filter-value"
                    type="text" 
                    value={formFilterValue} 
                    onChange={(e) => setFormFilterValue(e.target.value)} 
                    placeholder="مثال: القاهرة"
                    className={styles.input} 
                  />
                </div>
              </div>

              <div className={styles.formGroup}>
                <label htmlFor="workflow-action" className={styles.label}>نوع الإجراء المستهدف</label>
                <select 
                  id="workflow-action"
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
                <label htmlFor="workflow-payload" className={styles.label}>محتوى الإجراء أو نص الرسالة</label>
                <textarea 
                  id="workflow-payload"
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
        message={workflowNameToDelete ? `سيتم حذف قاعدة «${workflowNameToDelete}» نهائيًا، ولا يمكن التراجع عن هذا الإجراء.` : ''}
        confirmLabel="حذف"
        cancelLabel="إلغاء"
        onConfirm={handleConfirmDelete}
        onCancel={() => { setConfirmOpen(false); setWorkflowToDelete(null); }}
      />
      <ConfirmDialog
        isOpen={workflowToToggle !== null}
        title={workflowToToggle?.isActive ? 'إيقاف سير العمل' : 'تفعيل سير العمل'}
        message={workflowToToggle ? `${workflowToToggle.isActive ? 'سيتم إيقاف' : 'سيتم تفعيل'} سير العمل «${workflowToToggle.name}». التفعيل قد يرسل رسائل أو يغيّر بيانات CRM تلقائيًا.` : ''}
        confirmLabel={workflowToToggle?.isActive ? 'إيقاف' : 'تفعيل'}
        onConfirm={() => {
          const workflow = workflowToToggle;
          setWorkflowToToggle(null);
          if (workflow) void handleToggleActive(workflow);
        }}
        onCancel={() => setWorkflowToToggle(null)}
      />
    </div>
  );
}
