'use client';

import Link from 'next/link';
import styles from './auth.module.css';

export default function Register() {
  return (
    <div className={styles.container}>
      <div className={styles.card}>
        <div className={styles.header}>
          <h1 className={styles.title}>الوصول بدعوة فقط</h1>
          <p className={styles.subtitle}>حسابات سمارت كاستمر ينشئها مدير مساحة العمل لضمان ربط كل عضو بالمشروع والصلاحية الصحيحة.</p>
        </div>

        <div className={styles.successAlert} role="status">
          تواصل مع مالك المشروع أو مديره لإضافة حسابك، ثم استخدم بيانات الدخول التي استلمتها.
        </div>

        <div className={styles.footer}>
          <p className={styles.footerText}>
            <Link href="/" className={styles.link}>
              العودة إلى تسجيل الدخول
            </Link>
          </p>
        </div>
      </div>
    </div>
  );
}
