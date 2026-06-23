'use client';

import React, { useState, useEffect } from 'react';
import { Conversation } from '../../../types/chat';
import { Customer, CustomerTask, crmService } from '../../../services/crm';
import { 
  Sparkles,
  CheckCircle,
  Zap,
  Target,
  Plus,
  ListTodo,
  User,
  MapPin,
  FileText,
  Tags,
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
  
  // Profile edit states
  const [name, setName] = useState('');
  const [city, setCity] = useState('');
  const [notes, setNotes] = useState('');
  const [tags, setTags] = useState<string[]>([]);
  const [newTagText, setNewTagText] = useState('');
  const [leadScore, setLeadScore] = useState(0);

  // Tasks list state
  const [taskList, setTaskList] = useState<CustomerTask[]>([]);
  const [newTaskText, setNewTaskText] = useState('');
  const [loadingTasks, setLoadingTasks] = useState(false);

  // Sync state with customer prop
  useEffect(() => {
    if (customer) {
      setName(customer.name || '');
      setCity(customer.city || '');
      setNotes(customer.notes || '');
      setTags(customer.tags || []);
      setLeadScore(customer.leadScore || 0);

      // Load tasks from database
      setLoadingTasks(true);
      crmService.getCustomerTasks(customer.id)
        .then(setTaskList)
        .catch(err => console.error('Failed to load customer tasks:', err))
        .finally(() => setLoadingTasks(false));
    } else {
      setName('');
      setCity('');
      setNotes('');
      setTags([]);
      setTaskList([]);
    }
  }, [customer]);

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
    }
  };

  // Delete task
  const handleDeleteTask = async (taskId: string) => {
    try {
      await crmService.deleteCustomerTask(taskId);
      setTaskList(prev => prev.filter(t => t.id !== taskId));
    } catch (err) {
      console.error('Failed to delete task:', err);
    }
  };

  // Parse AI insights from newline-separated string
  const insightList = customer.aiInsights 
    ? customer.aiInsights.split('\n').filter(line => line.trim().length > 0)
    : [];

  // Parse automation rules
  const parsedRules = (() => {
    if (!customer.automationRules) {
      return { whatsappReminder24h: true, proposalFollowUp: false };
    }
    try {
      return JSON.parse(customer.automationRules);
    } catch {
      return { whatsappReminder24h: true, proposalFollowUp: false };
    }
  })();

  const handleToggleAutomation = async (ruleKey: 'whatsappReminder24h' | 'proposalFollowUp') => {
    const newRules = {
      ...parsedRules,
      [ruleKey]: !parsedRules[ruleKey]
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
          <span className={styles.profileLabel}>اسم العميل</span>
          <input
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
          <span className={styles.profileLabel}>المدينة</span>
          <div style={{ position: 'relative' }}>
            <MapPin size={14} style={{ position: 'absolute', right: '10px', top: '50%', transform: 'translateY(-50%)', color: 'var(--text-soft)' }} />
            <input
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
          <span className={styles.profileLabel}>ملاحظات العميل</span>
          <textarea
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
          <span className={styles.profileLabel}>الوسوم (Tags)</span>
          <div className={styles.tagsContainer}>
            {tags.map(tag => (
              <span key={tag} className={styles.tagItem}>
                {tag}
                <button 
                  type="button" 
                  className={styles.deleteTagBtn} 
                  onClick={() => handleRemoveTag(tag)}
                  disabled={updating}
                >
                  <X size={10} />
                </button>
              </span>
            ))}
          </div>
          <div className={styles.addTagRow}>
            <input
              type="text"
              className={styles.addTagInput}
              placeholder="أضف وسم جديد..."
              value={newTagText}
              onChange={(e) => setNewTagText(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleAddTag()}
              disabled={updating}
            />
            <button type="button" className={styles.addTagBtn} onClick={handleAddTag} disabled={updating}>
              <Plus size={12} />
            </button>
          </div>
        </div>

        {/* Lead Score slider */}
        <div className={styles.probabilitySection} style={{ marginTop: '8px', paddingTop: '12px', borderTop: '1px solid var(--border-subtle)' }}>
          <div className={styles.probabilityHeader}>
            <span>تقييم جودة العميل (Lead Score)</span>
            <span className={styles.probabilityValue}>{leadScore}/100</span>
          </div>
          <input
            type="range"
            min="0"
            max="100"
            className={styles.scoreSlider}
            value={leadScore}
            onChange={(e) => setLeadScore(Number(e.target.value))}
            onMouseUp={() => handleSaveField({ leadScore })}
            onTouchEnd={() => handleSaveField({ leadScore })}
            disabled={updating}
          />
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
          ) : taskList.length === 0 ? (
            <p style={{ color: 'var(--text-soft)', fontSize: '0.75rem', textAlign: 'center', padding: '8px 0' }}>لا توجد مهام حالية للعميل.</p>
          ) : (
            taskList.map(t => (
              <div key={t.id} className={styles.taskItem}>
                <button
                  type="button"
                  className={`${styles.taskCheckbox} ${t.isCompleted ? styles.taskCheckboxChecked : ''}`}
                  onClick={() => handleToggleTask(t)}
                >
                  {t.isCompleted && <CheckCircle size={12} />}
                </button>
                <span className={`${styles.taskText} ${t.isCompleted ? styles.taskTextDone : ''}`}>{t.title}</span>
                <button
                  type="button"
                  className={styles.deleteTaskBtn}
                  onClick={() => handleDeleteTask(t.id)}
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
          />
          <button type="button" className={styles.addTaskBtn} onClick={handleAddTask}>
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
              لا توجد رؤى متاحة حالياً. سيقوم الذكاء الاصطناعي بتحليل المحادثة قريباً.
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
              className={`${styles.toggleSwitch} ${parsedRules.whatsappReminder24h ? styles.toggleSwitchActive : ''}`}
              onClick={() => handleToggleAutomation('whatsappReminder24h')}
              disabled={updating}
            >
              <span className={styles.toggleKnob}></span>
            </button>
          </div>
          
          <div className={styles.automationToggleRow}>
            <span style={{ fontSize: '0.74rem', color: 'var(--text-strong)', lineHeight: '1.4' }}>
              عند قراءة المقترح المالي، افتح مهمة متابعة فورية.
            </span>
            <button
              type="button"
              className={`${styles.toggleSwitch} ${parsedRules.proposalFollowUp ? styles.toggleSwitchActive : ''}`}
              onClick={() => handleToggleAutomation('proposalFollowUp')}
              disabled={updating}
            >
              <span className={styles.toggleKnob}></span>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
