import { FormEvent, useEffect, useRef, useState } from 'react';
import { Bot, Send } from 'lucide-react';
import type { AnalystAnswer } from './types';
import styles from './reports.module.css';

const suggestions = ['ليه الحجوزات قلت؟', 'إيه أكبر اعتراض متكرر؟', 'مين محتاج متابعة دلوقتي؟'];

export function AskAnalyst({ onAsk }: { onAsk: (question: string) => Promise<AnalystAnswer> }) {
  const [question, setQuestion] = useState('');
  const [answer, setAnswer] = useState<AnalystAnswer | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const handleShortcut = (event: KeyboardEvent) => {
      if (event.altKey && event.key.toLowerCase() === 'a') {
        event.preventDefault();
        inputRef.current?.focus();
      }
    };
    window.addEventListener('keydown', handleShortcut);
    return () => window.removeEventListener('keydown', handleShortcut);
  }, []);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const clean = question.trim();
    if (clean.length < 3 || loading) return;
    setLoading(true);
    setError('');
    try {
      setAnswer(await onAsk(clean));
    } catch {
      setError('تعذر على محلل AI الإجابة الآن. جرّب مرة أخرى.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <section className={styles.askPanel} aria-labelledby="ask-title">
      <div className={styles.askTitle}><Bot size={22} aria-hidden="true" /><div><span className={styles.eyebrow}>اسأل بياناتك</span><h2 id="ask-title">ماذا تريد أن تفهم؟</h2></div></div>
      <form className={styles.askForm} onSubmit={submit}>
        <label className={styles.srOnly} htmlFor="sales-analyst-question">سؤال لمحلل المبيعات</label>
        <input ref={inputRef} id="sales-analyst-question" value={question} onChange={(event) => setQuestion(event.target.value)} placeholder="مثال: ليه الناس اللي سألت عن المواعيد ما حجزتش؟" minLength={3} maxLength={600} />
        <button type="submit" disabled={loading || question.trim().length < 3}>{loading ? 'يراجع كل التحليلات…' : 'اسأل'}<Send size={16} aria-hidden="true" /><kbd>Alt A</kbd></button>
      </form>
      <div className={styles.suggestions}>{suggestions.map((item) => <button type="button" key={item} onClick={() => { setQuestion(item); inputRef.current?.focus(); }}>{item}</button>)}</div>
      {error && <p className={styles.inlineError} role="alert">{error}</p>}
      {answer && (
        <div className={styles.answer} role="status">
          <div className={styles.answerStats} aria-label="نطاق تحليل الإجابة">
            <div><span>إجمالي الشاتات</span><strong>{answer.totalConversations.toLocaleString('ar-EG')}</strong></div>
            <div><span>تحليل محفوظ</span><strong>{answer.analyzedConversations.toLocaleString('ar-EG')}</strong></div>
            <div><span>راجع AI تفاصيلها</span><strong>{answer.detailedAnalysesReviewed.toLocaleString('ar-EG')}</strong></div>
            <div><span>غير محلل</span><strong>{Math.max(0, answer.totalConversations - answer.analyzedConversations).toLocaleString('ar-EG')}</strong></div>
            <div><span>التغطية</span><strong>{answer.analysisCoverage.toLocaleString('ar-EG')}٪</strong></div>
          </div>
          <p>{answer.answer}</p>
          <span>اختار AI عدد {answer.conversationIds.length.toLocaleString('ar-EG')} محادثات كأدلة مباشرة؛ بعد مراجعة تفاصيل {answer.detailedAnalysesReviewed.toLocaleString('ar-EG')} تحليل.</span>
        </div>
      )}
    </section>
  );
}
