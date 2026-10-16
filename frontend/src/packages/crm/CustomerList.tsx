'use client';

import React, { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useAuth } from '../../context/auth-context';
import { useToast } from '../../context/toast-context';
import { crmService, CustomerPage } from '../../services/crm';
import { api } from '../../services/api';
import CustomerDetail from '../../components/shared/CustomerDetail';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import {
  MapPin,
  Sparkles,
  MessageSquare,
  CalendarPlus,
  SlidersHorizontal,
  ChevronLeft,
  ChevronDown,
  ChevronRight,
  Upload,
  FileDown,
  ShieldAlert,
  Users,
  UserRound,
  UserRoundPlus,
  PhoneCall,
  CalendarClock,
  CircleCheck,
  CircleX,
  ListFilter,
  type LucideIcon
} from 'lucide-react';
import styles from './crm.module.css';
import Badge from '../../components/ui/Badge';
import PageHeader from '../../components/ui/PageHeader';
import SearchField from '../../components/ui/SearchField';
import Button from '../../components/ui/Button';
import EmptyState from '../../components/ui/EmptyState';
import FilterTabs from '../../components/ui/FilterTabs';
import CustomerExport from './CustomerExport';

const formatCount = (value: number) => Number.isFinite(value) ? value.toLocaleString('ar-EG') : 'غير متاح';
const formatPhone = (value: string) => value.startsWith('+') ? value : `+${value}`;
const stageStyles: Record<string, { tone: 'info' | 'success' | 'warning' | 'danger'; icon: LucideIcon }> = {
  New: { tone: 'info', icon: UserRoundPlus }, 'عميل جديد': { tone: 'info', icon: UserRoundPlus },
  Contacted: { tone: 'info', icon: PhoneCall }, 'تواصل أولي': { tone: 'info', icon: PhoneCall },
  Qualified: { tone: 'warning', icon: CalendarClock }, 'مهتم': { tone: 'warning', icon: CalendarClock },
  Proposal: { tone: 'warning', icon: CalendarClock }, Negotiation: { tone: 'warning', icon: CalendarClock },
  'متابعة': { tone: 'warning', icon: CalendarClock }, 'حجز': { tone: 'success', icon: CircleCheck },
  Won: { tone: 'success', icon: CircleCheck }, Lost: { tone: 'danger', icon: CircleX },
};

export default function CustomerList() {
  const { activeProject } = useAuth();
  const { showToast } = useToast();

  const [customerPage, setCustomerPage] = useState<CustomerPage | null>(null);
  const [loadedProjectId, setLoadedProjectId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  // Pagination State
  const [currentPage, setCurrentPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  // Search & Filter
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [stageFilter, setStageFilter] = useState('All');
  const [cityFilter, setCityFilter] = useState('All');
  const [selectedLabel, setSelectedLabel] = useState('All');
  const [showTools, setShowTools] = useState(false);
  const [showFilters, setShowFilters] = useState(false);
  const [detailSection, setDetailSection] = useState<'profile' | 'followups'>('profile');
  const [expandedCustomerId, setExpandedCustomerId] = useState<string | null>(null);

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

  const visiblePage = loadedProjectId === activeProject?.id ? customerPage : null;
  const customers = visiblePage?.items ?? [];
  const labelStats = visiblePage?.labels ?? [];
  const totalCustomers = visiblePage?.totalCustomers ?? 0;
  const totalFiltered = visiblePage?.totalFiltered ?? 0;

  useEffect(() => {
    const timer = window.setTimeout(() => { setDebouncedSearch(search); setCurrentPage(1); }, 300);
    return () => window.clearTimeout(timer);
  }, [search]);

  // Modal state
  const [selectedCustomerId, setSelectedCustomerId] = useState<string | null>(null);
  const loadRequestIdRef = React.useRef(0);
  const loadAbortRef = React.useRef<AbortController | null>(null);
  const importFileInputRef = React.useRef<HTMLInputElement>(null);

  const fetchCustomers = useCallback(async () => {
    const requestId = ++loadRequestIdRef.current;
    loadAbortRef.current?.abort();
    const controller = new AbortController();
    loadAbortRef.current = controller;
    if (!activeProject) {
      setCustomerPage(null);
      setLoading(false);
      setLoadError('تعذر تحميل مساحة العمل. أعد المحاولة أو تواصل مع المدير.');
      return;
    }
    try {
      setLoading(true);
      setLoadError(null);
      const page = await crmService.getCustomerPage(activeProject.id, {
        page: currentPage, pageSize, search: debouncedSearch, stage: stageFilter, city: cityFilter, label: selectedLabel,
      }, controller.signal);
      if (requestId !== loadRequestIdRef.current || controller.signal.aborted) return;
      setCustomerPage(page);
      setLoadedProjectId(activeProject.id);
      if (page.page !== currentPage) setCurrentPage(page.page);
    } catch (e) {
      if (requestId !== loadRequestIdRef.current || controller.signal.aborted) return;
      console.error('Failed to load CRM customers', e);
      setLoadError('تعذر تحميل سجل العملاء. لم يتم استبدال البيانات بقائمة فارغة.');
    } finally {
      if (requestId === loadRequestIdRef.current) setLoading(false);
    }
  }, [activeProject, currentPage, pageSize, debouncedSearch, stageFilter, cityFilter, selectedLabel]);

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
    return () => { window.clearTimeout(loadTimer); loadAbortRef.current?.abort(); };
  }, [fetchCustomers]);

  if (loading && loadedProjectId !== activeProject?.id) {
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
          key={selectedCustomerId}
          initialSection={detailSection}
          customerId={selectedCustomerId}
          projectId={activeProject.id}
          onClose={() => setSelectedCustomerId(null)}
          onUpdate={fetchCustomers}
          isInline={true}
        />
      </div>
    );
  }

  const stages = ['All', ...(visiblePage?.stages.map(stage => stage.name) ?? [])];
  const cities = ['All', ...(visiblePage?.cities ?? [])];
  const totalPages = Math.ceil(totalFiltered / pageSize) || 1;
  const displayedPage = visiblePage?.page ?? currentPage;
  const paginatedCustomers = customers;

  return (
    <div className={styles.container} aria-busy={loading}>
      <PageHeader title="العملاء" icon={<Users size={24} />} actions={
        <Button variant="ghost" onClick={() => setShowTools(!showTools)} aria-expanded={showTools}
          aria-controls="customer-tools"><FileDown size={16} aria-hidden="true" />الاستيراد والتصدير</Button>
      } />

      {loadError && <p role="alert">{loadError} <button type="button" onClick={() => void fetchCustomers()}>إعادة المحاولة</button></p>}
      {loading && <p role="status">جاري تحديث العملاء...</p>}

      <section id="customer-tools" hidden={!showTools} className={styles.customerTools} aria-label="أدوات العملاء">
        {activeProject && <details className={styles.exportMenu}>
          <summary><FileDown size={16} aria-hidden="true" />تصدير العملاء</summary>
          <div className={styles.exportContent}><CustomerExport key={activeProject.id} projectId={activeProject.id} /></div>
        </details>}
        <Button onClick={() => setShowImportPanel(!showImportPanel)} aria-expanded={showImportPanel}
          aria-controls="blacklist-import-panel"><Upload size={16} aria-hidden="true" />استيراد قائمة الحظر</Button>
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

      </section>

      <section className={styles.customerSearch} aria-label="البحث وتصفية العملاء">
        <FilterTabs compactOnMobile label="مراحل العملاء" selected={stageFilter} onChange={stage => { setStageFilter(stage); setCurrentPage(1); }}
          options={stages.map(stage => {
            const stageStyle = stageStyles[stage];
            const StageIcon = stage === 'All' ? Users : stageStyle?.icon ?? ListFilter;
            return { key: stage, label: stage === 'All' ? 'كل العملاء' : stage,
              count: stage === 'All' ? totalCustomers : visiblePage?.stages.find(entry => entry.name === stage)?.count ?? 0,
              tone: stageStyle?.tone, icon: <StageIcon size={16} aria-hidden="true" /> };
          })} />
        <div className={styles.searchToolbar}>
          <SearchField containerClassName={styles.searchWrapper} label="بحث في العملاء بالاسم أو رقم الهاتف"
            placeholder="ابحث بالاسم أو رقم الهاتف…" value={search}
            onChange={event => setSearch(event.target.value)} />
          <Button onClick={() => setShowFilters(!showFilters)} aria-expanded={showFilters} aria-controls="customer-filters">
            <SlidersHorizontal size={16} aria-hidden="true" />تصفية
            {[stageFilter, cityFilter, selectedLabel].filter(filter => filter !== 'All').length > 0 &&
              <span>({[stageFilter, cityFilter, selectedLabel].filter(filter => filter !== 'All').length})</span>}
          </Button>
        </div>
        <div id="customer-filters" hidden={!showFilters} className={styles.customerFilters}>
          <label>مرحلة البيع<select value={stageFilter} onChange={event => { setStageFilter(event.target.value); setCurrentPage(1); }}
            className="neon-input" aria-label="تصفية العملاء حسب مرحلة البيع">
            {stages.map(stage => <option key={stage} value={stage}>{stage === 'All' ? 'كل المراحل' : stage}</option>)}
          </select></label>
          <label>المدينة<select value={cityFilter} onChange={event => { setCityFilter(event.target.value); setCurrentPage(1); }}
            className="neon-input" aria-label="تصفية العملاء حسب المدينة">
            {cities.map(city => <option key={city} value={city}>{city === 'All' ? 'كل المدن' : city}</option>)}
          </select></label>
          <label>التصنيف<select value={selectedLabel} onChange={event => { setSelectedLabel(event.target.value); setCurrentPage(1); }}
            className="neon-input" aria-label="تصفية العملاء حسب التصنيف">
            <option value="All">كل التصنيفات</option>
            {labelStats.map(label => <option key={label.name} value={label.name}>{label.name} ({label.count})</option>)}
          </select></label>
        </div>
        <div className={styles.resultsSummary} role="status">
          <span>{formatCount(totalFiltered)} عميل{totalFiltered !== totalCustomers && ` من ${formatCount(totalCustomers)}`}</span>
          {(search || stageFilter !== 'All' || cityFilter !== 'All' || selectedLabel !== 'All') &&
            <Button variant="ghost" onClick={() => { setSearch(''); setStageFilter('All'); setCityFilter('All'); setSelectedLabel('All'); setCurrentPage(1); }}>مسح التصفية</Button>}
        </div>
      </section>

      {/* Customers Table / Grid */}
      <div className={`glass-panel ${styles.tablePanel}`}>
        {totalFiltered === 0 ? (
          <EmptyState title="لا توجد نتائج مطابقة" description="جرّب تعديل البحث أو إزالة أحد الفلاتر لعرض العملاء." />
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
                {paginatedCustomers.map(c => {
                  const stageStyle = stageStyles[c.pipelineStage ?? ''];
                  const StageIcon = stageStyle?.icon ?? ListFilter;
                  return (
                  <tr key={c.id} className={styles.tr} data-expanded={expandedCustomerId === c.id}>
                    <td data-label="العميل" className={styles.td}>
                      <button
                        type="button"
                        className={styles.customerCell}
                        onClick={() => { setDetailSection('profile'); setSelectedCustomerId(c.id); }}
                        style={{ border: 'none', background: 'none', display: 'flex', width: '100%', textAlign: 'right', font: 'inherit', color: 'inherit', padding: 0 }}
                      >
                        <div className={styles.avatar}>
                          {(c.name || '?').charAt(0).toUpperCase()}
                        </div>
                        <div className={styles.customerNameBox}>
                          <span className={styles.customerName}>{c.name || 'عميل بدون اسم'}</span>
                          <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                            <span dir="ltr" className={styles.customerPhone}>{c.phoneNumber}</span>
                            {c.label && (
                              <span className={styles.smartLabelBadge}>{c.label}</span>
                            )}
                            {c.isBlacklisted && (
                              <span className={styles.smartLabelBadge} style={{ backgroundColor: 'var(--danger-soft)', color: 'var(--danger)', border: '1px solid rgba(239, 68, 68, 0.25)' }}>
                                حظر رد آلي
                              </span>
                            )}
                          </div>
                        </div>
                      </button>
                      <button type="button" className={styles.mobileRowToggle} aria-expanded={expandedCustomerId === c.id}
                        onClick={() => setExpandedCustomerId(expandedCustomerId === c.id ? null : c.id)}>
                        {expandedCustomerId === c.id ? 'تفاصيل أقل' : 'تفاصيل أكثر'}<ChevronDown size={14} aria-hidden="true" />
                      </button>
                    </td>
                    <td data-label="المدينة" className={styles.td}>
                      <span className={styles.locationText}>
                        {c.city ? (
                          <>
                            <MapPin size={13} aria-hidden="true" />
                            {c.city}
                          </>
                        ) : (
                          <span style={{ color: 'hsl(var(--text-muted))' }}>غير محدد</span>
                        )}
                      </span>
                    </td>
                    <td data-label="التقييم" className={styles.td}>
                      <div className={styles.scoreBox}>
                        <Sparkles size={14} aria-hidden="true" />
                        <span className={styles.scoreVal}>{Number.isFinite(c.leadScore) ? c.leadScore : 'غير متاح'}</span>
                      </div>
                    </td>
                    <td data-label="المرحلة" className={styles.td}>
                      <Badge tone={stageStyle?.tone}>
                        <StageIcon size={13} aria-hidden="true" />
                        {c.pipelineStage || 'غير محددة'}
                      </Badge>
                    </td>
                    <td data-label="الميزانية" className={styles.td}>
                      <span className={styles.budgetText}>
                        {c.budget !== null && c.budget !== undefined ? <span title="العملة غير مسجلة في بيانات العميل">{c.budget.toLocaleString('ar-EG')}<small className={styles.currencyHint}>العملة غير محددة</small></span> : <span style={{ color: 'hsl(var(--text-muted))' }}>-</span>}
                      </span>
                    </td>
                    <td data-label="الوسوم" className={styles.td}>
                      <div className={styles.tagsContainer}>
                        {c.tags && c.tags.slice(0, 2).map(tag => (
                          <span key={tag} className={styles.tagBadge}>{tag}</span>
                        ))}
                        {c.tags && c.tags.length > 2 && (
                          <span className={styles.tagMore}>+{c.tags.length - 2}</span>
                        )}
                      </div>
                    </td>
                    <td data-label="الإجراءات" className={styles.td} style={{ textAlign: 'center' }}>
                      <div className={styles.rowActions}>
                        <Link href={`/inbox?customerId=${encodeURIComponent(c.id)}&startConversation=1`} className={styles.conversationLink}
                          aria-label={`فتح محادثة واتساب مع ${c.name || c.phoneNumber}`}>
                          <MessageSquare size={15} aria-hidden="true" />محادثة
                        </Link>
                        <Button variant="ghost" onClick={() => { setDetailSection('followups'); setSelectedCustomerId(c.id); }}
                          aria-label={`إضافة متابعة للعميل ${c.name || c.phoneNumber}`}>
                          <CalendarPlus size={15} aria-hidden="true" />متابعة
                        </Button>
                        <Button variant="ghost" onClick={() => { setDetailSection('profile'); setSelectedCustomerId(c.id); }}
                          aria-label={`فتح ملف ${c.name || c.phoneNumber}`}><UserRound size={15} aria-hidden="true" />الملف</Button>
                      </div>
                    </td>
                  </tr>
                ); })}
              </tbody>
            </table>

            {/* Pagination Controls */}
            {totalFiltered > 0 && (
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
                    عرض {Math.min((displayedPage - 1) * pageSize + 1, totalFiltered)} - {Math.min(displayedPage * pageSize, totalFiltered)} من {totalFiltered}
                  </span>
                </div>

                <div className={styles.paginationControls}>
                  <button
                    onClick={() => setCurrentPage(prev => Math.min(prev + 1, totalPages))}
                    disabled={loading || displayedPage === totalPages}
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
                          disabled={loading}
                          className={`${styles.paginationBtn} ${displayedPage === page ? styles.paginationBtnActive : ''}`}
                          aria-label={`الصفحة ${page}`}
                          aria-current={displayedPage === page ? 'page' : undefined}
                        >
                          {page}
                        </button>
                      );
                      return elements;
                    })}

                  <button
                    onClick={() => setCurrentPage(prev => Math.max(prev - 1, 1))}
                    disabled={loading || displayedPage === 1}
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
