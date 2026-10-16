'use client';

import { useEffect, useRef, useState } from 'react';
import { Download, ExternalLink, RefreshCw, X } from 'lucide-react';
import { api } from '../../services/api';
import styles from './GroupParticipationPanel.module.css';

interface ParticipationRow {
  customerId: string;
  name: string;
  phone: string;
  sentAtUtc: string | null;
  membership: 'joined' | 'not_joined' | 'unknown';
}

interface ParticipationReport {
  groupId: string;
  groupName: string;
  membershipAvailability: 'ready' | 'partial' | 'unavailable' | 'empty';
  checkedAtUtc: string | null;
  rows: ParticipationRow[];
}

const membershipLabels = { joined: 'في الجروب', not_joined: 'خارج الجروب', unknown: 'الدخول غير مؤكد' };
type Filter = 'sent' | 'joined' | 'not_joined' | 'unknown' | 'unsent' | 'all';
const filters: { key: Filter; label: string }[] = [
  { key: 'sent', label: 'اتبعث لهم' }, { key: 'joined', label: 'في الجروب' },
  { key: 'not_joined', label: 'خارج الجروب' }, { key: 'unknown', label: 'الدخول غير مؤكد' },
  { key: 'unsent', label: 'لم يُسجل إرسال' }, { key: 'all', label: 'كل الحجوزات' },
];

function matchesFilter(row: ParticipationRow, filter: Filter) {
  if (filter === 'all') return true;
  if (filter === 'unsent') return !row.sentAtUtc;
  return Boolean(row.sentAtUtc) && (filter === 'sent' || row.membership === filter);
}

function formatTime(value: string | null, timezone: string) {
  return value ? new Intl.DateTimeFormat('ar-EG', {
    dateStyle: 'short', timeStyle: 'short', timeZone: timezone,
  }).format(new Date(value)) : 'لم يُسجل إرسال';
}

export default function GroupParticipationPanel({ groupId, groupName, timezone, onClose }: {
  groupId: string; groupName: string; timezone: string; onClose: () => void;
}) {
  const [loaded, setLoaded] = useState<{
    groupId: string; revision: number; report: ParticipationReport | null; error: string;
  } | null>(null);
  const [exportError, setExportError] = useState('');
  const [refresh, setRefresh] = useState(0);
  const [filter, setFilter] = useState<Filter>('not_joined');
  const [search, setSearch] = useState('');
  const [exporting, setExporting] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);
  const loading = loaded?.groupId !== groupId || loaded?.revision !== refresh;
  const report = loading ? null : loaded.report;
  const error = exportError || (loading ? '' : loaded.error);

  useEffect(() => { heading.current?.focus(); }, []);
  useEffect(() => {
    const controller = new AbortController();
    api.get<ParticipationReport>(`/api/group-appointments/${groupId}/participation`, {
      signal: controller.signal, timeout: 25000,
    }).then(({ data }) => {
      if (!controller.signal.aborted) setLoaded({ groupId, revision: refresh, report: data, error: '' });
    }).catch(() => {
      if (!controller.signal.aborted) setLoaded({ groupId, revision: refresh, report: null,
        error: 'تعذر تحميل المتابعة. جرّب التحديث مرة تانية.' });
    });
    return () => controller.abort();
  }, [groupId, refresh]);

  const rows = report?.rows ?? [];
  const query = search.trim().toLocaleLowerCase('ar');
  const visibleRows = rows.filter(row => matchesFilter(row, filter)
    && (!query || `${row.name} ${row.phone}`.toLocaleLowerCase('ar').includes(query)));
  const filterLabel = filters.find(item => item.key === filter)!.label;

  async function downloadList() {
    setExporting(true);
    setExportError('');
    try {
      const XLSX = await import('xlsx');
      const sheet = XLSX.utils.json_to_sheet(visibleRows.map(row => ({
        'الاسم': row.name, 'رقم الهاتف': row.phone,
        'إرسال رسالة الجروب': formatTime(row.sentAtUtc, timezone),
        'حالة الدخول': membershipLabels[row.membership],
        'المحادثة': `${window.location.origin}/inbox?customerId=${encodeURIComponent(row.customerId)}&startConversation=1`,
      })));
      sheet['!cols'] = [{ wch: 28 }, { wch: 20 }, { wch: 30 }, { wch: 24 }, { wch: 65 }];
      const workbook = XLSX.utils.book_new();
      XLSX.utils.book_append_sheet(workbook, sheet, 'متابعة الجروب');
      const blob = new Blob([XLSX.write(workbook, { bookType: 'xlsx', type: 'array' })], {
        type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `متابعة-الجروب-${filterLabel}-${new Date().toISOString().slice(0, 10)}.xlsx`;
      link.click();
      URL.revokeObjectURL(url);
    } catch {
      setExportError('تعذر تنزيل القائمة. جرّب مرة تانية.');
    } finally {
      setExporting(false);
    }
  }

  return <section id="group-participation-panel" className={styles.panel} dir="rtl" aria-labelledby="participation-heading" aria-busy={loading}>
    <div className={styles.header}>
      <div>
        <h3 id="participation-heading" ref={heading} tabIndex={-1}>متابعة دخول الجروب · {groupName}</h3>
        <p>حجوزات المجموعة الحالية. أعداد الدخول محسوبة من اللي اتبعت لهم اللينك فعلًا.</p>
      </div>
      <div className={styles.actions}>
        <button type="button" onClick={() => { setExportError(''); setRefresh(value => value + 1); }} disabled={loading}><RefreshCw size={16} /> تحديث</button>
        <button type="button" onClick={onClose} aria-label="إغلاق متابعة الجروب"><X size={18} /></button>
      </div>
    </div>
    {error && <p role="alert" className={styles.notice}>{error}</p>}
    {loading && <p role="status">جاري مراجعة الرسائل وأعضاء الجروب…</p>}
    {report && <>
      {report.membershipAvailability === 'unavailable' && <p role="status" className={styles.notice}>
        حالة الدخول غير متاحة حاليًا. تأكد إن جروب واتساب اتعمل والحساب متصل، وبعدها اضغط تحديث. محدش بيتحسب خارج الجروب من غير تأكيد.
      </p>}
      {report.membershipAvailability === 'partial' && <p role="status" className={styles.notice}>واتساب لم يؤكد حالة بعض الأرقام. هتلاقيهم تحت «الدخول غير مؤكد».</p>}
      <div className={styles.filters} aria-label="تصفية متابعة الجروب">
        {filters.map(item => <button type="button" key={item.key} aria-pressed={filter === item.key}
          aria-label={`${item.label} ${rows.filter(row => matchesFilter(row, item.key)).length}`} onClick={() => setFilter(item.key)}>
          {item.label}<strong>{rows.filter(row => matchesFilter(row, item.key)).length}</strong>
        </button>)}
      </div>
      <div className={styles.toolbar}>
        <input type="search" aria-label="بحث بالاسم أو رقم الهاتف" placeholder="بحث بالاسم أو رقم الهاتف" value={search} onChange={event => setSearch(event.target.value)} />
        <button type="button" disabled={!visibleRows.length || exporting} onClick={downloadList}><Download size={16} /> {exporting ? 'جاري التنزيل…' : `تنزيل القائمة (${visibleRows.length})`}</button>
      </div>
      <div className={styles.tableScroll}>
        <table>
          <caption>{filterLabel} · {visibleRows.length} شخص</caption>
          <thead><tr><th>الاسم</th><th>رقم الهاتف</th><th>إرسال رسالة الجروب</th><th>حالة الدخول</th><th>تواصل</th></tr></thead>
          <tbody>{visibleRows.map(row => <tr key={`${row.customerId}:${row.phone}`}>
            <td>{row.name}</td><td><span dir="ltr">{row.phone}</span></td>
            <td>{formatTime(row.sentAtUtc, timezone)}</td>
            <td><span className={styles.badge} data-membership={row.membership}>{membershipLabels[row.membership]}</span></td>
            <td><a href={`/inbox?customerId=${encodeURIComponent(row.customerId)}&startConversation=1`} target="_blank" rel="noopener noreferrer" aria-label={`فتح محادثة ${row.name}`}><ExternalLink size={14} /> فتح المحادثة</a></td>
          </tr>)}</tbody>
        </table>
      </div>
      {!visibleRows.length && <p className={styles.empty}>{!rows.length ? 'لسه مفيش حجوزات في المجموعة دي.' : 'مفيش أشخاص مطابقين للتصفية الحالية.'}</p>}
      <p className={styles.note}>الحالة حسب أعضاء واتساب الآن؛ «خارج الجروب» ممكن تعني لم يدخل أو غادر الجروب.
        {report.checkedAtUtc && ` آخر مراجعة: ${formatTime(report.checkedAtUtc, timezone)}.`}</p>
    </>}
  </section>;
}
