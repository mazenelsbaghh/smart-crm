import { CheckCircle2, CircleDollarSign, PauseCircle, Target } from 'lucide-react';
import type { AdvertisingReadiness } from '../types';
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

export function ReadinessPanel({ readiness, onConfigure, canManage = true }: { readiness: AdvertisingReadiness; onConfigure: () => void; canManage?: boolean }) {
  if (readiness.ready) return null;
  return <section className={styles.readiness} aria-labelledby="readiness-title"><div><Target size={22} /><h2 id="readiness-title">جهّز المشروع قبل الصرف</h2><p>لن يتم إنشاء أو تعديل أي ميزانية قبل اكتمال العناصر التالية.</p></div>
    <ol>{readiness.items.map(item => <li key={item.key} className={item.ready ? styles.complete : ''}><span>{item.ready ? <CheckCircle2 size={18} /> : item.key === 'budget' ? <CircleDollarSign size={18} /> : <PauseCircle size={18} />}</span><div><strong>{item.label}</strong>{item.reason && <small>{readinessReason[item.reason] ?? item.reason}</small>}</div></li>)}</ol>
    {canManage ? <button className={styles.primaryButton} onClick={onConfigure}>إكمال الإعداد</button>
      : <p className={styles.readOnlyBadge}>يلزم دور مالك أو مدير لإكمال الإعداد.</p>}</section>;
}
