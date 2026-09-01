'use client';

import React, { useState, useEffect } from 'react';
import { useAuth } from '../../context/auth-context';
import { useRouter } from 'next/navigation';
import styles from './auth.module.css';
import { authErrorMessage } from './auth-error';

export default function Login() {
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
    } catch (err: unknown) {
      console.error(err);
      setError(authErrorMessage(err, 'البريد الإلكتروني أو كلمة المرور غير صحيحة'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className={styles.container}>
      <div className={styles.card}>
        <div className={styles.header}>
          <h1 className={styles.title}>سمارت كاستمر</h1>
          <p className={styles.subtitle}>تسجيل الدخول إلى منصة إدارة المبيعات والعملاء والردود الذكية</p>
        </div>

        {error && <div id="login-error" className={styles.errorAlert} role="alert" aria-live="assertive">{error}</div>}

        <form onSubmit={handleSubmit} className={styles.form} aria-busy={submitting}>
          <div className={styles.inputGroup}>
            <label htmlFor="login-email" className={styles.label}>البريد الإلكتروني</label>
            <input
              id="login-email"
              name="email"
              type="email"
              autoComplete="email"
              required
              className={`neon-input ${styles.input}`}
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="agent@company.com"
              disabled={submitting || loading}
              aria-invalid={Boolean(error)}
              aria-describedby={error ? 'login-error' : undefined}
            />
          </div>

          <div className={styles.inputGroup}>
            <label htmlFor="login-password" className={styles.label}>كلمة المرور</label>
            <input
              id="login-password"
              name="password"
              type="password"
              autoComplete="current-password"
              required
              className={`neon-input ${styles.input}`}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••"
              disabled={submitting || loading}
              aria-invalid={Boolean(error)}
              aria-describedby={error ? 'login-error' : undefined}
            />
          </div>

          <button
            type="submit"
            className={`neon-btn ${styles.button}`}
            disabled={submitting || loading}
          >
            {submitting ? 'جاري الدخول...' : 'تسجيل الدخول'}
          </button>
        </form>

        <div className={styles.footer}>
          <p className={styles.footerText}>الدخول متاح للحسابات التي يضيفها مدير مساحة العمل.</p>
        </div>
      </div>
    </div>
  );
}
