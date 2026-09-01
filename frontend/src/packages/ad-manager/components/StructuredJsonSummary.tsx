import styles from '../AdManager.module.css';

type SummaryLine = { label: string; value: string };

const readableKey = (key: string) => key
  .replace(/([a-z\d])([A-Z])/g, '$1 $2')
  .replace(/[_-]+/g, ' ')
  .trim();

const scalarText = (value: string | number | boolean | null) => {
  if (value === null) return 'غير محدد';
  if (typeof value === 'boolean') return value ? 'نعم' : 'لا';
  return String(value);
};

function summaryLines(value: unknown, path = '', depth = 0): SummaryLine[] {
  if (value === null || ['string', 'number', 'boolean'].includes(typeof value)) {
    return [{ label: path, value: scalarText(value as string | number | boolean | null) }];
  }
  if (depth >= 5) return [{ label: path, value: 'توجد تفاصيل متداخلة تحتاج مراجعة.' }];
  if (Array.isArray(value)) {
    if (value.length === 0) return [{ label: path, value: 'لا توجد عناصر' }];
    return value.flatMap((entry, index) => summaryLines(entry, path ? `${path} · ${index + 1}` : `${index + 1}`, depth + 1));
  }
  if (typeof value === 'object') {
    const entries = Object.entries(value);
    if (entries.length === 0) return [{ label: path, value: 'لا توجد تفاصيل' }];
    return entries.flatMap(([key, entry]) => summaryLines(entry, path ? `${path} · ${readableKey(key)}` : readableKey(key), depth + 1));
  }
  return [{ label: path, value: 'قيمة غير قابلة للعرض' }];
}

function parseSummary(raw: string): SummaryLine[] | null {
  if (!raw.trim()) return [];
  try {
    return summaryLines(JSON.parse(raw) as unknown);
  } catch {
    return null;
  }
}

export function structuredJsonText(raw: string, emptyLabel: string) {
  const lines = parseSummary(raw);
  if (lines === null) return 'الدليل غير متاح بصيغة قابلة للقراءة ويحتاج مراجعة.';
  if (lines.length === 0) return emptyLabel;
  return lines.map(({ label, value }) => label ? `${label}: ${value}` : value).join(' · ');
}

export function StructuredJsonSummary({ raw, emptyLabel }: { raw: string; emptyLabel: string }) {
  const lines = parseSummary(raw);
  if (lines === null) return <span>الدليل غير متاح بصيغة قابلة للقراءة ويحتاج مراجعة.</span>;
  if (lines.length === 0) return <span>{emptyLabel}</span>;
  return <ul className={styles.structuredSummary}>
    {lines.map(({ label, value }, index) => <li key={`${label}-${index}`}>
      {label && <strong>{label}</strong>}
      <span>{value}</span>
    </li>)}
  </ul>;
}
