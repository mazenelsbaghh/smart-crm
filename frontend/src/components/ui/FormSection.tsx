import type { ReactNode } from 'react';
import { ChevronDown } from 'lucide-react';
import styles from './ui.module.css';

export default function FormSection({ id, title, children }: { id: string; title: string; children: ReactNode }) {
  return <details id={id} className={styles.formSection}>
    <summary><span>{title}</span><ChevronDown size={18} aria-hidden="true" /></summary>
    <div>{children}</div>
  </details>;
}
