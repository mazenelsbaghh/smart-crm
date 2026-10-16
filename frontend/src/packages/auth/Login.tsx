'use client';

import React, { useState, useEffect } from 'react';
import { useAuth } from '../../context/auth-context';
import { useRouter } from 'next/navigation';
import Button from '../../components/ui/Button';
import Link from 'next/link';
import { MessagesSquare, Mail, KeyRound, Eye, EyeOff, LogIn, ShieldCheck } from 'lucide-react';
import styles from './auth.module.css';
import { authErrorMessage } from './auth-error';

export default function Login() {
  const { user, login, loading } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const router = useRouter();

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
          <span className={styles.brandMark}><MessagesSquare size={27} aria-hidden="true" /></span>
          <p className={styles.brandName}>سمارت كاستمر</p>
          <h1 className={styles.title}>أهلًا بعودتك</h1>
          <p className={styles.subtitle}>ادخل لمساحة عملك وتابع عملاءك ومحادثاتك.</p>
        </div>

        {error && <div id="login-error" className={styles.errorAlert} role="alert" aria-live="assertive">{error}</div>}

        <form onSubmit={handleSubmit} className={styles.form} aria-busy={submitting}>
          <div className={styles.inputGroup}>
            <label htmlFor="login-email" className={styles.label}><Mail size={16} aria-hidden="true" /> البريد الإلكتروني</label>
            <input
              id="login-email"
              name="email"
              type="email"
              dir="ltr"
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
            <label htmlFor="login-password" className={styles.label}><KeyRound size={16} aria-hidden="true" /> كلمة المرور</label>
            <div className={styles.passwordField}>
              <input
                id="login-password"
                name="password"
                type={passwordVisible ? 'text' : 'password'}
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
              <button type="button" className={styles.passwordToggle} onClick={() => setPasswordVisible(!passwordVisible)} aria-label={passwordVisible ? 'إخفاء كلمة المرور' : 'إظهار كلمة المرور'} aria-pressed={passwordVisible} disabled={submitting || loading}>{passwordVisible ? <EyeOff size={18} /> : <Eye size={18} />}</button>
            </div>
          </div>

          <Button type="submit" variant="primary" className={styles.button} loading={submitting} disabled={loading}>
            <LogIn size={18} aria-hidden="true" /> {submitting ? 'جاري الدخول...' : 'تسجيل الدخول'}
          </Button>
        </form>

        <div className={styles.footer}>
          <p className={styles.footerText}><ShieldCheck size={17} aria-hidden="true" /> الدخول متاح للحسابات التي يضيفها مدير مساحة العمل.</p>
          <nav className={styles.legalLinks} aria-label="معلومات الاستخدام"><Link href="/privacy">الخصوصية</Link><Link href="/terms">شروط الاستخدام</Link></nav>
        </div>
      </div>
    </div>
  );
}
