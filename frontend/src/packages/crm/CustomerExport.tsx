'use client';

import { useEffect, useRef, useState } from 'react';
import { FileDown } from 'lucide-react';
import { api } from '../../services/api';
import { useToast } from '../../context/toast-context';
import styles from './crm.module.css';

interface ExportCustomer { phoneNumber: string; name: string; city: string; label: string | null }

function downloadWorkbook(customers: ExportCustomer[], audience: string, XLSX: typeof import('xlsx')) {
  const worksheet = XLSX.utils.aoa_to_sheet([
    ['رقم الهاتف', 'الاسم', 'المدينة', 'التصنيف'],
    ...customers.map(customer => [customer.phoneNumber, customer.name, customer.city, customer.label ?? '']),
  ]);
  worksheet['!cols'] = [{ wch: 20 }, { wch: 30 }, { wch: 20 }, { wch: 25 }];
  const workbook = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(workbook, worksheet, 'العملاء');
  XLSX.writeFile(workbook, `crm_${audience}_${new Date().toISOString().slice(0, 10)}.xlsx`);
}

export default function CustomerExport({ projectId }: { projectId: string }) {
  const { showToast } = useToast();
  const [audience, setAudience] = useState('inactive30');
  const [exporting, setExporting] = useState(false);
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);

  async function exportCustomers() {
    setExporting(true);
    try {
      const [response, XLSX] = await Promise.all([
        api.get<ExportCustomer[]>(`/api/projects/${projectId}/customers/export`, { params: { audience } }),
        import('xlsx'),
      ]);
      if (!mounted.current) return;
      if (response.data.length === 0) { showToast('لا توجد أرقام مطابقة لشروط التنزيل.', 'info'); return; }
      downloadWorkbook(response.data, audience, XLSX);
      showToast(`تم تنزيل ${response.data.length.toLocaleString('ar-EG')} رقم بدون تكرار.`, 'success');
    } catch (error) {
      console.error('Failed to export CRM customers', error);
      if (mounted.current) showToast('تعذر تنزيل البيانات. حاول مرة أخرى.', 'error');
    } finally {
      if (mounted.current) setExporting(false);
    }
  }

  return (
    <section className={styles.exportBar} aria-label="تنزيل بيانات العملاء">
      <div className={styles.exportControls}>
        <label htmlFor="crm-export-audience">بيانات التنزيل</label>
        <select className="neon-input" id="crm-export-audience" value={audience} disabled={exporting} onChange={event => setAudience(event.target.value)}>
          <option value="inactive30">غير متفاعلين منذ ٣٠ يومًا</option>
          <option value="nonSubscribers">كل غير المشتركين</option>
        </select>
        <button type="button" className={styles.editButton} disabled={exporting} onClick={() => void exportCustomers()}>
          <FileDown size={16} aria-hidden="true" /> {exporting ? 'جاري تجهيز الملف...' : 'تنزيل Excel'}
        </button>
      </div>
      <p>يستبعد المشتركين والمدفوعين والمحظورين دائمًا. اختيار ٣٠ يومًا يستبعد أي رقم له محادثة أو رسالة خلال هذه المدة. التنزيل يشمل كل الأرقام المطابقة بالمشروع، ولا يتأثر بفلاتر الجدول.</p>
    </section>
  );
}
