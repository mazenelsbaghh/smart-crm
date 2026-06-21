'use client';

import React, { useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { useToast } from '../../context/toast-context';
import { crmService, Customer } from '../../services/crm';
import { api } from '../../services/api';
import CustomerDetail from '../../components/shared/CustomerDetail';
import { 
  Search, 
  MapPin, 
  UserCheck, 
  Sparkles,
  Edit2,
  ChevronLeft,
  ChevronRight
} from 'lucide-react';
import styles from './crm.module.css';

export default function CustomerList() {
  const { activeProject } = useAuth();
  const { showToast } = useToast();
  
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [loading, setLoading] = useState(true);
  
  // Pagination State
  const [currentPage, setCurrentPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  
  // Search & Filter
  const [search, setSearch] = useState('');
  const [stageFilter, setStageFilter] = useState('All');
  const [cityFilter, setCityFilter] = useState('All');
  const [selectedLabel, setSelectedLabel] = useState('All');

  // Reset to first page when search, filters, label or project change
  useEffect(() => {
    setCurrentPage(1);
  }, [search, stageFilter, cityFilter, selectedLabel, activeProject]);

  useEffect(() => {
    setSelectedLabel('All');
  }, [activeProject]);

  // Calculate label counts dynamically from ALL customers
  const labelStats = React.useMemo(() => {
    const stats: Record<string, number> = {};
    customers.forEach(c => {
      const lbl = c.label || 'بدون تصنيف';
      stats[lbl] = (stats[lbl] || 0) + 1;
    });
    return Object.entries(stats).map(([name, count]) => ({ name, count }));
  }, [customers]);
  
  // Modal state
  const [selectedCustomerId, setSelectedCustomerId] = useState<string | null>(null);
  const [generatingIds, setGeneratingIds] = useState<string[]>([]);

  const fetchCustomers = async () => {
    if (!activeProject) return;
    try {
      setLoading(true);
      const data = await crmService.getCustomers(activeProject.id);
      setCustomers(data);
    } catch (e) {
      console.error('Failed to load CRM customers', e);
    } finally {
      setLoading(false);
    }
  };

  const handleGenerateAIProfile = async (customerId: string) => {
    if (!activeProject) return;
    setGeneratingIds(prev => [...prev, customerId]);
    try {
      await api.post(`/api/projects/${activeProject.id}/customers/${customerId}/memory/generate`);
      await fetchCustomers();
      showToast('تم تحديث وتوليد ملف التعريف بالذكاء الاصطناعي بنجاح! ✨', 'success');
    } catch (err: any) {
      console.error('Failed to generate customer profile', err);
      const errMsg = err.response?.data || 'فشل توليد ملف التعريف. تأكد من وجود رسائل سابقة للعميل.';
      showToast(errMsg, 'error');
    } finally {
      setGeneratingIds(prev => prev.filter(id => id !== customerId));
    }
  };

  useEffect(() => {
    fetchCustomers();
  }, [activeProject]);

  if (loading) {
    return (
      <div className={styles.loadingBox}>
        <div className={styles.spinner}></div>
        <p>جاري تحميل سجل العملاء...</p>
      </div>
    );
  }

  if (selectedCustomerId && activeProject) {
    return (
      <div className={styles.container}>
        <CustomerDetail 
          customerId={selectedCustomerId}
          projectId={activeProject.id}
          onClose={() => setSelectedCustomerId(null)}
          onUpdate={fetchCustomers}
          isInline={true}
        />
      </div>
    );
  }

  // Extract unique stages and cities for filters
  const stages = ['All', ...Array.from(new Set(customers.map(c => c.pipelineStage).filter(Boolean)))];
  const cities = ['All', ...Array.from(new Set(customers.map(c => c.city).filter(Boolean)))];

  // Filtered List
  const filteredCustomers = customers.filter(c => {
    const matchesSearch = 
      (c.name || '').toLowerCase().includes(search.toLowerCase()) || 
      (c.phoneNumber || '').includes(search);
    
    const matchesStage = stageFilter === 'All' || c.pipelineStage === stageFilter;
    const matchesCity = cityFilter === 'All' || c.city === cityFilter;
    const matchesLabel = selectedLabel === 'All' || (c.label || 'بدون تصنيف') === selectedLabel;

    return matchesSearch && matchesStage && matchesCity && matchesLabel;
  });

  // Paginated List
  const totalPages = Math.ceil(filteredCustomers.length / pageSize) || 1;
  const paginatedCustomers = filteredCustomers.slice(
    (currentPage - 1) * pageSize,
    currentPage * pageSize
  );

  return (
    <div className={styles.container}>
      {/* Title */}
      <div className={styles.header}>
        <div>
          <h1 className={styles.pageTitle}>إدارة العملاء CRM</h1>
          <p className={styles.pageSubtitle}>راجع بيانات العملاء والتقييمات والوسوم ومراحل البيع</p>
        </div>
      </div>

      {/* AI Smart Labels Statistics & Filter Bar */}
      <div className={styles.labelsStatsBar}>
        <button 
          type="button"
          className={`${styles.labelCard} ${selectedLabel === 'All' ? styles.labelCardActive : ''}`}
          onClick={() => setSelectedLabel('All')}
          style={{ font: 'inherit', color: 'inherit' }}
        >
          <span className={styles.labelCardName}>كل التصنيفات</span>
          <span className={styles.labelCardCount}>{customers.length}</span>
        </button>
        {labelStats.map(stat => (
          <button 
            key={stat.name}
            type="button"
            className={`${styles.labelCard} ${selectedLabel === stat.name ? styles.labelCardActive : ''}`}
            onClick={() => setSelectedLabel(stat.name)}
            style={{ font: 'inherit', color: 'inherit' }}
          >
            <span className={styles.labelCardName}>{stat.name}</span>
            <span className={styles.labelCardCount}>{stat.count}</span>
          </button>
        ))}
      </div>

      {/* Search and Filters panel */}
      <div className={`glass-panel ${styles.filterBar}`}>
        <div className={styles.searchWrapper}>
          <Search size={18} className={styles.searchIcon} />
          <input 
            type="text" 
            placeholder="ابحث بالاسم أو رقم الهاتف..." 
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className={`neon-input ${styles.searchInput}`}
          />
        </div>

        <div className={styles.filtersGroup}>
          <div className={styles.filterSelectWrapper}>
            <UserCheck size={16} className={styles.filterIcon} />
            <select 
              value={stageFilter} 
              onChange={(e) => setStageFilter(e.target.value)}
              className={`neon-input ${styles.filterSelect}`}
            >
              {stages.map(st => (
                <option key={st} value={st}>{st === 'All' ? 'كل المراحل' : st}</option>
              ))}
            </select>
          </div>

          <div className={styles.filterSelectWrapper}>
            <MapPin size={16} className={styles.filterIcon} />
            <select 
              value={cityFilter} 
              onChange={(e) => setCityFilter(e.target.value)}
              className={`neon-input ${styles.filterSelect}`}
            >
              {cities.map(ct => (
                <option key={ct} value={ct}>{ct === 'All' ? 'كل المدن' : ct}</option>
              ))}
            </select>
          </div>
        </div>
      </div>

      {/* Customers Table / Grid */}
      <div className={`glass-panel ${styles.tablePanel}`}>
        {filteredCustomers.length === 0 ? (
          <div className={styles.emptyTable}>لا يوجد عملاء مطابقون للفلاتر الحالية.</div>
        ) : (
          <div style={{ overflowX: 'auto' }}>
            <table className={styles.table}>
              <thead>
                <tr>
                  <th className={styles.th}>العميل</th>
                  <th className={styles.th}>المدينة</th>
                  <th className={styles.th}>التقييم</th>
                  <th className={styles.th}>المرحلة</th>
                  <th className={styles.th}>الميزانية</th>
                  <th className={styles.th}>الوسوم</th>
                  <th className={styles.th} style={{ textAlign: 'center' }}>إجراءات</th>
                </tr>
              </thead>
              <tbody>
                {paginatedCustomers.map(c => (
                  <tr key={c.id} className={styles.tr}>
                    <td className={styles.td}>
                      <button 
                        type="button"
                        className={styles.customerCell} 
                        onClick={() => setSelectedCustomerId(c.id)} 
                        style={{ border: 'none', background: 'none', display: 'flex', width: '100%', textAlign: 'right', font: 'inherit', color: 'inherit', padding: 0 }}
                      >
                        <div className={styles.avatar}>
                          {(c.name || 'C').charAt(0).toUpperCase()}
                        </div>
                        <div className={styles.customerNameBox}>
                          <span className={styles.customerName}>{c.name || 'عميل بدون اسم'}</span>
                          <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                            <span className={styles.customerPhone}>{c.phoneNumber}</span>
                            {c.label && (
                              <span className={styles.smartLabelBadge}>{c.label}</span>
                            )}
                            {c.isBlacklisted && (
                              <span className={styles.smartLabelBadge} style={{ backgroundColor: 'rgba(239, 68, 68, 0.15)', color: 'hsl(0, 100%, 65%)', border: '1px solid rgba(239, 68, 68, 0.25)' }}>
                                حظر رد آلي
                              </span>
                            )}
                          </div>
                        </div>
                      </button>
                    </td>
                    <td className={styles.td}>
                      <span className={styles.locationText}>
                        {c.city ? (
                          <>
                            <MapPin size={12} style={{ marginRight: '4px', verticalAlign: 'middle' }} />
                            {c.city}
                          </>
                        ) : (
                          <span style={{ color: 'hsl(var(--text-muted))' }}>غير محدد</span>
                        )}
                      </span>
                    </td>
                    <td className={styles.td}>
                      <div className={styles.scoreBox}>
                        <Sparkles size={12} style={{ color: 'hsl(var(--accent-secondary))', marginRight: '4px' }} />
                        <span className={styles.scoreVal}>{c.leadScore || 0}</span>
                      </div>
                    </td>
                    <td className={styles.td}>
                      <span className={styles.stageBadge} style={{
                        backgroundColor: c.pipelineStage === 'Won' ? 'rgba(16, 185, 129, 0.15)' : 
                                         c.pipelineStage === 'Lost' ? 'rgba(239, 68, 68, 0.15)' : 'rgba(255,255,255,0.04)',
                        color: c.pipelineStage === 'Won' ? 'hsl(140, 100%, 65%)' : 
                               c.pipelineStage === 'Lost' ? 'hsl(0, 100%, 65%)' : 'hsl(var(--text-secondary))'
                      }}>
                        {c.pipelineStage || 'جديد'}
                      </span>
                    </td>
                    <td className={styles.td}>
                      <span className={styles.budgetText}>
                        {c.budget ? `$${c.budget.toLocaleString()}` : <span style={{ color: 'hsl(var(--text-muted))' }}>-</span>}
                      </span>
                    </td>
                    <td className={styles.td}>
                      <div className={styles.tagsContainer}>
                        {c.tags && c.tags.slice(0, 2).map(tag => (
                          <span key={tag} className={styles.tagBadge}>{tag}</span>
                        ))}
                        {c.tags && c.tags.length > 2 && (
                          <span className={styles.tagMore}>+{c.tags.length - 2}</span>
                        )}
                      </div>
                    </td>
                    <td className={styles.td} style={{ textAlign: 'center' }}>
                      <div style={{ display: 'flex', gap: '6px', justifyContent: 'center' }}>
                        <button 
                          onClick={() => setSelectedCustomerId(c.id)} 
                          className={styles.editButton}
                          title="تعديل الملف"
                        >
                          <Edit2 size={14} />
                        </button>
                        <button 
                          type="button"
                          onClick={() => handleGenerateAIProfile(c.id)} 
                          disabled={generatingIds.includes(c.id)}
                          className={styles.editButton}
                          style={{
                            background: 'rgba(168, 85, 247, 0.12)',
                            borderColor: 'rgba(168, 85, 247, 0.25)',
                            color: 'hsl(270, 84%, 75%)'
                          }}
                          title="تحديث ذكي بالـ AI"
                        >
                          {generatingIds.includes(c.id) ? (
                            <div className={styles.spinnerMini} />
                          ) : (
                            <Sparkles size={14} />
                          )}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            
            {/* Pagination Controls */}
            {filteredCustomers.length > 0 && (
              <div className={styles.pagination}>
                <div className={styles.paginationInfo}>
                  <span>عرض السطور:</span>
                  <select
                    value={pageSize}
                    onChange={(e) => {
                      setPageSize(Number(e.target.value));
                      setCurrentPage(1);
                    }}
                    className={styles.paginationSelect}
                  >
                    {[5, 10, 25, 50].map((size) => (
                      <option key={size} value={size}>
                        {size}
                      </option>
                    ))}
                  </select>
                  <span style={{ marginRight: '12px', marginLeft: '12px' }}>
                    عرض {Math.min((currentPage - 1) * pageSize + 1, filteredCustomers.length)} - {Math.min(currentPage * pageSize, filteredCustomers.length)} من {filteredCustomers.length}
                  </span>
                </div>

                <div className={styles.paginationControls}>
                  <button
                    onClick={() => setCurrentPage(prev => Math.min(prev + 1, totalPages))}
                    disabled={currentPage === totalPages}
                    className={styles.paginationBtn}
                    title="الصفحة التالية"
                  >
                    <ChevronLeft size={16} />
                  </button>

                  {Array.from({ length: totalPages }, (_, i) => i + 1)
                    .filter(page => page === 1 || page === totalPages || Math.abs(page - currentPage) <= 1)
                    .map((page, idx, arr) => {
                      const elements = [];
                      if (idx > 0 && page - arr[idx - 1] > 1) {
                        elements.push(<span key={`ellipsis-${page}`} style={{ color: 'var(--text-soft)', padding: '0 4px' }}>...</span>);
                      }
                      elements.push(
                        <button
                          key={page}
                          onClick={() => setCurrentPage(page)}
                          className={`${styles.paginationBtn} ${currentPage === page ? styles.paginationBtnActive : ''}`}
                        >
                          {page}
                        </button>
                      );
                      return elements;
                    })}

                  <button
                    onClick={() => setCurrentPage(prev => Math.max(prev - 1, 1))}
                    disabled={currentPage === 1}
                    className={styles.paginationBtn}
                    title="الصفحة السابقة"
                  >
                    <ChevronRight size={16} />
                  </button>
                </div>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
