'use client';

import React, { useEffect, useState, useRef } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '../../context/auth-context';
import { crmService, Customer, Deal } from '../../services/crm';
import { api } from '../../services/api';
import { 
  Users, 
  TrendingUp, 
  RefreshCw, 
  Sparkles, 
  MessageSquare, 
  ArrowRight, 
  Activity, 
  Star, 
  Landmark, 
  Zap, 
  ChevronRight, 
  ChevronLeft, 
  Filter, 
  Download, 
  Send,
  MoreVertical
} from 'lucide-react';
import styles from './dashboard.module.css';

export default function Dashboard() {
  const { activeProject } = useAuth();
  const router = useRouter();
  const containerRef = useRef<HTMLDivElement>(null);
  
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [deals, setDeals] = useState<Deal[]>([]);
  const [totalChats, setTotalChats] = useState<number>(0);
  const [loading, setLoading] = useState(true);
  const [recalculating, setRecalculating] = useState(false);
  const [stageFilter, setStageFilter] = useState('الكل');

  // Chatbot state
  const [chatInput, setChatInput] = useState('');
  const [chatMessages, setChatMessages] = useState([
    {
      sender: 'ai',
      text: 'أهلاً بك مروان! قمت بتحليل آخر المحادثات مع العملاء. لقد وجدت أن ٨٠٪ منهم يسألون عن خطط التقسيط والأسعار. هل تود أن أقوم بتجهيز رد آلي لهذه النقطة؟',
      time: '١٠:٢٤ ص'
    }
  ]);

  const fetchDashboardData = async () => {
    if (!activeProject) return;
    try {
      setLoading(true);
      const [custData, dealData, convsData] = await Promise.all([
        crmService.getCustomers(activeProject.id),
        crmService.getDeals(activeProject.id),
        api.get<any[]>(`/api/projects/${activeProject.id}/conversations`)
      ]);
      setCustomers(custData);
      setDeals(dealData);
      setTotalChats(convsData.data?.length || 0);
    } catch (err) {
      console.error('Failed to load dashboard data', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchDashboardData();
  }, [activeProject]);

  const handleRecalculate = async () => {
    if (!activeProject || recalculating) return;
    setRecalculating(true);
    try {
      await crmService.recalculateAnalytics(activeProject.id);
      await fetchDashboardData();
    } catch (err) {
      console.error('Failed to recalculate metrics', err);
    } finally {
      setRecalculating(false);
    }
  };

  const handleSendMessage = (e: React.FormEvent) => {
    e.preventDefault();
    if (!chatInput.trim()) return;
    
    const userMsg = {
      sender: 'user',
      text: chatInput.trim(),
      time: new Date().toLocaleTimeString('ar-EG', { hour: '2-digit', minute: '2-digit' })
    };
    
    setChatMessages(prev => [...prev, userMsg]);
    setChatInput('');
    
    setTimeout(() => {
      setChatMessages(prev => [...prev, {
        sender: 'ai',
        text: 'جاري تحليل طلبك وضبط تفضيلات الرد الآلي للعملاء... سأعلمك فور التحديث.',
        time: new Date().toLocaleTimeString('ar-EG', { hour: '2-digit', minute: '2-digit' })
      }]);
    }, 1000);
  };

  if (loading) {
    return (
      <div className={styles.container}>
        {/* Title Header Skeleton */}
        <div className={styles.dashboardHeader}>
          <div>
            <div className={styles.breadcrumbs}>
              <div className={styles.skeleton} style={{ width: '40px', height: '10px' }}></div>
              <span className={styles.chevron}>&gt;</span>
              <div className={styles.skeleton} style={{ width: '60px', height: '10px' }}></div>
            </div>
            <div className={`${styles.skeleton} ${styles.skeletonTitle}`} style={{ width: '220px', height: '28px', marginTop: '6px', marginBottom: '8px' }}></div>
            <div className={styles.skeleton} style={{ width: '320px', height: '14px' }}></div>
          </div>
          <div className={styles.headerActions}>
            <div className={styles.skeleton} style={{ width: '90px', height: '36px', borderRadius: 'var(--radius-md)' }}></div>
            <div className={styles.skeleton} style={{ width: '130px', height: '36px', borderRadius: 'var(--radius-md)' }}></div>
          </div>
        </div>

        {/* KPI Stats Cards Skeleton */}
        <div className={styles.statsGrid}>
          {[1, 2, 3, 4].map(idx => (
            <div key={idx} className={`glass-panel ${styles.statCard} ${styles.skeletonCard}`}>
              <div className={styles.statTop}>
                <div className={`${styles.skeleton} ${styles.skeletonCircle}`} style={{ width: '42px', height: '42px', borderRadius: 'var(--radius-md)' }}></div>
                {idx !== 4 && <div className={styles.skeleton} style={{ width: '50px', height: '18px', borderRadius: 'var(--radius-full)' }}></div>}
                {idx === 4 && <div className={styles.skeleton} style={{ width: '70px', height: '18px', borderRadius: 'var(--radius-sm)' }}></div>}
              </div>
              <div className={styles.statContent} style={{ gap: '8px' }}>
                <div className={styles.skeleton} style={{ width: '90px', height: '12px' }}></div>
                <div className={styles.valueRow}>
                  {idx !== 4 && <div className={styles.skeleton} style={{ width: '70px', height: '24px' }}></div>}
                  {idx === 4 && <div className={styles.skeleton} style={{ width: '100%', height: '20px' }}></div>}
                </div>
              </div>
            </div>
          ))}
        </div>

        {/* Content Grid Skeleton */}
        <div className={styles.contentGrid}>
          {/* Left Column: Recent Hot Leads Table Skeleton */}
          <div className={`glass-panel ${styles.leadsPanel} ${styles.skeletonCard}`}>
            <div className={styles.panelHeader} style={{ marginBottom: '24px' }}>
              <div>
                <div className={styles.skeleton} style={{ width: '150px', height: '18px', marginBottom: '6px' }}></div>
                <div className={styles.skeleton} style={{ width: '180px', height: '12px' }}></div>
              </div>
              <div className={styles.skeleton} style={{ width: '100px', height: '28px', borderRadius: 'var(--radius-md)' }}></div>
            </div>

            <div className={styles.tableContainer}>
              <table className={styles.leadsTable}>
                <thead>
                  <tr>
                    <th>العميل</th>
                    <th>نقاط الاهتمام</th>
                    <th>الحالة</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {[1, 2, 3, 4, 5].map((idx) => (
                    <tr key={idx} style={{ pointerEvents: 'none' }}>
                      <td>
                        <div className={styles.customerProfileCell}>
                          <div className={`${styles.skeleton} ${styles.skeletonCircle}`} style={{ width: '36px', height: '36px' }}></div>
                          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                            <div className={styles.skeleton} style={{ width: '110px', height: '12px' }}></div>
                            <div className={styles.skeleton} style={{ width: '80px', height: '9px' }}></div>
                          </div>
                        </div>
                      </td>
                      <td>
                        <div className={styles.scoreCell}>
                          <div className={styles.skeleton} style={{ width: '45px', height: '14px' }}></div>
                        </div>
                      </td>
                      <td>
                        <div className={styles.skeleton} style={{ width: '65px', height: '18px', borderRadius: '9999px' }}></div>
                      </td>
                      <td>
                        <div className={styles.skeleton} style={{ width: '16px', height: '16px' }}></div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className={styles.tableFooter}>
              <div className={styles.pagination}>
                <div className={styles.skeleton} style={{ width: '28px', height: '28px', borderRadius: '4px' }}></div>
                <div className={styles.skeleton} style={{ width: '80px', height: '12px' }}></div>
                <div className={styles.skeleton} style={{ width: '28px', height: '28px', borderRadius: '4px' }}></div>
              </div>
              <div className={styles.skeleton} style={{ width: '120px', height: '12px' }}></div>
            </div>
          </div>

          {/* Right Column: Actions / Chatbot Skeleton */}
          <div className={styles.actionsColumn}>
            {/* AI Assistant Skeleton */}
            <div className={`glass-panel ${styles.aiAssistantPanel} ${styles.skeletonCard}`}>
              <div className={styles.aiAssistantHeader}>
                <div className={styles.aiBotProfile}>
                  <div className={`${styles.skeleton} ${styles.skeletonCircle}`} style={{ width: '36px', height: '36px', borderRadius: 'var(--radius-md)' }}></div>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                    <div className={styles.skeleton} style={{ width: '90px', height: '12px' }}></div>
                    <div className={styles.skeleton} style={{ width: '50px', height: '8px' }}></div>
                  </div>
                </div>
              </div>

              <div className={styles.aiChatTimeline} style={{ gap: '16px' }}>
                <div className={`${styles.chatBubble} ${styles.bubbleAi}`} style={{ width: '75%', border: 'none' }}>
                  <div className={styles.skeleton} style={{ width: '100%', height: '10px', marginBottom: '6px' }}></div>
                  <div className={styles.skeleton} style={{ width: '90%', height: '10px', marginBottom: '6px' }}></div>
                  <div className={styles.skeleton} style={{ width: '40%', height: '8px' }}></div>
                </div>
                <div className={`${styles.chatBubble} ${styles.bubbleUser}`} style={{ width: '60%', border: 'none', alignSelf: 'flex-end' }}>
                  <div className={styles.skeleton} style={{ width: '100%', height: '10px', marginBottom: '6px' }}></div>
                  <div className={styles.skeleton} style={{ width: '30%', height: '8px' }}></div>
                </div>
              </div>

              <div className={styles.aiChatInputArea}>
                <div className={styles.skeleton} style={{ width: '100%', height: '38px', borderRadius: 'var(--radius-md)' }}></div>
              </div>
            </div>

            {/* Operations Health Skeleton */}
            <div className={`glass-panel ${styles.healthPanel} ${styles.skeletonCard}`}>
              <div className={styles.skeleton} style={{ width: '140px', height: '16px', marginBottom: '20px' }}></div>
              <div className={styles.healthStatsList} style={{ gap: '16px' }}>
                {[1, 2].map(idx => (
                  <div key={idx} className={styles.healthStatItem}>
                    <div className={styles.healthStatLabels} style={{ marginBottom: '4px' }}>
                      <div className={styles.skeleton} style={{ width: '80px', height: '12px' }}></div>
                      <div className={styles.skeleton} style={{ width: '30px', height: '12px' }}></div>
                    </div>
                    <div className={styles.progressBar}>
                      <div className={styles.skeleton} style={{ width: idx === 1 ? '42%' : '89%', height: '100%' }}></div>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      </div>
    );
  }

  // Calculate Metrics dynamically
  const totalCustomers = customers.length;
  
  // KPI 1: Total Chats
  const totalChatsCount = totalChats;
  
  // KPI 2: New Leads / Total Customers
  const totalLeadsCount = totalCustomers;

  // KPI 3: Conversion Rate
  const wonDeals = deals.filter(d => d.status === 1);
  const totalDealsCount = deals.length;
  const conversionRate = totalDealsCount > 0 
    ? Math.round((wonDeals.length / totalDealsCount) * 1000) / 10 
    : 0;

  // Filtered Leads
  const filteredCustomers = customers.filter(c => {
    if (stageFilter === 'الكل') return true;
    return c.pipelineStage === stageFilter;
  });

  // Top leads by LeadScore
  const sortedLeads = [...filteredCustomers]
    .sort((a, b) => (b.leadScore || 0) - (a.leadScore || 0))
    .slice(0, 10);

  // Available unique pipeline stages for filtering
  const stages = ['الكل', ...Array.from(new Set(customers.map(c => c.pipelineStage).filter(Boolean)))];

  // Dynamic AI Insight description
  let aiInsightDesc = "مساعد الذكاء الاصطناعي يحلل الآن محادثات عملائك الواردة لاستخلاص نقاط الاهتمام وتصنيفهم.";
  if (activeProject) {
    if (activeProject.settings?.aiAutoReplyEnabled) {
      aiInsightDesc = "الرد التلقائي بالذكاء الاصطناعي نشط حالياً للمشروع. يقوم المساعد بالرد الفوري التلقائي على استفسارات عملائك وتحديث تفاصيلهم.";
    } else {
      aiInsightDesc = "الرد التلقائي بالذكاء الاصطناعي غير مفعّل حالياً. يمكنك تفعيله من الإعدادات لمضاعفة سرعة الاستجابة للعملاء الجدد.";
    }
  }

  return (
    <div ref={containerRef} className={styles.container}>
      {/* Title Header */}
      <div className={styles.dashboardHeader}>
        <div>
          <div className={styles.breadcrumbs}>
            <span>الرئيسية</span>
            <span className={styles.chevron}>&gt;</span>
            <span className={styles.activeBreadcrumb}>لوحة التحكم</span>
          </div>
          <h1 className={styles.pageTitle}>نظرة عامة على الأداء</h1>
          <p className={styles.pageSubtitle}>تتبع مؤشرات النمو والعملاء في الوقت الفعلي للمشروع {activeProject?.name}</p>
        </div>
        <div className={styles.headerActions}>
          <button className={styles.rangeButton}>
            <span>آخر ٣٠ يوم</span>
          </button>
          <button 
            onClick={handleRecalculate} 
            disabled={recalculating} 
            className={styles.refreshButton}
          >
            <RefreshCw size={16} className={recalculating ? styles.spinIcon : ''} />
            <span>{recalculating ? 'جاري التحديث...' : 'تحديث المؤشرات'}</span>
          </button>
        </div>
      </div>

      {/* KPI Stats Cards */}
      <div className={styles.statsGrid}>
        {/* KPI 1: Total Chats */}
        <div className={`glass-panel ${styles.statCard}`}>
          <div className={styles.statTop}>
            <div className={styles.statIconBox} style={{ color: 'var(--accent)', backgroundColor: 'var(--accent-soft)' }}>
              <MessageSquare size={22} />
            </div>
            <span className={styles.trendBadge} style={{ color: 'var(--accent)', backgroundColor: 'var(--accent-soft)' }}>
              ١٢.٥٪ +
            </span>
          </div>
          <div className={styles.statContent}>
            <span className={styles.statLabel}>إجمالي الشاتات</span>
            <div className={styles.valueRow}>
              <span className={styles.statValue}>{totalChatsCount.toLocaleString('ar-EG')}</span>
            </div>
          </div>
        </div>

        {/* KPI 2: Customers */}
        <div className={`glass-panel ${styles.statCard}`}>
          <div className={styles.statTop}>
            <div className={styles.statIconBox} style={{ color: 'var(--accent)', backgroundColor: 'var(--accent-soft)' }}>
              <Users size={22} />
            </div>
            <span className={styles.trendBadge} style={{ color: 'var(--accent)', backgroundColor: 'var(--accent-soft)' }}>
              ٨.٢٪ +
            </span>
          </div>
          <div className={styles.statContent}>
            <span className={styles.statLabel}>العملاء الجدد</span>
            <div className={styles.valueRow}>
              <span className={styles.statValue}>{totalLeadsCount.toLocaleString('ar-EG')}</span>
            </div>
          </div>
        </div>

        {/* KPI 3: Conversion Rate */}
        <div className={`glass-panel ${styles.statCard}`}>
          <div className={styles.statTop}>
            <div className={styles.statIconBox} style={{ color: 'var(--accent)', backgroundColor: 'var(--accent-soft)' }}>
              <Star size={22} />
            </div>
            <span className={styles.trendBadge} style={{ color: 'hsl(var(--accent-danger))', backgroundColor: 'var(--danger-soft)' }}>
              ٢.١٪ -
            </span>
          </div>
          <div className={styles.statContent}>
            <span className={styles.statLabel}>معدل التحويل</span>
            <div className={styles.valueRow}>
              <span className={styles.statValue}>{conversionRate}٪</span>
            </div>
          </div>
        </div>

        {/* KPI 4: AI Insights */}
        <div className={`glass-panel ${styles.statCard} ${styles.aiKpiCard}`}>
          <div className={styles.statTop}>
            <div className={styles.statIconBox} style={{ color: 'var(--accent-ink)', backgroundColor: 'var(--accent)' }}>
              <Sparkles size={20} />
            </div>
            <span className={styles.aiBadge}>AI Insights</span>
          </div>
          <div className={styles.statContent}>
            <span className={styles.aiKpiTitle}>توصية الذكاء الاصطناعي</span>
            <p className={styles.aiKpiDesc}>
              {aiInsightDesc}
            </p>
          </div>
        </div>
      </div>

      {/* Main Grid Section */}
      <div className={styles.contentGrid}>
        {/* Left Column: Leads Table Panel */}
        <div className={`glass-panel ${styles.leadsPanel}`}>
          <div className={styles.panelHeader}>
            <div>
              <h2 className={styles.panelTitle}>أهم العملاء المحتملين</h2>
              <p className={styles.panelSubtitle}>متابعة حالة العملاء الأعلى تقييماً</p>
            </div>
            
            <div className={styles.filterActions}>
              <div className={styles.selectWrapper}>
                <Filter size={14} className={styles.filterIcon} />
                <select 
                  value={stageFilter} 
                  onChange={(e) => setStageFilter(e.target.value)}
                  className={styles.stageSelect}
                >
                  {stages.map(stg => (
                    <option key={stg} value={stg}>{stg}</option>
                  ))}
                </select>
              </div>
            </div>
          </div>

          <div className={styles.tableContainer}>
            <table className={styles.leadsTable}>
              <thead>
                <tr>
                  <th>العميل</th>
                  <th>نقاط الاهتمام</th>
                  <th>الحالة</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {sortedLeads.length === 0 ? (
                  <tr>
                    <td colSpan={4} className={styles.emptyTable}>لا توجد بيانات مطابقة للعملاء</td>
                  </tr>
                ) : (
                  sortedLeads.map((c, index) => (
                    <tr key={c.id}>
                      <td>
                        <div className={styles.customerProfileCell}>
                          <div className={styles.avatarLetter}>
                            {(c.name || 'ع')[0].toUpperCase()}
                          </div>
                          <div>
                            <div className={styles.customerName}>{c.name || 'عميل بدون اسم'}</div>
                            <div className={styles.customerPhone}>{c.phoneNumber}</div>
                          </div>
                        </div>
                      </td>
                      <td>
                        <div className={styles.scoreCell}>
                          <Zap size={14} fill="currentColor" className={styles.boltIcon} />
                          <span className={styles.scoreVal}>{c.leadScore || 50}</span>
                        </div>
                      </td>
                      <td>
                        <span className={styles.stageBadge}>{c.pipelineStage || 'جديد'}</span>
                      </td>
                      <td>
                        <button type="button" className={styles.actionBtn}>
                          <MoreVertical size={16} />
                        </button>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>

          <div className={styles.tableFooter}>
            <div className={styles.pagination}>
              <button type="button" className={styles.pageBtn} disabled>
                <ChevronRight size={16} />
              </button>
              <span className={styles.pageText}>الصفحة ١ من ١</span>
              <button type="button" className={styles.pageBtn} disabled>
                <ChevronLeft size={16} />
              </button>
            </div>
            <span className={styles.totalRowsText}>عرض {sortedLeads.length} من إجمالي {filteredCustomers.length} عميل</span>
          </div>
        </div>

        {/* Right Column: AI Assistant & Quick Health */}
        <div className={styles.actionsColumn}>
          {/* Gemini chatbot assistant */}
          <div className={`glass-panel ${styles.aiAssistantPanel}`}>
            <div className={styles.aiAssistantHeader}>
              <div className={styles.aiBotProfile}>
                <div className={styles.aiBotIcon}>
                  <MessageSquare size={20} />
                </div>
                <div>
                  <h3 className={styles.aiBotTitle}>مساعد Gemini</h3>
                  <div className={styles.aiStatusIndicator}>
                    <span className={styles.aiPulseDot}></span>
                    <span className={styles.aiStatusText}>متصل الآن</span>
                  </div>
                </div>
              </div>
            </div>

            <div className={styles.aiChatTimeline}>
              {chatMessages.map((msg, index) => (
                <div 
                  key={index} 
                  className={`${styles.chatBubble} ${msg.sender === 'ai' ? styles.bubbleAi : styles.bubbleUser}`}
                >
                  <p className={styles.bubbleText}>{msg.text}</p>
                  <span className={styles.bubbleTime}>{msg.time}</span>
                </div>
              ))}
            </div>

            <form onSubmit={handleSendMessage} className={styles.aiChatInputArea}>
              <input 
                type="text" 
                value={chatInput}
                onChange={(e) => setChatInput(e.target.value)}
                placeholder="اسأل المساعد عن أي شيء..."
                className={styles.aiChatInput}
              />
              <button type="submit" className={styles.aiSendButton}>
                <Send size={16} fill="currentColor" />
              </button>
            </form>
          </div>

          {/* Daily health stats */}
          <div className={`glass-panel ${styles.healthPanel}`}>
            <h3 className={styles.healthTitle}>
              <Activity size={18} className={styles.healthTitleIcon} />
              <span>إحصائيات التشغيل اليومية</span>
            </h3>
            
            <div className={styles.healthStatsList}>
              <div className={styles.healthStatItem}>
                <div className={styles.healthStatLabels}>
                  <span className={styles.healthStatName}>استهلاك الخادم</span>
                  <span className={styles.healthStatVal}>٤٢٪</span>
                </div>
                <div className={styles.progressBar}>
                  <div className={styles.progressFill} style={{ width: '42%', backgroundColor: 'var(--accent)' }}></div>
                </div>
              </div>

              <div className={styles.healthStatItem}>
                <div className={styles.healthStatLabels}>
                  <span className={styles.healthStatName}>طلبات API</span>
                  <span className={styles.healthStatVal}>٨٩٪</span>
                </div>
                <div className={styles.progressBar}>
                  <div className={styles.progressFill} style={{ width: '89%', backgroundColor: 'var(--accent)', boxShadow: '0 0 10px rgba(182, 246, 41, 0.4)' }}></div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
