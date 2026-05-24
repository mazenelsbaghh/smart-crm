'use client';

import React, { useEffect } from 'react';
import { AlertTriangle, RefreshCw, Home } from 'lucide-react';

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
    <div style={styles.container}>
      <div style={styles.backdropGlow1}></div>
      <div style={styles.backdropGlow2}></div>

      <div style={styles.card}>
        <div style={styles.iconContainer}>
          <AlertTriangle size={48} color="#ec4899" style={styles.iconAnimation} />
        </div>

        <h1 style={styles.title}>Something went wrong</h1>
        <p style={styles.subtitle}>
          An unexpected error occurred while processing your request.
        </p>

        {error.message && (
          <div style={styles.errorDetails}>
            <p style={styles.errorText}>
              <strong>Details:</strong> {error.message}
            </p>
            {error.digest && (
              <p style={styles.digestText}>
                <strong>Digest ID:</strong> <code>{error.digest}</code>
              </p>
            )}
          </div>
        )}

        <div style={styles.buttonGroup}>
          <button
            onClick={() => reset()}
            style={styles.primaryButton}
            className="neon-btn"
          >
            <RefreshCw size={16} style={{ marginRight: '8px' }} />
            Try Again
          </button>
          <button
            onClick={() => (window.location.href = '/dashboard')}
            style={styles.secondaryButton}
            className="neon-btn-secondary"
          >
            <Home size={16} style={{ marginRight: '8px' }} />
            Return Home
          </button>
        </div>
      </div>
    </div>
  );
}

const styles: { [key: string]: React.CSSProperties } = {
  container: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '100vh',
    width: '100%',
    position: 'relative',
    background: 'radial-gradient(ellipse at 50% 50%, #0f172a 0%, #020617 100%)',
    overflow: 'hidden',
    padding: '24px',
    color: '#f8fafc',
    fontFamily: 'Inter, system-ui, sans-serif',
  },
  backdropGlow1: {
    position: 'absolute',
    top: '20%',
    left: '15%',
    width: '350px',
    height: '350px',
    background: 'radial-gradient(circle, rgba(99, 102, 241, 0.15) 0%, rgba(0,0,0,0) 70%)',
    filter: 'blur(50px)',
    pointerEvents: 'none',
  },
  backdropGlow2: {
    position: 'absolute',
    bottom: '20%',
    right: '15%',
    width: '400px',
    height: '400px',
    background: 'radial-gradient(circle, rgba(236, 72, 153, 0.12) 0%, rgba(0,0,0,0) 70%)',
    filter: 'blur(60px)',
    pointerEvents: 'none',
  },
  card: {
    position: 'relative',
    zIndex: 10,
    width: '100%',
    maxWidth: '520px',
    background: 'rgba(15, 23, 42, 0.45)',
    backdropFilter: 'blur(16px)',
    WebkitBackdropFilter: 'blur(16px)',
    border: '1px solid rgba(255, 255, 255, 0.08)',
    borderRadius: '16px',
    padding: '40px',
    textAlign: 'center',
    boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.5)',
  },
  iconContainer: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '80px',
    height: '80px',
    borderRadius: '50%',
    background: 'rgba(236, 72, 153, 0.1)',
    border: '1px solid rgba(236, 72, 153, 0.25)',
    marginBottom: '24px',
    boxShadow: '0 0 20px rgba(236, 72, 153, 0.15)',
  },
  iconAnimation: {
    animation: 'pulse 2s infinite',
  },
  title: {
    fontSize: '24px',
    fontWeight: 700,
    letterSpacing: '-0.025em',
    marginBottom: '12px',
    background: 'linear-gradient(135deg, #f8fafc 0%, #cbd5e1 100%)',
    WebkitBackgroundClip: 'text',
    WebkitTextFillColor: 'transparent',
  },
  subtitle: {
    fontSize: '15px',
    color: '#94a3b8',
    lineHeight: '1.6',
    marginBottom: '24px',
  },
  errorDetails: {
    background: 'rgba(15, 23, 42, 0.6)',
    border: '1px solid rgba(255, 255, 255, 0.05)',
    borderRadius: '8px',
    padding: '16px',
    marginBottom: '32px',
    textAlign: 'left',
    fontSize: '13px',
  },
  errorText: {
    color: '#ef4444',
    margin: 0,
    wordBreak: 'break-word',
    fontFamily: 'monospace',
  },
  digestText: {
    color: '#64748b',
    margin: '8px 0 0 0',
    fontSize: '12px',
  },
  buttonGroup: {
    display: 'flex',
    gap: '16px',
    justifyContent: 'center',
    alignItems: 'center',
  },
  primaryButton: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    padding: '12px 24px',
    fontSize: '14px',
  },
  secondaryButton: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    padding: '12px 24px',
    fontSize: '14px',
  },
};
