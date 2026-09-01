import { ShieldCheck, Target } from 'lucide-react';
import type { AudienceStrategy } from '../types';
import styles from '../AdManager.module.css';

const list = (raw: string) => {
  try {
    const parsedAudience = JSON.parse(raw) as unknown;
    return Array.isArray(parsedAudience) ? parsedAudience.map(String) : parsedAudience && typeof parsedAudience === 'object' ? Object.values(parsedAudience).flatMap(audienceEntry => Array.isArray(audienceEntry) ? audienceEntry.map(String) : [String(audienceEntry)]) : [];
  } catch { return []; }
};

const evidence = (raw: string) => {
  try { return JSON.parse(raw) as { estimatedReach?: number; withheldReasons?: string[]; reasonCodes?: string[] }; }
  catch { return {}; }
};

const suggestionLabel = (value: string) => value === 'AdvantagePlusBroad' ? 'استهداف واسع Advantage+ داخل القيود الإلزامية' : value;

const specialCategory = (raw: string) => {
  try {
    const value = JSON.parse(raw) as { SpecialAdCategory?: string | null; Resolved?: boolean };
    return value.SpecialAdCategory ? value.SpecialAdCategory : value.Resolved ? 'لا توجد فئة إعلانية خاصة' : 'تحتاج مراجعة';
  } catch { return 'تحتاج مراجعة'; }
};

export function AudiencesView({ rows }: { rows: AudienceStrategy[] }) {
  if (!rows.length) return <section className={styles.empty}><Target /><h2>لا توجد استراتيجية جمهور بعد</h2><p>Advantage+ سيعمل واسعًا داخل الموقع والعمر والاستبعادات المصرّح بها.</p></section>;
  return <section className={styles.detailList} aria-label="استراتيجيات الجمهور">{rows.map(row => {
    const included = list(row.includedGeoJson);
    const excluded = [...list(row.excludedGeoJson), ...list(row.customAudienceExclusionsJson)];
    const languages = list(row.requiredLanguagesJson);
    const suggestions = list(row.audienceSuggestionsJson);
    const proof = evidence(row.evidenceJson);
    const reach = evidence(row.estimatedReachJson);
    const withheld = proof.withheldReasons ?? proof.reasonCodes ?? [];
    return <article key={row.id}>
      <header className={styles.detailHeading}><ShieldCheck size={19} /><div><strong>الجمهور الأساسي · نسخة {row.version}</strong><p>الـAI يوسّع التوزيع داخل الحدود التالية فقط، ولا يمكنه تغييرها.</p></div></header>
      <div className={styles.audienceMode}><Target size={17} /><div><strong>طريقة الاستهداف</strong><span>Broad + Advantage+: ميتا تبحث عن الأكثر قابلية لبدء محادثة واتساب، مع تثبيت البلد والعمر والاستبعادات.</span></div></div>
      <dl className={styles.factGrid}>
        <div><dt>المواقع المسموحة — إلزامي</dt><dd>{included.join('، ') || 'لم تُحدّد'}</dd></div><div><dt>العمر — إلزامي</dt><dd>من {row.minimumAge}{row.maximumAgeSuggestion ? ` إلى ${row.maximumAgeSuggestion}` : '+'} سنة</dd></div>
        <div><dt>اللغات</dt><dd>{languages.join('، ') || 'كل اللغات داخل السوق'}</dd></div><div><dt>الاستبعادات</dt><dd>{excluded.join('، ') || 'لا توجد استبعادات محفوظة'}</dd></div>
        <div><dt>إشارات Advantage+ — اقتراح فقط</dt><dd>{suggestions.map(suggestionLabel).join('، ') || 'بدون اهتمامات ضيقة'}</dd></div><div><dt>الوصول التقديري</dt><dd>{(reach.estimatedReach ?? proof.estimatedReach)?.toLocaleString('ar-EG') || 'سيظهر بعد اعتماد Meta للحملة المتوقفة'}</dd></div>
        <div><dt>الفئة الإعلانية الخاصة</dt><dd>{specialCategory(row.specialCategoryConstraintsJson)}</dd></div><div><dt>حالة الأهلية</dt><dd>{row.state === 'Eligible' ? 'مؤهل للاستخدام' : row.state}</dd></div>
      </dl>
      {withheld.length > 0 && <p className={styles.withheldReason}>تم حجب التوسيع أو التقدير: {withheld.join('، ')}</p>}
    </article>;
  })}</section>;
}
