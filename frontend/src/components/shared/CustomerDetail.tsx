import React, { useEffect, useState } from 'react';
import { Customer, crmService } from '../../services/crm';
import { api } from '../../services/api';
import { X, Plus, Calendar, Tag, Sparkles, ArrowRight } from 'lucide-react';
import { useToast } from '../../context/toast-context';
import Tooltip from './Tooltip';
import PhantomLoader from './PhantomLoader';
import styles from './customer-detail.module.css';

interface CustomerDetailProps {
  customerId: string;
  projectId: string;
  onClose: () => void;
  onUpdate: () => void;
  isInline?: boolean;
}

interface FollowUp {
  id: string;
  customerId: string;
  dueDate: string;
  status: 'Pending' | 'Completed' | 'Missed';
  notes: string;
  type?: 'Nurturing' | 'AppointmentReminder';
  appointmentTime?: string;
  tone?: string;
}

export default function CustomerDetail({ customerId, projectId, onClose, onUpdate, isInline = false }: CustomerDetailProps) {
  const { showToast } = useToast();
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [followUps, setFollowUps] = useState<FollowUp[]>([]);
  
  // Form fields
  const [name, setName] = useState('');
  const [city, setCity] = useState('');
  const [budget, setBudget] = useState('');
  const [leadScore, setLeadScore] = useState(0);
  const [notes, setNotes] = useState('');
  const [tags, setTags] = useState<string[]>([]);
  const [pipelineStage, setPipelineStage] = useState('New');
  const [label, setLabel] = useState('');
  const [isBlacklisted, setIsBlacklisted] = useState(false);

  // AI Memory / Profile fields
  const [editableSummary, setEditableSummary] = useState('');
  const [editableFacts, setEditableFacts] = useState('');
  const [editableTriggers, setEditableTriggers] = useState('');
  const [editableObjections, setEditableObjections] = useState('');
  const [savingMemory, setSavingMemory] = useState(false);
  const [loadingMemory, setLoadingMemory] = useState(false);
  const [generatingMemory, setGeneratingMemory] = useState(false);
  
  // Follow-up form
  const [newFollowUpDate, setNewFollowUpDate] = useState('');
  const [newFollowUpNotes, setNewFollowUpNotes] = useState('');
  const [creatingFollowUp, setCreatingFollowUp] = useState(false);
  const [newFollowUpType, setNewFollowUpType] = useState<'Nurturing' | 'AppointmentReminder'>('Nurturing');
  const [newAppointmentTime, setNewAppointmentTime] = useState('');
  const [newFollowUpTone, setNewFollowUpTone] = useState<string>('Default');

  // New tag field
  const [newTag, setNewTag] = useState('');

  const fetchCustomerData = async () => {
    try {
      setLoading(true);
      const data = await crmService.getCustomer(customerId);
      setCustomer(data);
      setName(data.name || '');
      setCity(data.city || '');
      setBudget(data.budget ? data.budget.toString() : '');
      setLeadScore(data.leadScore || 0);
      setNotes(data.notes || '');
      setTags(data.tags || []);
      setPipelineStage(data.pipelineStage || 'New');
      setLabel(data.label || '');
      setIsBlacklisted(data.isBlacklisted || false);

      // Fetch followups
      const fuResp = await api.get<FollowUp[]>(`/api/projects/${projectId}/follow-ups`);
      const filtered = fuResp.data.filter(f => f.customerId === customerId);
      setFollowUps(filtered);

      // Fetch AI Customer Memory
      let hasMemory = false;
      try {
        setLoadingMemory(true);
        const memResp = await api.get(`/api/customers/${customerId}/memory`);
        if (memResp.data) {
          const summary = memResp.data.longTermSummary || '';
          setEditableSummary(summary);

          let facts: string[] = [];
          try {
            facts = JSON.parse(memResp.data.factsJson || '[]');
            setEditableFacts(facts.join(', '));
          } catch { setEditableFacts(''); }
          try {
            const triggers = JSON.parse(memResp.data.triggersJson || '[]');
            setEditableTriggers(triggers.join(', '));
          } catch { setEditableTriggers(''); }
          try {
            const objections = JSON.parse(memResp.data.objectionsJson || '[]');
            setEditableObjections(objections.join(', '));
          } catch { setEditableObjections(''); }

          if (summary.trim() !== '' || facts.length > 0) {
            hasMemory = true;
          }
        }
      } catch (err) {
        console.error('Customer memory not found or failed to load', err);
        setEditableSummary('');
        setEditableFacts('');
        setEditableTriggers('');
        setEditableObjections('');
      } finally {
        setLoadingMemory(false);
      }

      if (!hasMemory) {
        // Automatically trigger AI memory generation!
        try {
          setGeneratingMemory(true);
          const resp = await api.post(`/api/projects/${projectId}/customers/${customerId}/memory/generate`);
          if (resp.data) {
            const mem = resp.data;
            setEditableSummary(mem.longTermSummary || '');
            try {
              const facts = JSON.parse(mem.factsJson || '[]');
              setEditableFacts(facts.join(', '));
            } catch { setEditableFacts(''); }
            try {
              const triggers = JSON.parse(mem.triggersJson || '[]');
              setEditableTriggers(triggers.join(', '));
            } catch { setEditableTriggers(''); }
            try {
              const objections = JSON.parse(mem.objectionsJson || '[]');
              setEditableObjections(objections.join(', '));
            } catch { setEditableObjections(''); }

            // Reload customer basic info to show updated name, city, budget, leadScore, etc.
            const custResp = await crmService.getCustomer(customerId);
            setCustomer(custResp);
            setName(custResp.name || '');
            setCity(custResp.city || '');
            setBudget(custResp.budget ? custResp.budget.toString() : '');
            setLeadScore(custResp.leadScore || 0);
            setNotes(custResp.notes || '');
            setTags(custResp.tags || []);
            setPipelineStage(custResp.pipelineStage || 'New');
            setLabel(custResp.label || '');
            setIsBlacklisted(custResp.isBlacklisted || false);
          }
        } catch (genErr) {
          console.error('Failed to auto-generate memory on load', genErr);
        } finally {
          setGeneratingMemory(false);
        }
      }

    } catch (e) {
      console.error('Error loading customer detail data', e);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchCustomerData();
  }, [customerId, projectId]);

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    if (saving) return;

    setSaving(true);
    try {
      await crmService.updateCustomer(customerId, {
        name,
        city,
        leadScore: Math.min(100, Math.max(0, leadScore)),
        notes,
        tags,
        budget: budget ? parseFloat(budget) : null,
        pipelineStage,
        label,
        isBlacklisted,
      });
      onUpdate();
      onClose();
    } catch (err) {
      console.error('Failed to save customer updates', err);
    } finally {
      setSaving(false);
    }
  };

  const handleSaveMemory = async (e: React.FormEvent) => {
    e.preventDefault();
    setSavingMemory(true);
    try {
      const parseCsv = (csv: string) => csv.split(',').map(s => s.trim()).filter(Boolean);
      const payload = {
        longTermSummary: editableSummary,
        factsJson: JSON.stringify(parseCsv(editableFacts)),
        triggersJson: JSON.stringify(parseCsv(editableTriggers)),
        objectionsJson: JSON.stringify(parseCsv(editableObjections)),
      };
      await api.put(`/api/customers/${customerId}/memory`, payload);
      showToast('تم تحديث ملف تعريف العميل بنجاح! ✨', 'success');
    } catch (err) {
      console.error('Failed to save customer memory', err);
      showToast('فشل حفظ تفاصيل ملف العميل.', 'error');
    } finally {
      setSavingMemory(false);
    }
  };

  const handleGenerateMemory = async () => {
    setGeneratingMemory(true);
    try {
      const resp = await api.post(`/api/projects/${projectId}/customers/${customerId}/memory/generate`);
      if (resp.data) {
        await fetchCustomerData();
        showToast('تم تحديث وتوليد ملف التعريف بالذكاء الاصطناعي بنجاح! 🧠', 'success');
      }
    } catch (err: any) {
      console.error('Failed to generate customer profile', err);
      const errMsg = err.response?.data || 'فشل توليد ملف التعريف. تأكد من وجود رسائل سابقة للعميل.';
      showToast(errMsg, 'error');
    } finally {
      setGeneratingMemory(false);
    }
  };

  const handleAddTag = () => {
    if (!newTag.trim() || tags.includes(newTag.trim())) return;
    setTags([...tags, newTag.trim()]);
    setNewTag('');
  };

  const handleRemoveTag = (tagToRemove: string) => {
    setTags(tags.filter(t => t !== tagToRemove));
  };

  const handleAddFollowUp = async (e: React.FormEvent) => {
    e.preventDefault();
    if (newFollowUpType === 'Nurturing' && !newFollowUpDate) return;
    if (newFollowUpType === 'AppointmentReminder' && !newAppointmentTime) return;
    if (creatingFollowUp) return;

    setCreatingFollowUp(true);
    try {
      const payload = {
        notes: newFollowUpNotes,
        type: newFollowUpType,
        dueDate: newFollowUpType === 'Nurturing' 
          ? new Date(newFollowUpDate).toISOString() 
          : new Date(newAppointmentTime).toISOString(),
        appointmentTime: newFollowUpType === 'AppointmentReminder' 
          ? new Date(newAppointmentTime).toISOString() 
          : undefined,
        tone: newFollowUpTone
      };

      await api.post(`/api/customers/${customerId}/follow-ups`, payload);
      setNewFollowUpDate('');
      setNewAppointmentTime('');
      setNewFollowUpNotes('');
      setNewFollowUpTone('Default');
      setNewFollowUpType('Nurturing');
      
      // Reload follow-ups
      const fuResp = await api.get<FollowUp[]>(`/api/projects/${projectId}/follow-ups`);
      const filtered = fuResp.data.filter(f => f.customerId === customerId);
      setFollowUps(filtered);
    } catch (err) {
      console.error('Failed to create follow-up', err);
    } finally {
      setCreatingFollowUp(false);
    }
  };

  if (loading) {
    const loadingMarkup = (
      <PhantomLoader loading label="تحميل ملف العميل">
        <div className={styles.customerLoadingShell}>
          <div className={styles.customerLoadingHeader}>
            <div>
              <div className={styles.customerLoadingTitle}>ملف العميل والتفاصيل الأساسية</div>
              <div className={styles.customerLoadingSubtitle}>بيانات التواصل وسجل المحادثات</div>
            </div>
            <div className={styles.customerLoadingAction}>إغلاق</div>
          </div>
          <div className={styles.customerLoadingGrid}>
            <div className={styles.customerLoadingColumn}>
              <div className={styles.customerLoadingField}>اسم العميل</div>
              <div className={styles.customerLoadingField}>رقم الهاتف</div>
              <div className={styles.customerLoadingField}>المدينة والميزانية</div>
              <div className={styles.customerLoadingArea}>ملاحظات العميل وسياق المحادثة</div>
            </div>
            <div className={styles.customerLoadingColumn}>
              <div className={styles.customerLoadingPanel}>ملخص AI ودرجة الاهتمام</div>
              <div className={styles.customerLoadingPanel}>المتابعات القادمة وسجل الإجراءات</div>
            </div>
          </div>
        </div>
      </PhantomLoader>
    );

    if (isInline) {
      return (
        <div className={`glass-panel ${styles.inlineCard}`}>
          {loadingMarkup}
        </div>
      );
    }
    return (
      <div className={styles.backdrop}>
        <div className={`glass-panel ${styles.modal}`}>
          {loadingMarkup}
        </div>
      </div>
    );
  }

  const clampedDisplayScore = Math.min(100, Math.max(0, leadScore));

  const contentMarkup = (
    <div className={`glass-panel ${isInline ? styles.inlineCard : styles.modal}`}>
      {/* Header */}
      <div className={styles.header}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <h2 className={styles.title}>{customer?.name || 'تفاصيل العميل'}</h2>
            {customer?.label && (
              <span className={styles.smartLabelBadge}>{customer.label}</span>
            )}
          </div>
          <p className={styles.subtitle}>{customer?.phoneNumber}</p>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
          {isInline && (
            <button
              type="button"
              onClick={onClose}
              className={styles.backBtn}
            >
              <ArrowRight size={16} style={{ marginLeft: '6px' }} />
              الرجوع للقائمة
            </button>
          )}
          <div onClick={onClose} className={styles.closeBtn}>
            <X size={20} />
          </div>
        </div>
      </div>

        {/* Content Tabs / Split view */}
        <div className={styles.bodyGrid}>
          {/* Left Column: Profile Info Form */}
          <form onSubmit={handleSave} className={styles.formColumn}>
            <h3 className={styles.sectionTitle}>سياق الملف الشخصي</h3>
            
            <div className={styles.formGroup}>
              <label className={styles.label}>الاسم الكامل</label>
              <input 
                type="text" 
                value={name} 
                onChange={(e) => setName(e.target.value)} 
                className={styles.input} 
                required
              />
            </div>

            <div className={styles.formGroup}>
              <label className={styles.label}>تصنيف العميل (Label)</label>
              <input 
                type="text" 
                value={label} 
                onChange={(e) => setLabel(e.target.value)} 
                className={styles.input} 
                placeholder="مثال: استفسار عن السعر، طلب شراء، ترحيب..."
              />
            </div>

            <div className={styles.formRow}>
              <div className={styles.formGroup}>
                <label className={styles.label}>المدينة</label>
                <input 
                  type="text" 
                  value={city} 
                  onChange={(e) => setCity(e.target.value)} 
                  className={styles.input} 
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>الميزانية ($)</label>
                <input 
                  type="number" 
                  step="0.01"
                  value={budget} 
                  onChange={(e) => setBudget(e.target.value)} 
                  className={styles.input} 
                />
              </div>
            </div>

            <div className={styles.formRow}>
              <div className={styles.formGroup}>
                <label className={styles.label}>تقييم الاهتمام (Lead Score)</label>
                <input
                  type="number"
                  min="0"
                  max="100"
                  value={leadScore}
                  onChange={(e) => setLeadScore(Math.min(100, Math.max(0, parseInt(e.target.value) || 0)))}
                  className={styles.input}
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>مرحلة مسار المبيعات (Pipeline Stage)</label>
                <select
                  value={pipelineStage}
                  onChange={(e) => setPipelineStage(e.target.value)}
                  className={styles.select}
                >
                  <option value="New">جديد (New)</option>
                  <option value="Contacted">تم التواصل (Contacted)</option>
                  <option value="Proposal">عرض سعر (Proposal)</option>
                  <option value="Negotiation">تفاوض (Negotiation)</option>
                  <option value="Won">تم البيع (Won)</option>
                  <option value="Lost">خسارة (Lost)</option>
                </select>
              </div>
            </div>

            <div className={styles.formGroup} style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '4px 0 12px 0' }}>
              <input 
                type="checkbox" 
                id="isBlacklistedCheckbox"
                checked={isBlacklisted} 
                onChange={(e) => setIsBlacklisted(e.target.checked)} 
                style={{ width: '18px', height: '18px', cursor: 'pointer', accentColor: 'hsl(var(--accent-primary))' }}
              />
              <label htmlFor="isBlacklistedCheckbox" className={styles.label} style={{ marginBottom: 0, cursor: 'pointer', fontWeight: '500' }}>
                حظر الرد الآلي بالذكاء الاصطناعي (Blacklist)
              </label>
            </div>

            <div className={styles.formGroup}>
              <label className={styles.label}>ملاحظات المحادثة</label>
              <textarea
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                className={styles.textarea}
                rows={4}
              />
            </div>

            {/* Tag manager */}
            <div className={styles.formGroup}>
              <label className={styles.label}>الوسوم والكلمات الدلالية</label>
              <div className={styles.tagInputRow}>
                <input
                  type="text"
                  placeholder="وسم جديد..."
                  value={newTag}
                  onChange={(e) => setNewTag(e.target.value)}
                  className={styles.input}
                  style={{ flexGrow: 1, marginBottom: 0 }}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') {
                      e.preventDefault();
                      handleAddTag();
                    }
                  }}
                />
                <Tooltip content="إضافة وسم جديد لتصنيف العميل" position="top">
                  <button type="button" onClick={handleAddTag} className={styles.addTagBtn} style={{ height: '100%' }}>
                    <Plus size={16} />
                  </button>
                </Tooltip>
              </div>
              <div className={styles.tagCloud}>
                {tags.map(tag => (
                  <span key={tag} className={styles.tag}>
                    <Tag size={12} style={{ marginRight: '4px' }} />
                    {tag}
                    <X size={12} onClick={() => handleRemoveTag(tag)} className={styles.removeTagIcon} />
                  </span>
                ))}
              </div>
            </div>

            <Tooltip content="حفظ التغييرات في ملف العميل الحالي" position="top" style={{ width: '100%' }}>
              <button type="submit" disabled={saving} className={styles.saveBtn} style={{ width: '100%' }}>
                <PhantomLoader loading={saving} label="حفظ التغييرات">
                  <span>حفظ التغييرات</span>
                </PhantomLoader>
              </button>
            </Tooltip>
          </form>

          {/* Right Column: Followups & History */}
          <div className={styles.interactionsColumn}>
            {/* AI intelligence warning score indicators */}
            <div className={styles.scoreIndicatorPanel}>
              <h3 className={styles.sectionTitle}>AI Summary & Profile</h3>
              <div className={styles.scoreCards} style={{ marginBottom: '12px' }}>
                <div className={styles.scoreCard} style={{ border: '1px solid var(--accent)', backgroundColor: 'var(--accent-soft)' }}>
                  <span className={styles.scoreLabel}>Lead Score</span>
                  <span className={styles.scoreVal}>{clampedDisplayScore}/100</span>
                </div>
                <div className={styles.scoreCard} style={{ border: '1px solid rgba(243, 92, 110, 0.25)', backgroundColor: 'var(--accent-secondary-soft)' }}>
                  <span className={styles.scoreLabel}>System Stage</span>
                  <span className={styles.scoreVal}>{pipelineStage}</span>
                </div>
              </div>

              {/* Editable Customer Memory */}
              <form onSubmit={handleSaveMemory} style={{ display: 'flex', flexDirection: 'column', gap: '8px', borderTop: '1px solid rgba(255,255,255,0.06)', paddingTop: '12px' }}>
                <div className={styles.formGroup}>
                  <label className={styles.label}>ملخص العميل (AI Summary)</label>
                  <textarea 
                    value={editableSummary} 
                    onChange={(e) => setEditableSummary(e.target.value)} 
                    className={styles.textarea} 
                    rows={2}
                    placeholder="ملخص طويل المدى لشخصية العميل وطلباته..."
                  />
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label}>الحقائق المكتشفة (Facts - مفصولة بفاصلة)</label>
                  <input 
                    type="text"
                    value={editableFacts} 
                    onChange={(e) => setEditableFacts(e.target.value)} 
                    className={styles.input} 
                    placeholder="مثال: مهتم بالدورة، يفضل التواصل واتساب"
                  />
                </div>
                <div className={styles.formRow}>
                  <div className={styles.formGroup}>
                    <label className={styles.label}>الاعتراضات (Objections)</label>
                    <input 
                      type="text"
                      value={editableObjections} 
                      onChange={(e) => setEditableObjections(e.target.value)} 
                      className={styles.input} 
                      placeholder="السعر مرتفع..."
                    />
                  </div>
                  <div className={styles.formGroup}>
                    <label className={styles.label}>المحفزات (Triggers)</label>
                    <input 
                      type="text"
                      value={editableTriggers} 
                      onChange={(e) => setEditableTriggers(e.target.value)} 
                      className={styles.input} 
                      placeholder="خصم فوري..."
                    />
                  </div>
                </div>
                <div style={{ display: 'flex', gap: '8px', marginTop: '4px' }}>
                  <Tooltip content="حفظ الملخص المكتوب والبيانات الحالية للعميل" position="top" style={{ flex: 1 }}>
                    <button type="submit" disabled={savingMemory || generatingMemory} className={styles.scheduleBtn} style={{ width: '100%', background: 'var(--accent-soft)', borderColor: 'var(--border-strong)', color: 'var(--accent)' }}>
                      <PhantomLoader loading={savingMemory} label="حفظ التعديلات">
                        <span>حفظ التعديلات</span>
                      </PhantomLoader>
                    </button>
                  </Tooltip>
                  <Tooltip content="تحليل المحادثات عبر الذكاء الاصطناعي وتحديث الملخص والسمات تلقائياً" position="top" style={{ flex: 1 }}>
                    <button type="button" onClick={handleGenerateMemory} disabled={generatingMemory || savingMemory} className={styles.scheduleBtn} style={{ width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '6px', background: 'rgba(203, 184, 255, 0.12)', borderColor: 'rgba(203, 184, 255, 0.25)', color: '#CBB8FF' }}>
                      <Sparkles size={14} />
                      <PhantomLoader loading={generatingMemory} label="تحديث ملف العميل بالذكاء الاصطناعي">
                        <span>تحديث ذكي بالـ AI</span>
                      </PhantomLoader>
                    </button>
                  </Tooltip>
                </div>
              </form>
            </div>

            {/* Schedule Followup Form */}
            <div className={styles.followUpFormBox}>
              <h3 className={styles.sectionTitle}>جدولة متابعة / تذكير</h3>
              <form onSubmit={handleAddFollowUp} style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-sm)' }}>
                <div className={styles.formGroup}>
                  <label className={styles.label}>نوع الإجراء</label>
                  <select
                    value={newFollowUpType}
                    onChange={(e) => setNewFollowUpType(e.target.value as any)}
                    className={styles.select}
                  >
                    <option value="Nurturing">متابعة لتنشيط العميل (Nurturing)</option>
                    <option value="AppointmentReminder">تذكير بموعد / كورس (Reminder)</option>
                  </select>
                </div>

                {newFollowUpType === 'Nurturing' ? (
                  <div className={styles.formGroup}>
                    <label className={styles.label}>تاريخ ووقت المتابعة</label>
                    <input 
                      type="datetime-local" 
                      value={newFollowUpDate}
                      onChange={(e) => setNewFollowUpDate(e.target.value)}
                      className={styles.input} 
                      required
                    />
                  </div>
                ) : (
                  <div className={styles.formGroup}>
                    <label className={styles.label}>تاريخ ووقت الكورس / الموعد</label>
                    <input 
                      type="datetime-local" 
                      value={newAppointmentTime}
                      onChange={(e) => setNewAppointmentTime(e.target.value)}
                      className={styles.input} 
                      required
                    />
                    <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))', marginTop: '2px' }}>
                      سيتم إرسال رسالة التذكير تلقائياً قبل هذا الموعد بـ 24 ساعة.
                    </span>
                  </div>
                )}

                <div className={styles.formGroup}>
                  <label className={styles.label}>نبرة المتابعة (Tone)</label>
                  <select
                    value={newFollowUpTone}
                    onChange={(e) => setNewFollowUpTone(e.target.value)}
                    className={styles.select}
                  >
                    <option value="Default">الوضع الافتراضي (Default)</option>
                    <option value="Creative">إبداعي (Creative)</option>
                    <option value="Salesy">سلزجي صايع (Salesy)</option>
                  </select>
                </div>

                <div className={styles.formGroup}>
                  <label className={styles.label}>نص الرسالة / ملاحظات</label>
                  <input 
                    type="text" 
                    placeholder="اكتب رسالة مخصصة أو اتركها فارغة للإرسال التلقائي"
                    value={newFollowUpNotes}
                    onChange={(e) => setNewFollowUpNotes(e.target.value)}
                    className={styles.input}
                  />
                </div>

                <button type="submit" disabled={creatingFollowUp} className={styles.scheduleBtn} style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px' }}>
                  <Calendar size={16} />
                  جدولة المهمة
                </button>
              </form>
            </div>

            {/* Followup History List */}
            <div style={{ flexGrow: 1, overflowY: 'auto' }}>
              <h3 className={styles.sectionTitle}>جدول المتابعات المجدولة ({followUps.length})</h3>
              {followUps.length === 0 ? (
                <div className={styles.emptyFollowUps}>لا توجد متابعات مجدولة</div>
              ) : (
                <div className={styles.followUpList}>
                  {followUps.map(f => (
                    <div key={f.id} className={styles.followUpCard}>
                      <div className={styles.followUpHeader}>
                        <span className={styles.followUpDate}>
                          الإرسال: {new Date(f.dueDate).toLocaleDateString('ar-EG')} {new Date(f.dueDate).toLocaleTimeString('ar-EG', { hour: '2-digit', minute: '2-digit' })}
                        </span>
                        <span className={`${styles.statusBadge} ${
                          f.status === 'Completed' ? styles.statusCompleted : f.status === 'Missed' ? styles.statusMissed : styles.statusPending
                        }`}>
                          {f.status === 'Completed' ? 'مكتملة' : f.status === 'Missed' ? 'فائتة' : 'معلقة'}
                        </span>
                      </div>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '8px', margin: '4px 0' }}>
                        {f.type === 'AppointmentReminder' ? (
                          <span className={styles.statusBadge} style={{
                            backgroundColor: 'rgba(16, 185, 129, 0.12)',
                            color: 'hsl(140, 100%, 65%)',
                            padding: '2px 6px',
                            fontSize: '0.7rem'
                          }}>
                            تذكير بموعد
                          </span>
                        ) : (
                          <span className={styles.statusBadge} style={{
                            backgroundColor: 'var(--accent-soft)',
                            color: 'var(--accent)',
                            padding: '2px 6px',
                            fontSize: '0.7rem'
                          }}>
                            متابعة عميل
                          </span>
                        )}
                        {f.tone && f.tone !== 'Default' && (
                          <span className={styles.statusBadge} style={{
                            backgroundColor: 'rgba(203, 184, 255, 0.12)',
                            color: '#CBB8FF',
                            padding: '2px 6px',
                            fontSize: '0.7rem'
                          }}>
                            {f.tone === 'Creative' ? 'إبداعي' : 'سلزجي صايع'}
                          </span>
                        )}
                        {f.type === 'AppointmentReminder' && f.appointmentTime && (
                          <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))' }}>
                            الموعد: {new Date(f.appointmentTime).toLocaleString('ar-EG')}
                          </span>
                        )}
                      </div>
                      {f.notes && <p className={styles.followUpNotes}>{f.notes}</p>}
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        </div>
    </div>
  );

  if (isInline) {
    return contentMarkup;
  }

  return (
    <div className={styles.backdrop}>
      {contentMarkup}
    </div>
  );
}
