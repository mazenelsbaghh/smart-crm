'use client';

import React, { createContext, useContext, useState, useCallback, useEffect, useRef, ReactNode } from 'react';
import { CheckCircle, AlertCircle, Info, AlertTriangle, X } from 'lucide-react';
import styles from './toast.module.css';

type ToastType = 'success' | 'error' | 'info' | 'warning';

interface Toast {
  id: string;
  message: string;
  type: ToastType;
}

interface ToastContextType {
  showToast: (message: string, type?: ToastType, duration?: number) => void;
}

const ToastContext = createContext<ToastContextType | undefined>(undefined);

export function useToast() {
  const context = useContext(ToastContext);
  if (!context) {
    throw new Error('useToast must be used within a ToastProvider');
  }
  return context;
}

interface ToastProviderProps {
  children: ReactNode;
}

export function ToastProvider({ children }: ToastProviderProps) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const timers = useRef(new Map<string, number>());

  useEffect(() => () => {
    timers.current.forEach((timer) => window.clearTimeout(timer));
    timers.current.clear();
  }, []);

  const showToast = useCallback((message: string, type: ToastType = 'success', duration = 4000) => {
    const id = crypto.randomUUID();
    setToasts((prev) => [...prev, { id, message, type }]);

    // Errors require an explicit acknowledgement so assistive-technology users do not lose recovery guidance.
    if (type === 'error') return;

    const timer = window.setTimeout(() => {
      setToasts((prev) => prev.filter((t) => t.id !== id));
      timers.current.delete(id);
    }, Math.max(type === 'warning' ? 6000 : 5000, duration));
    timers.current.set(id, timer);
  }, []);

  const removeToast = useCallback((id: string) => {
    const timer = timers.current.get(id);
    if (timer) window.clearTimeout(timer);
    timers.current.delete(id);
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const getToastIcon = (type: ToastType) => {
    switch (type) {
      case 'success':
        return <CheckCircle size={18} className={styles.iconSuccess} />;
      case 'error':
        return <AlertCircle size={18} className={styles.iconError} />;
      case 'warning':
        return <AlertTriangle size={18} className={styles.iconWarning} />;
      case 'info':
      default:
        return <Info size={18} className={styles.iconInfo} />;
    }
  };

  const getToastClass = (type: ToastType) => {
    switch (type) {
      case 'success':
        return styles.toastSuccess;
      case 'error':
        return styles.toastError;
      case 'warning':
        return styles.toastWarning;
      case 'info':
      default:
        return styles.toastInfo;
    }
  };

  return (
    <ToastContext.Provider value={{ showToast }}>
      {children}
      <div className={styles.toastContainer} aria-label="التنبيهات">
        {toasts.map((toast) => (
          <div
            key={toast.id}
            className={`${styles.toast} ${getToastClass(toast.type)}`}
            role={toast.type === 'error' ? 'alert' : 'status'}
            aria-live={toast.type === 'error' ? 'assertive' : 'polite'}
            aria-atomic="true"
          >
            <div className={styles.iconWrapper} aria-hidden="true">
              {getToastIcon(toast.type)}
            </div>
            <div className={styles.content}>{toast.message}</div>
            <button
              onClick={() => removeToast(toast.id)}
              className={styles.closeBtn}
              type="button"
              aria-label={`إغلاق التنبيه: ${toast.message}`}
            >
              <X size={14} />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}
