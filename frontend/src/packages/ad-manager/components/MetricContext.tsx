import type { MetricContext as Value } from '../types';
import styles from '../AdManager.module.css';

export function MetricContext({ value }: { value: Value }) {
  const format = (date: string) => new Intl.DateTimeFormat('ar-EG', {
    dateStyle: 'medium', timeStyle: 'short', timeZone: value.timezoneIana,
  }).format(new Date(date));
  return <p className={styles.metricContext} aria-label="سياق الأرقام"><span>{format(value.startUtc)} — {format(value.endUtc)}</span><span>{value.timezoneIana}</span><span>{value.currency}</span><span>{value.attributionWindow}</span><span>{value.truthSource}</span></p>;
}
