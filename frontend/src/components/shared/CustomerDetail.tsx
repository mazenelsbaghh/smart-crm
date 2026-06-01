import React, { useEffect, useState } from 'react';
import { Customer, crmService } from '../../services/crm';
import { api } from '../../services/api';
import { X, Plus, Calendar, Tag } from 'lucide-react';
import styles from './customer-detail.module.css';

interface CustomerDetailProps {
  customerId: string;
  projectId: string;
  onClose: () => void;
  onUpdate: () => void;
}

interface FollowUp {
  id: string;
  customerId: string;
  dueDate: string;
  status: 'Pending' | 'Completed' | 'Missed';
  notes: string;
  type?: 'Nurturing' | 'AppointmentReminder';
  appointmentTime?: string;
}

export default function CustomerDetail({ customerId, projectId, onClose, onUpdate }: CustomerDetailProps) {
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
  
  // Follow-up form
  const [newFollowUpDate, setNewFollowUpDate] = useState('');
  const [newFollowUpNotes, setNewFollowUpNotes] = useState('');
  const [creatingFollowUp, setCreatingFollowUp] = useState(false);
  const [newFollowUpType, setNewFollowUpType] = useState<'Nurturing' | 'AppointmentReminder'>('Nurturing');
  const [newAppointmentTime, setNewAppointmentTime] = useState('');

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

      // Fetch followups
      const fuResp = await api.get<FollowUp[]>(`/api/projects/${projectId}/follow-ups`);
      const filtered = fuResp.data.filter(f => f.customerId === customerId);
      setFollowUps(filtered);
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
        leadScore,
        notes,
        tags,
        budget: budget ? parseFloat(budget) : null,
        pipelineStage,
      });
      onUpdate();
      onClose();
    } catch (err) {
      console.error('Failed to save customer updates', err);
    } finally {
      setSaving(false);
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
          : undefined
      };

      await api.post(`/api/customers/${customerId}/follow-ups`, payload);
      setNewFollowUpDate('');
      setNewAppointmentTime('');
      setNewFollowUpNotes('');
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
    return (
      <div className={styles.backdrop}>
        <div className={`glass-panel ${styles.modal}`}>
          <div className={styles.loadingSpinner}>Loading customer profile...</div>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.backdrop}>
      <div className={`glass-panel ${styles.modal}`}>
        {/* Header */}
        <div className={styles.header}>
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <h2 className={styles.title}>{customer?.name || 'Customer Details'}</h2>
              {customer?.label && (
                <span className={styles.smartLabelBadge}>{customer.label}</span>
              )}
            </div>
            <p className={styles.subtitle}>{customer?.phoneNumber}</p>
          </div>
          <div onClick={onClose} className={styles.closeBtn}>
            <X size={20} />
          </div>
        </div>

        {/* Content Tabs / Split view */}
        <div className={styles.bodyGrid}>
          {/* Left Column: Profile Info Form */}
          <form onSubmit={handleSave} className={styles.formColumn}>
            <h3 className={styles.sectionTitle}>Profile Context</h3>
            
            <div className={styles.formGroup}>
              <label className={styles.label}>Name</label>
              <input 
                type="text" 
                value={name} 
                onChange={(e) => setName(e.target.value)} 
                className={styles.input} 
                required
              />
            </div>

            <div className={styles.formRow}>
              <div className={styles.formGroup}>
                <label className={styles.label}>City</label>
                <input 
                  type="text" 
                  value={city} 
                  onChange={(e) => setCity(e.target.value)} 
                  className={styles.input} 
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>Budget ($)</label>
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
                <label className={styles.label}>Lead Score</label>
                <input 
                  type="number" 
                  min="0" 
                  max="100"
                  value={leadScore} 
                  onChange={(e) => setLeadScore(parseInt(e.target.value) || 0)} 
                  className={styles.input} 
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>Pipeline Stage</label>
                <select 
                  value={pipelineStage} 
                  onChange={(e) => setPipelineStage(e.target.value)} 
                  className={styles.select}
                >
                  <option value="New">New</option>
                  <option value="Contacted">Contacted</option>
                  <option value="Proposal">Proposal</option>
                  <option value="Negotiation">Negotiation</option>
                  <option value="Won">Won</option>
                  <option value="Lost">Lost</option>
                </select>
              </div>
            </div>

            <div className={styles.formGroup}>
              <label className={styles.label}>Conversation Notes</label>
              <textarea 
                value={notes} 
                onChange={(e) => setNotes(e.target.value)} 
                className={styles.textarea} 
                rows={4}
              />
            </div>

            {/* Tag manager */}
            <div className={styles.formGroup}>
              <label className={styles.label}>Tags</label>
              <div className={styles.tagInputRow}>
                <input 
                  type="text" 
                  placeholder="New tag..." 
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
                <button type="button" onClick={handleAddTag} className={styles.addTagBtn}>
                  <Plus size={16} />
                </button>
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

            <button type="submit" disabled={saving} className={styles.saveBtn}>
              {saving ? 'Saving changes...' : 'Save Context'}
            </button>
          </form>

          {/* Right Column: Followups & History */}
          <div className={styles.interactionsColumn}>
            {/* AI intelligence warning score indicators */}
            <div className={styles.scoreIndicatorPanel}>
              <h3 className={styles.sectionTitle}>AI Summary</h3>
              <div className={styles.scoreCards}>
                <div className={styles.scoreCard} style={{ borderLeft: '4px solid hsl(var(--accent-primary))' }}>
                  <span className={styles.scoreLabel}>Lead Score</span>
                  <span className={styles.scoreVal}>{leadScore}/100</span>
                </div>
                <div className={styles.scoreCard} style={{ borderLeft: '4px solid hsl(var(--accent-secondary))' }}>
                  <span className={styles.scoreLabel}>System Stage</span>
                  <span className={styles.scoreVal}>{pipelineStage}</span>
                </div>
              </div>
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
                            backgroundColor: 'rgba(99, 102, 241, 0.12)',
                            color: 'hsl(239, 84%, 75%)',
                            padding: '2px 6px',
                            fontSize: '0.7rem'
                          }}>
                            متابعة عميل
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
    </div>
  );
}
