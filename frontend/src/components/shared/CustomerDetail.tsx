import React, { useCallback, useEffect, useRef, useState } from 'react';
import axios from 'axios';
import { Customer, crmService } from '../../services/crm';
import { api } from '../../services/api';
import { X, Plus, Calendar, Tag, Sparkles, ArrowRight } from 'lucide-react';
import { useToast } from '../../context/toast-context';
import Tooltip from './Tooltip';
import PhantomLoader from './PhantomLoader';
import { isolateModal } from './modal-accessibility';
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
  const [loadError, setLoadError] = useState(false);
  const [saving, setSaving] = useState(false);
  const [followUps, setFollowUps] = useState<FollowUp[]>([]);
  
  // Form fields
  const [name, setName] = useState('');
  const [city, setCity] = useState('');
  const [leadScore, setLeadScore] = useState(0);
  const [notes, setNotes] = useState('');
  const [tags, setTags] = useState<string[]>([]);
  const [label, setLabel] = useState('');
  const [isBlacklisted, setIsBlacklisted] = useState(false);

  // AI Memory / Profile fields
  const [editableSummary, setEditableSummary] = useState('');
  const [editableFacts, setEditableFacts] = useState('');
  const [editableTriggers, setEditableTriggers] = useState('');
  const [editableObjections, setEditableObjections] = useState('');
  const [savingMemory, setSavingMemory] = useState(false);
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
  const modalRef = useRef<HTMLDivElement>(null);
  const overlayRef = useRef<HTMLDivElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const onCloseRef = useRef(onClose);

  useEffect(() => {
    onCloseRef.current = onClose;
  }, [onClose]);

  const fetchCustomerData = useCallback(async () => {
    try {
      setLoading(true);
      setLoadError(false);
      const data = await crmService.getCustomer(customerId);
      setCustomer(data);
      setName(data.name || '');
      setCity(data.city || '');
      setLeadScore(data.leadScore || 0);
      setNotes(data.notes || '');
      setTags(data.tags || []);
      setLabel(data.label || '');
      setIsBlacklisted(data.isBlacklisted || false);

      // Fetch followups
      const fuResp = await api.get<FollowUp[]>(`/api/projects/${projectId}/follow-ups`);
      const filtered = fuResp.data.filter(f => f.customerId === customerId);
      setFollowUps(filtered);

      // Fetch AI Customer Memory
      try {
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

        }
      } catch (memoryError: unknown) {
        if (axios.isAxiosError(memoryError) && memoryError.response?.status === 404) {
          setEditableSummary('');
          setEditableFacts('');
          setEditableTriggers('');
          setEditableObjections('');
        } else {
          console.error('Failed to load customer memory', memoryError);
          showToast('تعذر تحميل ملخص الذكاء الاصطناعي، وبقية بيانات العميل ما زالت متاحة.', 'warning');
        }
      }

    } catch (e) {
      console.error('Error loading customer detail data', e);
      setLoadError(true);
    } finally {
      setLoading(false);
    }
  }, [customerId, projectId, showToast]);

  useEffect(() => {
    const timer = window.setTimeout(() => void fetchCustomerData(), 0);
    return () => window.clearTimeout(timer);
  }, [fetchCustomerData]);

  useEffect(() => {
    if (isInline) return;
    const previouslyFocused = document.activeElement as HTMLElement | null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    closeButtonRef.current?.focus();
    const restoreIsolation = overlayRef.current ? isolateModal(overlayRef.current) : () => undefined;

    const trapCustomerDialogKeyboard = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        onCloseRef.current();
        return;
      }
      if (event.key !== 'Tab') return;
      const focusable = Array.from(
        modalRef.current?.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), textarea:not(:disabled), select:not(:disabled), [href], [tabindex]:not([tabindex="-1"])') ?? [],
      );
      if (focusable.length === 0) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener('keydown', trapCustomerDialogKeyboard);
    return () => {
      document.removeEventListener('keydown', trapCustomerDialogKeyboard);
      document.body.style.overflow = previousOverflow;
      restoreIsolation();
      previouslyFocused?.focus();
    };
  }, [isInline, loadError, loading]);

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
        label,
        isBlacklisted,
      });
      showToast('تم حفظ بيانات العميل.', 'success');
      onUpdate();
      onClose();
    } catch (err) {
      console.error('Failed to save customer updates', err);
      showToast('تعذر حفظ بيانات العميل. راجع الاتصال ثم أعد المحاولة.', 'error');
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
        showToast('تم تحديث وتوليد ملف التعريف بالذكاء الاصطناعي بنجاح.', 'success');
      }
    } catch (err: unknown) {
      console.error('Failed to generate customer profile', err);
      const errMsg = getRequestError(err, 'فشل توليد ملف التعريف. تأكد من وجود رسائل سابقة للعميل.');
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
      showToast('تمت جدولة المتابعة.', 'success');
    } catch (err) {
      console.error('Failed to create follow-up', err);
      showToast('تعذر جدولة المتابعة. لم يتم حفظ المهمة.', 'error');
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
            <button ref={closeButtonRef} type="button" className={styles.customerLoadingAction} onClick={onClose}>إغلاق</button>
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
        <div className={styles.inlineCard} aria-busy="true">
          {loadingMarkup}
        </div>
      );
    }
    return (
      <div ref={overlayRef} className={styles.backdrop}>
        <div ref={modalRef} className={styles.modal} role="dialog" aria-modal="true" aria-label="تحميل ملف العميل" aria-busy="true">
          {loadingMarkup}
        </div>
      </div>
    );
  }

  if (loadError) {
    const errorState = (
      <div
        ref={isInline ? undefined : modalRef}
        className={styles.customerError}
        role={isInline ? 'alert' : 'dialog'}
        aria-modal={isInline ? undefined : true}
        aria-labelledby="customer-error-title"
        aria-describedby="customer-error-description"
      >
        <h2 id="customer-error-title">تعذر تحميل ملف العميل</h2>
        <p id="customer-error-description">تحقق من الاتصال ثم أعد المحاولة. لم يتم تغيير أي بيانات.</p>
        <div>
          <button type="button" className={styles.scheduleBtn} onClick={() => void fetchCustomerData()}>إعادة المحاولة</button>
          <button ref={isInline ? undefined : closeButtonRef} type="button" className={styles.backBtn} onClick={onClose}>إغلاق</button>
        </div>
      </div>
    );
    return isInline ? errorState : <div ref={overlayRef} className={styles.backdrop}>{errorState}</div>;
  }

  const clampedDisplayScore = Math.min(100, Math.max(0, leadScore));

  const contentMarkup = (
    <div
      ref={modalRef}
      className={isInline ? styles.inlineCard : styles.modal}
      role={isInline ? 'region' : 'dialog'}
      aria-modal={isInline ? undefined : true}
      aria-labelledby="customer-detail-title"
    >
      {/* Header */}
      <div className={styles.header}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <h2 id="customer-detail-title" className={styles.title}>{customer?.name || 'تفاصيل العميل'}</h2>
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
          <button ref={closeButtonRef} type="button" onClick={onClose} className={styles.closeBtn} aria-label="إغلاق ملف العميل">
            <X size={20} aria-hidden="true" />
          </button>
        </div>
      </div>

        {/* Content Tabs / Split view */}
        <div className={styles.bodyGrid}>
          {/* Left Column: Profile Info Form */}
          <form onSubmit={handleSave} className={styles.formColumn}>
            <h3 className={styles.sectionTitle}>سياق الملف الشخصي</h3>
            
            <div className={styles.formGroup}>
              <label htmlFor="customer-detail-name" className={styles.label}>الاسم الكامل</label>
              <input 
                id="customer-detail-name"
                type="text" 
                value={name} 
                onChange={(e) => setName(e.target.value)} 
                className={styles.input} 
                required
              />
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
                حظر الرد الآلي بالذكاء الاصطناعي
              </label>
            </div>

            <div className={styles.formGroup}>
              <label htmlFor="customer-detail-label" className={styles.label}>تصنيف العميل</label>
              <input 
                id="customer-detail-label"
                type="text" 
                value={label} 
                onChange={(e) => setLabel(e.target.value)} 
                className={styles.input} 
                placeholder="مثال: استفسار عن السعر، طلب شراء، ترحيب..."
              />
            </div>

            <div className={styles.formRow}>
              <div className={styles.formGroup}>
                <label htmlFor="customer-detail-city" className={styles.label}>المدينة</label>
                <input 
                  id="customer-detail-city"
                  type="text" 
                  value={city} 
                  onChange={(e) => setCity(e.target.value)} 
                  className={styles.input} 
                />
              </div>

              <div className={styles.formGroup}>
                <label htmlFor="customer-detail-score" className={styles.label}>تقييم الاهتمام من 100</label>
                <input
                  id="customer-detail-score"
                  type="number"
                  min="0"
                  max="100"
                  value={leadScore}
                  onChange={(e) => setLeadScore(Math.min(100, Math.max(0, parseInt(e.target.value) || 0)))}
                  className={styles.input}
                />
              </div>
            </div>

            <div className={styles.formGroup}>
              <label htmlFor="customer-detail-notes" className={styles.label}>ملاحظات المحادثة</label>
              <textarea
                id="customer-detail-notes"
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                className={styles.textarea}
                rows={4}
              />
            </div>

            {/* Tag manager */}
            <div className={styles.formGroup}>
              <label htmlFor="customer-detail-tag" className={styles.label}>الوسوم والكلمات الدلالية</label>
              <div className={styles.tagInputRow}>
                <input
                  id="customer-detail-tag"
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
                  <button type="button" onClick={handleAddTag} className={styles.addTagBtn} style={{ height: '100%' }} aria-label="إضافة الوسم">
                    <Plus size={16} />
                  </button>
                </Tooltip>
              </div>
              <div className={styles.tagCloud}>
                {tags.map(tag => (
                  <span key={tag} className={styles.tag}>
                    <Tag size={12} style={{ marginRight: '4px' }} />
                    {tag}
                    <button type="button" onClick={() => handleRemoveTag(tag)} className={styles.removeTagButton} aria-label={`إزالة الوسم ${tag}`}><X size={12} aria-hidden="true" /></button>
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
              <h3 className={styles.sectionTitle}>ملخص العميل ودرجة الاهتمام</h3>
              <div className={styles.scoreCards} style={{ marginBottom: '12px' }}>
                <div className={styles.scoreCard} style={{ border: '1px solid var(--accent)', backgroundColor: 'var(--accent-soft)', width: '100%' }}>
                  <span className={styles.scoreLabel}>درجة الاهتمام</span>
                  <span className={styles.scoreVal}>{clampedDisplayScore}/100</span>
                </div>
              </div>

              {/* Editable Customer Memory */}
              <form onSubmit={handleSaveMemory} style={{ display: 'flex', flexDirection: 'column', gap: '8px', borderTop: '1px solid rgba(255,255,255,0.06)', paddingTop: '12px' }}>
                <div className={styles.formGroup}>
                  <label htmlFor="customer-memory-summary" className={styles.label}>ملخص العميل بالذكاء الاصطناعي</label>
                  <textarea 
                    id="customer-memory-summary"
                    value={editableSummary} 
                    onChange={(e) => setEditableSummary(e.target.value)} 
                    className={styles.textarea} 
                    rows={2}
                    placeholder="ملخص طويل المدى لشخصية العميل وطلباته..."
                  />
                </div>
                <div className={styles.formGroup}>
                  <label htmlFor="customer-memory-facts" className={styles.label}>الحقائق المكتشفة، مفصولة بفاصلة</label>
                  <input 
                    id="customer-memory-facts"
                    type="text"
                    value={editableFacts} 
                    onChange={(e) => setEditableFacts(e.target.value)} 
                    className={styles.input} 
                    placeholder="مثال: مهتم بالدورة، يفضل التواصل واتساب"
                  />
                </div>
                <div className={styles.formRow}>
                  <div className={styles.formGroup}>
                    <label htmlFor="customer-memory-objections" className={styles.label}>الاعتراضات</label>
                    <input 
                      id="customer-memory-objections"
                      type="text"
                      value={editableObjections} 
                      onChange={(e) => setEditableObjections(e.target.value)} 
                      className={styles.input} 
                      placeholder="السعر مرتفع..."
                    />
                  </div>
                  <div className={styles.formGroup}>
                    <label htmlFor="customer-memory-triggers" className={styles.label}>المحفزات</label>
                    <input 
                      id="customer-memory-triggers"
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
                        <span>تحديث بالذكاء الاصطناعي</span>
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
                  <label htmlFor="customer-follow-up-type" className={styles.label}>نوع الإجراء</label>
                  <select
                    id="customer-follow-up-type"
                    value={newFollowUpType}
                    onChange={(e) => setNewFollowUpType(e.target.value as 'Nurturing' | 'AppointmentReminder')}
                    className={styles.select}
                  >
                    <option value="Nurturing">متابعة لتنشيط العميل</option>
                    <option value="AppointmentReminder">تذكير بموعد أو كورس</option>
                  </select>
                </div>

                {newFollowUpType === 'Nurturing' ? (
                  <div className={styles.formGroup}>
                    <label htmlFor="customer-follow-up-date" className={styles.label}>تاريخ ووقت المتابعة</label>
                    <input 
                      id="customer-follow-up-date"
                      type="datetime-local" 
                      value={newFollowUpDate}
                      onChange={(e) => setNewFollowUpDate(e.target.value)}
                      className={styles.input} 
                      required
                    />
                  </div>
                ) : (
                  <div className={styles.formGroup}>
                    <label htmlFor="customer-appointment-date" className={styles.label}>تاريخ ووقت الكورس أو الموعد</label>
                    <input 
                      id="customer-appointment-date"
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
                  <label htmlFor="customer-follow-up-tone" className={styles.label}>نبرة المتابعة</label>
                  <select
                    id="customer-follow-up-tone"
                    value={newFollowUpTone}
                    onChange={(e) => setNewFollowUpTone(e.target.value)}
                    className={styles.select}
                  >
                    <option value="Default">النبرة الافتراضية</option>
                    <option value="Creative">إبداعية</option>
                    <option value="Salesy">مبيعات مباشرة</option>
                  </select>
                </div>

                <div className={styles.formGroup}>
                  <label htmlFor="customer-follow-up-notes" className={styles.label}>نص الرسالة أو الملاحظات</label>
                  <input 
                    id="customer-follow-up-notes"
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
                            {f.tone === 'Creative' ? 'إبداعية' : 'مبيعات مباشرة'}
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
    <div ref={overlayRef} className={styles.backdrop} onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
      {contentMarkup}
    </div>
  );
}

function getRequestError(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null) {
    const response = (error as { response?: { data?: unknown } }).response;
    if (typeof response?.data === 'string') return response.data;
    if (typeof response?.data === 'object' && response.data !== null) {
      const message = (response.data as { message?: string; error?: string }).message
        ?? (response.data as { message?: string; error?: string }).error;
      if (message) return message;
    }
  }
  return fallback;
}
