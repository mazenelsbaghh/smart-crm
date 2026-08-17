import React from 'react';

const pageStyle: React.CSSProperties = {
  backgroundColor: '#0F1115',
  color: '#E8E8E8',
  fontFamily: "'Cairo', 'Inter', -apple-system, sans-serif",
  minHeight: '100vh',
  padding: '40px 20px',
  direction: 'rtl',
  lineHeight: '1.8',
};

const cardStyle: React.CSSProperties = {
  maxWidth: '800px',
  margin: '0 auto',
  backgroundColor: '#171A21',
  borderRadius: '24px',
  border: '1px solid rgba(255, 255, 255, 0.06)',
  padding: '40px',
  boxShadow: '0 10px 30px rgba(0,0,0,0.3)',
};

const sectionStyle: React.CSSProperties = { marginBottom: '30px' };
const headingStyle: React.CSSProperties = {
  fontSize: '1.35rem', fontWeight: 700, color: '#FFFFFF', marginBottom: '12px',
};
const bodyStyle: React.CSSProperties = { color: '#B0B0B0', fontSize: '0.95rem' };

export default function TermsOfService() {
  return (
    <main style={pageStyle}>
      <article style={cardStyle}>
        <h1 style={{ fontSize: '2rem', fontWeight: 800, color: '#D8F15D', marginBottom: '20px' }}>
          شروط الاستخدام / Terms of Service
        </h1>
        <p style={{ ...bodyStyle, marginBottom: '30px' }}>
          تنطبق هذه الشروط على خدمة «أكمل الآية» المتاحة عبر منصة Smart Sales.
        </p>

        <section style={sectionStyle}>
          <h2 style={headingStyle}>1. استخدام الخدمة</h2>
          <p style={bodyStyle}>
            تتيح الخدمة إنشاء معاينات وفيديوهات لمسابقات قرآنية، واختيار النص والوصف وإعدادات النشر قبل إرسال الفيديو إلى الحساب الذي يربطه المستخدم بنفسه.
          </p>
        </section>

        <section style={sectionStyle}>
          <h2 style={headingStyle}>2. ربط TikTok والنشر</h2>
          <p style={bodyStyle}>
            عند ربط TikTok، يمنح المستخدم الخدمة الصلاحيات التي يوافق عليها في شاشة TikTok. يظل المستخدم مسؤولاً عن مراجعة الفيديو والوصف والخصوصية والتفاعلات قبل كل عملية نشر، وعن الالتزام بشروط وسياسات TikTok.
          </p>
        </section>

        <section style={sectionStyle}>
          <h2 style={headingStyle}>3. المحتوى والمسؤولية</h2>
          <p style={bodyStyle}>
            لا يجوز استخدام الخدمة لنشر محتوى مخالف للقانون أو لحقوق الآخرين أو لسياسات المنصة المرتبطة. يمكن للمستخدم فصل الحساب المرتبط في أي وقت من داخل الاستوديو.
          </p>
        </section>

        <section style={sectionStyle}>
          <h2 style={headingStyle}>4. التواصل</h2>
          <p style={bodyStyle}>
            للاستفسارات المتعلقة بالخدمة أو بهذه الشروط، تواصل عبر <a href="mailto:mazenelsbagh12@gmail.com" style={{ color: '#D8F15D' }}>mazenelsbagh12@gmail.com</a>.
          </p>
        </section>

        <p style={{ ...bodyStyle, borderTop: '1px solid rgba(255, 255, 255, 0.06)', paddingTop: '15px', fontSize: '0.8rem' }}>
          آخر تحديث: 23 يوليو 2026
        </p>
      </article>
    </main>
  );
}
