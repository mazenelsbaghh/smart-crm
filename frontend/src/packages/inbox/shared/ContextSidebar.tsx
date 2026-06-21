'use client';

import React, { useState } from 'react';
import { Conversation } from '../../../types/chat';
import { Customer } from '../../../services/crm';
import { 
  Sparkles,
  CheckCircle,
  Zap,
  Target,
  Plus,
  DollarSign,
  TrendingUp,
  ListTodo
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
  
  // Form/Input States
  const [budget, setBudget] = useState<number>(customer?.budget || 0);
  const [leadScore, setLeadScore] = useState<number>(customer?.leadScore || 0);
  const [probability, setProbability] = useState<number>(customer?.purchaseProbability || 50);
  const [stage, setStage] = useState<string>(customer?.pipelineStage || 'New');

  // AI & Automation States
  const [automationActive, setAutomationActive] = useState<boolean>(true);
  const [tasks, setTasks] = useState<{ id: string; text: string; done: boolean }[]>([
    { id: '1', text: 'إرسال المقترح الفني والمالي للعميل', done: false },
    { id: '2', text: 'مراجعة الميزانية وتوافق الدفع', done: true }
  ]);
  const [newTaskText, setNewTaskText] = useState('');

  if (!activeConv) {
    return (
      <div className={styles.detailsPanelEmpty}>
        <Target size={32} style={{ color: 'var(--text-soft)', marginBottom: '8px' }} />
        <p>لا توجد تفاصيل نشطة</p>
      </div>
    );
  }

  // Handle updates
  const handleSaveField = async (fields: Partial<Customer>) => {
    try {
      await onUpdateCustomer(fields);
    } catch (e) {
      console.error('Failed to update CRM data:', e);
    }
  };

  const handleAddTask = () => {
    if (!newTaskText.trim()) return;
    setTasks([...tasks, { id: Date.now().toString(), text: newTaskText, done: false }]);
    setNewTaskText('');
  };

  const handleToggleTask = (id: string) => {
    setTasks(tasks.map(t => t.id === id ? { ...t, done: !t.done } : t));
  };

  return (
    <div className={styles.detailsPanel}>
      {/* 1. Lead Score & Profit card (Lavender Accent styled) */}
      <div className={styles.crmIntelligenceCard}>
        <div className={styles.crmCardTitleRow}>
          <TrendingUp size={16} />
          <h4>تفاصيل وأرباح الصفقة</h4>
        </div>

        {/* Potential profit value */}
        <div className={styles.profitSection}>
          <span className={styles.profitLabel}>القيمة التقديرية للمشروع</span>
          <div className={styles.profitInputRow}>
            <DollarSign size={18} className={styles.profitCurrencyIcon} />
            <input
              type="number"
              className={styles.profitInput}
              value={budget}
              onChange={(e) => setBudget(Number(e.target.value))}
              onBlur={() => handleSaveField({ budget })}
              placeholder="0.00"
              disabled={updating}
            />
          </div>
        </div>

        {/* Stage selection checkboxes */}
        <div className={styles.stageSelectorsRow}>
          {['Negotiation', 'Won', 'Lost'].map(item => (
            <button
              key={item}
              type="button"
              className={`${styles.stageSelectorBtn} ${stage === item ? styles.stageSelectorBtnActive : ''}`}
              onClick={() => {
                setStage(item);
                handleSaveField({ pipelineStage: item });
              }}
              disabled={updating}
            >
              {item === 'Negotiation' && 'مفاوضات'}
              {item === 'Won' && 'مكتملة'}
              {item === 'Lost' && 'ملغية'}
            </button>
          ))}
        </div>

        {/* Purchase probability slider */}
        <div className={styles.probabilitySection}>
          <div className={styles.probabilityHeader}>
            <span>احتمالية إتمام البيع</span>
            <span className={styles.probabilityValue}>{probability}%</span>
          </div>
          <input
            type="range"
            min="0"
            max="100"
            className={styles.probabilitySlider}
            value={probability}
            onChange={(e) => setProbability(Number(e.target.value))}
            onMouseUp={() => handleSaveField({ purchaseProbability: probability })}
            onTouchEnd={() => handleSaveField({ purchaseProbability: probability })}
            disabled={updating}
          />
        </div>

        {/* Score slider */}
        <div className={styles.probabilitySection}>
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
          {tasks.map(t => (
            <div key={t.id} className={styles.taskItem}>
              <button
                type="button"
                className={`${styles.taskCheckbox} ${t.done ? styles.taskCheckboxChecked : ''}`}
                onClick={() => handleToggleTask(t.id)}
              >
                {t.done && <CheckCircle size={12} />}
              </button>
              <span className={`${styles.taskText} ${t.done ? styles.taskTextDone : ''}`}>{t.text}</span>
            </div>
          ))}
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
          <div className={styles.insightItem}>
            <div className={styles.insightDot}></div>
            <p>العميل متفاعل جداً ومهتم بالبرنامج المتقدم.</p>
          </div>
          <div className={styles.insightItem}>
            <div className={styles.insightDot}></div>
            <p>آخر رد كان منذ ساعتين، يفضل المتابعة الآن.</p>
          </div>
          <div className={styles.insightItem}>
            <div className={styles.insightDot}></div>
            <p>أنسب موعد للرد على هذا العميل هو في حدود الـ 6 مساءً.</p>
          </div>
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
            <span>إذا لم يرد العميل خلال 24 ساعة، أرسل تذكير واتساب.</span>
            <button
              type="button"
              className={`${styles.toggleSwitch} ${automationActive ? styles.toggleSwitchActive : ''}`}
              onClick={() => setAutomationActive(!automationActive)}
            >
              <span className={styles.toggleKnob}></span>
            </button>
          </div>
          
          <div className={styles.automationToggleRow}>
            <span>عند قراءة المقترح المالي، افتح مهمة متابعة فورية.</span>
            <button
              type="button"
              className={`${styles.toggleSwitch} ${styles.toggleDisabled}`}
              disabled
            >
              <span className={styles.toggleKnob}></span>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
