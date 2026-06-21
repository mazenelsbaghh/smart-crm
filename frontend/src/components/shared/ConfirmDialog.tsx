'use client';

import React, { useEffect, useRef } from 'react';
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

  // Focus confirm button when modal opens
  useEffect(() => {
    if (isOpen) {
      setTimeout(() => {
        confirmBtnRef.current?.focus();
      }, 50);
    }
  }, [isOpen]);

  // Trap keyboard events
  useEffect(() => {
    if (!isOpen) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        onCancel();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, onCancel]);

  if (!isOpen) return null;

  return (
    <div className={styles.overlay} onClick={onCancel}>
      <div 
        className={styles.modal} 
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirm-dialog-title"
        aria-describedby="confirm-dialog-desc"
      >
        <div className={styles.header}>
          <h3 id="confirm-dialog-title" className={styles.title}>{title}</h3>
        </div>
        <div className={styles.body}>
          <p id="confirm-dialog-desc" className={styles.message}>{message}</p>
        </div>
        <div className={styles.actions}>
          {!isAlertOnly && (
            <button 
              type="button" 
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
    </div>
  );
}
