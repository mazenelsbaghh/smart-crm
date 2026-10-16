import Badge from '../../../components/ui/Badge';
import { Megaphone } from 'lucide-react';
import type { ManagedAd } from '../types';
import styles from '../AdManager.module.css';

export function CampaignHierarchyView({ rows }: { rows: ManagedAd[] }) {
  if (!rows.length) return <section className={styles.empty}><Megaphone /><h2>لا توجد حملة مدارة</h2><p>الإنشاء يبدأ متوقفًا، ثم تُقرأ الحالة الفعلية من Meta قبل أي تشغيل.</p></section>;
  return <div className={styles.tableWrap} tabIndex={0} role="region" aria-label="جدول التسلسل الإعلاني وحالته، قابل للتمرير"><table><caption className={styles.srOnly}>الحالة المخططة والفعلية للتسلسل الإعلاني</caption><thead><tr><th>التسلسل</th><th>المخطط</th><th>الفعلي</th><th>الاختلاف</th><th>الميزانية</th><th>الملكية</th></tr></thead><tbody>{rows.map(row => {
    const drift = row.status.toUpperCase() !== row.effectiveStatus?.toUpperCase();
    return <tr key={row.id}><td data-label="التسلسل"><strong>{row.name}</strong><small className={styles.hierarchyIds}>Campaign {row.campaignExternalId ?? '—'} · AdSet {row.adSetExternalId ?? '—'} · Ad {row.adExternalId ?? '—'}</small></td><td data-label="المخطط">{row.status}</td><td data-label="الفعلي">{row.effectiveStatus || 'غير مقروء'}</td><td data-label="الاختلاف"><Badge tone={drift ? 'warning' : 'success'}>{drift ? 'يحتاج مصالحة' : 'متطابق'}</Badge></td><td data-label="الميزانية">{row.dailyBudget}</td><td data-label="الملكية">{row.managementSource}</td></tr>;
  })}</tbody></table></div>;
}
