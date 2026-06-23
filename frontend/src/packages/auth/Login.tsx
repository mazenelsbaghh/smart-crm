'use client';

import React, { useState, useEffect } from 'react';
import { useAuth } from '../../context/auth-context';
import { useRouter } from 'next/navigation';
import styles from './auth.module.css';

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
    } catch (err: any) {
      console.error(err);
      setError(err.response?.data?.message || err.message || 'البريد الإلكتروني أو كلمة المرور غير صحيحة');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className={styles.container}>
      <div className={`glass-panel ${styles.card}`}>
        <div className={styles.header}>
          <h1 className={styles.title}>سمارت سيلز</h1>
          <p className={styles.subtitle}>تسجيل الدخول إلى منصة إدارة المبيعات والعملاء والردود الذكية</p>
        </div>

        {error && <div className={styles.errorAlert}>{error}</div>}

        <form onSubmit={handleSubmit} className={styles.form}>
          <div className={styles.inputGroup}>
            <label className={styles.label}>البريد الإلكتروني</label>
            <input
              type="email"
              required
              className={`neon-input ${styles.input}`}
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="agent@company.com"
              disabled={submitting || loading}
            />
          </div>

          <div className={styles.inputGroup}>
            <label className={styles.label}>كلمة المرور</label>
            <input
              type="password"
              required
              className={`neon-input ${styles.input}`}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••"
              disabled={submitting || loading}
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
          <p className={styles.footerText}>
            ليس لديك حساب؟{' '}
            <span 
              onClick={() => router.push('/register')} 
              className={styles.link}
            >
              إنشاء حساب
            </span>
          </p>
        </div>
      </div>
    </div>
  );
}
