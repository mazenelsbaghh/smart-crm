'use client';

import React, { useState, useEffect } from 'react';
import { useAuth } from '../../context/auth-context';
import { useRouter } from 'next/navigation';
import styles from './auth.module.css';

export default function Register() {
  const { user, register, loading } = useAuth();
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);
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

    if (password !== confirmPassword) {
      setError('كلمتا المرور غير متطابقتين');
      return;
    }

    setSubmitting(true);
    try {
      await register(email, password, fullName);
      setSuccess(true);
      setTimeout(() => {
        router.push('/');
      }, 2000);
    } catch (err: any) {
      console.error(err);
      setError(err.response?.data?.message || err.message || 'تعذر إنشاء الحساب');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className={styles.container}>
      <div className={`glass-panel ${styles.card}`}>
        <div className={styles.header}>
          <h1 className={styles.title}>إنشاء حساب</h1>
          <p className={styles.subtitle}>أضف حساب جديد لفريق خدمة العملاء</p>
        </div>

        {error && <div className={styles.errorAlert}>{error}</div>}
        {success && (
          <div className={styles.successAlert}>
            تم إنشاء الحساب بنجاح. سيتم تحويلك لتسجيل الدخول...
          </div>
        )}

        <form onSubmit={handleSubmit} className={styles.form}>
          <div className={styles.inputGroup}>
            <label className={styles.label}>الاسم الكامل</label>
            <input
              type="text"
              required
              className={`neon-input ${styles.input}`}
              value={fullName}
              onChange={(e) => setFullName(e.target.value)}
              placeholder="اسم الموظف"
              disabled={submitting || success || loading}
            />
          </div>

          <div className={styles.inputGroup}>
            <label className={styles.label}>البريد الإلكتروني</label>
            <input
              type="email"
              required
              className={`neon-input ${styles.input}`}
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="agent@company.com"
              disabled={submitting || success || loading}
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
              disabled={submitting || success || loading}
            />
          </div>

          <div className={styles.inputGroup}>
            <label className={styles.label}>تأكيد كلمة المرور</label>
            <input
              type="password"
              required
              className={`neon-input ${styles.input}`}
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              placeholder="••••••••"
              disabled={submitting || success || loading}
            />
          </div>

          <button
            type="submit"
            className={`neon-btn ${styles.button}`}
            disabled={submitting || success || loading}
          >
            {submitting ? 'جاري الإنشاء...' : 'إنشاء الحساب'}
          </button>
        </form>

        <div className={styles.footer}>
          <p className={styles.footerText}>
            لديك حساب بالفعل؟{' '}
            <span 
              onClick={() => router.push('/')} 
              className={styles.link}
            >
              تسجيل الدخول
            </span>
          </p>
        </div>
      </div>
    </div>
  );
}
