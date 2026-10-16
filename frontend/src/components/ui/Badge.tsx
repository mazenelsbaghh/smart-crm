import type { ReactNode } from 'react';
import styles from './ui.module.css';

export default function Badge({ children, tone = 'neutral' }: {
  children: ReactNode; tone?: 'neutral' | 'success' | 'warning' | 'danger' | 'info';
}) {
  return <span className={styles.badge} data-tone={tone}>{children}</span>;
}
