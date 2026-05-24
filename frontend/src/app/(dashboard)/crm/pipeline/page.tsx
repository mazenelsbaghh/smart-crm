'use client';

import React, { useEffect, useState } from 'react';
import { useAuth } from '../../../../context/auth-context';
import { crmService, Deal, PipelineStage, Customer } from '../../../../services/crm';
import { 
  ChevronLeft, 
  ChevronRight, 
  DollarSign, 
  TrendingUp, 
  Plus, 
  Check, 
  X as XIcon, 
  User,
  Sparkles,
  AlertCircle
} from 'lucide-react';

export default function PipelinePage() {
  const { activeProject } = useAuth();
  
  const [stages, setStages] = useState<PipelineStage[]>([]);
  const [deals, setDeals] = useState<Deal[]>([]);
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [loading, setLoading] = useState(true);
  
  // Deal creation form
  const [showAddDeal, setShowAddDeal] = useState<string | null>(null); // holds stageId where form is opened
  const [dealTitle, setDealTitle] = useState('');
  const [dealAmount, setDealAmount] = useState('');
  const [dealCustomerId, setDealCustomerId] = useState('');

  const fetchPipelineData = async () => {
    if (!activeProject) return;
    try {
      setLoading(true);
      const stageData = await crmService.getPipelineStages(activeProject.id);
      const dealData = await crmService.getDeals(activeProject.id);
      const custData = await crmService.getCustomers(activeProject.id);
      
      setStages(stageData);
      setDeals(dealData);
      setCustomers(custData);
    } catch (e) {
      console.error('Failed to load pipeline context', e);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchPipelineData();
  }, [activeProject]);

  const moveDeal = async (dealId: string, direction: 'prev' | 'next', currentStageId: string) => {
    const currentIndex = stages.findIndex(s => s.id === currentStageId);
    if (currentIndex === -1) return;

    let targetIndex = direction === 'next' ? currentIndex + 1 : currentIndex - 1;
    if (targetIndex < 0 || targetIndex >= stages.length) return;

    const targetStage = stages[targetIndex];
    try {
      // Optimistic UI update
      setDeals(prev => prev.map(d => d.id === dealId ? { ...d, pipelineStageId: targetStage.id } : d));
      await crmService.updateDealStage(dealId, targetStage.id);
    } catch (e) {
      console.error('Failed to update deal stage on backend', e);
      // Revert on error
      fetchPipelineData();
    }
  };

  const handleUpdateStatus = async (dealId: string, status: 0 | 1 | 2) => {
    try {
      // Optimistic update
      setDeals(prev => prev.map(d => d.id === dealId ? { ...d, status } : d));
      await crmService.updateDealStatus(dealId, status);
    } catch (e) {
      console.error('Failed to update deal status', e);
      fetchPipelineData();
    }
  };

  const handleAddDealSubmit = async (e: React.FormEvent, stageId: string) => {
    e.preventDefault();
    if (!activeProject || !dealTitle || !dealCustomerId) return;

    try {
      await crmService.createDeal(activeProject.id, {
        title: dealTitle,
        amount: dealAmount ? parseFloat(dealAmount) : 0,
        customerId: dealCustomerId,
        pipelineStageId: stageId
      });
      setDealTitle('');
      setDealAmount('');
      setDealCustomerId('');
      setShowAddDeal(null);
      
      // Refresh list
      const dealData = await crmService.getDeals(activeProject.id);
      setDeals(dealData);
    } catch (err) {
      console.error('Failed to create deal', err);
    }
  };

  if (loading) {
    return (
      <div style={styles.loadingBox}>
        <div style={styles.spinner}></div>
        <p>Loading pipeline deal workflow...</p>
      </div>
    );
  }

  return (
    <div style={styles.container}>
      {/* Title */}
      <div style={styles.header}>
        <div>
          <h1 style={styles.pageTitle}>Deals Pipeline</h1>
          <p style={styles.pageSubtitle}>Track active contract values, deal stages, and close opportunities</p>
        </div>
      </div>

      {/* Board Columns container */}
      <div style={styles.boardGrid}>
        {stages.map((stage) => {
          // Filter open deals for this stage
          const stageDeals = deals.filter(d => d.pipelineStageId === stage.id && d.status === 0);
          const stageDealsTotalValue = stageDeals.reduce((sum, d) => sum + d.amount, 0);

          return (
            <div key={stage.id} className="glass-panel" style={styles.stageColumn}>
              {/* Column Header */}
              <div style={styles.columnHeader}>
                <div style={styles.columnTitleBox}>
                  <h3 style={styles.columnName}>{stage.name}</h3>
                  <span style={styles.dealCount}>{stageDeals.length}</span>
                </div>
                <span style={styles.totalValue}>
                  ${stageDealsTotalValue.toLocaleString(undefined, { maximumFractionDigits: 0 })}
                </span>
              </div>

              {/* Add deal trigger */}
              {showAddDeal === stage.id ? (
                <form onSubmit={(e) => handleAddDealSubmit(e, stage.id)} style={styles.addDealForm}>
                  <input 
                    type="text" 
                    placeholder="Deal Title..."
                    value={dealTitle}
                    onChange={(e) => setDealTitle(e.target.value)}
                    style={styles.addInput}
                    required
                  />
                  <input 
                    type="number" 
                    placeholder="Value ($)..."
                    value={dealAmount}
                    onChange={(e) => setDealAmount(e.target.value)}
                    style={styles.addInput}
                  />
                  <select 
                    value={dealCustomerId}
                    onChange={(e) => setDealCustomerId(e.target.value)}
                    style={styles.addSelect}
                    required
                  >
                    <option value="">Select Contact...</option>
                    {customers.map(c => (
                      <option key={c.id} value={c.id}>{c.name || c.phoneNumber}</option>
                    ))}
                  </select>
                  <div style={styles.formActionButtons}>
                    <button type="submit" style={styles.submitDealBtn}>Add</button>
                    <button type="button" onClick={() => setShowAddDeal(null)} style={styles.cancelDealBtn}>Cancel</button>
                  </div>
                </form>
              ) : (
                <button onClick={() => setShowAddDeal(stage.id)} style={styles.addDealTrigger}>
                  <Plus size={14} style={{ marginRight: '6px' }} />
                  Create Deal
                </button>
              )}

              {/* Cards List */}
              <div style={styles.dealsList}>
                {stageDeals.length === 0 ? (
                  <div style={styles.emptyStage}>No open deals</div>
                ) : (
                  stageDeals.map((deal) => {
                    const customerObj = customers.find(c => c.id === deal.customerId);
                    
                    return (
                      <div key={deal.id} style={styles.dealCard}>
                        <div style={styles.dealCardHeader}>
                          <h4 style={styles.dealTitle}>{deal.title}</h4>
                          <span style={styles.dealAmount}>${deal.amount.toLocaleString()}</span>
                        </div>

                        {/* Customer label */}
                        <div style={styles.dealCustomerInfo}>
                          <User size={12} style={{ color: 'hsl(var(--text-muted))' }} />
                          <span>{customerObj?.name || 'Anonymous Contact'}</span>
                        </div>

                        {/* Bottom Action Section */}
                        <div style={styles.cardActions}>
                          <div style={styles.moveButtons}>
                            <button 
                              onClick={() => moveDeal(deal.id, 'prev', stage.id)}
                              disabled={stages.indexOf(stage) === 0}
                              style={{
                                ...styles.actionBtn,
                                opacity: stages.indexOf(stage) === 0 ? 0.3 : 1,
                                cursor: stages.indexOf(stage) === 0 ? 'not-allowed' : 'pointer'
                              }}
                              title="Move back"
                            >
                              <ChevronLeft size={14} />
                            </button>
                            <button 
                              onClick={() => moveDeal(deal.id, 'next', stage.id)}
                              disabled={stages.indexOf(stage) === stages.length - 1}
                              style={{
                                ...styles.actionBtn,
                                opacity: stages.indexOf(stage) === stages.length - 1 ? 0.3 : 1,
                                cursor: stages.indexOf(stage) === stages.length - 1 ? 'not-allowed' : 'pointer'
                              }}
                              title="Move forward"
                            >
                              <ChevronRight size={14} />
                            </button>
                          </div>

                          <div style={styles.statusActions}>
                            <button 
                              onClick={() => handleUpdateStatus(deal.id, 1)}
                              style={styles.winBtn}
                              title="Mark Won"
                            >
                              <Check size={12} />
                            </button>
                            <button 
                              onClick={() => handleUpdateStatus(deal.id, 2)}
                              style={styles.loseBtn}
                              title="Mark Lost"
                            >
                              <XIcon size={12} />
                            </button>
                          </div>
                        </div>
                      </div>
                    );
                  })
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-lg)',
    height: '100%',
  },
  loadingBox: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    padding: 'var(--space-xl)',
    color: 'hsl(var(--text-secondary))',
    gap: 'var(--space-md)',
  },
  spinner: {
    width: '30px',
    height: '30px',
    border: '2px solid rgba(255, 255, 255, 0.05)',
    borderTopColor: 'hsl(var(--accent-primary))',
    borderRadius: '50%',
    animation: 'spin 1s linear infinite',
  },
  header: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  pageTitle: {
    margin: 0,
    fontSize: '1.75rem',
    fontWeight: 800,
    color: 'hsl(var(--text-primary))',
  },
  pageSubtitle: {
    margin: 'var(--space-xs) 0 0 0',
    fontSize: '0.9rem',
    color: 'hsl(var(--text-muted))',
  },
  boardGrid: {
    display: 'flex',
    gap: 'var(--space-md)',
    overflowX: 'auto',
    alignItems: 'flex-start',
    paddingBottom: 'var(--space-md)',
    flexGrow: 1,
  },
  stageColumn: {
    width: '280px',
    minWidth: '280px',
    backgroundColor: 'rgba(255, 255, 255, 0.01)',
    border: '1px solid rgba(255, 255, 255, 0.05)',
    borderRadius: 'var(--radius-lg)',
    padding: 'var(--space-md)',
    display: 'flex',
    flexDirection: 'column',
    maxHeight: 'calc(100vh - 200px)',
  },
  columnHeader: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingBottom: 'var(--space-sm)',
    borderBottom: '1px solid rgba(255, 255, 255, 0.05)',
    marginBottom: 'var(--space-sm)',
  },
  columnTitleBox: {
    display: 'flex',
    alignItems: 'center',
    gap: 'var(--space-xs)',
  },
  columnName: {
    margin: 0,
    fontSize: '0.9rem',
    fontWeight: 700,
    color: 'hsl(var(--text-primary))',
  },
  dealCount: {
    fontSize: '0.7rem',
    backgroundColor: 'rgba(255,255,255,0.04)',
    color: 'hsl(var(--text-secondary))',
    padding: '2px 6px',
    borderRadius: 'var(--radius-full)',
    fontWeight: 700,
  },
  totalValue: {
    fontSize: '0.8rem',
    fontWeight: 700,
    color: 'hsl(var(--text-muted))',
  },
  addDealTrigger: {
    width: '100%',
    backgroundColor: 'rgba(255, 255, 255, 0.02)',
    border: '1px dashed rgba(255, 255, 255, 0.08)',
    borderRadius: 'var(--radius-md)',
    color: 'hsl(var(--text-secondary))',
    padding: '8px',
    fontSize: '0.8rem',
    fontWeight: 600,
    cursor: 'pointer',
    marginBottom: 'var(--space-md)',
    transition: 'var(--transition-fast)',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  addDealForm: {
    backgroundColor: 'rgba(255, 255, 255, 0.02)',
    border: '1px solid rgba(255,255,255,0.06)',
    borderRadius: 'var(--radius-md)',
    padding: 'var(--space-sm)',
    display: 'flex',
    flexDirection: 'column',
    gap: '6px',
    marginBottom: 'var(--space-md)',
  },
  addInput: {
    backgroundColor: 'rgba(0,0,0,0.2)',
    border: '1px solid rgba(255,255,255,0.08)',
    borderRadius: 'var(--radius-sm)',
    color: 'white',
    padding: '6px 8px',
    fontSize: '0.8rem',
    outline: 'none',
  },
  addSelect: {
    backgroundColor: 'rgba(0,0,0,0.2)',
    border: '1px solid rgba(255,255,255,0.08)',
    borderRadius: 'var(--radius-sm)',
    color: 'white',
    padding: '6px 8px',
    fontSize: '0.8rem',
    outline: 'none',
    cursor: 'pointer',
  },
  formActionButtons: {
    display: 'flex',
    gap: 'var(--space-xs)',
    marginTop: '4px',
  },
  submitDealBtn: {
    flexGrow: 1,
    backgroundColor: 'hsl(var(--accent-primary))',
    color: 'white',
    border: 'none',
    borderRadius: 'var(--radius-sm)',
    padding: '6px',
    fontSize: '0.75rem',
    fontWeight: 700,
    cursor: 'pointer',
  },
  cancelDealBtn: {
    flexGrow: 1,
    backgroundColor: 'transparent',
    color: 'hsl(var(--text-muted))',
    border: '1px solid rgba(255,255,255,0.08)',
    borderRadius: 'var(--radius-sm)',
    padding: '6px',
    fontSize: '0.75rem',
    fontWeight: 600,
    cursor: 'pointer',
  },
  dealsList: {
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-sm)',
    overflowY: 'auto',
    flexGrow: 1,
  },
  emptyStage: {
    padding: 'var(--space-md)',
    textAlign: 'center',
    color: 'hsl(var(--text-muted))',
    fontSize: '0.75rem',
    border: '1px dashed rgba(255, 255, 255, 0.04)',
    borderRadius: 'var(--radius-md)',
  },
  dealCard: {
    backgroundColor: 'rgba(255, 255, 255, 0.02)',
    border: '1px solid rgba(255, 255, 255, 0.05)',
    borderRadius: 'var(--radius-md)',
    padding: 'var(--space-md)',
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-sm)',
    transition: 'var(--transition-fast)',
  },
  dealCardHeader: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    gap: 'var(--space-xs)',
  },
  dealTitle: {
    margin: 0,
    fontSize: '0.85rem',
    fontWeight: 700,
    color: 'hsl(var(--text-primary))',
  },
  dealAmount: {
    fontSize: '0.8rem',
    fontWeight: 700,
    color: 'hsl(var(--text-secondary))',
  },
  dealCustomerInfo: {
    display: 'flex',
    alignItems: 'center',
    gap: '6px',
    fontSize: '0.75rem',
    color: 'hsl(var(--text-secondary))',
  },
  cardActions: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    borderTop: '1px solid rgba(255,255,255,0.03)',
    paddingTop: 'var(--space-sm)',
    marginTop: '4px',
  },
  moveButtons: {
    display: 'flex',
    gap: '2px',
  },
  actionBtn: {
    backgroundColor: 'rgba(255,255,255,0.02)',
    border: '1px solid rgba(255,255,255,0.08)',
    color: 'hsl(var(--text-secondary))',
    borderRadius: 'var(--radius-sm)',
    padding: '4px',
    cursor: 'pointer',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  statusActions: {
    display: 'flex',
    gap: '4px',
  },
  winBtn: {
    backgroundColor: 'rgba(16, 185, 129, 0.1)',
    border: '1px solid rgba(16, 185, 129, 0.3)',
    color: 'hsl(140, 100%, 65%)',
    borderRadius: 'var(--radius-sm)',
    padding: '4px 6px',
    cursor: 'pointer',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  loseBtn: {
    backgroundColor: 'rgba(239, 68, 68, 0.1)',
    border: '1px solid rgba(239, 68, 68, 0.3)',
    color: 'hsl(0, 100%, 65%)',
    borderRadius: 'var(--radius-sm)',
    padding: '4px 6px',
    cursor: 'pointer',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
};
