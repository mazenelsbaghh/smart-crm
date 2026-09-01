'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { useToast } from '../../context/toast-context';
import { crmService, Deal, PipelineStage, Customer } from '../../services/crm';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
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
  const { showToast } = useToast();
  
  const [stages, setStages] = useState<PipelineStage[]>([]);
  const [deals, setDeals] = useState<Deal[]>([]);
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [pendingStatusChange, setPendingStatusChange] = useState<{ dealId: string; title: string; status: 1 | 2 } | null>(null);
  
  // Deal creation form
  const [showAddDeal, setShowAddDeal] = useState<string | null>(null); // holds stageId where form is opened
  const [dealTitle, setDealTitle] = useState('');
  const [dealAmount, setDealAmount] = useState('');
  const [dealCustomerId, setDealCustomerId] = useState('');
  const loadRequestIdRef = React.useRef(0);

  const fetchPipelineData = useCallback(async () => {
    const requestId = ++loadRequestIdRef.current;
    if (!activeProject) {
      setStages([]);
      setDeals([]);
      setCustomers([]);
      setLoading(false);
      setLoadError('تعذر تحميل مساحة العمل. أعد المحاولة أو تواصل مع المدير.');
      return;
    }
    try {
      setLoading(true);
      setLoadError(null);
      setStages([]);
      setDeals([]);
      setCustomers([]);
      const [stageData, dealData, custData] = await Promise.all([
        crmService.getPipelineStages(activeProject.id),
        crmService.getDeals(activeProject.id),
        crmService.getCustomers(activeProject.id),
      ]);
      if (requestId !== loadRequestIdRef.current) return;
      
      setStages(stageData);
      setDeals(dealData);
      setCustomers(custData);
    } catch (e) {
      if (requestId !== loadRequestIdRef.current) return;
      console.error('Failed to load pipeline context', e);
      setLoadError('تعذر تحميل مسار الصفقات.');
    } finally {
      if (requestId === loadRequestIdRef.current) setLoading(false);
    }
  }, [activeProject]);

  useEffect(() => {
    const loadTimer = window.setTimeout(() => void fetchPipelineData(), 0);
    return () => window.clearTimeout(loadTimer);
  }, [fetchPipelineData]);

  const moveDeal = async (dealId: string, direction: 'prev' | 'next', currentStageId: string) => {
    const currentIndex = stages.findIndex(s => s.id === currentStageId);
    if (currentIndex === -1) return;

    const targetIndex = direction === 'next' ? currentIndex + 1 : currentIndex - 1;
    if (targetIndex < 0 || targetIndex >= stages.length) return;

    const targetStage = stages[targetIndex];
    try {
      // Optimistic UI update
      setDeals(prev => prev.map(d => d.id === dealId ? { ...d, pipelineStageId: targetStage.id } : d));
      await crmService.updateDealStage(dealId, targetStage.id);
    } catch (e) {
      console.error('Failed to update deal stage on backend', e);
      showToast('تعذر نقل الصفقة. تمت استعادة بيانات الخادم.', 'error');
      void fetchPipelineData();
    }
  };

  const handleUpdateStatus = async (dealId: string, status: 0 | 1 | 2) => {
    try {
      // Optimistic update
      setDeals(prev => prev.map(d => d.id === dealId ? { ...d, status } : d));
      await crmService.updateDealStatus(dealId, status);
      showToast(status === 1 ? 'تم إغلاق الصفقة كرابحة.' : 'تم إغلاق الصفقة كخاسرة.', 'success');
    } catch (e) {
      console.error('Failed to update deal status', e);
      showToast('تعذر إغلاق الصفقة. تمت استعادة بيانات الخادم.', 'error');
      void fetchPipelineData();
    }
  };

  const handleAddDealSubmit = async (e: React.FormEvent, stageId: string) => {
    e.preventDefault();
    if (!activeProject || !dealTitle || !dealCustomerId || dealAmount.trim() === '') return;
    const parsedAmount = Number(dealAmount);
    if (!Number.isFinite(parsedAmount) || parsedAmount < 0) {
      showToast('أدخل قيمة صفقة صحيحة غير سالبة.', 'error');
      return;
    }

    try {
      await crmService.createDeal(activeProject.id, {
        title: dealTitle,
        amount: parsedAmount,
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
      showToast('تم إنشاء الصفقة.', 'success');
    } catch (err) {
      console.error('Failed to create deal', err);
      showToast('تعذر إنشاء الصفقة. راجع البيانات وحاول مرة أخرى.', 'error');
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

  if (loadError) {
    return (
      <div className={styles.loadingBox} role="alert">
        <p>{loadError}</p>
        {activeProject && <button type="button" className={styles.addDealTrigger} onClick={() => void fetchPipelineData()}>إعادة المحاولة</button>}
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
                  {stageDealsTotalValue.toLocaleString('ar-EG', { maximumFractionDigits: 0 })} — العملة غير محددة
                </span>
              </div>

              {/* Add deal trigger */}
              {showAddDeal === stage.id ? (
                <form onSubmit={(e) => handleAddDealSubmit(e, stage.id)} className={styles.addDealForm}>
                  <label className="sr-only" htmlFor={`deal-title-${stage.id}`}>عنوان الصفقة</label>
                  <input 
                    id={`deal-title-${stage.id}`}
                    type="text" 
                    placeholder="عنوان الصفقة..."
                    value={dealTitle}
                    onChange={(e) => setDealTitle(e.target.value)}
                    className={styles.addInput}
                    required
                  />
                  <label className="sr-only" htmlFor={`deal-amount-${stage.id}`}>قيمة الصفقة دون افتراض عملة</label>
                  <input 
                    id={`deal-amount-${stage.id}`}
                    type="number" 
                    min="0"
                    step="any"
                    placeholder="القيمة..."
                    value={dealAmount}
                    onChange={(e) => setDealAmount(e.target.value)}
                    className={styles.addInput}
                    required
                  />
                  <label className="sr-only" htmlFor={`deal-customer-${stage.id}`}>جهة اتصال الصفقة</label>
                  <select 
                    id={`deal-customer-${stage.id}`}
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
                          <span className={styles.dealAmount}>{deal.amount.toLocaleString('ar-EG')} — العملة غير محددة</span>
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
                              aria-label={`نقل الصفقة ${deal.title} إلى المرحلة السابقة`}
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
                              aria-label={`نقل الصفقة ${deal.title} إلى المرحلة التالية`}
                            >
                              <ChevronLeft size={14} />
                            </button>
                          </div>

                          <div className={styles.statusActions}>
                            <button 
                              onClick={() => setPendingStatusChange({ dealId: deal.id, title: deal.title, status: 1 })}
                              className={styles.winBtn}
                              title="مغلقة رابحة"
                              aria-label={`إغلاق الصفقة ${deal.title} كرابحة`}
                            >
                              <Check size={12} />
                            </button>
                            <button 
                              onClick={() => setPendingStatusChange({ dealId: deal.id, title: deal.title, status: 2 })}
                              className={styles.loseBtn}
                              title="مغلقة خاسرة"
                              aria-label={`إغلاق الصفقة ${deal.title} كخاسرة`}
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
      <ConfirmDialog
        isOpen={pendingStatusChange !== null}
        title="تأكيد إغلاق الصفقة"
        message={pendingStatusChange ? `سيتم إغلاق الصفقة «${pendingStatusChange.title}» ك${pendingStatusChange.status === 1 ? 'رابحة' : 'خاسرة'}.` : ''}
        confirmLabel="تأكيد الإغلاق"
        onConfirm={() => {
          const statusChange = pendingStatusChange;
          setPendingStatusChange(null);
          if (statusChange) void handleUpdateStatus(statusChange.dealId, statusChange.status);
        }}
        onCancel={() => setPendingStatusChange(null)}
      />
    </div>
  );
}
