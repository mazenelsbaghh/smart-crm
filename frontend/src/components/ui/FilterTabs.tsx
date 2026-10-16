import type { ReactNode } from 'react';
import styles from './ui.module.css';

export default function FilterTabs<Key extends string>({ label, options, selected, onChange, compactOnMobile = false }: {
  label: string; options: readonly { key: Key; label: string; count?: number; description?: string; icon?: ReactNode; tone?: 'info' | 'success' | 'warning' | 'danger' | 'intelligence' }[];
  selected: Key; onChange: (key: Key) => void;
  compactOnMobile?: boolean;
}) {
  const description = options.find(option => option.key === selected)?.description;
  return <div className={styles.filterTabs} data-compact-mobile={compactOnMobile || undefined}>
    {compactOnMobile && <label className={styles.mobileChoice}>
      <span>{label}</span>
      <select aria-label={label} value={selected} onChange={event => onChange(event.target.value as Key)}>
        {options.map(option => <option key={option.key} value={option.key}>
          {option.label}{option.count !== undefined ? ` (${option.count.toLocaleString('ar-EG')})` : ''}
        </option>)}
      </select>
    </label>}
    <div role="group" aria-label={label} className={styles.filterOptions}>
      {options.map(option => <button type="button" key={option.key} aria-pressed={selected === option.key} data-tone={option.tone}
        onClick={() => onChange(option.key)}>{option.icon}{option.label}
        {option.count !== undefined && <span>{option.count.toLocaleString('ar-EG')}</span>}
      </button>)}
    </div>
    {description && <p role="status">{description}</p>}
  </div>;
}
