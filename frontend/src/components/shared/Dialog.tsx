'use client';

import { useEffect, useRef, type KeyboardEvent, type ReactNode } from 'react';
import { X } from 'lucide-react';
import { useDialogViewport } from './useDialogViewport';
import { lockBodyScroll } from './modal-accessibility';
import styles from './dialog.module.css';

function containDialogKeyboard(event: KeyboardEvent<HTMLDialogElement>) {
  event.stopPropagation();
  if (event.key !== 'Tab') return;
  const controls = Array.from(event.currentTarget.querySelectorAll<HTMLElement>(
    'button:not(:disabled), a[href], input:not(:disabled):not([type="hidden"]), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])',
  )).filter(control => control.getClientRects().length > 0 && !control.closest('[inert], [hidden]')
    && getComputedStyle(control).visibility !== 'hidden');
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

export default function Dialog({ children, label, onClose, placement = 'center', open = true, title, footer, closeLabel = 'إغلاق', closeDisabled = false }: {
  children: ReactNode; label: string; onClose: () => void; placement?: 'center' | 'drawer'; open?: boolean;
  title?: ReactNode; footer?: ReactNode; closeLabel?: string; closeDisabled?: boolean;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  useDialogViewport(dialogRef, open);
  useEffect(() => {
    if (!open) return;
    const dialog = dialogRef.current!;
    const previousFocus = document.activeElement as HTMLElement | null;
    const releaseScrollLock = lockBodyScroll();
    dialog.showModal();
    return () => {
      dialog.close();
      releaseScrollLock();
      previousFocus?.focus();
    };
  }, [open]);
  return <dialog ref={dialogRef} aria-label={label} data-placement={placement} className={styles.dialog}
    onKeyDown={containDialogKeyboard}
    onCancel={event => { event.preventDefault(); if (!closeDisabled) onClose(); }}
    onClick={event => { if (event.target === event.currentTarget && !closeDisabled) onClose(); }}>
    {title && <header className={styles.header}>
      <h2>{title}</h2>
      <button type="button" className={styles.closeButton} aria-label={closeLabel} disabled={closeDisabled} onClick={onClose}><X size={21} aria-hidden="true" /></button>
    </header>}
    <div className={styles.body}>{children}</div>
    {footer && <footer className={styles.footer}>{footer}</footer>}
  </dialog>;
}
