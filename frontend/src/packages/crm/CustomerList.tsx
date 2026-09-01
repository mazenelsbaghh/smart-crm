'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { useToast } from '../../context/toast-context';
import { crmService, Customer } from '../../services/crm';
import { api } from '../../services/api';
import CustomerDetail from '../../components/shared/CustomerDetail';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
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

const formatCount = (value: number) => Number.isFinite(value) ? value.toLocaleString('ar-EG') : 'غير متاح';
const formatPhone = (value: string) => value.startsWith('+') ? value : `+${value}`;

export default function CustomerList() {
  const { activeProject } = useAuth();
  const { showToast } = useToast();
  
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  
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
  const [pendingImportFile, setPendingImportFile] = useState<File | null>(null);
  const [importResult, setImportResult] = useState<{
    matchedCount: number;
    newCount: number;
    removedBookingsCount: number;
    cancelledFollowUpsCount: number;
    matchedPhones: string[];
    newPhones: string[];
  } | null>(null);

  useEffect(() => {
    const resetTimer = window.setTimeout(() => {
      setSelectedLabel('All');
      setCurrentPage(1);
    }, 0);
    return () => window.clearTimeout(resetTimer);
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
  const loadRequestIdRef = React.useRef(0);
  const importFileInputRef = React.useRef<HTMLInputElement>(null);

  const fetchCustomers = useCallback(async () => {
    const requestId = ++loadRequestIdRef.current;
    if (!activeProject) {
      setCustomers([]);
      setLoading(false);
      setLoadError('تعذر تحميل مساحة العمل. أعد المحاولة أو تواصل مع المدير.');
      return;
    }
    try {
      setLoading(true);
      setLoadError(null);
      setCustomers([]);
      const data = await crmService.getCustomers(activeProject.id);
      if (requestId !== loadRequestIdRef.current) return;
      setCustomers(data);
    } catch (e) {
      if (requestId !== loadRequestIdRef.current) return;
      console.error('Failed to load CRM customers', e);
      setLoadError('تعذر تحميل سجل العملاء. لم يتم استبدال البيانات بقائمة فارغة.');
    } finally {
      if (requestId === loadRequestIdRef.current) setLoading(false);
    }
  }, [activeProject]);

  const handleGenerateAIProfile = async (customerId: string) => {
    if (!activeProject) return;
    setGeneratingIds(prev => [...prev, customerId]);
    try {
      await api.post(`/api/projects/${activeProject.id}/customers/${customerId}/memory/generate`);
      await fetchCustomers();
      showToast('تم تحديث وتوليد ملف التعريف بالذكاء الاصطناعي بنجاح! ✨', 'success');
    } catch (err) {
      console.error('Failed to generate customer profile', err);
      showToast('فشل توليد ملف التعريف. تأكد من وجود رسائل سابقة للعميل.', 'error');
    } finally {
      setGeneratingIds(prev => prev.filter(id => id !== customerId));
    }
  };

  const handleDownloadTemplate = async () => {
    try {
      const XLSX = await import('xlsx');
      const headers = [['رقم الهاتف']];

      const worksheet = XLSX.utils.aoa_to_sheet(headers);
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
      URL.revokeObjectURL(url);
    } catch (error) {
      console.error('Failed to generate blacklist import template', error);
      showToast('تعذر إنشاء نموذج Excel. حاول مجددًا.', 'error');
    }
  };

  const importPaidCustomers = async (file: File) => {
    if (!activeProject) return;
    const projectId = activeProject.id;
    setImporting(true);
    setImportResult(null);
    try {
      const XLSX = await import('xlsx');
      const reader = new FileReader();
      reader.onload = async (evt) => {
        try {
          const bstr = evt.target?.result;
          const workbook = XLSX.read(bstr, { type: 'binary' });
          const worksheetName = workbook.SheetNames[0];
          const worksheet = workbook.Sheets[worksheetName];
          const rawData = XLSX.utils.sheet_to_json<Record<string, unknown>>(worksheet);

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
          const response = await api.post<{
            matchedCount: number;
            newCount: number;
            removedBookingsCount: number;
            cancelledFollowUpsCount: number;
            matchedPhones: string[];
            newPhones: string[];
          }>(`/api/projects/${projectId}/import-blacklist`, phones);
          const data = response.data;
          
          setImportResult({
            matchedCount: data.matchedCount,
            newCount: data.newCount,
            removedBookingsCount: data.removedBookingsCount,
            cancelledFollowUpsCount: data.cancelledFollowUpsCount,
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
      reader.onerror = () => {
        showToast('تعذر قراءة الملف من الجهاز. اختر ملفًا صالحًا وحاول مرة أخرى.', 'error');
        setImporting(false);
      };
      reader.readAsBinaryString(file);
    } catch (err) {
      console.error(err);
      showToast('حدث خطأ أثناء تحميل الملف.', 'error');
      setImporting(false);
    }
  };

  useEffect(() => {
    const loadTimer = window.setTimeout(() => void fetchCustomers(), 0);
    return () => window.clearTimeout(loadTimer);
  }, [fetchCustomers]);

  if (loading) {
    return (
      <div className={styles.loadingBox}>
        <div className={styles.spinner}></div>
        <p>جاري تحميل سجل العملاء...</p>
      </div>
    );
  }

  if (loadError && customers.length === 0) {
    return (
      <div className={styles.loadingBox} role="alert">
        <p>{loadError}</p>
        {activeProject && (
          <button type="button" className={styles.editButton} onClick={() => void fetchCustomers()}>
            إعادة المحاولة
          </button>
        )}
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
          type="button"
          onClick={() => setShowImportPanel(!showImportPanel)}
          aria-expanded={showImportPanel}
          aria-controls="blacklist-import-panel"
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
            transition: 'background-color 0.2s, border-color 0.2s, color 0.2s'
          }}
        >
          <Upload size={16} />
          استيراد الطلاب المدفوعة (Excel)
        </button>
      </div>

      {/* Import Blacklist Panel */}
      {showImportPanel && (
        <section id="blacklist-import-panel" className="glass-panel" aria-labelledby="blacklist-import-heading" style={{ padding: 'var(--space-md)', marginBottom: 'var(--space-md)', display: 'flex', flexDirection: 'column', gap: 'var(--space-sm)' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h3 id="blacklist-import-heading" style={{ fontSize: '0.95rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '8px', color: 'hsl(var(--text-primary))' }}>
              <ShieldAlert size={18} style={{ color: 'hsl(var(--accent-warning))' }} />
              استيراد الطلاب الذين قاموا بالدفع لحظرهم تلقائياً من الرد
            </h3>
            <button
              type="button"
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
          <p role="note" style={{ fontSize: '0.85rem', color: 'hsl(var(--text-secondary))', lineHeight: '1.4' }}>
            سيحظر النظام الرد الآلي للأرقام، وينشئ العملاء غير الموجودين، ويحذف الحجوزات المطابقة، ويلغي المتابعات المعلقة. ستظهر خطوة تأكيد بعد اختيار الملف وقبل إرسال أي بيانات.
          </p>
          <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-md)', marginTop: 'var(--space-xs)' }}>
            <button
              type="button"
              onClick={() => importFileInputRef.current?.click()}
              disabled={importing}
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
            </button>
            <input
              ref={importFileInputRef}
              type="file"
              accept=".xlsx,.xls"
              onChange={(event) => {
                setPendingImportFile(event.target.files?.[0] ?? null);
                event.target.value = '';
              }}
              disabled={importing}
              hidden
              tabIndex={-1}
            />
          </div>

          {/* Results display */}
          {importResult && (
            <div style={{ marginTop: 'var(--space-md)', borderTop: '1px solid var(--border-subtle)', paddingTop: 'var(--space-md)' }}>
              <div style={{ display: 'flex', gap: 'var(--space-lg)', marginBottom: 'var(--space-md)' }}>
                <div style={{ padding: '10px 14px', background: 'rgba(16, 185, 129, 0.06)', border: '1px solid rgba(16, 185, 129, 0.15)', borderRadius: 'var(--radius-md)', flex: 1 }}>
                  <div style={{ fontSize: '0.8rem', color: 'var(--text-soft)', fontWeight: 600, marginBottom: '2px' }}>تمت مطابقتهم وحظرهم (مسجلين بالفعل):</div>
                  <div style={{ fontSize: '1.4rem', fontWeight: 700, color: 'rgb(16, 185, 129)' }}>{formatCount(importResult.matchedCount)} طالب</div>
                </div>
                <div style={{ padding: '10px 14px', background: 'rgba(249, 115, 22, 0.06)', border: '1px solid rgba(249, 115, 22, 0.15)', borderRadius: 'var(--radius-md)', flex: 1 }}>
                  <div style={{ fontSize: '0.8rem', color: 'var(--text-soft)', fontWeight: 600, marginBottom: '2px' }}>أرقام جديدة غير مسجلة (حظر وقائي):</div>
                  <div style={{ fontSize: '1.4rem', fontWeight: 700, color: 'rgb(249, 115, 22)' }}>{formatCount(importResult.newCount)} طالب</div>
                </div>
              </div>
              <p role="status" style={{ color: 'var(--text-soft)', fontSize: '0.82rem', marginBottom: 'var(--space-md)' }}>
                حُذفت {formatCount(importResult.removedBookingsCount)} حجوزات مطابقة، وأُلغيت {formatCount(importResult.cancelledFollowUpsCount)} متابعات معلقة.
              </p>

              {/* Detailed Lists */}
              <div style={{ display: 'flex', gap: 'var(--space-md)', flexDirection: 'row', flexWrap: 'wrap' }}>
                {importResult.matchedPhones.length > 0 && (
                  <div style={{ flex: 1, minWidth: '240px', background: 'var(--surface-muted)', padding: 'var(--space-sm)', borderRadius: 'var(--radius-md)', border: '1px solid var(--border-subtle)' }}>
                    <div style={{ fontSize: '0.8rem', fontWeight: 700, marginBottom: '6px', color: 'rgb(16, 185, 129)' }}>أرقام الطلاب المطابِقة (تم حظرهم):</div>
                    <div style={{ maxHeight: '120px', overflowY: 'auto', fontSize: '0.78rem', fontFamily: 'monospace', color: 'var(--text-strong)', lineHeight: '1.4' }}>
                      {importResult.matchedPhones.map(phone => (
                        <div key={phone} style={{ padding: '2px 0' }}>{formatPhone(phone)}</div>
                      ))}
                    </div>
                  </div>
                )}
                {importResult.newPhones.length > 0 && (
                  <div style={{ flex: 1, minWidth: '240px', background: 'var(--surface-muted)', padding: 'var(--space-sm)', borderRadius: 'var(--radius-md)', border: '1px solid var(--border-subtle)' }}>
                    <div style={{ fontSize: '0.8rem', fontWeight: 700, marginBottom: '6px', color: 'rgb(249, 115, 22)' }}>أرقام الطلاب الجدد (حظر وقائي):</div>
                    <div style={{ maxHeight: '120px', overflowY: 'auto', fontSize: '0.78rem', fontFamily: 'monospace', color: 'var(--text-strong)', lineHeight: '1.4' }}>
                      {importResult.newPhones.map(phone => (
                        <div key={phone} style={{ padding: '2px 0' }}>{formatPhone(phone)}</div>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            </div>
          )}
        </section>
      )}

      {/* AI Smart Labels Statistics & Filter Bar */}
      <div className={styles.labelsStatsBar}>
        <button 
          type="button"
          className={`${styles.labelCard} ${selectedLabel === 'All' ? styles.labelCardActive : ''}`}
          onClick={() => {
            setSelectedLabel('All');
            setCurrentPage(1);
          }}
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
            onClick={() => {
              setSelectedLabel(stat.name);
              setCurrentPage(1);
            }}
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
            aria-label="بحث في العملاء بالاسم أو رقم الهاتف"
            placeholder="ابحث بالاسم أو رقم الهاتف..." 
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setCurrentPage(1);
            }}
            className={`neon-input ${styles.searchInput}`}
          />
        </div>

        <div className={styles.filtersGroup}>
          <div className={styles.filterSelectWrapper}>
            <UserCheck size={16} className={styles.filterIcon} />
            <select 
              aria-label="تصفية العملاء حسب مرحلة البيع"
              value={stageFilter} 
              onChange={(e) => {
                setStageFilter(e.target.value);
                setCurrentPage(1);
              }}
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
              aria-label="تصفية العملاء حسب المدينة"
              value={cityFilter} 
              onChange={(e) => {
                setCityFilter(e.target.value);
                setCurrentPage(1);
              }}
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
              <caption className="sr-only">سجل العملاء وبيانات CRM</caption>
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
                          {(c.name || '?').charAt(0).toUpperCase()}
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
                        <span className={styles.scoreVal}>{Number.isFinite(c.leadScore) ? c.leadScore : 'غير متاح'}</span>
                      </div>
                    </td>
                    <td className={styles.td}>
                      <span className={styles.stageBadge} style={{
                        backgroundColor: c.pipelineStage === 'Won' ? 'rgba(16, 185, 129, 0.15)' : 
                                         c.pipelineStage === 'Lost' ? 'rgba(239, 68, 68, 0.15)' : 'rgba(255,255,255,0.04)',
                        color: c.pipelineStage === 'Won' ? 'hsl(140, 100%, 65%)' : 
                               c.pipelineStage === 'Lost' ? 'hsl(0, 100%, 65%)' : 'hsl(var(--text-secondary))'
                      }}>
                        {c.pipelineStage || 'غير محددة'}
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
                          aria-label={`تعديل ملف ${c.name || c.phoneNumber}`}
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
                          aria-label={`توليد ملف ذكاء اصطناعي للعميل ${c.name || c.phoneNumber}`}
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
                    aria-label="عدد العملاء في الصفحة"
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
                    aria-label="الصفحة التالية"
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
                          aria-label={`الصفحة ${page}`}
                          aria-current={currentPage === page ? 'page' : undefined}
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
                    aria-label="الصفحة السابقة"
                  >
                    <ChevronRight size={16} />
                  </button>
                </div>
              </div>
            )}
          </div>
        )}
      </div>
      <ConfirmDialog
        isOpen={pendingImportFile !== null}
        title="تأكيد الاستيراد والحذف"
        message={`سيتم حظر الرد الآلي لكل رقم في ${pendingImportFile?.name ?? 'الملف'}، وإنشاء العملاء غير الموجودين، وحذف حجوزاتهم المطابقة، وإلغاء متابعاتهم المعلقة. لا يمكن التراجع عن حذف الحجوزات؛ راجع الملف قبل المتابعة.`}
        confirmLabel="تنفيذ الاستيراد"
        onConfirm={() => {
          const selectedFile = pendingImportFile;
          setPendingImportFile(null);
          if (selectedFile) void importPaidCustomers(selectedFile);
        }}
        onCancel={() => setPendingImportFile(null)}
      />
    </div>
  );
}
