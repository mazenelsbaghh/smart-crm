'use client';

import React, { useState, useEffect } from 'react';
import { Conversation } from '../../../types/chat';
import { Customer, CustomerTask, crmService } from '../../../services/crm';
import { useToast } from '../../../context/toast-context';
import ConfirmDialog from '../../../components/shared/ConfirmDialog';
import { 
  Sparkles,
  CheckCircle,
  Zap,
  Target,
  Plus,
  ListTodo,
  User,
  MapPin,
  Trash2,
  X
} from 'lucide-react';
import styles from '../inbox.module.css';

interface ContextSidebarProps {
  activeConv: Conversation | null;
  customer: Customer | null;
  onUpdateCustomer: (data: Partial<Customer>) => Promise<void>;
  updating: boolean;
}

export default function ContextSidebar({
  activeConv,
  customer,
  onUpdateCustomer,
  updating
}: ContextSidebarProps) {
  const { showToast } = useToast();
  const customerId = customer?.id;
  
  // Profile edit states
  const [name, setName] = useState(() => customer?.name || '');
  const [city, setCity] = useState(() => customer?.city || '');
  const [notes, setNotes] = useState(() => customer?.notes || '');
  const [tags, setTags] = useState<string[]>(() => customer?.tags || []);
  const [newTagText, setNewTagText] = useState('');
  const [leadScore, setLeadScore] = useState<number | null>(() => (
    customer && Number.isFinite(customer.leadScore) ? customer.leadScore : null
  ));

  // Tasks list state
  const [taskList, setTaskList] = useState<CustomerTask[]>([]);
  const [newTaskText, setNewTaskText] = useState('');
  const [loadingTasks, setLoadingTasks] = useState(() => Boolean(customer));
  const [taskLoadError, setTaskLoadError] = useState<string | null>(null);
  const [taskReloadToken, setTaskReloadToken] = useState(0);
  const [taskToDelete, setTaskToDelete] = useState<CustomerTask | null>(null);
  const [automationTogglePending, setAutomationTogglePending] = useState(false);

  // Sync state with customer prop
  useEffect(() => {
    if (customerId) {
      crmService.getCustomerTasks(customerId)
        .then((tasks) => {
          setTaskList(tasks);
          setTaskLoadError(null);
        })
        .catch(err => {
          console.error('Failed to load customer tasks:', err);
          setTaskLoadError('تعذر تحميل مهام العميل.');
          showToast('تعذر تحميل مهام العميل.', 'error');
        })
        .finally(() => setLoadingTasks(false));
    }
  }, [customerId, showToast, taskReloadToken]);

  if (!activeConv || !customer) {
    return (
      <div className={styles.detailsPanelEmpty}>
        <Target size={32} style={{ color: 'var(--text-soft)', marginBottom: '8px' }} />
        <p>لا توجد تفاصيل نشطة</p>
      </div>
    );
  }

  // Handle saving customer fields
  const handleSaveField = async (fields: Partial<Customer>) => {
    try {
      await onUpdateCustomer(fields);
    } catch (e) {
      console.error('Failed to update CRM data:', e);
    }
  };

  // Add tag
  const handleAddTag = async () => {
    if (!newTagText.trim()) return;
    const cleanTag = newTagText.trim();
    if (tags.includes(cleanTag)) return;
    const updatedTags = [...tags, cleanTag];
    setTags(updatedTags);
    setNewTagText('');
    await handleSaveField({ tags: updatedTags });
  };

  // Remove tag
  const handleRemoveTag = async (tagToRemove: string) => {
    const updatedTags = tags.filter(t => t !== tagToRemove);
    setTags(updatedTags);
    await handleSaveField({ tags: updatedTags });
  };

  // Add task to checklist
  const handleAddTask = async () => {
    if (!newTaskText.trim()) return;
    try {
      const newTask = await crmService.createCustomerTask(customer.id, newTaskText.trim());
      setTaskList(prev => [...prev, newTask]);
      setNewTaskText('');
    } catch (err) {
      console.error('Failed to add customer task:', err);
      showToast('تعذر إضافة المهمة.', 'error');
    }
  };

  // Toggle task completion
  const handleToggleTask = async (task: CustomerTask) => {
    try {
      const updated = await crmService.updateCustomerTask(task.id, { 
        isCompleted: !task.isCompleted 
      });
      setTaskList(prev => prev.map(t => t.id === task.id ? updated : t));
    } catch (err) {
      console.error('Failed to toggle task:', err);
      showToast('تعذر تحديث حالة المهمة.', 'error');
    }
  };

  // Delete task
  const handleDeleteTask = async (taskId: string) => {
    try {
      await crmService.deleteCustomerTask(taskId);
      setTaskList(prev => prev.filter(t => t.id !== taskId));
    } catch (err) {
      console.error('Failed to delete task:', err);
      showToast('تعذر حذف المهمة.', 'error');
    } finally {
      setTaskToDelete(null);
    }
  };

  // Parse AI insights from newline-separated string
  const insightList = customer.aiInsights 
    ? customer.aiInsights.split('\n').filter(line => line.trim().length > 0)
    : [];

  // Parse automation rules
  const parsedRules = (() => {
    if (!customer.automationRules) {
      return {} as Record<string, unknown>;
    }
    try {
      const rules: unknown = JSON.parse(customer.automationRules);
      return rules !== null && typeof rules === 'object' && !Array.isArray(rules)
        ? rules as Record<string, unknown>
        : {} as Record<string, unknown>;
    } catch {
      return {} as Record<string, unknown>;
    }
  })();
  const whatsappReminderEnabled = parsedRules.whatsappReminder24h === true;

  const handleToggleAutomation = async () => {
    const newRules = {
      ...parsedRules,
      whatsappReminder24h: !whatsappReminderEnabled,
    };
    await handleSaveField({ automationRules: JSON.stringify(newRules) });
  };

  return (
    <div className={styles.detailsPanel}>
      
      {/* 1. Customer Profile details & Lead Score card */}
      <div className={styles.profileCard}>
        <div className={styles.crmCardTitleRow}>
          <User size={16} />
          <h4>بيانات وتقييم العميل</h4>
        </div>

        {/* Name input */}
        <div className={styles.profileInputGroup}>
          <label htmlFor={`customer-name-${customer.id}`} className={styles.profileLabel}>اسم العميل</label>
          <input
            id={`customer-name-${customer.id}`}
            type="text"
            className={styles.profileInput}
            value={name}
            onChange={(e) => setName(e.target.value)}
            onBlur={() => handleSaveField({ name })}
            placeholder="اسم العميل..."
            disabled={updating}
          />
        </div>

        {/* City input */}
        <div className={styles.profileInputGroup}>
          <label htmlFor={`customer-city-${customer.id}`} className={styles.profileLabel}>المدينة</label>
          <div style={{ position: 'relative' }}>
            <MapPin size={14} style={{ position: 'absolute', right: '10px', top: '50%', transform: 'translateY(-50%)', color: 'var(--text-soft)' }} />
            <input
              id={`customer-city-${customer.id}`}
              type="text"
              className={styles.profileInput}
              style={{ paddingRight: '30px' }}
              value={city}
              onChange={(e) => setCity(e.target.value)}
              onBlur={() => handleSaveField({ city })}
              placeholder="المدينة..."
              disabled={updating}
            />
          </div>
        </div>

        {/* Notes textarea */}
        <div className={styles.profileInputGroup}>
          <label htmlFor={`customer-notes-${customer.id}`} className={styles.profileLabel}>ملاحظات العميل</label>
          <textarea
            id={`customer-notes-${customer.id}`}
            className={styles.profileTextarea}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            onBlur={() => handleSaveField({ notes })}
            placeholder="ملاحظات العميل الإدارية..."
            disabled={updating}
          />
        </div>

        {/* Tags input */}
        <div className={styles.profileInputGroup}>
          <label htmlFor={`customer-tag-${customer.id}`} className={styles.profileLabel}>الوسوم</label>
          <div className={styles.tagsContainer}>
            {tags.map(tag => (
              <span key={tag} className={styles.tagItem}>
                {tag}
                <button 
                  type="button" 
                  className={styles.deleteTagBtn} 
                  onClick={() => handleRemoveTag(tag)}
                  disabled={updating}
                  aria-label={`إزالة الوسم ${tag}`}
                >
                  <X size={10} />
                </button>
              </span>
            ))}
          </div>
          <div className={styles.addTagRow}>
            <input
              id={`customer-tag-${customer.id}`}
              type="text"
              className={styles.addTagInput}
              placeholder="أضف وسم جديد..."
              value={newTagText}
              onChange={(e) => setNewTagText(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleAddTag()}
              disabled={updating}
            />
            <button type="button" className={styles.addTagBtn} onClick={handleAddTag} disabled={updating} aria-label="إضافة الوسم">
              <Plus size={12} />
            </button>
          </div>
        </div>

        {/* Lead Score slider */}
        <div className={styles.probabilitySection} style={{ marginTop: '8px', paddingTop: '12px', borderTop: '1px solid var(--border-subtle)' }}>
          <div className={styles.probabilityHeader}>
            <span>تقييم جودة العميل (Lead Score)</span>
            <span className={styles.probabilityValue}>{leadScore === null ? 'غير متاح' : `${leadScore}/100`}</span>
          </div>
          {leadScore === null ? (
            <p style={{ margin: 0, color: 'var(--text-soft)', fontSize: '0.74rem' }}>لم يرسل المصدر تقييمًا لهذا العميل.</p>
          ) : (
            <input
              type="range"
              min="0"
              max="100"
              className={styles.scoreSlider}
              value={leadScore}
              onChange={(e) => setLeadScore(Number(e.target.value))}
              onBlur={() => handleSaveField({ leadScore })}
              onKeyUp={() => handleSaveField({ leadScore })}
              disabled={updating}
              aria-label="تقييم جودة العميل"
              aria-valuetext={`${leadScore} من 100`}
            />
          )}
        </div>
      </div>

      {/* 2. Tasks list card */}
      <div className={styles.tasksCard}>
        <div className={styles.crmCardTitleRow}>
          <ListTodo size={16} />
          <h4>المهام المجدولة والمطلوبة</h4>
        </div>

        <div className={styles.taskListContainer}>
          {loadingTasks && taskList.length === 0 ? (
            <p style={{ color: 'var(--text-soft)', fontSize: '0.75rem', textAlign: 'center' }}>جاري تحميل المهام...</p>
          ) : taskLoadError ? (
            <div role="alert" style={{ display: 'grid', gap: '8px', justifyItems: 'start' }}>
              <p style={{ color: 'var(--text-soft)', fontSize: '0.75rem', margin: 0 }}>{taskLoadError}</p>
              <button
                type="button"
                className={styles.retryConversationsBtn}
                onClick={() => {
                  setLoadingTasks(true);
                  setTaskLoadError(null);
                  setTaskReloadToken((current) => current + 1);
                }}
              >
                إعادة المحاولة
              </button>
            </div>
          ) : taskList.length === 0 ? (
            <p style={{ color: 'var(--text-soft)', fontSize: '0.75rem', textAlign: 'center', padding: '8px 0' }}>لا توجد مهام حالية للعميل.</p>
          ) : (
            taskList.map(t => (
              <div key={t.id} className={styles.taskItem}>
                <button
                  type="button"
                  className={`${styles.taskCheckbox} ${t.isCompleted ? styles.taskCheckboxChecked : ''}`}
                  onClick={() => handleToggleTask(t)}
                  aria-label={`${t.isCompleted ? 'إعادة فتح' : 'إكمال'} المهمة ${t.title}`}
                  aria-pressed={t.isCompleted}
                >
                  {t.isCompleted && <CheckCircle size={12} />}
                </button>
                <span className={`${styles.taskText} ${t.isCompleted ? styles.taskTextDone : ''}`}>{t.title}</span>
                <button
                  type="button"
                  className={styles.deleteTaskBtn}
                  onClick={() => setTaskToDelete(t)}
                  aria-label={`حذف المهمة ${t.title}`}
                >
                  <Trash2 size={12} />
                </button>
              </div>
            ))
          )}
        </div>

        <div className={styles.addTaskRow}>
          <input
            type="text"
            className={styles.addTaskInput}
            placeholder="أضف مهمة جديدة..."
            value={newTaskText}
            onChange={(e) => setNewTaskText(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleAddTask()}
            aria-label="مهمة جديدة"
          />
          <button type="button" className={styles.addTaskBtn} onClick={handleAddTask} aria-label="إضافة المهمة">
            <Plus size={16} />
          </button>
        </div>
      </div>

      {/* 3. AI Insights card */}
      <div className={styles.aiInsightsCard}>
        <div className={styles.crmCardTitleRow}>
          <Sparkles size={16} className={styles.aiSparkleIcon} />
          <h4>رؤى وتوصيات الذكاء الاصطناعي</h4>
        </div>
        <div className={styles.aiInsightList}>
          {insightList.length === 0 ? (
            <p style={{ color: 'var(--text-soft)', fontSize: '0.74rem', textAlign: 'center', padding: '12px 0', direction: 'rtl' }}>
              لا توجد رؤى صادرة من المصدر لهذه المحادثة حاليًا.
            </p>
          ) : (
            insightList.map((insight, idx) => (
              <div key={idx} className={styles.insightItem}>
                <div className={styles.insightDot}></div>
                <p>{insight}</p>
              </div>
            ))
          )}
        </div>
      </div>

      {/* 4. Automations card */}
      <div className={styles.automationsCard}>
        <div className={styles.crmCardTitleRow}>
          <Zap size={16} />
          <h4>أتمتة المتابعة والتنبيهات</h4>
        </div>

        <div className={styles.automationList}>
          <div className={styles.automationToggleRow}>
            <span style={{ fontSize: '0.74rem', color: 'var(--text-strong)', lineHeight: '1.4' }}>
              إذا لم يرد العميل خلال 24 ساعة، أرسل تذكير واتساب.
            </span>
            <button
              type="button"
              className={`${styles.toggleSwitch} ${whatsappReminderEnabled ? styles.toggleSwitchActive : ''}`}
              onClick={() => setAutomationTogglePending(true)}
              disabled={updating}
              role="switch"
              aria-checked={whatsappReminderEnabled}
              aria-label="إرسال تذكير واتساب إذا لم يرد العميل خلال 24 ساعة"
            >
              <span className={styles.toggleKnob}></span>
            </button>
          </div>
          
          <div className={styles.automationToggleRow}>
            <span id={`proposal-follow-up-unavailable-${customer.id}`} style={{ fontSize: '0.74rem', color: 'var(--text-strong)', lineHeight: '1.4' }}>
              متابعة المقترح المالي غير متاحة لأن الخادم لا ينفّذ هذه القاعدة حاليًا.
            </span>
            <button
              type="button"
              className={`${styles.toggleSwitch} ${styles.toggleDisabled}`}
              disabled
              role="switch"
              aria-checked="false"
              aria-describedby={`proposal-follow-up-unavailable-${customer.id}`}
              title="غير متاح حتى يدعم الخادم تنفيذ القاعدة"
            >
              <span className={styles.toggleKnob}></span>
            </button>
          </div>
        </div>
      </div>
      <ConfirmDialog
        isOpen={taskToDelete !== null}
        title="حذف مهمة العميل"
        message={taskToDelete ? `سيتم حذف المهمة «${taskToDelete.title}» نهائيًا.` : ''}
        confirmLabel="حذف المهمة"
        onConfirm={() => taskToDelete && void handleDeleteTask(taskToDelete.id)}
        onCancel={() => setTaskToDelete(null)}
      />
      <ConfirmDialog
        isOpen={automationTogglePending}
        title="تغيير أتمتة العميل"
        message={`${whatsappReminderEnabled ? 'سيتم إيقاف' : 'سيتم تفعيل'} إرسال تذكير واتساب تلقائي بعد 24 ساعة لهذا العميل.`}
        confirmLabel="تأكيد التغيير"
        onConfirm={() => {
          setAutomationTogglePending(false);
          void handleToggleAutomation();
        }}
        onCancel={() => setAutomationTogglePending(false)}
      />
    </div>
  );
}
