import React from 'react';
import styles from '../../styles/legal.module.css';

export default function TermsOfService() {
  return (
    <main className={styles.page}>
      <article className={styles.article}>
        <h1>شروط الاستخدام (Terms of Service)</h1>
        <p className={styles.intro}>تنطبق هذه الشروط على خدمة «أكمل الآية» المتاحة عبر منصة Smart Customer Core (سمارت كاستمر).</p>

        <section className={styles.section}>
          <h2>1. استخدام الخدمة</h2>
          <p>تتيح الخدمة إنشاء معاينات وفيديوهات لمسابقات قرآنية، واختيار النص والوصف وإعدادات النشر قبل إرسال الفيديو إلى الحساب الذي يربطه المستخدم.</p>
        </section>

        <section className={styles.section}>
          <h2>2. ربط TikTok والنشر</h2>
          <p>يتم ربط TikTok والنشر عبر Zernio. يراجع المستخدم الوصف والخصوصية والتفاعلات والجدول قبل تفعيل النشر التلقائي، ويمكنه إيقافه في أي وقت. يظل المستخدم مسؤولًا عن المحتوى والالتزام بسياسات TikTok وZernio.</p>
        </section>

        <section className={styles.section}>
          <h2>3. المحتوى والمسؤولية</h2>
          <p>لا يجوز استخدام الخدمة لنشر محتوى مخالف للقانون أو لحقوق الآخرين أو لسياسات المنصات المرتبطة. يمكن للمستخدم إدارة الحساب المرتبط أو فصله من لوحة Zernio.</p>
        </section>

        <section className={styles.section}>
          <h2>4. التواصل</h2>
          <p>للاستفسارات المتعلقة بالخدمة أو بهذه الشروط، تواصل عبر <a href="mailto:mazenelsbagh12@gmail.com">mazenelsbagh12@gmail.com</a>.</p>
        </section>

        <footer className={styles.footer}>آخر تحديث: 23 يوليو 2026</footer>
      </article>
    </main>
  );
}
