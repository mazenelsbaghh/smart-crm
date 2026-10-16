'use client';

import React, { useEffect, useId, useRef } from 'react';
import { isolateModal, lockBodyScroll } from './modal-accessibility';
import { useDialogViewport } from './useDialogViewport';
import styles from './confirm-dialog.module.css';

interface ConfirmDialogProps {
  isOpen: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  isAlertOnly?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export default function ConfirmDialog({
  isOpen,
  title,
  message,
  confirmLabel = 'موافق',
  cancelLabel = 'إلغاء',
  isAlertOnly = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const confirmBtnRef = useRef<HTMLButtonElement>(null);
  const cancelBtnRef = useRef<HTMLButtonElement>(null);
  const dialogRef = useRef<HTMLDivElement>(null);
  const overlayRef = useRef<HTMLDialogElement>(null);
  const previouslyFocusedRef = useRef<HTMLElement | null>(null);
  const onCancelRef = useRef(onCancel);
  const titleId = useId();
  const descriptionId = useId();
  useDialogViewport(overlayRef, isOpen);

  useEffect(() => {
    onCancelRef.current = onCancel;
  }, [onCancel]);

  useEffect(() => {
    if (!isOpen) return;

    previouslyFocusedRef.current = document.activeElement as HTMLElement | null;
    const releaseScrollLock = lockBodyScroll();
    const overlay = overlayRef.current;
    overlay?.showModal();
    (isAlertOnly ? confirmBtnRef.current : cancelBtnRef.current)?.focus();
    const restoreIsolation = overlayRef.current ? isolateModal(overlayRef.current) : () => undefined;

    const trapDialogKeyboard = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        e.stopPropagation();
        onCancelRef.current();
        return;
      }

      if (e.key === 'Tab') {
        const focusable = Array.from(
          dialogRef.current?.querySelectorAll<HTMLElement>('button:not(:disabled), [href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])') ?? [],
        ).filter(control => !control.closest('[hidden], [inert]') && getComputedStyle(control).visibility !== 'hidden');
        if (focusable.length === 0) return;
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (e.shiftKey && document.activeElement === first) {
          e.preventDefault();
          last.focus();
        } else if (!e.shiftKey && document.activeElement === last) {
          e.preventDefault();
          first.focus();
        }
      }
    };

    document.addEventListener('keydown', trapDialogKeyboard, true);
    return () => {
      document.removeEventListener('keydown', trapDialogKeyboard, true);
      overlay?.close();
      releaseScrollLock();
      restoreIsolation();
      previouslyFocusedRef.current?.focus();
    };
  }, [isAlertOnly, isOpen]);

  if (!isOpen) return null;

  return (
    <dialog ref={overlayRef} className={styles.overlay}
      role={isAlertOnly ? 'alertdialog' : undefined}
      aria-modal="true" aria-labelledby={titleId} aria-describedby={descriptionId}
      onCancel={event => { event.preventDefault(); onCancel(); }}
      onMouseDown={(event) => { if (event.target === event.currentTarget) onCancel(); }}>
      <div
        ref={dialogRef}
        className={styles.modal}
      >
        <div className={styles.header}>
          <h3 id={titleId} className={styles.title}>{title}</h3>
        </div>
        <div className={styles.body}>
          <p id={descriptionId} className={styles.message}>{message}</p>
        </div>
        <div className={styles.actions}>
          {!isAlertOnly && (
            <button
              type="button"
              ref={cancelBtnRef}
              className={styles.cancelBtn}
              onClick={onCancel}
            >
              {cancelLabel}
            </button>
          )}
          <button
            type="button"
            ref={confirmBtnRef}
            className={styles.confirmBtn}
            onClick={onConfirm}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </dialog>
  );
}
