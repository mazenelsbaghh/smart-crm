'use client';

import { useRef, type ReactNode } from 'react';
import styles from './ui.module.css';

export default function Tabs<Key extends string>({ idPrefix, label, tabs, selected, onChange, compactOnMobile = false }: {
  idPrefix: string; label: string; tabs: readonly { key: Key; label: string; icon?: ReactNode }[];
  selected: Key; onChange: (key: Key) => boolean | void;
  compactOnMobile?: boolean;
}) {
  const controls = useRef<Record<string, HTMLButtonElement | null>>({});
  return <div className={styles.tabsWrapper} data-compact-mobile={compactOnMobile || undefined}>
    {compactOnMobile && <label className={styles.mobileChoice}>
      <span>{label}</span>
      <select aria-label={label} value={selected} aria-controls={`${idPrefix}-panel-${selected}`}
        onChange={event => onChange(event.target.value as Key)}>
        {tabs.map(tab => <option key={tab.key} value={tab.key}>{tab.label}</option>)}
      </select>
    </label>}
    <nav role="tablist" aria-label={label} className={styles.tabList}>
    {tabs.map((tab, index) => <button key={tab.key} type="button" role="tab" id={`${idPrefix}-tab-${tab.key}`}
      aria-selected={selected === tab.key} aria-controls={`${idPrefix}-panel-${tab.key}`} tabIndex={selected === tab.key ? 0 : -1}
      ref={control => { controls.current[tab.key] = control; }} className={styles.tabButton} onClick={() => onChange(tab.key)}
      onKeyDown={event => {
        if (!['ArrowRight', 'ArrowLeft', 'Home', 'End'].includes(event.key)) return;
        event.preventDefault();
        const nextIndex = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1
          : (index + (event.key === 'ArrowRight' ? -1 : 1) + tabs.length) % tabs.length;
        const nextKey = tabs[nextIndex].key;
        if (onChange(nextKey) === false) return;
        window.requestAnimationFrame(() => controls.current[nextKey]?.focus());
      }}>{tab.icon}{tab.label}</button>)}
    </nav>
  </div>;
}
