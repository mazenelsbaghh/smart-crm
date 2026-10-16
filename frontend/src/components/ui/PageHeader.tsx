import type { ReactNode } from 'react';
import styles from './ui.module.css';

interface PageHeaderProps {
  title: string;
  titleId?: string;
  description?: string;
  eyebrow?: ReactNode;
  actions?: ReactNode;
  icon?: ReactNode;
}

export default function PageHeader({ title, titleId, description, eyebrow, actions, icon }: PageHeaderProps) {
  return <header className={styles.pageHeader}>
    <div className={styles.pageHeading}>
      {eyebrow && <div className={styles.eyebrow}>{eyebrow}</div>}
      <div className={styles.pageTitleRow}>{icon && <span className={styles.pageIcon} aria-hidden="true">{icon}</span>}<h1 id={titleId}>{title}</h1></div>
      {description && <p>{description}</p>}
    </div>
    {actions && <div className={styles.headerActions}>{actions}</div>}
  </header>;
}
