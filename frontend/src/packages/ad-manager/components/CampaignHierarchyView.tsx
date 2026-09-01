import { Megaphone } from 'lucide-react';
import type { ManagedAd } from '../types';
import styles from '../AdManager.module.css';

export function CampaignHierarchyView({ rows }: { rows: ManagedAd[] }) {
  if (!rows.length) return <section className={styles.empty}><Megaphone /><h2>لا توجد حملة مدارة</h2><p>الإنشاء يبدأ متوقفًا، ثم تُقرأ الحالة الفعلية من Meta قبل أي تشغيل.</p></section>;
  return <div className={styles.tableWrap}><table><caption className={styles.srOnly}>الحالة المخططة والفعلية للتسلسل الإعلاني</caption><thead><tr><th>التسلسل</th><th>المخطط</th><th>الفعلي</th><th>الاختلاف</th><th>الميزانية</th><th>الملكية</th></tr></thead><tbody>{rows.map(row => {
    const drift = row.status.toUpperCase() !== row.effectiveStatus?.toUpperCase();
    return <tr key={row.id}><td><strong>{row.name}</strong><small className={styles.hierarchyIds}>Campaign {row.campaignExternalId ?? '—'} · AdSet {row.adSetExternalId ?? '—'} · Ad {row.adExternalId ?? '—'}</small></td><td>{row.status}</td><td>{row.effectiveStatus || 'غير مقروء'}</td><td><span className={drift ? styles.driftBlocking : styles.driftClear}>{drift ? 'يحتاج مصالحة' : 'متطابق'}</span></td><td>{row.dailyBudget}</td><td>{row.managementSource}</td></tr>;
  })}</tbody></table></div>;
}
