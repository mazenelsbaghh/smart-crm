'use client';

import { useRef, useState } from 'react';
import { Bot } from 'lucide-react';
import { adManagerApi } from '../api/ad-manager-api';
import type { AdDecision, AdDecisionDetail } from '../types';
import { StructuredJsonSummary } from './StructuredJsonSummary';
import styles from '../AdManager.module.css';

export function DecisionsView({ projectId, rows }: { projectId: string; rows: AdDecision[] }) {
  const [selected, setSelected] = useState<AdDecisionDetail | null>(null);
  const [loadingId, setLoadingId] = useState<string | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  const detailRequestRef = useRef(0);
  if (!rows.length) return <section className={styles.empty}><Bot /><h2>لا توجد قرارات بعد</h2><p>قلة البيانات تنتج WAIT بسبب واضح، وليس إيقافًا أو زيادة مبكرة.</p></section>;
  const open = async (id: string) => {
    const requestId = ++detailRequestRef.current;
    setLoadingId(id); setDetailError(null); setSelected(null);
    try {
      const decision = await adManagerApi.decision(projectId, id);
      if (requestId === detailRequestRef.current) setSelected(decision);
    } catch {
      if (requestId === detailRequestRef.current) setDetailError('تعذّر تحميل دليل القرار. أعد المحاولة بدون تنفيذ أي تغيير.');
    } finally {
      if (requestId === detailRequestRef.current) setLoadingId(null);
    }
  };
  return <div className={styles.splitDetail}><section className={styles.detailList} aria-label="قرارات AI">{rows.map(row => <article key={row.id}>
    <strong>{row.actionType} · {row.state}</strong><p>{row.reason ?? 'راجع الدليل والمراجعات قبل التنفيذ.'}</p>
    <small>{row.targetType} · خطورة {row.riskClass} · {new Date(row.createdAt).toLocaleString('ar-EG')}</small>
    <button className={styles.secondaryButton} disabled={loadingId === row.id} onClick={() => void open(row.id)}>{loadingId === row.id ? 'جارٍ التحميل…' : 'الدليل والتنفيذ'}</button>
  </article>)}</section>
  {detailError && <p className={styles.error} role="alert">{detailError}</p>}
  {selected && <aside className={styles.detailPanel} aria-live="polite"><header><Bot size={20} /><div><h2>{selected.actionType}</h2><p>{selected.state} · {selected.riskClass}</p></div></header>
    <dl className={styles.factGrid}><div><dt>الاقتراح</dt><dd><StructuredJsonSummary raw={selected.proposedChangeJson} emptyLabel="لم يُسجّل تغيير مقترح." /></dd></div><div><dt>أسباب WAIT/القرار</dt><dd><StructuredJsonSummary raw={selected.reasonCodesJson} emptyLabel="لم تُسجّل أسباب." /></dd></div><div><dt>نافذة الدليل</dt><dd>{new Date(selected.evidenceStartUtc).toLocaleString('ar-EG')} — {new Date(selected.evidenceEndUtc).toLocaleString('ar-EG')}</dd></div><div><dt>موعد التقييم</dt><dd>{selected.evaluateAfterUtc ? new Date(selected.evaluateAfterUtc).toLocaleString('ar-EG') : 'حسب نضج البيانات'}</dd></div></dl>
    <h3>الدليل</h3><StructuredJsonSummary raw={selected.evidenceJson} emptyLabel="لم يُسجّل دليل بعد." />
    <h3>المراجعات</h3>{selected.reviews.length ? selected.reviews.map((review, index) => <div key={`${review.reviewerType}-${index}`}><p>{review.reviewerType}: {review.verdict}</p><StructuredJsonSummary raw={review.reasonsJson} emptyLabel="لم تُسجّل أسباب للمراجعة." /></div>) : <p>لم تكتمل المراجعات.</p>}
    <h3>الأوامر والمصالحة</h3>{selected.commands.length ? selected.commands.map(command => <p key={command.id}>{command.commandType}: {command.state} · {command.reconciledAtUtc ? 'تمت المصالحة' : 'لم تُحسم المصالحة'}{command.lastError ? ` · ${command.lastError}` : ''}</p>) : <p>لم يصدر أمر إلى Meta.</p>}
    <h3>الأثر والتراجع</h3>{selected.impacts.length ? selected.impacts.map(impact => <p key={impact.id}>{impact.goal}: {impact.label}{impact.rollbackCommandId ? ' · صدر أمر تراجع' : ''}</p>) : <p>القياس لم ينضج بعد؛ النتيجة WAIT.</p>}
  </aside>}
  </div>;
}
