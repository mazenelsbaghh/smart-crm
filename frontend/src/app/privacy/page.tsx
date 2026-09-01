import React from 'react';
import styles from '../../styles/legal.module.css';

export default function PrivacyPolicy() {
  return (
    <main className={styles.page}>
      <article className={styles.article}>
        <h1>سياسة الخصوصية (Privacy Policy)</h1>
        <p className={styles.intro}>
          توضح هذه السياسة كيف تجمع منصة <strong>Smart Customer Core (سمارت كاستمر)</strong> بياناتك وتستخدمها وتحميها عند ربط Facebook وWhatsApp بالخدمة.
        </p>

        <section className={styles.section}>
          <h2>1. البيانات التي نجمعها (Data Collection)</h2>
          <p>عند ربط Facebook وMessenger نطلب الصلاحيات اللازمة لتقديم الوظائف التي تختارها:</p>
          <ul>
            <li><strong>قائمة الصفحات (pages_show_list):</strong> لعرض الصفحات التي تديرها واختيار الصفحة التي تريد ربطها.</li>
            <li><strong>الوصول إلى الرسائل (pages_messaging):</strong> لاستقبال رسائل Messenger وإرسال الردود التي تضبطها داخل المنصة.</li>
            <li><strong>بيانات الصفحة (pages_manage_metadata وpages_read_engagement):</strong> لاستقبال تحديثات التعليقات والمنشورات عبر Webhooks.</li>
          </ul>
        </section>

        <section className={styles.section}>
          <h2>2. كيف نستخدم بياناتك (How We Use Your Data)</h2>
          <p>
            نستخدم بيانات الصفحات والرسائل لتقديم الردود الآلية وإدارة العملاء داخل لوحة التحكم. لا نبيع بياناتك. يُحفظ رمز وصول الصفحة بصورة مشفرة طوال مدة الربط النشط.
          </p>
        </section>

        <section className={styles.section}>
          <h2>3. بيانات TikTok</h2>
          <p>
            يُدار ربط TikTok ورموز الوصول عبر مزود النشر Zernio. يحتفظ نظامنا بمعرف الحساب والملف لدى Zernio، ويرسل الفيديو والوصف وإعدادات النشر التي راجعها المستخدم. يمكنك إدارة الربط أو فصله من لوحة Zernio.
          </p>
        </section>

        <section className={styles.section}>
          <h2>4. طلب حذف البيانات (Data Deletion)</h2>
          <p>يمكنك إزالة الصفحة المرتبطة وطلب حذف بياناتك بإحدى الطرق التالية:</p>
          <ol>
            <li><strong>من إعدادات المنصة:</strong> افتح الإعدادات واختر إلغاء الربط بجوار الصفحة المعنية.</li>
            <li><strong>من Facebook:</strong> افتح الإعدادات والخصوصية، ثم التطبيقات ومواقع الويب، وأزل تطبيق <strong>Smart Customer Core</strong>.</li>
            <li><strong>عبر البريد الإلكتروني:</strong> أرسل طلب الحذف إلى <a href="mailto:mazenelsbagh12@gmail.com">mazenelsbagh12@gmail.com</a>. نعالج الطلب خلال 24 ساعة.</li>
          </ol>
        </section>

        <section className={styles.section}>
          <h2>5. معلومات الاتصال</h2>
          <p>للاستفسار عن الخصوصية أو معالجة البيانات، تواصل معنا عبر <a href="mailto:mazenelsbagh12@gmail.com">mazenelsbagh12@gmail.com</a>.</p>
        </section>

        <footer className={styles.footer}>جميع الحقوق محفوظة © {new Date().getFullYear()} Smart Customer Core</footer>
      </article>
    </main>
  );
}
