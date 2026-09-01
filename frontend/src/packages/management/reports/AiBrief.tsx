import { AlertTriangle, BrainCircuit, CheckCircle2, Lightbulb } from 'lucide-react';
import type { AiDigest } from './types';
import styles from './reports.module.css';

export function AiBrief({ digest, coverage }: { digest?: AiDigest | null; coverage: number }) {
  return (
    <section className={styles.aiBrief} aria-labelledby="ai-brief-title">
      <div className={styles.aiBriefTitle}>
        <BrainCircuit size={22} aria-hidden="true" />
        <div><span className={styles.eyebrow}>مدير المبيعات AI</span><h2 id="ai-brief-title">قراءة الفترة</h2></div>
        <span className={styles.coverage}>{coverage.toLocaleString('ar-EG')}٪ تغطية</span>
      </div>
      {!digest ? (
        <div className={styles.emptyInline}>
          <p>الأرقام جاهزة، لكن الملخص الإداري لم يُنشأ لهذه الفترة بعد.</p>
          <span>اضغط «حلّل الآن» لإنشاء القراءة والتوصيات.</span>
        </div>
      ) : (
        <>
          <p className={styles.executiveSummary}>{digest.executiveSummary}</p>
          <div className={styles.briefColumns}>
            <BriefList icon={CheckCircle2} title="ما اكتشفه" items={digest.findings} />
            <BriefList icon={Lightbulb} title="ماذا نفعل" items={digest.recommendations} />
            {digest.risks.length > 0 && <BriefList icon={AlertTriangle} title="حدود القراءة" items={digest.risks} />}
          </div>
          <div className={styles.modelNote}>تم التوليد {new Date(digest.generatedAtUtc).toLocaleString('ar-EG', { timeZone: 'Africa/Cairo', dateStyle: 'medium', timeStyle: 'short' })}، {digest.model}</div>
        </>
      )}
    </section>
  );
}

function BriefList({ icon: Icon, title, items }: { icon: typeof CheckCircle2; title: string; items: string[] }) {
  return (
    <div className={styles.briefList}>
      <h3><Icon size={16} aria-hidden="true" />{title}</h3>
      {items.length ? <ul>{items.map((item) => <li key={item}>{item}</li>)}</ul> : <p>لا توجد نقاط مسجلة.</p>}
    </div>
  );
}
