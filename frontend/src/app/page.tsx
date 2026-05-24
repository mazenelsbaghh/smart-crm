'use client';

import React, { useState, useEffect } from 'react';
import { useAuth } from '../context/auth-context';
import { useRouter } from 'next/navigation';

export default function LoginPage() {
  const { user, login, loading } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const router = useRouter();

  // Redirect if already logged in
  useEffect(() => {
    if (user) {
      router.push('/dashboard');
    }
  }, [user, router]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await login(email, password);
      router.push('/dashboard');
    } catch (err: any) {
      console.error(err);
      setError(err.response?.data?.message || err.message || 'Invalid email or password');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div style={styles.container}>
      <div style={styles.backdropGlow1}></div>
      <div style={styles.backdropGlow2}></div>
      
      <div className="glass-panel" style={styles.card}>
        <div style={styles.header}>
          <h1 style={styles.title}>Smart WhatsApp</h1>
          <p style={styles.subtitle}>Smart Customer Core Login</p>
        </div>

        {error && <div style={styles.errorAlert}>{error}</div>}

        <form onSubmit={handleSubmit} style={styles.form}>
          <div style={styles.inputGroup}>
            <label style={styles.label}>Email Address</label>
            <input
              type="email"
              required
              className="neon-input"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="agent@company.com"
              style={styles.input}
              disabled={submitting || loading}
            />
          </div>

          <div style={styles.inputGroup}>
            <label style={styles.label}>Password</label>
            <input
              type="password"
              required
              className="neon-input"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••"
              style={styles.input}
              disabled={submitting || loading}
            />
          </div>

          <button
            type="submit"
            className="neon-btn"
            style={styles.button}
            disabled={submitting || loading}
          >
            {submitting ? 'Connecting...' : 'Secure Log In'}
          </button>
        </form>

        <div style={styles.footer}>
          <p style={styles.footerText}>
            Need an account?{' '}
            <span 
              onClick={() => router.push('/register')} 
              style={styles.link}
            >
              Register here
            </span>
          </p>
        </div>
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  container: {
    position: 'relative',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '100vh',
    width: '100vw',
    backgroundColor: 'hsl(var(--bg-primary))',
    overflow: 'hidden',
    padding: 'var(--space-md)',
  },
  backdropGlow1: {
    position: 'absolute',
    top: '20%',
    left: '25%',
    width: '300px',
    height: '300px',
    borderRadius: 'var(--radius-full)',
    background: 'radial-gradient(circle, rgba(99, 102, 241, 0.15) 0%, rgba(99, 102, 241, 0) 70%)',
    filter: 'blur(40px)',
    pointerEvents: 'none',
  },
  backdropGlow2: {
    position: 'absolute',
    bottom: '20%',
    right: '25%',
    width: '350px',
    height: '350px',
    borderRadius: 'var(--radius-full)',
    background: 'radial-gradient(circle, rgba(236, 72, 153, 0.1) 0%, rgba(236, 72, 153, 0) 70%)',
    filter: 'blur(50px)',
    pointerEvents: 'none',
  },
  card: {
    width: '100%',
    maxWidth: '450px',
    padding: 'var(--space-xl)',
    borderRadius: 'var(--radius-lg)',
    zIndex: 10,
    border: '1px solid rgba(255, 255, 255, 0.08)',
  },
  header: {
    textAlign: 'center',
    marginBottom: 'var(--space-lg)',
  },
  title: {
    fontSize: '2rem',
    fontWeight: 800,
    background: 'linear-gradient(135deg, hsl(var(--text-primary)) 30%, hsl(var(--accent-primary)) 100%)',
    WebkitBackgroundClip: 'text',
    WebkitTextFillColor: 'transparent',
    letterSpacing: '-0.5px',
    marginBottom: 'var(--space-xs)',
  },
  subtitle: {
    color: 'hsl(var(--text-secondary))',
    fontSize: '0.875rem',
  },
  errorAlert: {
    backgroundColor: 'rgba(239, 68, 68, 0.1)',
    border: '1px solid rgba(239, 68, 68, 0.2)',
    color: 'hsl(var(--accent-danger))',
    padding: 'var(--space-sm) var(--space-md)',
    borderRadius: 'var(--radius-md)',
    fontSize: '0.875rem',
    marginBottom: 'var(--space-md)',
    textAlign: 'center',
  },
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-md)',
  },
  inputGroup: {
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-sm)',
  },
  label: {
    fontSize: '0.75rem',
    textTransform: 'uppercase',
    letterSpacing: '1px',
    color: 'hsl(var(--text-secondary))',
    fontWeight: 600,
  },
  input: {
    width: '100%',
    fontSize: '0.95rem',
  },
  button: {
    marginTop: 'var(--space-sm)',
    padding: 'var(--space-md)',
    fontSize: '1rem',
  },
  footer: {
    marginTop: 'var(--space-lg)',
    textAlign: 'center',
  },
  footerText: {
    color: 'hsl(var(--text-muted))',
    fontSize: '0.875rem',
  },
  link: {
    color: 'hsl(var(--accent-primary))',
    fontWeight: 600,
    cursor: 'pointer',
    textDecoration: 'underline',
  },
};
