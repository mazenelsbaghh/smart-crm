'use client';

import { useEffect, useState } from 'react';
import { RefreshCw, MessagesSquare, CalendarDays } from 'lucide-react';
import { useAuth } from '@/context/auth-context';
import { api } from '@/services/api';
import ConversationReview from './ConversationReview';
import ConversationQueue from './ConversationQueue';
import FollowUpReview from './FollowUpReview';
import ReviewProcessingPanel from './ReviewProcessingPanel';
import { DailyReview, numberLabel, reviewUrl, timeLabel } from './types';
import styles from './reply-review.module.css';

export default function DailyReplyReviewPage() {
  const { activeProject, user, loading } = useAuth();
  if (loading) return <p role="status">جاري تحميل المشروع…</p>;
  if (!activeProject) return <p>اختر مشروعًا لعرض المراجعة اليومية.</p>;
  return <ReviewWorkspace key={activeProject.id} projectId={activeProject.id} canManage={user?.role === 'Owner' || user?.role === 'Admin'} />;
}

function ReviewWorkspace({ projectId, canManage }: { projectId: string; canManage: boolean }) {
  const [date, setDate] = useState('');
  const [view, setView] = useState('attention');
  const [page, setPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ scope: string; data: DailyReview } | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(true);
  const [selected, setSelected] = useState<string | null>(null);
  const scope = `${date}:${view}:${page}`;
  const data = result?.scope === scope ? result.data : null;
  const conversation = data?.conversations.find(row => row.conversationId === selected);

  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      setBusy(true);
      try {
        const response = await api.get<DailyReview>(reviewUrl(projectId), {
          params: { date: date || undefined, view, page }, signal: controller.signal,
        });
        if (controller.signal.aborted) return;
        setResult({ scope, data: response.data });
        setError('');
      } catch {
        if (!controller.signal.aborted) setError('تعذر تحديث المراجعة. أعد المحاولة؛ البيانات المعروضة قد تكون أقدم.');
      } finally { if (!controller.signal.aborted) setBusy(false); }
    }
    void load();
    return () => controller.abort();
  }, [projectId, date, view, page, scope, revision]);

  useEffect(() => {
    const timer = window.setInterval(() => { if (!document.hidden) setRevision(v => v + 1); }, 60_000);
    return () => window.clearInterval(timer);
  }, []);

  const refresh = () => setRevision(v => v + 1);
  return <main className={styles.page} dir="rtl">
    <header className={styles.header}>
      <div><p className={styles.eyebrow}>جودة خدمة العملاء</p><h1>مراجعة الردود اليومية</h1>
        <p className={styles.muted}>اعرف مين محتاج تدخل، راجع الكلام بتوقيته، وتابع الإرسال الفعلي.</p></div>
      <div className={styles.controls}>
        <label className={styles.date}><CalendarDays size={17} aria-hidden="true" /><span className={styles.srOnly}>يوم المراجعة</span>
          <input aria-label="يوم المراجعة" type="date" value={date || data?.date || ''} onChange={e => { setDate(e.target.value); setPage(1); setSelected(null); }} /></label>
        <button onClick={() => { setDate(''); setPage(1); setSelected(null); refresh(); }}>اليوم</button>
        <button onClick={refresh} disabled={busy} aria-label="تحديث المراجعة"><RefreshCw size={17} aria-hidden="true" /> تحديث</button>
      </div>
    </header>
    <div className={styles.meta}><span>{data ? `توقيت المشروع: ${data.timezone}` : 'جاري تحديد توقيت المشروع…'}</span>
      {data && <span>آخر تحديث {timeLabel(data.generatedAtUtc, data.timezone)} · تحديث تلقائي كل دقيقة</span>}</div>
    {error && <p role="alert" className={styles.error}>{error}</p>}
    {data ? <>
      <section className={styles.metrics} aria-label="ملخص اليوم">
        <div><strong>{numberLabel(data.summary.conversations)}</strong><span>محادثات اليوم</span></div>
        <div><strong className={styles.warning}>{numberLabel(data.summary.needsAttention)}</strong><span>تحتاج مراجعة</span></div>
        <div><strong>{numberLabel(data.summary.awaitingAnalysis)}</strong><span>تحتاج تحليلًا محدثًا</span></div>
        <div><strong>{numberLabel(data.summary.waitingForHuman)}</strong><span>تنتظر موظفًا حاليًا</span></div>
        <div><strong className={styles.warning}>{numberLabel(data.summary.followUpsOverdue)}</strong><span>متابعات متأخرة</span></div>
      </section>
      <ReviewProcessingPanel projectId={projectId} timezone={data.timezone} canManage={canManage} refreshSignal={revision} />
      <section aria-label="مراجعة المحادثات" className={styles.reviewSection}>
        <div className={styles.sectionHeading}><h2><MessagesSquare size={20} aria-hidden="true" /> المحادثات</h2>
          <div className={styles.tabs} role="group" aria-label="فلتر المحادثات">
            {([['attention', 'تحتاج مراجعة'], ['all', 'الكل'], ['unanalyzed', 'بانتظار التحليل']] as const).map(([key, label]) =>
              <button key={key} aria-pressed={view === key} onClick={() => { setView(key); setPage(1); setSelected(null); }}>{label}</button>)}
          </div></div>
        <p className={styles.note}>التكرار والتأخير علامات للمراجعة. تقييم الذكاء الاصطناعي تقديري، وعدم وجود علامة لا يعني سلامة الرد.</p>
        <div className={`${styles.workspace} ${conversation ? styles.hasSelection : ''}`}>
          <ConversationQueue report={data} selected={selected} page={page} busy={busy} onSelect={setSelected}
            onPage={next => { setPage(next); setSelected(null); }} />
          <div className={styles.detail}>{conversation ? <ConversationReview key={`${date}:${conversation.conversationId}`} projectId={projectId} row={conversation}
            timezone={data.timezone} canManage={canManage} onReviewed={refresh} onBack={() => setSelected(null)} /> :
            <div className={styles.empty}><MessagesSquare size={32} aria-hidden="true" /><h3>اختار محادثة للمراجعة</h3><p>شوف الرسائل كاملة، الفواصل الزمنية، واقتراح معالجة المشكلة.</p></div>}</div>
        </div>
      </section>
      <FollowUpReview rows={data.followUps} timezone={data.timezone} generatedAtUtc={data.generatedAtUtc} />
      <p className={styles.note}>أوقات الاستجابة محسوبة لرسائل اليوم المختار؛ حالة المعالجة والموظف والمتابعة هي الحالة الحالية. الجدولة تعمل وفق الإعدادات أعلاه.</p>
    </> : <div className={styles.empty} role="status">{busy ? 'جاري تحميل مراجعة اليوم…' : 'لم تُحمّل بيانات المراجعة.'}</div>}
  </main>;
}
