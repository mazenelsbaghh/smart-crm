'use client';

import React, { useEffect } from 'react';
import Link from 'next/link';
import { AlertTriangle, RefreshCw, Home } from 'lucide-react';
import styles from './error-boundary.module.css';

interface ErrorProps {
  error: Error & { digest?: string };
  reset: () => void;
}

export default function ErrorBoundary({ error, reset }: ErrorProps) {
  useEffect(() => {
    // Log the error to console or error tracker
    console.error('Unhandled runtime error:', error);
  }, [error]);

  return (
    <section className={styles.container} aria-labelledby="runtime-error-title">
      <div className={styles.card} role="alert" aria-labelledby="runtime-error-title">
        <div className={styles.iconContainer}>
          <AlertTriangle size={40} aria-hidden="true" />
        </div>

        <h1 id="runtime-error-title" className={styles.title}>حصل خطأ غير متوقع</h1>
        <p className={styles.subtitle}>
          بياناتك لم تتغيّر بسبب هذه الشاشة. حاول تحميل الجزء ده مرة تانية، أو ارجع للوحة التحكم.
        </p>

        <div className={styles.buttonGroup}>
          <button
            type="button"
            onClick={() => reset()}
            className={styles.primaryButton}
          >
            <RefreshCw size={18} aria-hidden="true" />
            حاول تاني
          </button>
          <Link href="/" className={styles.secondaryButton}>
            <Home size={18} aria-hidden="true" />
            ارجع للوحة التحكم
          </Link>
        </div>
      </div>
    </section>
  );
}
