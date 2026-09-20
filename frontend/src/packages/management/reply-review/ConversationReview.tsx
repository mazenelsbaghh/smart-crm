'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { isAxiosError } from 'axios';
import { api } from '@/services/api';
import ConversationTranscript from './ConversationTranscript';
import { analysisLabels, inboxLink, ReplyDraft, ReviewConversation, ReviewMessage, reviewUrl, timeLabel } from './types';
import styles from './reply-review.module.css';

const limit = 100;
function errorMessage(error: unknown) {
  return isAxiosError(error) && typeof error.response?.data?.error === 'string'
    ? error.response.data.error : 'تعذر إتمام الطلب. أعد المحاولة.';
}

export default function ConversationReview({ projectId, row, timezone, canManage, onReviewed, onBack }: {
  projectId: string; row: ReviewConversation; timezone: string; canManage: boolean; onReviewed: () => void; onBack: () => void;
}) {
  const [messages, setMessages] = useState<ReviewMessage[]>([]);
  const [older, setOlder] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [transcriptError, setTranscriptError] = useState('');
  const [action, setAction] = useState('');
  const [draft, setDraft] = useState<ReplyDraft | null>(null);
  const [notice, setNotice] = useState('');
  const request = useRef<AbortController | null>(null);
  const actionRequest = useRef<AbortController | null>(null);
  const url = `${reviewUrl(projectId)}/conversations/${row.conversationId}`;
  const href = inboxLink(row.conversationId, row.channel);
  const loadMessages = useCallback((before?: ReviewMessage) => {
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    return api.get<ReviewMessage[]>(`/api/conversations/${row.conversationId}/messages`, {
        params: { limit, before: before?.createdAt, beforeId: before?.id }, signal: controller.signal,
      }).then(response => {
      if (controller.signal.aborted) return;
      setMessages(current => before ? [...response.data.filter(item => !current.some(m => m.id === item.id)), ...current] : response.data);
      setOlder(response.data.length === limit);
      setTranscriptError('');
      }).catch(failure => { if (!controller.signal.aborted) setTranscriptError(errorMessage(failure)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
  }, [row.conversationId]);

  useEffect(() => {
    void loadMessages();
    return () => request.current?.abort();
  }, [loadMessages, row.lastActivityAtUtc]);
  useEffect(() => () => actionRequest.current?.abort(), []);

  async function runAction(kind: 'analyze' | 'draft-reply' | 'schedule') {
    const controller = new AbortController();
    actionRequest.current = controller;
    setAction(kind); setNotice(''); setError('');
    if (kind === 'draft-reply') setDraft(null);
    try {
      const endpoint = kind === 'schedule' ? `${reviewUrl(projectId)}/processing/conversations/${row.conversationId}/review` : `${url}/${kind}`;
      const response = await api.post<ReplyDraft>(endpoint, {}, { signal: controller.signal });
      if (controller.signal.aborted) return;
      if (kind === 'draft-reply') {
        setDraft(response.data);
        void loadMessages();
        setNotice('مسودة فقط؛ راجع المعلومات قبل نسخها وإرسالها من المحادثة.');
      } else { setNotice(kind === 'schedule' ? 'تمت جدولة مراجعة المحادثة ومعالجتها في الطابور.' : 'تم تحديث التحليل.'); onReviewed(); }
    } catch (failure) { if (!controller.signal.aborted) setError(errorMessage(failure)); }
    finally { if (!controller.signal.aborted) setAction(''); }
  }

  async function copyDraft() {
    if (!draft) return;
    try { await navigator.clipboard.writeText(draft.content); setNotice('تم نسخ المسودة. افتح المحادثة لمراجعتها وإرسالها.'); }
    catch { setNotice('تعذر النسخ التلقائي. يمكنك تحديد النص ونسخه يدويًا.'); }
  }
  const latest = messages.filter(message => message.messageType !== 'Reaction').at(-1);
  const draftOutdated = !!draft && ((latest && latest.id !== draft.basedOnMessageId)
    || new Date(row.lastActivityAtUtc) > new Date(draft.generatedAtUtc));
  const evidenceIds = new Set(row.issues.flatMap(issue => issue.messageIds));
  return <section aria-label={`مراجعة محادثة ${row.customerName}`}>
    <header className={styles.detailHeading}><div><button className={styles.mobileBack} onClick={onBack}>الرجوع للقائمة</button><h3>{row.customerName}</h3>
      <span className={styles.muted}>{row.channel} · {analysisLabels[row.analysisStatus]}</span></div>
      {href && <a href={href}>فتح المحادثة ↗</a>}</header>
    {row.humanHandoffPending && <p className={styles.warningPanel}>العميل ينتظر موظفًا. الردود والمتابعات التلقائية موقوفة لحين إعادة فتح المحادثة.</p>}
    {(row.summary || row.recommendation) && <div className={styles.analysis}>
      {row.analysisStatus !== 'Current' && <p className={styles.warning}>هذا التحليل غير مطابق تمامًا لرسائل اليوم المختار.</p>}
      {row.summary && <p>{row.summary}</p>}{row.recommendation && <p><strong>المقترح: </strong>{row.recommendation}</p>}
      {row.analyzedAtUtc && <small>حُلّل في {timeLabel(row.analyzedAtUtc, timezone, true)}</small>}
    </div>}
    {canManage && <div className={styles.actions}><button disabled={!!action} onClick={() => void runAction('analyze')}>{action === 'analyze' ? 'جاري التحليل…' : 'تحديث تحليل المحادثة'}</button>
      <button disabled={!!action} onClick={() => void runAction('schedule')}>{action === 'schedule' ? 'جاري الجدولة…' : 'جدولة المراجعة والمعالجة'}</button>
      <button className={styles.primary} disabled={!!action} onClick={() => void runAction('draft-reply')}>{action === 'draft-reply' ? 'جاري تجهيز الرد…' : 'تجهيز مسودة رد مصحح'}</button></div>}
    {error && <p role="alert" className={styles.error}>{error}</p>}
    {notice && <p role="status" className={styles.note}>{notice}</p>}
    {draft && <div className={styles.draft}><label htmlFor="corrective-draft">مسودة للمراجعة</label>
      {draftOutdated && <p role="alert" className={styles.warning}>المحادثة اتغيرت بعد المسودة. حدّث الرسائل وجهّز ردًا جديدًا.</p>}
      <textarea id="corrective-draft" value={draft.content} onChange={e => setDraft({ ...draft, content: e.target.value })} rows={5} />
      <button disabled={draftOutdated || !draft.content.trim()} onClick={() => void copyDraft()}>نسخ المسودة</button></div>}
    {transcriptError && <p role="alert" className={styles.error}>{transcriptError}</p>}
    <ConversationTranscript messages={messages} timezone={timezone} loading={loading} older={older} evidenceIds={evidenceIds}
      onLoad={before => { setLoading(true); void loadMessages(before); }} />
  </section>;
}
