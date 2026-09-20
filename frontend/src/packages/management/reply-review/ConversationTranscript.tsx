import MessageAttachment from '@/packages/inbox/shared/MessageAttachment';
import { minutesLabel, ReviewMessage, timeLabel } from './types';
import styles from './reply-review.module.css';

export default function ConversationTranscript({ messages, timezone, loading, older, evidenceIds, onLoad }: {
  messages: ReviewMessage[]; timezone: string; loading: boolean; older: boolean;
  evidenceIds: Set<string>; onLoad: (before?: ReviewMessage) => void;
}) {
  return <>
    <div className={styles.transcriptHeading}><h4>سجل المحادثة</h4><button disabled={loading} onClick={() => onLoad()}>تحديث الرسائل</button></div>
    <p className={styles.note}>الرسائل من كل الأيام. الفاصل هو الوقت بين رسالتين متتاليتين؛ الرسائل المشار إليها مميزة بإطار.</p>
    <div className={styles.transcript} aria-label="رسائل المحادثة" aria-busy={loading}>
      {older && <button className={styles.loadOlder} disabled={loading} onClick={() => onLoad(messages[0])}>تحميل رسائل أقدم</button>}
      {!older && messages.length > 0 && <p className={styles.start}>بداية المحادثة</p>}
      {!messages.length && <p role="status">{loading ? 'جاري تحميل الرسائل…' : 'لا توجد رسائل محملة.'}</p>}
      {messages.map((message, index) => {
        const previous = messages[index - 1];
        const gap = previous ? (new Date(message.createdAt).getTime() - new Date(previous.createdAt).getTime()) / 60_000 : 0;
        const incoming = message.direction === 'Incoming';
        return <div key={message.id}>
          {gap >= 1 && <p className={styles.gap}>بعد {minutesLabel(gap)}</p>}
          <article className={`${styles.message} ${incoming ? styles.incoming : styles.outgoing} ${evidenceIds.has(message.id) ? styles.evidence : ''}`}>
            <header><strong>{incoming ? 'العميل' : message.senderType === 'AI' ? 'الذكاء الاصطناعي' : message.senderType === 'System' ? 'النظام' : 'الموظف'}</strong>
              <time dateTime={message.createdAt}>{timeLabel(message.createdAt, timezone, true)}</time></header>
            <p dir="auto">{message.content}</p>
            {message.transcription && <p dir="auto" className={styles.transcription}>تفريغ الصوت: {message.transcription}</p>}
            {message.mediaType && <MessageAttachment assetId={message.assetId} mediaType={message.mediaType} />}
            {evidenceIds.has(message.id) && <small className={styles.warning}>رسالة مرتبطة بعلامة مراجعة</small>}
          </article></div>;
      })}
    </div>
  </>;
}
