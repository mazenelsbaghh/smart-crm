'use client';

import React, { useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { useToast } from '../../context/toast-context';
import { crmService, Customer } from '../../services/crm';
import { api } from '../../services/api';
import CustomerDetail from '../../components/shared/CustomerDetail';
import * as XLSX from 'xlsx';
import { 
  Search, 
  MapPin, 
  UserCheck, 
  Sparkles,
  Edit2,
  ChevronLeft,
  ChevronRight,
  Upload,
  FileDown,
  ShieldAlert
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

  const [importing, setImporting] = useState(false);
  const [showImportPanel, setShowImportPanel] = useState(false);
  const [importResult, setImportResult] = useState<{
    matchedCount: number;
    newCount: number;
    matchedPhones: string[];
    newPhones: string[];
  } | null>(null);

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

  const handleDownloadTemplate = () => {
    const headers = [['رقم الهاتف']];
    const examples = [
      ['01068690092'],
      ['20122334455']
    ];
    const wsData = [...headers, ...examples];

    const worksheet = XLSX.utils.aoa_to_sheet(wsData);
    const workbook = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(workbook, worksheet, 'قالب أرقام الطلاب');

    const excelBuffer = XLSX.write(workbook, { bookType: 'xlsx', type: 'array' });
    const blob = new Blob([excelBuffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.setAttribute('href', url);
    link.setAttribute('download', 'paid_students_template.xlsx');
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  };

  const handleImportExcel = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files;
    if (!files || files.length === 0) return;
    const file = files[0];

    setImporting(true);
    setImportResult(null);
    try {
      const reader = new FileReader();
      reader.onload = async (evt) => {
        try {
          const bstr = evt.target?.result;
          const workbook = XLSX.read(bstr, { type: 'binary' });
          const worksheetName = workbook.SheetNames[0];
          const worksheet = workbook.Sheets[worksheetName];
          const rawData = XLSX.utils.sheet_to_json<any>(worksheet);

          // Extract only the phone numbers from any column that might represent phone
          const phones = rawData.map(row => {
            const phone = row['رقم الهاتف'] || row['Phone'] || row['phone'] || row['الرقم'] || Object.values(row)[0] || '';
            return String(phone).trim();
          }).filter(p => p !== '');

          if (phones.length === 0) {
            showToast('لم يتم العثور على أي أرقام هواتف صالحة في الملف المرفوع.', 'error');
            setImporting(false);
            return;
          }

          // Send to backend
          const response = await api.post(`/api/projects/${activeProject!.id}/import-blacklist`, phones);
          const data = response.data;
          
          setImportResult({
            matchedCount: data.matchedCount,
            newCount: data.newCount,
            matchedPhones: data.matchedPhones || [],
            newPhones: data.newPhones || []
          });

          showToast(`تمت معالجة الملف بنجاح!`, 'success');
          void fetchCustomers();
        } catch (err) {
          console.error(err);
          showToast('فشل قراءة ملف Excel. يرجى التحقق من الصيغة.', 'error');
        } finally {
          setImporting(false);
        }
      };
      reader.readAsBinaryString(file);
    } catch (err) {
      console.error(err);
      showToast('حدث خطأ أثناء تحميل الملف.', 'error');
      setImporting(false);
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
      <div className={styles.header} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 'var(--space-md)' }}>
        <div>
          <h1 className={styles.pageTitle}>إدارة العملاء CRM</h1>
          <p className={styles.pageSubtitle}>راجع بيانات العملاء والتقييمات والوسوم ومراحل البيع</p>
        </div>
        <button
          onClick={() => setShowImportPanel(!showImportPanel)}
          className={styles.editButton}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            fontSize: '0.85rem',
            padding: '8px 16px',
            background: 'rgba(16, 185, 129, 0.12)',
            borderColor: 'rgba(16, 185, 129, 0.25)',
            color: 'rgb(16, 185, 129)',
            borderRadius: 'var(--radius-md)',
            cursor: 'pointer',
            fontWeight: 600,
            transition: 'all 0.2s'
          }}
        >
          <Upload size={16} />
          استيراد الطلاب المدفوعة (Excel)
        </button>
      </div>

      {/* Import Blacklist Panel */}
      {showImportPanel && (
        <div className="glass-panel" style={{ padding: 'var(--space-md)', marginBottom: 'var(--space-md)', display: 'flex', flexDirection: 'column', gap: 'var(--space-sm)' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h3 style={{ fontSize: '0.95rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '8px', color: 'hsl(var(--text-primary))' }}>
              <ShieldAlert size={18} style={{ color: 'hsl(var(--accent-warning))' }} />
              استيراد الطلاب الذين قاموا بالدفع لحظرهم تلقائياً من الرد
            </h3>
            <button
              onClick={handleDownloadTemplate}
              className={styles.editButton}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '6px',
                fontSize: '0.8rem',
                padding: '4px 10px',
                background: 'rgba(59, 130, 246, 0.12)',
                borderColor: 'rgba(59, 130, 246, 0.25)',
                color: 'rgb(59, 130, 246)',
                cursor: 'pointer'
              }}
            >
              <FileDown size={14} />
              تحميل نموذج الملف (Template)
            </button>
          </div>
          <p style={{ fontSize: '0.85rem', color: 'hsl(var(--text-secondary))', lineHeight: '1.4' }}>
            ارفع ملف Excel (.xlsx) يحتوي على قائمة الطلاب الذين دفعوا بالفعل. سيقوم النظام بالبحث عنهم وحظرهم تلقائياً بحيث لا يقوم بوت الرد الآلي بإرسال أي رسائل لهم بعد الآن.
          </p>
          <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-md)', marginTop: 'var(--space-xs)' }}>
            <label 
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
                fontSize: '0.85rem',
                padding: '10px 16px',
                background: 'var(--surface-muted)',
                border: '1px dashed var(--border-subtle)',
                borderRadius: 'var(--radius-md)',
                color: 'var(--text-soft)',
                cursor: importing ? 'not-allowed' : 'pointer',
                fontWeight: 600,
                opacity: importing ? 0.6 : 1
              }}
            >
              <Upload size={16} />
              {importing ? 'جاري الاستيراد وقراءة الملف...' : 'اختر ملف Excel لرفعه وحظر الطلاب'}
              <input
                type="file"
                accept=".xlsx, .xls"
                onChange={handleImportExcel}
                disabled={importing}
                style={{ display: 'none' }}
              />
            </label>
          </div>

          {/* Results display */}
          {importResult && (
            <div style={{ marginTop: 'var(--space-md)', borderTop: '1px solid var(--border-subtle)', paddingTop: 'var(--space-md)' }}>
              <div style={{ display: 'flex', gap: 'var(--space-lg)', marginBottom: 'var(--space-md)' }}>
                <div style={{ padding: '10px 14px', background: 'rgba(16, 185, 129, 0.06)', border: '1px solid rgba(16, 185, 129, 0.15)', borderRadius: 'var(--radius-md)', flex: 1 }}>
                  <div style={{ fontSize: '0.8rem', color: 'var(--text-soft)', fontWeight: 600, marginBottom: '2px' }}>تمت مطابقتهم وحظرهم (مسجلين بالفعل):</div>
                  <div style={{ fontSize: '1.4rem', fontWeight: 700, color: 'rgb(16, 185, 129)' }}>{importResult.matchedCount} طالب</div>
                </div>
                <div style={{ padding: '10px 14px', background: 'rgba(249, 115, 22, 0.06)', border: '1px solid rgba(249, 115, 22, 0.15)', borderRadius: 'var(--radius-md)', flex: 1 }}>
                  <div style={{ fontSize: '0.8rem', color: 'var(--text-soft)', fontWeight: 600, marginBottom: '2px' }}>أرقام جديدة غير مسجلة (حظر وقائي):</div>
                  <div style={{ fontSize: '1.4rem', fontWeight: 700, color: 'rgb(249, 115, 22)' }}>{importResult.newCount} طالب</div>
                </div>
              </div>

              {/* Detailed Lists */}
              <div style={{ display: 'flex', gap: 'var(--space-md)', flexDirection: 'row', flexWrap: 'wrap' }}>
                {importResult.matchedPhones.length > 0 && (
                  <div style={{ flex: 1, minWidth: '240px', background: 'var(--surface-muted)', padding: 'var(--space-sm)', borderRadius: 'var(--radius-md)', border: '1px solid var(--border-subtle)' }}>
                    <div style={{ fontSize: '0.8rem', fontWeight: 700, marginBottom: '6px', color: 'rgb(16, 185, 129)' }}>أرقام الطلاب المطابِقة (تم حظرهم):</div>
                    <div style={{ maxHeight: '120px', overflowY: 'auto', fontSize: '0.78rem', fontFamily: 'monospace', color: 'var(--text-strong)', lineHeight: '1.4' }}>
                      {importResult.matchedPhones.map(phone => (
                        <div key={phone} style={{ padding: '2px 0' }}>+{phone}</div>
                      ))}
                    </div>
                  </div>
                )}
                {importResult.newPhones.length > 0 && (
                  <div style={{ flex: 1, minWidth: '240px', background: 'var(--surface-muted)', padding: 'var(--space-sm)', borderRadius: 'var(--radius-md)', border: '1px solid var(--border-subtle)' }}>
                    <div style={{ fontSize: '0.8rem', fontWeight: 700, marginBottom: '6px', color: 'rgb(249, 115, 22)' }}>أرقام الطلاب الجدد (حظر وقائي):</div>
                    <div style={{ maxHeight: '120px', overflowY: 'auto', fontSize: '0.78rem', fontFamily: 'monospace', color: 'var(--text-strong)', lineHeight: '1.4' }}>
                      {importResult.newPhones.map(phone => (
                        <div key={phone} style={{ padding: '2px 0' }}>+{phone}</div>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            </div>
          )}
        </div>
      )}

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
