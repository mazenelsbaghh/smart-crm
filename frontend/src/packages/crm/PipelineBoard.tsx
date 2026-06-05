'use client';

import React, { useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { crmService, Deal, PipelineStage, Customer } from '../../services/crm';
import { 
  ChevronLeft, 
  ChevronRight, 
  Plus, 
  Check, 
  X as XIcon, 
  User
} from 'lucide-react';
import styles from './crm.module.css';

const stageNamesAr: Record<string, string> = {
  'New': 'جديد',
  'Contacted': 'تم التواصل',
  'Qualified': 'مؤهل',
  'Proposal': 'تقديم عرض سعر',
  'Negotiation': 'تفاوض وبحث',
  'Won': 'صفقات ناجحة',
  'Lost': 'صفقات خاسرة'
};

export default function PipelineBoard() {
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
      <div className={styles.loadingBox}>
        <div className={styles.spinner}></div>
        <p>جاري تحميل خط المبيعات ومسار الصفقات...</p>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      {/* Title */}
      <div className={styles.header}>
        <div>
          <h1 className={styles.pageTitle}>مسار الصفقات والفرص</h1>
          <p className={styles.pageSubtitle}>تتبع قيم العقود والصفقات الحالية، تنقل بين مراحل البيع، وأغلق الفرص البيعية بنجاح</p>
        </div>
      </div>

      {/* Board Columns container */}
      <div className={styles.boardGrid}>
        {stages.map((stage) => {
          // Filter open deals for this stage
          const stageDeals = deals.filter(d => d.pipelineStageId === stage.id && d.status === 0);
          const stageDealsTotalValue = stageDeals.reduce((sum, d) => sum + d.amount, 0);

          return (
            <div key={stage.id} className={`glass-panel ${styles.stageColumn}`}>
              {/* Column Header */}
              <div className={styles.columnHeader}>
                <div className={styles.columnTitleBox}>
                  <h3 className={styles.columnName}>{stageNamesAr[stage.name] || stage.name}</h3>
                  <span className={styles.dealCount}>{stageDeals.length}</span>
                </div>
                <span className={styles.totalValue}>
                  ${stageDealsTotalValue.toLocaleString(undefined, { maximumFractionDigits: 0 })}
                </span>
              </div>

              {/* Add deal trigger */}
              {showAddDeal === stage.id ? (
                <form onSubmit={(e) => handleAddDealSubmit(e, stage.id)} className={styles.addDealForm}>
                  <input 
                    type="text" 
                    placeholder="عنوان الصفقة..."
                    value={dealTitle}
                    onChange={(e) => setDealTitle(e.target.value)}
                    className={styles.addInput}
                    required
                  />
                  <input 
                    type="number" 
                    placeholder="القيمة ($)..."
                    value={dealAmount}
                    onChange={(e) => setDealAmount(e.target.value)}
                    className={styles.addInput}
                  />
                  <select 
                    value={dealCustomerId}
                    onChange={(e) => setDealCustomerId(e.target.value)}
                    className={styles.addSelect}
                    required
                  >
                    <option value="">اختر جهة الاتصال...</option>
                    {customers.map(c => (
                      <option key={c.id} value={c.id}>{c.name || c.phoneNumber}</option>
                    ))}
                  </select>
                  <div className={styles.formActionButtons}>
                    <button type="submit" className={styles.submitDealBtn}>إضافة</button>
                    <button type="button" onClick={() => setShowAddDeal(null)} className={styles.cancelDealBtn}>إلغاء</button>
                  </div>
                </form>
              ) : (
                <button onClick={() => setShowAddDeal(stage.id)} className={styles.addDealTrigger}>
                  <Plus size={14} style={{ marginLeft: '6px' }} />
                  إضافة صفقة جديدة
                </button>
              )}

              {/* Cards List */}
              <div className={styles.dealsList}>
                {stageDeals.length === 0 ? (
                  <div className={styles.emptyStage}>لا توجد صفقات مفتوحة</div>
                ) : (
                  stageDeals.map((deal) => {
                    const customerObj = customers.find(c => c.id === deal.customerId);
                    
                    return (
                      <div key={deal.id} className={styles.dealCard}>
                        <div className={styles.dealCardHeader}>
                          <h4 className={styles.dealTitle}>{deal.title}</h4>
                          <span className={styles.dealAmount}>${deal.amount.toLocaleString()}</span>
                        </div>

                        {/* Customer label */}
                        <div className={styles.dealCustomerInfo} style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-start', gap: '4px' }}>
                          <div style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                            <User size={12} style={{ marginLeft: '6px' }} />
                            <span>{customerObj?.name || 'جهة اتصال مجهولة'}</span>
                          </div>
                          {customerObj?.label && (
                            <span className={styles.smartLabelBadge} style={{ marginRight: '18px' }}>{customerObj.label}</span>
                          )}
                        </div>

                        {/* Bottom Action Section */}
                        <div className={styles.cardActions}>
                          <div className={styles.moveButtons}>
                            <button 
                              onClick={() => moveDeal(deal.id, 'prev', stage.id)}
                              disabled={stages.indexOf(stage) === 0}
                              className={styles.actionBtn}
                              style={{
                                opacity: stages.indexOf(stage) === 0 ? 0.3 : 1,
                                cursor: stages.indexOf(stage) === 0 ? 'not-allowed' : 'pointer'
                              }}
                              title="نقل للخلف"
                            >
                              <ChevronRight size={14} />
                            </button>
                            <button 
                              onClick={() => moveDeal(deal.id, 'next', stage.id)}
                              disabled={stages.indexOf(stage) === stages.length - 1}
                              className={styles.actionBtn}
                              style={{
                                opacity: stages.indexOf(stage) === stages.length - 1 ? 0.3 : 1,
                                cursor: stages.indexOf(stage) === stages.length - 1 ? 'not-allowed' : 'pointer'
                              }}
                              title="نقل للأمام"
                            >
                              <ChevronLeft size={14} />
                            </button>
                          </div>

                          <div className={styles.statusActions}>
                            <button 
                              onClick={() => handleUpdateStatus(deal.id, 1)}
                              className={styles.winBtn}
                              title="مغلقة رابحة"
                            >
                              <Check size={12} />
                            </button>
                            <button 
                              onClick={() => handleUpdateStatus(deal.id, 2)}
                              className={styles.loseBtn}
                              title="مغلقة خاسرة"
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
