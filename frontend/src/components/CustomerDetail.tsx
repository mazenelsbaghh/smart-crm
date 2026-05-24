import React, { useEffect, useState } from 'react';
import { Customer, crmService } from '../services/crm';
import { api } from '../services/api';
import { X, Plus, Calendar, Tag, ShieldAlert, CheckCircle, Sparkles } from 'lucide-react';

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
    if (!newFollowUpDate || creatingFollowUp) return;

    setCreatingFollowUp(true);
    try {
      await api.post(`/api/customers/${customerId}/follow-ups`, {
        dueDate: new Date(newFollowUpDate).toISOString(),
        notes: newFollowUpNotes,
      });
      setNewFollowUpDate('');
      setNewFollowUpNotes('');
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
      <div style={styles.backdrop}>
        <div className="glass-panel" style={styles.modal}>
          <div style={styles.loadingSpinner}>Loading customer profile...</div>
        </div>
      </div>
    );
  }

  return (
    <div style={styles.backdrop}>
      <div className="glass-panel" style={styles.modal}>
        {/* Header */}
        <div style={styles.header}>
          <div>
            <h2 style={styles.title}>{customer?.name || 'Customer Details'}</h2>
            <p style={styles.subtitle}>{customer?.phoneNumber}</p>
          </div>
          <div onClick={onClose} style={styles.closeBtn}>
            <X size={20} />
          </div>
        </div>

        {/* Content Tabs / Split view */}
        <div style={styles.bodyGrid}>
          {/* Left Column: Profile Info Form */}
          <form onSubmit={handleSave} style={styles.formColumn}>
            <h3 style={styles.sectionTitle}>Profile Context</h3>
            
            <div style={styles.formGroup}>
              <label style={styles.label}>Name</label>
              <input 
                type="text" 
                value={name} 
                onChange={(e) => setName(e.target.value)} 
                style={styles.input} 
                required
              />
            </div>

            <div style={styles.formRow}>
              <div style={styles.formGroup}>
                <label style={styles.label}>City</label>
                <input 
                  type="text" 
                  value={city} 
                  onChange={(e) => setCity(e.target.value)} 
                  style={styles.input} 
                />
              </div>

              <div style={styles.formGroup}>
                <label style={styles.label}>Budget ($)</label>
                <input 
                  type="number" 
                  step="0.01"
                  value={budget} 
                  onChange={(e) => setBudget(e.target.value)} 
                  style={styles.input} 
                />
              </div>
            </div>

            <div style={styles.formRow}>
              <div style={styles.formGroup}>
                <label style={styles.label}>Lead Score</label>
                <input 
                  type="number" 
                  min="0" 
                  max="100"
                  value={leadScore} 
                  onChange={(e) => setLeadScore(parseInt(e.target.value) || 0)} 
                  style={styles.input} 
                />
              </div>

              <div style={styles.formGroup}>
                <label style={styles.label}>Pipeline Stage</label>
                <select 
                  value={pipelineStage} 
                  onChange={(e) => setPipelineStage(e.target.value)} 
                  style={styles.select}
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

            <div style={styles.formGroup}>
              <label style={styles.label}>Conversation Notes</label>
              <textarea 
                value={notes} 
                onChange={(e) => setNotes(e.target.value)} 
                style={styles.textarea} 
                rows={4}
              />
            </div>

            {/* Tag manager */}
            <div style={styles.formGroup}>
              <label style={styles.label}>Tags</label>
              <div style={styles.tagInputRow}>
                <input 
                  type="text" 
                  placeholder="New tag..." 
                  value={newTag}
                  onChange={(e) => setNewTag(e.target.value)}
                  style={{ ...styles.input, flexGrow: 1, marginBottom: 0 }}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') {
                      e.preventDefault();
                      handleAddTag();
                    }
                  }}
                />
                <button type="button" onClick={handleAddTag} style={styles.addTagBtn}>
                  <Plus size={16} />
                </button>
              </div>
              <div style={styles.tagCloud}>
                {tags.map(tag => (
                  <span key={tag} style={styles.tag}>
                    <Tag size={12} style={{ marginRight: '4px' }} />
                    {tag}
                    <X size={12} onClick={() => handleRemoveTag(tag)} style={styles.removeTagIcon} />
                  </span>
                ))}
              </div>
            </div>

            <button type="submit" disabled={saving} style={styles.saveBtn}>
              {saving ? 'Saving changes...' : 'Save Context'}
            </button>
          </form>

          {/* Right Column: Followups & History */}
          <div style={styles.interactionsColumn}>
            {/* AI intelligence warning score indicators */}
            <div style={styles.scoreIndicatorPanel}>
              <h3 style={styles.sectionTitle}>AI Summary</h3>
              <div style={styles.scoreCards}>
                <div style={{...styles.scoreCard, borderLeft: '4px solid hsl(var(--accent-primary))'}}>
                  <span style={styles.scoreLabel}>Lead Score</span>
                  <span style={styles.scoreVal}>{leadScore}/100</span>
                </div>
                <div style={{...styles.scoreCard, borderLeft: '4px solid hsl(var(--accent-secondary))'}}>
                  <span style={styles.scoreLabel}>System Stage</span>
                  <span style={styles.scoreVal}>{pipelineStage}</span>
                </div>
              </div>
            </div>

            {/* Schedule Followup Form */}
            <div style={styles.followUpFormBox}>
              <h3 style={styles.sectionTitle}>Schedule Follow-up</h3>
              <form onSubmit={handleAddFollowUp} style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-sm)' }}>
                <input 
                  type="datetime-local" 
                  value={newFollowUpDate}
                  onChange={(e) => setNewFollowUpDate(e.target.value)}
                  style={styles.input} 
                  required
                />
                <input 
                  type="text" 
                  placeholder="Notes (e.g. Call to discuss proposal)"
                  value={newFollowUpNotes}
                  onChange={(e) => setNewFollowUpNotes(e.target.value)}
                  style={styles.input}
                />
                <button type="submit" disabled={creatingFollowUp} style={styles.scheduleBtn}>
                  <Calendar size={16} style={{ marginRight: '6px' }} />
                  Schedule Task
                </button>
              </form>
            </div>

            {/* Followup History List */}
            <div style={{ flexGrow: 1, overflowY: 'auto' }}>
              <h3 style={styles.sectionTitle}>Scheduled Follow-ups ({followUps.length})</h3>
              {followUps.length === 0 ? (
                <div style={styles.emptyFollowUps}>No follow-ups scheduled</div>
              ) : (
                <div style={styles.followUpList}>
                  {followUps.map(f => (
                    <div key={f.id} style={styles.followUpCard}>
                      <div style={styles.followUpHeader}>
                        <span style={styles.followUpDate}>
                          {new Date(f.dueDate).toLocaleDateString()} {new Date(f.dueDate).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}
                        </span>
                        <span style={{
                          ...styles.statusBadge,
                          ...(f.status === 'Completed' ? styles.statusCompleted : f.status === 'Missed' ? styles.statusMissed : styles.statusPending)
                        }}>
                          {f.status}
                        </span>
                      </div>
                      {f.notes && <p style={styles.followUpNotes}>{f.notes}</p>}
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

const styles: Record<string, React.CSSProperties> = {
  backdrop: {
    position: 'fixed',
    top: 0,
    left: 0,
    width: '100vw',
    height: '100vh',
    backgroundColor: 'rgba(0, 0, 0, 0.6)',
    backdropFilter: 'blur(6px)',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    zIndex: 100,
    animation: 'fadeIn 0.2s ease',
  },
  modal: {
    width: '900px',
    maxWidth: '95vw',
    maxHeight: '90vh',
    borderRadius: 'var(--radius-lg)',
    backgroundColor: 'hsl(var(--bg-secondary))',
    border: '1px solid rgba(255, 255, 255, 0.08)',
    display: 'flex',
    flexDirection: 'column',
    boxShadow: '0 20px 50px -10px rgba(0,0,0,0.8)',
    overflow: 'hidden',
  },
  loadingSpinner: {
    padding: 'var(--space-xl)',
    textAlign: 'center',
    color: 'hsl(var(--text-secondary))',
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: 'var(--space-lg)',
    borderBottom: '1px solid rgba(255, 255, 255, 0.05)',
  },
  title: {
    margin: 0,
    fontSize: '1.4rem',
    fontWeight: 800,
    color: 'hsl(var(--text-primary))',
  },
  subtitle: {
    margin: 'var(--space-xs) 0 0 0',
    fontSize: '0.875rem',
    color: 'hsl(var(--text-muted))',
  },
  closeBtn: {
    cursor: 'pointer',
    color: 'hsl(var(--text-secondary))',
    padding: 'var(--space-xs)',
    borderRadius: 'var(--radius-full)',
    transition: 'var(--transition-fast)',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  bodyGrid: {
    display: 'grid',
    gridTemplateColumns: '1.2fr 1fr',
    gap: 'var(--space-lg)',
    padding: 'var(--space-lg)',
    overflowY: 'auto',
    flexGrow: 1,
  },
  formColumn: {
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-md)',
  },
  interactionsColumn: {
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-md)',
    borderLeft: '1px solid rgba(255, 255, 255, 0.05)',
    paddingLeft: 'var(--space-lg)',
  },
  sectionTitle: {
    margin: '0 0 var(--space-xs) 0',
    fontSize: '0.95rem',
    fontWeight: 700,
    color: 'hsl(var(--text-secondary))',
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
  },
  formGroup: {
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-xs)',
  },
  formRow: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: 'var(--space-md)',
  },
  label: {
    fontSize: '0.8rem',
    fontWeight: 600,
    color: 'hsl(var(--text-muted))',
  },
  input: {
    backgroundColor: 'rgba(255, 255, 255, 0.02)',
    border: '1px solid rgba(255, 255, 255, 0.08)',
    borderRadius: 'var(--radius-md)',
    color: 'hsl(var(--text-primary))',
    padding: 'var(--space-sm) var(--space-md)',
    fontSize: '0.9rem',
    outline: 'none',
    transition: 'var(--transition-normal)',
  },
  select: {
    backgroundColor: 'rgba(255, 255, 255, 0.02)',
    border: '1px solid rgba(255, 255, 255, 0.08)',
    borderRadius: 'var(--radius-md)',
    color: 'hsl(var(--text-primary))',
    padding: 'var(--space-sm) var(--space-md)',
    fontSize: '0.9rem',
    outline: 'none',
    cursor: 'pointer',
  },
  textarea: {
    backgroundColor: 'rgba(255, 255, 255, 0.02)',
    border: '1px solid rgba(255, 255, 255, 0.08)',
    borderRadius: 'var(--radius-md)',
    color: 'hsl(var(--text-primary))',
    padding: 'var(--space-sm) var(--space-md)',
    fontSize: '0.9rem',
    outline: 'none',
    resize: 'vertical',
  },
  tagInputRow: {
    display: 'flex',
    gap: 'var(--space-xs)',
  },
  addTagBtn: {
    backgroundColor: 'rgba(99, 102, 241, 0.15)',
    border: '1px solid rgba(99, 102, 241, 0.3)',
    borderRadius: 'var(--radius-md)',
    color: 'hsl(var(--accent-primary))',
    padding: '0 var(--space-md)',
    cursor: 'pointer',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  tagCloud: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: 'var(--space-xs)',
    marginTop: 'var(--space-xs)',
  },
  tag: {
    display: 'flex',
    alignItems: 'center',
    backgroundColor: 'rgba(255, 255, 255, 0.04)',
    border: '1px solid rgba(255, 255, 255, 0.08)',
    borderRadius: 'var(--radius-full)',
    padding: '3px 8px',
    fontSize: '0.75rem',
    color: 'hsl(var(--text-secondary))',
  },
  removeTagIcon: {
    cursor: 'pointer',
    marginLeft: '6px',
    color: 'hsl(var(--text-muted))',
  },
  saveBtn: {
    backgroundColor: 'hsl(var(--accent-primary))',
    color: 'white',
    border: 'none',
    borderRadius: 'var(--radius-md)',
    padding: 'var(--space-md)',
    fontSize: '0.95rem',
    fontWeight: 700,
    cursor: 'pointer',
    marginTop: 'var(--space-md)',
    boxShadow: '0 4px 14px 0 rgba(99, 102, 241, 0.4)',
    transition: 'var(--transition-normal)',
  },
  scoreIndicatorPanel: {
    backgroundColor: 'rgba(255,255,255,0.01)',
    border: '1px solid rgba(255,255,255,0.04)',
    borderRadius: 'var(--radius-md)',
    padding: 'var(--space-md)',
  },
  scoreCards: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: 'var(--space-md)',
  },
  scoreCard: {
    backgroundColor: 'rgba(255,255,255,0.02)',
    padding: 'var(--space-sm) var(--space-md)',
    borderRadius: 'var(--radius-sm)',
    display: 'flex',
    flexDirection: 'column',
  },
  scoreLabel: {
    fontSize: '0.75rem',
    color: 'hsl(var(--text-muted))',
    fontWeight: 600,
  },
  scoreVal: {
    fontSize: '1.25rem',
    fontWeight: 800,
    color: 'hsl(var(--text-primary))',
  },
  followUpFormBox: {
    backgroundColor: 'rgba(255,255,255,0.01)',
    border: '1px solid rgba(255,255,255,0.04)',
    borderRadius: 'var(--radius-md)',
    padding: 'var(--space-md)',
  },
  scheduleBtn: {
    backgroundColor: 'rgba(255,255,255,0.04)',
    border: '1px solid rgba(255,255,255,0.08)',
    borderRadius: 'var(--radius-md)',
    color: 'hsl(var(--text-primary))',
    padding: 'var(--space-sm) var(--space-md)',
    fontSize: '0.9rem',
    fontWeight: 600,
    cursor: 'pointer',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    transition: 'var(--transition-fast)',
  },
  emptyFollowUps: {
    padding: 'var(--space-md)',
    textAlign: 'center',
    color: 'hsl(var(--text-muted))',
    fontSize: '0.875rem',
    backgroundColor: 'rgba(255, 255, 255, 0.01)',
    borderRadius: 'var(--radius-md)',
    border: '1px dashed rgba(255, 255, 255, 0.08)',
  },
  followUpList: {
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-sm)',
  },
  followUpCard: {
    backgroundColor: 'rgba(255, 255, 255, 0.02)',
    border: '1px solid rgba(255, 255, 255, 0.06)',
    borderRadius: 'var(--radius-md)',
    padding: 'var(--space-sm) var(--space-md)',
  },
  followUpHeader: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 'var(--space-xs)',
  },
  followUpDate: {
    fontSize: '0.75rem',
    color: 'hsl(var(--text-secondary))',
    fontWeight: 600,
  },
  statusBadge: {
    fontSize: '0.65rem',
    padding: '2px 6px',
    borderRadius: 'var(--radius-full)',
    fontWeight: 700,
    textTransform: 'uppercase',
  },
  statusPending: {
    backgroundColor: 'rgba(245, 158, 11, 0.15)',
    color: 'hsl(20, 100%, 60%)',
  },
  statusCompleted: {
    backgroundColor: 'rgba(16, 185, 129, 0.15)',
    color: 'hsl(140, 100%, 60%)',
  },
  statusMissed: {
    backgroundColor: 'rgba(239, 68, 68, 0.15)',
    color: 'hsl(0, 100%, 65%)',
  },
  followUpNotes: {
    margin: 0,
    fontSize: '0.85rem',
    color: 'hsl(var(--text-muted))',
  },
};
