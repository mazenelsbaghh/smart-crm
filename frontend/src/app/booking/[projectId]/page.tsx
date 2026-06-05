'use client';

import React, { useEffect, useState, use } from 'react';
import axios from 'axios';
import { Calendar, Users, CheckCircle, AlertCircle, Clock, Smartphone, User } from 'lucide-react';

const API_URL = process.env.NEXT_PUBLIC_API_URL !== undefined ? process.env.NEXT_PUBLIC_API_URL : 'http://localhost';

interface GroupAppointment {
  id: string;
  name: string;
  dateTime: string;
  capacity: number;
  bookedCount: number;
  slotsLeft: number;
  mode?: string;
}

interface PageProps {
  params: Promise<{
    projectId: string;
  }>;
}

export default function PublicBookingPage({ params }: PageProps) {
  const { projectId } = use(params);
  
  const [groups, setGroups] = useState<GroupAppointment[]>([]);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [selectedGroupId, setSelectedGroupId] = useState<string>('');
  
  // Form values
  const [customerName, setCustomerName] = useState('');
  const [customerPhone, setCustomerPhone] = useState('');
  
  // Alerts and responses
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<boolean>(false);

  const fetchActiveGroups = async () => {
    if (!projectId) return;
    try {
      setLoading(true);
      setError(null);
      const response = await axios.get<GroupAppointment[]>(
        `${API_URL}/api/public/group-appointments/active/${projectId}`
      );
      setGroups(response.data);
      if (response.data.length > 0) {
        // Auto-select first available group if active
        const available = response.data.find(g => g.slotsLeft > 0);
        if (available) {
          setSelectedGroupId(available.id);
        }
      }
    } catch (e: any) {
      console.error(e);
      setError('تعذر تحميل مواعيد المجموعات المتاحة حالياً. يرجى إعادة المحاولة لاحقاً.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void fetchActiveGroups();
  }, [projectId]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedGroupId || !customerName.trim() || !customerPhone.trim()) {
      setError('يرجى ملء جميع الحقول واختيار المجموعة.');
      return;
    }

    try {
      setSubmitting(true);
      setError(null);

      // Clean phone number: remove any spaces, +, or non-numeric characters
      const cleanPhone = customerPhone.replace(/\D/g, '');
      if (cleanPhone.length < 7) {
        setError('يرجى إدخال رقم هاتف صحيح شامل كود الدولة.');
        setSubmitting(false);
        return;
      }

      await axios.post(`${API_URL}/api/public/group-appointments/book`, {
        projectId,
        groupAppointmentId: selectedGroupId,
        customerName: customerName.trim(),
        customerPhone: cleanPhone
      });

      setSuccess(true);
      setCustomerName('');
      setCustomerPhone('');
    } catch (e: any) {
      console.error(e);
      const errMsg = e.response?.data?.error || 'فشل تسجيل الحجز، يرجى المحاولة مرة أخرى.';
      setError(errMsg);
    } finally {
      setSubmitting(false);
    }
  };

  const formatDate = (isoString: string) => {
    const options: Intl.DateTimeFormatOptions = { 
      weekday: 'long', 
      year: 'numeric', 
      month: 'long', 
      day: 'numeric', 
      hour: '2-digit', 
      minute: '2-digit' 
    };
    return new Date(isoString).toLocaleDateString('ar-EG', options);
  };

  if (loading) {
    return (
      <div style={{
        minHeight: '100vh',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        background: '#0a0e17',
        color: '#fff',
        fontFamily: 'Cairo, sans-serif'
      }}>
        <div style={{
          width: '40px',
          height: '40px',
          border: '3px solid #1f2937',
          borderTopColor: '#00f3ff',
          borderRadius: '50%',
          animation: 'spin 1s linear infinite'
        }}></div>
        <p style={{ marginTop: '1rem', fontSize: '0.95rem' }}>جاري تحميل المواعيد المتاحة...</p>
        <style dangerouslySetInnerHTML={{__html: `
          @keyframes spin { to { transform: rotate(360deg); } }
        `}} />
      </div>
    );
  }

  return (
    <div style={{
      minHeight: '100vh',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      background: 'radial-gradient(circle at center, #0f172a 0%, #020617 100%)',
      fontFamily: 'Cairo, sans-serif',
      color: '#f8fafc',
      padding: '2rem 1rem',
      direction: 'rtl'
    }}>
      <div style={{
        width: '100%',
        maxWidth: '520px',
        backgroundColor: 'rgba(15, 23, 42, 0.45)',
        border: '1px solid rgba(255, 255, 255, 0.08)',
        backdropFilter: 'blur(16px)',
        borderRadius: '16px',
        padding: '2rem',
        boxShadow: '0 10px 25px -5px rgba(0, 0, 0, 0.3), 0 8px 10px -6px rgba(0, 0, 0, 0.3)'
      }}>
        {success ? (
          <div style={{ textAlign: 'center', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '1.25rem' }}>
            <div style={{
              width: '64px',
              height: '64px',
              borderRadius: '50%',
              backgroundColor: 'rgba(16, 185, 129, 0.1)',
              color: '#10b981',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              boxShadow: '0 0 15px rgba(16, 185, 129, 0.2)'
            }}>
              <CheckCircle size={36} />
            </div>
            <div>
              <h2 style={{ fontSize: '1.5rem', fontWeight: 700, marginBottom: '0.5rem' }}>تم تأكيد حجزك بنجاح!</h2>
              <p style={{ fontSize: '0.9rem', color: '#94a3b8', lineHeight: '1.6' }}>
                تم تسجيل بياناتك وحجز مقعدك في المجموعة المحددة بنجاح. سنقوم بالتواصل معك عبر واتساب لتأكيد التفاصيل.
              </p>
            </div>
            <button
              onClick={() => { setSuccess(false); void fetchActiveGroups(); }}
              style={{
                background: '#00f3ff',
                color: '#020617',
                border: 'none',
                padding: '0.75rem 2rem',
                borderRadius: '8px',
                fontWeight: 700,
                cursor: 'pointer',
                fontSize: '0.9rem',
                transition: 'all 0.2s',
                marginTop: '1rem'
              }}
              onMouseOver={(e) => { e.currentTarget.style.boxShadow = '0 0 15px #00f3ff'; }}
              onMouseOut={(e) => { e.currentTarget.style.boxShadow = 'none'; }}
            >
              حجز موعد آخر
            </button>
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
            <div style={{ textAlign: 'center', borderBottom: '1px solid rgba(255, 255, 255, 0.08)', paddingBottom: '1rem' }}>
              <div style={{ display: 'inline-flex', padding: '0.5rem', backgroundColor: 'rgba(0, 243, 255, 0.08)', borderRadius: '12px', color: '#00f3ff', marginBottom: '0.75rem' }}>
                <Calendar size={24} />
              </div>
              <h1 style={{ fontSize: '1.35rem', fontWeight: 800, color: '#fff' }}>حجز موعد المجموعات</h1>
              <p style={{ fontSize: '0.825rem', color: '#94a3b8', marginTop: '0.25rem' }}>
                يرجى إدخال اسمك ورقم واتساب واختيار المجموعة المناسبة لتسجيل الحجز.
              </p>
            </div>

            {error && (
              <div style={{
                display: 'flex',
                alignItems: 'center',
                gap: '0.75rem',
                padding: '0.75rem 1rem',
                backgroundColor: 'rgba(239, 68, 68, 0.1)',
                border: '1px solid rgba(239, 68, 68, 0.2)',
                borderRadius: '8px',
                color: '#ef4444',
                fontSize: '0.85rem'
              }}>
                <AlertCircle size={16} style={{ flexShrink: 0 }} />
                <span>{error}</span>
              </div>
            )}

            {groups.length === 0 ? (
              <div style={{ textAlign: 'center', padding: '2rem 0', color: '#94a3b8' }}>
                <Calendar size={40} style={{ opacity: 0.3, marginBottom: '0.5rem' }} />
                <p style={{ fontSize: '0.9rem' }}>عذراً، لا تتوفر أي مجموعات نشطة للحجز حالياً.</p>
              </div>
            ) : (
              <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '1.25rem' }}>
                {/* Name */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                  <label style={{ fontSize: '0.8rem', fontWeight: 600, color: '#94a3b8' }}>الاسم بالكامل</label>
                  <div style={{ position: 'relative' }}>
                    <span style={{ position: 'absolute', right: '12px', top: '50%', transform: 'translateY(-50%)', color: '#64748b' }}>
                      <User size={16} />
                    </span>
                    <input
                      type="text"
                      placeholder="مثال: محمد أحمد"
                      value={customerName}
                      onChange={(e) => setCustomerName(e.target.value)}
                      style={{
                        width: '100%',
                        padding: '0.75rem 2.5rem 0.75rem 1rem',
                        backgroundColor: '#0f172a',
                        border: '1px solid #334155',
                        borderRadius: '8px',
                        color: '#fff',
                        fontSize: '0.9rem',
                        outline: 'none'
                      }}
                      required
                    />
                  </div>
                </div>

                {/* Phone */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                  <label style={{ fontSize: '0.8rem', fontWeight: 600, color: '#94a3b8' }}>رقم واتساب (مع كود الدولة)</label>
                  <div style={{ position: 'relative' }}>
                    <span style={{ position: 'absolute', right: '12px', top: '50%', transform: 'translateY(-50%)', color: '#64748b' }}>
                      <Smartphone size={16} />
                    </span>
                    <input
                      type="tel"
                      placeholder="مثال: 201012345678"
                      value={customerPhone}
                      onChange={(e) => setCustomerPhone(e.target.value)}
                      style={{
                        width: '100%',
                        padding: '0.75rem 2.5rem 0.75rem 1rem',
                        backgroundColor: '#0f172a',
                        border: '1px solid #334155',
                        borderRadius: '8px',
                        color: '#fff',
                        fontSize: '0.9rem',
                        outline: 'none',
                        textAlign: 'left',
                        direction: 'ltr'
                      }}
                      required
                    />
                  </div>
                  <span style={{ fontSize: '0.7rem', color: '#64748b' }}>أدخل الأرقام فقط بدون رمز + أو مسافات</span>
                </div>

                {/* Group Selector */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                  <label style={{ fontSize: '0.8rem', fontWeight: 600, color: '#94a3b8' }}>المجموعات المتاحة</label>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                    {groups.map((group) => {
                      const isFull = group.slotsLeft <= 0;
                      const isSelected = selectedGroupId === group.id;

                      return (
                        <div
                          key={group.id}
                          onClick={() => { if (!isFull) setSelectedGroupId(group.id); }}
                          style={{
                            border: isSelected ? '2px solid #00f3ff' : '1px solid #334155',
                            backgroundColor: isSelected ? 'rgba(0, 243, 255, 0.04)' : '#0f172a',
                            borderRadius: '8px',
                            padding: '1rem',
                            cursor: isFull ? 'not-allowed' : 'pointer',
                            opacity: isFull ? 0.5 : 1,
                            transition: 'all 0.2s',
                            display: 'flex',
                            justifyContent: 'space-between',
                            alignItems: 'center'
                          }}
                        >
                          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.25rem' }}>
                            <span style={{ fontWeight: 700, fontSize: '0.925rem', color: isSelected ? '#00f3ff' : '#fff' }}>
                              {group.mode === 'online' ? 'أونلاين (Online)' : 'في السنتر (Offline)'}
                            </span>
                            <span style={{ fontSize: '0.75rem', color: '#94a3b8', display: 'flex', alignItems: 'center', gap: '4px' }}>
                              <Clock size={12} />
                              {formatDate(group.dateTime)}
                            </span>
                          </div>
                          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: '0.25rem' }}>
                            <span style={{
                              fontSize: '0.75rem',
                              fontWeight: 600,
                              color: isFull ? '#ef4444' : '#10b981',
                              backgroundColor: isFull ? 'rgba(239, 68, 68, 0.1)' : 'rgba(16, 185, 129, 0.1)',
                              padding: '2px 8px',
                              borderRadius: '4px'
                            }}>
                              {isFull ? 'مكتملة' : `متاح ${group.slotsLeft} مقاعد`}
                            </span>
                            <span style={{ fontSize: '0.7rem', color: '#64748b' }}>السعة الكلية: {group.capacity}</span>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>

                {/* Submit */}
                <button
                  type="submit"
                  disabled={submitting}
                  style={{
                    background: '#00f3ff',
                    color: '#020617',
                    border: 'none',
                    padding: '0.85rem',
                    borderRadius: '8px',
                    fontWeight: 700,
                    cursor: submitting ? 'not-allowed' : 'pointer',
                    fontSize: '0.95rem',
                    transition: 'all 0.2s',
                    marginTop: '0.5rem',
                    display: 'flex',
                    justifyContent: 'center',
                    alignItems: 'center',
                    gap: '8px'
                  }}
                  onMouseOver={(e) => { if (!submitting) e.currentTarget.style.boxShadow = '0 0 15px #00f3ff'; }}
                  onMouseOut={(e) => { e.currentTarget.style.boxShadow = 'none'; }}
                >
                  {submitting && (
                    <div style={{
                      width: '16px',
                      height: '16px',
                      border: '2px solid #020617',
                      borderTopColor: 'transparent',
                      borderRadius: '50%',
                      animation: 'spin 1s linear infinite'
                    }}></div>
                  )}
                  {submitting ? 'جاري تسجيل الحجز...' : 'تأكيد الحجز الآن'}
                </button>
              </form>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
