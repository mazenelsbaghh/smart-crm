import { Film } from 'lucide-react';
import type { AdDecision, Creative, CreativeComparison } from '../types';
import { CreativeLab } from './CreativeLab';
import styles from '../AdManager.module.css';

export function CreativesView({ projectId, creatives, comparisons, decisions, onChanged, canManage = true }: { projectId: string; creatives: Creative[]; comparisons: CreativeComparison[]; decisions: AdDecision[]; onChanged: () => Promise<unknown>; canManage?: boolean }) {
  const latestTest = decisions.find(item => item.actionType === 'CreateWhatsAppTest' || item.actionType === 'CreateTest');
  return <><CreativeLab projectId={projectId} creatives={creatives} onChanged={onChanged} latestTest={latestTest} canManage={canManage} />
    {!comparisons.length && !creatives.length ? <section className={styles.empty}><Film aria-hidden="true" /><h2>لا يوجد محتوى مؤهل</h2><p>يستخدم النظام المصدر الأصلي ويصنع نصوصًا وقصّات آمنة، ولا يولّد صورة أو فيديو من الصفر.</p></section>
      : <div className={styles.tableWrap}><table><caption>المحتوى الإعلاني المؤهل ونتائجه الحالية</caption><thead><tr><th scope="col">المصدر</th><th scope="col">النوع</th><th scope="col">الأهلية</th><th scope="col">توصية البداية</th><th scope="col">النتيجة</th><th scope="col">الإرهاق</th></tr></thead><tbody>{creatives.map(item => {
        const performance = comparisons.find(row => row.id === item.id);
        return <tr key={item.id}><td>{item.sourceType}</td><td>{item.mediaType}</td><td>{item.eligibility}</td><td>{item.recommendationScore}%</td><td>{performance ? `${performance.results} · ${performance.verdict}` : 'ينتظر صرفًا كافيًا'}</td><td>{item.fatigueState}</td></tr>;
      })}</tbody></table></div>}
  </>;
}
