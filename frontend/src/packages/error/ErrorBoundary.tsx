'use client';

import React, { useEffect } from 'react';
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
    <div className={styles.container}>
      <div className={styles.backdropGlow1}></div>
      <div className={styles.backdropGlow2}></div>

      <div className={styles.card}>
        <div className={styles.iconContainer}>
          <AlertTriangle size={48} color="#ec4899" className={styles.iconAnimation} />
        </div>

        <h1 className={styles.title}>Something went wrong</h1>
        <p className={styles.subtitle}>
          An unexpected error occurred while processing your request.
        </p>

        {error.message && (
          <div className={styles.errorDetails}>
            <p className={styles.errorText}>
              <strong>Details:</strong> {error.message}
            </p>
            {error.digest && (
              <p className={styles.digestText}>
                <strong>Digest ID:</strong> <code>{error.digest}</code>
              </p>
            )}
          </div>
        )}

        <div className={styles.buttonGroup}>
          <button
            onClick={() => reset()}
            className={`neon-btn ${styles.primaryButton}`}
          >
            <RefreshCw size={16} style={{ marginRight: '8px' }} />
            Try Again
          </button>
          <button
            onClick={() => (window.location.href = '/dashboard')}
            className={`neon-btn-secondary ${styles.secondaryButton}`}
          >
            <Home size={16} style={{ marginRight: '8px' }} />
            Return Home
          </button>
        </div>
      </div>
    </div>
  );
}
