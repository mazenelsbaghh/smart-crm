import { CheckCircle2, CircleDollarSign, PauseCircle, Target } from 'lucide-react';
import type { AdvertisingReadiness, ReadinessItem } from '../types';
import styles from '../AdManager.module.css';

const readinessReason: Record<string, string> = {
  ADS_GATEWAY_NOT_CONNECTED: 'Gateway واتساب غير متصل بالمشروع',
  ADS_DATASET_REQUIRED: 'اختر WABA ورقم واتساب وDataset من موارد Meta الرسمية',
  ADS_CAPABILITY_MISSING: 'لم يتم فحص حساب الإعلانات والصفحة بعد',
  ADS_WHATSAPP_CAPABILITY_UNPROVEN: 'أعد حفظ الإعداد لإكمال فحص حساب الإعلانات والصفحة مع Gateway',
  ADS_OFFER_REQUIRED: 'لا يوجد عرض مؤهل في عقل الشركة',
  ADS_ENVELOPE_REQUIRED: 'لم يتم حفظ سقف الميزانية بعد',
  ADS_ENVELOPE_REAUTHORIZE: 'السقف القديم ناقص؛ أعد حفظ السقف اليومي والشهري والدولة',
  ADS_TRACKING_INCIDENT: 'النتائج تصل، لكن بعض الحجوزات غير مربوطة بمحادثة وإعلان حتى الآن',
  ADS_TRACKING_SNAPSHOT_REQUIRED: 'لم تصل عينة قياس بعد',
  ADS_TRACKING_UNSAFE: 'جودة القياس أقل من الحد الآمن',
  ADS_TRACKING_STALE: 'آخر قياس قديم ويحتاج تحديثًا',
};

function ReadinessCheck({ item }: { item: ReadinessItem }) {
  return <li className={item.ready ? styles.complete : ''}>
    <span>{item.ready ? <CheckCircle2 size={18} aria-hidden="true" /> : item.key === 'budget' ? <CircleDollarSign size={18} aria-hidden="true" /> : <PauseCircle size={18} aria-hidden="true" />}</span>
    <div><strong>{item.label}</strong>{item.reason && <small>{readinessReason[item.reason] ?? item.reason}</small>}</div>
  </li>;
}

export function ReadinessPanel({ readiness, onConfigure, canManage = true }: { readiness: AdvertisingReadiness; onConfigure: () => void; canManage?: boolean }) {
  if (readiness.ready) return null;
  const incomplete = readiness.items.filter(item => !item.ready);
  const complete = readiness.items.filter(item => item.ready);
  return <section className={styles.readiness} aria-labelledby="readiness-title">
    <div className={styles.readinessHeading}>
      <div><h2 id="readiness-title"><Target size={20} aria-hidden="true" />جهّز المشروع قبل الصرف</h2><p>لن يتم إنشاء أو تعديل أي ميزانية قبل اكتمال العناصر التالية.</p></div>
      {canManage ? <button className={styles.primaryButton} onClick={onConfigure}>إكمال الإعداد</button>
        : <p className={styles.readOnlyBadge}>يلزم دور مالك أو مدير لإكمال الإعداد.</p>}
    </div>
    <ol>{incomplete.map(item => <ReadinessCheck key={item.key} item={item} />)}</ol>
    {complete.length > 0 && <details className={styles.completedChecks}><summary><CheckCircle2 size={17} aria-hidden="true" />{complete.length.toLocaleString('ar-EG')} عناصر مكتملة</summary><ol>{complete.map(item => <ReadinessCheck key={item.key} item={item} />)}</ol></details>}
  </section>;
}
