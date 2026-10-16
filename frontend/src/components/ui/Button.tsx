import type { ButtonHTMLAttributes } from 'react';
import { LoaderCircle } from 'lucide-react';
import styles from './ui.module.css';

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: 'primary' | 'secondary' | 'danger' | 'ghost';
  loading?: boolean;
};

export default function Button({ variant = 'secondary', loading = false, children,
  className = '', disabled, type = 'button', ...props }: ButtonProps) {
  return <button {...props} type={type} data-variant={variant} aria-busy={loading || undefined}
    disabled={disabled || loading} className={`${styles.button} ${className}`}>
    {loading && <LoaderCircle size={16} className={styles.spin} aria-hidden="true" />}
    {children}
  </button>;
}
