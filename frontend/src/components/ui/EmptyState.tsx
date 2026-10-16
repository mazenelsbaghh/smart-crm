import type { ReactNode } from 'react';
import { Inbox } from 'lucide-react';
import styles from './ui.module.css';

export default function EmptyState({ title, description, action }: {
  title: string; description?: string; action?: ReactNode;
}) {
  return <div className={styles.emptyState}>
    <span className={styles.emptyIcon}><Inbox size={26} aria-hidden="true" /></span>
    <h2>{title}</h2>
    {description && <p>{description}</p>}
    {action}
  </div>;
}
