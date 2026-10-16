'use client';

import { UserRoundPlus, LogIn } from 'lucide-react';
import Link from 'next/link';
import styles from './auth.module.css';

export default function Register() {
  return (
    <div className={styles.container}>
      <div className={styles.card}>
        <div className={styles.header}>
          <span className={styles.brandMark}><UserRoundPlus size={27} aria-hidden="true" /></span>
          <h1 className={styles.title}>الوصول بدعوة فقط</h1>
          <p className={styles.subtitle}>حسابات سمارت كاستمر ينشئها مدير مساحة العمل لضمان ربط كل عضو بالمشروع والصلاحية الصحيحة.</p>
        </div>

        <div className={styles.successAlert} role="status">
          تواصل مع مالك المشروع أو مديره لإضافة حسابك، ثم استخدم بيانات الدخول التي استلمتها.
        </div>

        <div className={styles.footer}>
          <p className={styles.footerText}>
            <Link href="/" className={styles.link}>
              <LogIn size={18} aria-hidden="true" /> العودة إلى تسجيل الدخول
            </Link>
          </p>
        </div>
      </div>
    </div>
  );
}
