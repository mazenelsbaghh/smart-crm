'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import Dialog from '../../components/shared/Dialog';
import {
  GitFork,
  Plus,
  Play,
  Pause,
  Trash2,
  Zap,
  ListFilter,
  ArrowLeft,
  CheckCircle2,
} from 'lucide-react';
import PageHeader from '../../components/ui/PageHeader';
import FilterTabs from '../../components/ui/FilterTabs';
import SearchField from '../../components/ui/SearchField';
import StatusMessage from '../../components/ui/StatusMessage';
import styles from './management.module.css';
import { useListFilters } from './hooks/use-list-filters';

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

const listFilterConfig = { statuses: ['all', 'active', 'paused'] as const, defaultStatus: 'all' as const, pagination: false };

export default function Workflows() {
  return <React.Suspense fallback={<p role="status">جاري تحميل القائمة…</p>}><WorkflowsList /></React.Suspense>;
}

function WorkflowsList() {
  const { activeProject } = useAuth();

  const { search: searchQuery, setSearch: setSearchQuery, status: statusFilter, setStatus: setStatusFilter,
    clearFilters, hasFilters } = useListFilters(listFilterConfig);
  const [workflows, setWorkflows] = useState<AutomationWorkflow[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadedProjectId, setLoadedProjectId] = useState<string | null>(null);
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
      setLoadedProjectId(activeProject.id);
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

  const visibleWorkflows = workflows.filter(workflow =>
    (statusFilter === 'all' || workflow.isActive === (statusFilter === 'active')) &&
    `${workflow.name} ${triggerMapAr[workflow.triggerType] || workflow.triggerType}`.toLocaleLowerCase('ar').includes(searchQuery.trim().toLocaleLowerCase('ar')));
  const workflowNameToDelete = workflows.find((workflow) => workflow.id === workflowToDelete)?.name;
  const countsAvailable = !loading && !loadError && loadedProjectId === activeProject?.id;

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <PageHeader icon={<GitFork size={22} aria-hidden="true" />} title="أتمتة العمليات" description="قواعد تربط أحداث العملاء بإجراءات تلقائية." />
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

      {message && <StatusMessage tone={message.type === 'error' ? 'danger' : 'success'}>{message.text}</StatusMessage>}

      <div className={styles.operationsToolbar}>
        <FilterTabs compactOnMobile label="حالة قواعد الأتمتة" selected={statusFilter} onChange={setStatusFilter} options={[
          { key: 'all', label: 'كل القواعد', count: countsAvailable ? workflows.length : undefined, icon: <GitFork size={16} aria-hidden="true" /> },
          { key: 'active', label: 'نشطة', count: countsAvailable ? workflows.filter(w => w.isActive).length : undefined, icon: <Play size={16} aria-hidden="true" />, tone: 'success' },
          { key: 'paused', label: 'متوقفة', count: countsAvailable ? workflows.filter(w => !w.isActive).length : undefined, icon: <Pause size={16} aria-hidden="true" />, tone: 'warning' },
        ]} />
        <div className={styles.listSearchTools}>
          <SearchField label="بحث في قواعد الأتمتة" placeholder="اسم القاعدة أو الحدث…" value={searchQuery} onChange={event => setSearchQuery(event.target.value)} />
          {hasFilters && <button type="button" className={`${styles.btn} ${styles.btnSecondary}`} onClick={clearFilters}>إعادة ضبط التصفية</button>}
        </div>
      </div>
      <div className={`${styles.panel} ${styles.operationsPanel}`}>
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
        ) : visibleWorkflows.length === 0 ? (
          <div className={styles.emptyState}>
            <GitFork size={48} style={{ color: 'hsl(var(--text-muted))' }} />
            <h3 className={styles.emptyStateTitle}>{workflows.length ? 'لا توجد قواعد مطابقة' : 'لا توجد قواعد أتمتة'}</h3>
            <p className={styles.emptyStateDesc}>أنشئ قواعد تلقائية مثل تصنيف جهات الاتصال تلقائياً أو جدولة مهام عند استقبال الرسائل.</p>
            <button onClick={() => setIsModalOpen(true)} className={`${styles.btn} ${styles.btnPrimary}`}>
              إنشاء قاعدة
            </button>
          </div>
        ) : (
          <div className={styles.workflowList}>
            {visibleWorkflows.map(wf => {
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
                <article key={wf.id} className={styles.workflowRecord}>
                  <div className={styles.recordHeading}>
                    <div>
                      <h2>{wf.name}</h2>
                      <span className={`${styles.statusBadge} ${wf.isActive ? styles.statusCompleted : styles.statusBypassed}`}>
                        {wf.isActive ? <CheckCircle2 size={14} aria-hidden="true" /> : <Pause size={14} aria-hidden="true" />}{wf.isActive ? 'نشطة' : 'متوقفة'}
                      </span>
                    </div>
                    <div className={styles.recordActions}>
                      <button type="button" onClick={() => setWorkflowToToggle(wf)} disabled={actionLoading}
                        className={`${styles.btn} ${styles.btnSecondary}`} aria-label={`${wf.isActive ? 'إيقاف' : 'تفعيل'} سير العمل ${wf.name}`}>
                        {wf.isActive ? <Pause size={16} aria-hidden="true" /> : <Play size={16} aria-hidden="true" />}{wf.isActive ? 'إيقاف مؤقت' : 'تفعيل'}
                      </button>
                      <details className={styles.recordDisclosure}><summary>المزيد</summary><div className={styles.secondaryTools}>
                        <span className={styles.recordMeta}>الإصدار {wf.version} · المعرّف {wf.id}</span>
                        <button type="button" onClick={() => handleDelete(wf.id)} disabled={actionLoading} className={`${styles.btn} ${styles.btnDanger}`} aria-label={`حذف سير العمل ${wf.name}`}><Trash2 size={16} aria-hidden="true" /> حذف القاعدة</button>
                      </div></details>
                    </div>
                  </div>
                  <div className={styles.workflowSteps}>
                    <div className={styles.workflowTrigger}><span className={styles.recordLabel}><Zap size={15} aria-hidden="true" /> عند حدوث</span><strong>{triggerMapAr[wf.triggerType] || wf.triggerType}</strong></div>
                    <ArrowLeft className={styles.stepArrow} size={20} aria-hidden="true" />
                    {Object.keys(filters).length > 0 && <>
                      <div className={styles.workflowCondition}><span className={styles.recordLabel}><ListFilter size={15} aria-hidden="true" /> بشرط</span><p>{Object.entries(filters).map(([key, value]) => `${key} = ${typeof value === 'string' ? value : JSON.stringify(value)}`).join('، ')}</p></div>
                      <ArrowLeft className={styles.stepArrow} size={20} aria-hidden="true" />
                    </>}
                    <div className={styles.workflowAction}><span className={styles.recordLabel}><Play size={15} aria-hidden="true" /> ينفّذ</span>
                      {actions.map((action, index) => <div key={index}><strong>{actionMapAr[action.type] || action.type}</strong>{action.payload && <p>{action.payload}</p>}</div>)}
                      {actions.length === 0 && <p>لا توجد إجراءات صالحة في المصدر.</p>}
                    </div>
                  </div>
                </article>
              );
            })}
          </div>
        )}
      </div>

      {/* Creation Modal Overlay */}
      {isModalOpen && (
        <Dialog label="تحرير الأتمتة" onClose={() => setIsModalOpen(false)}>
          <div className={styles.dialogContent}>
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
        </Dialog>
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
