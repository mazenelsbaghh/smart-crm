'use client';

import { useEffect, useRef, type KeyboardEvent, type ReactNode } from 'react';
import styles from './dialog.module.css';

function containDialogKeyboard(event: KeyboardEvent<HTMLDialogElement>) {
  event.stopPropagation();
  if (event.key !== 'Tab') return;
  const controls = Array.from(event.currentTarget.querySelectorAll<HTMLElement>(
    'button:not(:disabled), a[href], input:not(:disabled):not([type="hidden"]), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])',
  )).filter(control => control.getClientRects().length > 0);
  const firstControl = controls[0];
  const lastControl = controls[controls.length - 1];
  if (event.shiftKey && document.activeElement === firstControl) {
    event.preventDefault();
    lastControl?.focus();
  } else if (!event.shiftKey && document.activeElement === lastControl) {
    event.preventDefault();
    firstControl?.focus();
  }
}

export default function Dialog({ children, label, onClose, placement = 'center', open = true }: {
  children: ReactNode; label: string; onClose: () => void; placement?: 'center' | 'drawer'; open?: boolean;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    if (!open) return;
    const dialog = dialogRef.current!;
    const previousFocus = document.activeElement as HTMLElement | null;
    const previousOverflow = document.body.style.overflow;
    dialog.showModal();
    document.body.style.overflow = 'hidden';
    return () => {
      dialog.close();
      document.body.style.overflow = previousOverflow;
      previousFocus?.focus();
    };
  }, [open]);
  return <dialog ref={dialogRef} aria-label={label} data-placement={placement} className={styles.dialog}
    onKeyDown={containDialogKeyboard}
    onCancel={event => { event.preventDefault(); onClose(); }}
    onClick={event => { if (event.target === event.currentTarget) onClose(); }}>
    <div className={styles.surface}>{children}</div>
  </dialog>;
}
