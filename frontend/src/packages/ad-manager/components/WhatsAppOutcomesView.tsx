import { MessageCircleMore } from 'lucide-react';
import type { AttributionTouch, Conversion, ConversionDelivery, TrackingHealth } from '../types';
import styles from '../AdManager.module.css';

export function WhatsAppOutcomesView({ rows, touches, deliveries, tracking }: { rows: Conversion[]; touches: AttributionTouch[]; deliveries: ConversionDelivery[]; tracking: TrackingHealth[] }) {
  if (!rows.length && !touches.length) return <section className={styles.empty}><MessageCircleMore /><h2>لم يصل ليد من Gateway بعد</h2><p>ستظهر أول رسالة حقيقية كـLead، ولن تتحول إلى Qualified إلا بعد ظهور نية شراء أو حجز واضحة.</p></section>;
  const latest = tracking[0];
  const gatewayLeads = rows.filter(row => row.eventType === 'Lead' || row.eventType === 'QualifiedLead');
  const leads = gatewayLeads.filter(row => row.eventType === 'Lead').length;
  const qualified = gatewayLeads.filter(row => row.eventType === 'QualifiedLead').length;
  const attributed = gatewayLeads.filter(row => row.attributionState === 'Attributed' && touches.some(touch =>
    touch.hasClickIdentifier && (touch.id === row.attributionTouchId || touch.conversionId === row.id))).length;
  return <section aria-label="نتائج واتساب">
    <div className={styles.detailPanel}>
      <header><MessageCircleMore size={20} /><div><h2>نتائج واتساب المحمّلة</h2><p>الأرقام تخص آخر نتائج محمّلة، حتى 200 نتيجة. التأهيل يأتي من تصنيف المصدر؛ غياب علامة الإعلان لا يتم تخمينه.</p></div></header>
      <dl className={styles.factGrid}>
        <div><dt>محادثات جديدة</dt><dd>{leads.toLocaleString('ar-EG')}</dd></div>
        <div><dt>ليد مؤهل</dt><dd>{qualified.toLocaleString('ar-EG')}</dd></div>
        <div><dt>منسوب لإعلان بدقة</dt><dd>{attributed.toLocaleString('ar-EG')}</dd></div>
        <div><dt>حالة الاستقبال</dt><dd>{latest?.state ?? 'غير متاحة'}</dd></div>
      </dl>
    </div>
    <div className={styles.tableWrap} tabIndex={0} role="region" aria-label="جدول نتائج واتساب وإسناد الإعلانات، قابل للتمرير"><table><caption className={styles.srOnly}>ليدز Gateway والحقيقة التجارية والإسناد</caption><thead><tr><th>النتيجة</th><th>الوقت</th><th>القيمة</th><th>درجة التحقق</th><th>الإسناد للإعلان</th><th>إرسال Meta</th></tr></thead><tbody>{rows.map(row => {
      const touch = touches.find(item => item.id === row.attributionTouchId || item.conversionId === row.id);
      const delivery = deliveries.find(item => item.conversionId === row.id);
      return <tr key={row.id}><td data-label="النتيجة">{row.eventType === 'Lead' ? 'محادثة جديدة' : row.eventType === 'QualifiedLead' ? 'ليد مؤهل' : row.eventType}</td><td data-label="الوقت">{new Date(row.occurredAtUtc).toLocaleString('ar-EG')}</td><td data-label="القيمة">{row.currentValue ?? '—'} {row.currency}</td><td data-label="درجة التحقق">{row.eventType === 'Lead' ? 'وارد من Gateway' : row.truthState ?? row.state} · {row.correctionState ?? 'None'}</td><td data-label="الإسناد للإعلان">{touch ? `${touch.method}${touch.hasClickIdentifier ? ' · علامة إعلان مثبتة' : ' · بدون معرف'}` : 'غير منسوب — لم نخمن'}</td><td data-label="إرسال Meta">{delivery ? `${delivery.eventName} · ${delivery.state}${delivery.suppressionReason ? ` · ${delivery.suppressionReason}` : ''}` : 'غير مستخدم في وضع Gateway'}</td></tr>;
    })}</tbody></table></div>
  </section>;
}
