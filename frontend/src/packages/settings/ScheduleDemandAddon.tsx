'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { CalendarClock, ExternalLink, RefreshCw, Zap } from 'lucide-react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import settingsStyles from './settings.module.css';
import styles from './schedule-demand-addon.module.css';

interface ScheduleDemandRow {
  customerId: string;
  customerName: string;
  phoneNumber: string;
  requestedScheduleText: string;
  requestedScheduleLabel: string;
  lastMessageAtUtc: string;
}

interface ScheduleDemandOverview {
  totalPeople: number;
  distinctSchedules: number;
  pendingLegacyExtraction: number;
  groups: Array<{ label: string; peopleCount: number }>;
  rows: ScheduleDemandRow[];
}

interface ScheduleDemandAddonProps {
  timezone: string;
  onMessage: (message: { type: 'success' | 'error'; text: string }) => void;
}

const reportWindow = () => {
  const toUtc = new Date();
  return {
    fromUtc: new Date(toUtc.getTime() - 90 * 24 * 60 * 60 * 1000).toISOString(),
    toUtc: toUtc.toISOString(),
  };
};

export default function ScheduleDemandAddon({ timezone, onMessage }: ScheduleDemandAddonProps) {
  const { activeProject } = useAuth();
  const [overview, setOverview] = useState<ScheduleDemandOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [extracting, setExtracting] = useState(false);

  const loadOverview = useCallback(async () => {
    if (!activeProject) return;
    setLoading(true);
    try {
      const response = await api.get<ScheduleDemandOverview>(
        `/api/projects/${activeProject.id}/reports/sales-intelligence/schedule-demand`,
        { params: reportWindow() },
      );
      setOverview(response.data);
    } catch {
      onMessage({ type: 'error', text: 'تعذّر تحميل شيت المواعيد المطلوبة.' });
    } finally {
      setLoading(false);
    }
  }, [activeProject, onMessage]);

  useEffect(() => {
    const timer = window.setTimeout(() => void loadOverview(), 0);
    return () => window.clearTimeout(timer);
  }, [loadOverview]);

  const extractLegacyChats = async () => {
    if (!activeProject) return;
    setExtracting(true);
    try {
      const response = await api.post<{ pending: number }>(
        `/api/projects/${activeProject.id}/reports/sales-intelligence/analyze-all`,
        reportWindow(),
      );
      onMessage({
        type: 'success',
        text: response.data.pending > 0
          ? `بدأ استخراج المواعيد من ${response.data.pending} شات. حدّث الشيت بعد اكتمال التحليل.`
          : 'كل الشاتات محدثة بالفعل.',
      });
    } catch {
      onMessage({ type: 'error', text: 'تعذّر بدء استخراج المواعيد من الشاتات القديمة.' });
      setExtracting(false);
    }
  };

  return (
    <section className={`glass-panel ${styles.card}`} aria-labelledby="schedule-demand-title">
      <header className={styles.header}>
        <span className={styles.icon}><CalendarClock size={22} aria-hidden="true" /></span>
        <span className={styles.aiBadge}>استخراج AI تلقائي</span>
      </header>
      <div>
        <h3 id="schedule-demand-title" className={styles.title}>شيت المواعيد المطلوبة</h3>
        <p className={styles.description}>يجمع العملاء الذين رفضوا المواعيد الحالية وذكروا موعدًا بديلًا صريحًا خلال آخر 90 يومًا.</p>
      </div>
      <DemandMetrics overview={overview} />
      <DemandContent overview={overview} loading={loading} timezone={timezone} />
      <footer className={styles.actions}>
        <Link href="/management/schedule-demand" className={`${settingsStyles.btn} ${settingsStyles.btnPrimary}`}>
          <ExternalLink size={14} aria-hidden="true" />فتح الصفحة
        </Link>
        {Boolean(overview?.pendingLegacyExtraction) && (
          <button type="button" className={`${settingsStyles.btn} ${settingsStyles.btnPrimary}`} disabled={extracting} onClick={extractLegacyChats}>
            <Zap size={14} aria-hidden="true" />{extracting ? 'جاري الاستخراج' : 'استخراج القديم'}
          </button>
        )}
        <button type="button" className={`${settingsStyles.btn} ${settingsStyles.btnSecondary}`} disabled={loading} onClick={() => void loadOverview()}>
          <RefreshCw size={14} aria-hidden="true" />تحديث الشيت
        </button>
      </footer>
    </section>
  );
}

function DemandMetrics({ overview }: { overview: ScheduleDemandOverview | null }) {
  const metrics = [
    ['أشخاص', overview?.totalPeople ?? '—'],
    ['مواعيد مختلفة', overview?.distinctSchedules ?? '—'],
    ['استخراج قديم', overview?.pendingLegacyExtraction ?? '—'],
  ];
  return <div className={styles.metrics}>{metrics.map(([label, value]) => (
    <div key={label}><strong>{value}</strong><span>{label}</span></div>
  ))}</div>;
}

function DemandContent({ overview, loading, timezone }: {
  overview: ScheduleDemandOverview | null;
  loading: boolean;
  timezone: string;
}) {
  if (loading) return <p className={styles.empty}>جاري تحميل الشيت...</p>;
  if (!overview?.rows.length) return <p className={styles.empty}>لا توجد مواعيد بديلة صريحة مسجلة حتى الآن.</p>;
  return <div className={styles.sheet}>
    <div className={styles.groups} aria-label="تجميع المواعيد المطلوبة">
      {overview.groups.map((group) => <span key={group.label}>{group.label}: <strong>{group.peopleCount}</strong></span>)}
    </div>
    <div className={styles.rows}>{overview.rows.map((row) => (
      <DemandRow key={row.customerId} row={row} timezone={timezone} />
    ))}</div>
  </div>;
}

function DemandRow({ row, timezone }: { row: ScheduleDemandRow; timezone: string }) {
  const lastMessage = new Date(row.lastMessageAtUtc).toLocaleString('ar-EG', {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', timeZone: timezone,
  });
  return <div className={styles.row}>
    <div><strong>{row.customerName || 'عميل بدون اسم'}</strong><b>{row.requestedScheduleLabel}</b><p>«{row.requestedScheduleText}»</p></div>
    <small><span dir="ltr">{row.phoneNumber}</span><time dateTime={row.lastMessageAtUtc}>{lastMessage}</time></small>
  </div>;
}
