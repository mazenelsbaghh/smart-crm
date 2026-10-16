import type { ReactNode } from 'react';
import { AlertCircle, CheckCircle2, Info } from 'lucide-react';
import styles from './ui.module.css';

export default function StatusMessage({ children, tone }: { children: ReactNode; tone: 'success' | 'danger' | 'warning' | 'info' }) {
  const Icon = tone === 'success' ? CheckCircle2 : tone === 'danger' || tone === 'warning' ? AlertCircle : Info;
  return <div role={tone === 'danger' ? 'alert' : 'status'} className={styles.statusMessage} data-tone={tone}>
    <Icon size={19} aria-hidden="true" /><div>{children}</div>
  </div>;
}
