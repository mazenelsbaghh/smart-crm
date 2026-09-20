import { analysisLabels, DailyReview, minutesLabel, numberLabel, ReviewConversation, timeLabel } from './types';
import styles from './reply-review.module.css';

export default function ConversationQueue({ report, selected, page, busy, onSelect, onPage }: {
  report: DailyReview; selected: string | null; page: number; busy: boolean;
  onSelect: (id: string) => void; onPage: (page: number) => void;
}) {
  return <div className={styles.queue}>
            {report.conversations.length ? <ul aria-label="قائمة المحادثات">{report.conversations.map(row => <li key={row.conversationId}>
              <button className={styles.row} aria-pressed={selected === row.conversationId} onClick={() => onSelect(row.conversationId)}>
                <span className={styles.rowTitle}><strong>{row.customerName}</strong><time>{timeLabel(row.lastActivityAtUtc, report.timezone)}</time></span>
                <span className={styles.muted}>{row.channel} · {numberLabel(row.incomingCount + row.outgoingCount)} رسالة خلال اليوم</span>
                <span className={styles.issueList}>{row.issues.length ? row.issues.map(issue => <span key={issue.code} className={styles.badge}>{issue.label}</span>) : <span className={styles.muted}>لا توجد علامات مرصودة</span>}</span>
                <span className={styles.rowFoot}><span>{analysisLabels[row.analysisStatus]}{row.replyQualityScore != null ? ` · ${numberLabel(row.replyQualityScore)}/١٠٠` : ''}</span>
                  <span>{responseTimeLabel(row, report)}</span></span>
              </button></li>)}</ul> : <div className={styles.empty}><h3>لا توجد محادثات في الفلتر الحالي</h3><p>اعرض «الكل» أو اختر يومًا آخر.</p></div>}
            <nav className={styles.pagination} aria-label="صفحات المحادثات"><button disabled={page <= 1 || busy} onClick={() => onPage(page - 1)}>السابق</button>
              <span>{numberLabel(page)} / {numberLabel(Math.max(1, Math.ceil(report.filteredCount / report.pageSize)))} · {numberLabel(report.filteredCount)} محادثة</span>
              <button disabled={page * report.pageSize >= report.filteredCount || busy} onClick={() => onPage(page + 1)}>التالي</button></nav>
          </div>;
}

function responseTimeLabel(row: ReviewConversation, report: DailyReview) {
  if (!row.waitingSinceUtc) return `أطول رد: ${row.longestResponseMinutes == null ? '—' : minutesLabel(row.longestResponseMinutes)}`;
  const evaluatedAt = Math.min(new Date(report.generatedAtUtc).getTime(), new Date(report.windowEndUtc).getTime());
  return `انتظار: ${minutesLabel((evaluatedAt - new Date(row.waitingSinceUtc).getTime()) / 60_000)}`;
}
