import React from 'react';

export default function PrivacyPolicy() {
  return (
    <div style={{
      backgroundColor: '#0F1115',
      color: '#E8E8E8',
      fontFamily: "'Cairo', 'Inter', -apple-system, sans-serif",
      minHeight: '100vh',
      padding: '40px 20px',
      direction: 'rtl',
      lineHeight: '1.8'
    }}>
      <div style={{
        maxWidth: '800px',
        margin: '0 auto',
        backgroundColor: '#171A21',
        borderRadius: '24px',
        border: '1px solid rgba(255, 255, 255, 0.06)',
        padding: '40px',
        boxShadow: '0 10px 30px rgba(0,0,0,0.3)'
      }}>
        <h1 style={{
          fontSize: '2rem',
          fontWeight: 800,
          color: '#D8F15D',
          marginBottom: '20px',
          borderBottom: '1px solid rgba(255, 255, 255, 0.06)',
          paddingBottom: '15px'
        }}>
          سياسة الخصوصية / Privacy Policy
        </h1>
        
        <p style={{ fontSize: '1.05rem', color: '#B0B0B0', marginBottom: '30px' }}>
          توضح هذه الصفحة سياسة الخصوصية لمنصة <strong>Smart Sales (سمارت سيلز)</strong> وكيفية جمع واستخدام وحماية البيانات الخاصة بك عند ربط حساب فيسبوك الخاص بك وواتساب بخدماتنا.
        </p>

        <section style={{ marginBottom: '30px' }}>
          <h2 style={{ fontSize: '1.35rem', fontWeight: 700, color: '#FFFFFF', marginBottom: '12px' }}>
            1. البيانات التي نجمعها (Data Collection)
          </h2>
          <p style={{ color: '#B0B0B0', fontSize: '0.95rem' }}>
            عند استخدام ميزة ربط فيسبوك وماسنجر، نطلب الصلاحيات التالية عبر نظام Meta OAuth:
          </p>
          <ul style={{ paddingRight: '20px', color: '#B0B0B0', fontSize: '0.95rem', listStyleType: 'disc', marginTop: '10px' }}>
            <li><strong>قائمة الصفحات (pages_show_list):</strong> لعرض الصفحات التي تديرها واختيار الصفحة المراد ربطها بالمنصة.</li>
            <li><strong>الوصول للرسائل (pages_messaging):</strong> لاستقبل وإرسال رسائل ماسنجر تلقائياً عبر روبوت الذكاء الاصطناعي الخاص بالمنصة.</li>
            <li><strong>إدارة محتوى الصفحة (pages_manage_metadata & pages_read_engagement):</strong> لتلقي إشعارات التعليقات والمنشورات عبر Webhooks للرد عليها تلقائياً.</li>
          </ul>
        </section>

        <section style={{ marginBottom: '30px' }}>
          <h2 style={{ fontSize: '1.35rem', fontWeight: 700, color: '#FFFFFF', marginBottom: '12px' }}>
            2. كيف نستخدم بياناتك (How We Use Your Data)
          </h2>
          <p style={{ color: '#B0B0B0', fontSize: '0.95rem' }}>
            نحن نستخدم بيانات الصفحات والرسائل فقط لتقديم خدمات الرد الآلي بالذكاء الاصطناعي، وإدارة العملاء (CRM) داخل لوحة التحكم الخاصة بك. لا يتم مشاركة أو بيع أي بيانات لأطراف خارجية مطلقاً. يتم الاحتفاظ برمز الوصول الخاص بصفحتك (Page Access Token) بشكل مشفر وآمن داخل قاعدة بياناتنا طالما كان الربط نشطاً.
          </p>
        </section>

        <section style={{ marginBottom: '30px' }}>
          <h2 style={{ fontSize: '1.35rem', fontWeight: 700, color: '#FFFFFF', marginBottom: '12px' }}>
            3. طلب حذف البيانات (Data Deletion Instructions)
          </h2>
          <p style={{ color: '#B0B0B0', fontSize: '0.95rem', marginBottom: '10px' }}>
            نحن نحترم حقك الكامل في التحكم ببياناتك وحذفها في أي وقت. يمكنك إزالة أو إلغاء ربط صفحتك وحذف كافة البيانات المرتبطة بها بالطرق التالية:
          </p>
          <ul style={{ paddingRight: '20px', color: '#B0B0B0', fontSize: '0.95rem', listStyleType: 'decimal' }}>
            <li><strong>من إعدادات المنصة:</strong> اذهب إلى صفحة الإعدادات (Settings) في لوحة التحكم، واضغط على زر "إلغاء الربط" بجانب الصفحة المرتبطة. سيقوم النظام بحذف رمز الوصول فوراً وإلغاء الاشتراك في كافة تحديثات فيسبوك.</li>
            <li><strong>من إعدادات فيسبوك الشخصية:</strong> يمكنك الانتقال إلى حسابك على فيسبوك &rarr; الإعدادات والخصوصية &rarr; التطبيقات ومواقع الويب &rarr; ثم إزالة تطبيق <strong>Smart Sales</strong>.</li>
            <li><strong>طلب الحذف عبر البريد الإلكتروني:</strong> يمكنك إرسال طلب حذف كامل لبيانات حسابك والصفحات المرتبطة إلى بريد الدعم الفني: <a href="mailto:mazenelsbagh12@gmail.com" style={{ color: '#D8F15D', textDecoration: 'none' }}>mazenelsbagh12@gmail.com</a> وسنقوم بمعالجة الطلب وحذف كافة البيانات نهائياً خلال 24 ساعة.</li>
          </ul>
        </section>

        <section style={{ marginBottom: '30px', borderTop: '1px solid rgba(255, 255, 255, 0.06)', paddingTop: '20px' }}>
          <h2 style={{ fontSize: '1.35rem', fontWeight: 700, color: '#FFFFFF', marginBottom: '12px' }}>
            4. معلومات الاتصال (Contact Information)
          </h2>
          <p style={{ color: '#B0B0B0', fontSize: '0.95rem' }}>
            إذا كان لديك أي استفسار بخصوص سياسة الخصوصية أو معالجة البيانات، يرجى التواصل معنا عبر البريد الإلكتروني: <a href="mailto:mazenelsbagh12@gmail.com" style={{ color: '#D8F15D', textDecoration: 'none' }}>mazenelsbagh12@gmail.com</a>
          </p>
        </section>

        <div style={{
          textAlign: 'center',
          fontSize: '0.8rem',
          color: '#7D7D7D',
          marginTop: '40px',
          borderTop: '1px solid rgba(255, 255, 255, 0.06)',
          paddingTop: '15px'
        }}>
          جميع الحقوق محفوظة © {new Date().getFullYear()} Smart Sales
        </div>
      </div>
    </div>
  );
}
