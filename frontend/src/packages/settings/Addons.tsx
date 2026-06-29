'use client';

import React, { useState, useEffect } from 'react';
import { Calendar, Settings, Sparkles, Zap } from 'lucide-react';
import styles from './settings.module.css';

interface AddonsProps {
  onManageGroups: () => void;
  isGroupAppointmentsEnabled: boolean;
  onToggleGroupAppointments: (enabled: boolean) => Promise<void>;
  isWhatsAppGroupAutomationEnabled: boolean;
  onToggleWhatsAppGroupAutomation: (enabled: boolean) => Promise<void>;
  groupAutomationManagerPhone: string;
  onUpdateGroupAutomationManagerPhone: (phone: string) => Promise<void>;
}

export default function Addons({ 
  onManageGroups, 
  isGroupAppointmentsEnabled, 
  onToggleGroupAppointments,
  isWhatsAppGroupAutomationEnabled,
  onToggleWhatsAppGroupAutomation,
  groupAutomationManagerPhone,
  onUpdateGroupAutomationManagerPhone
}: AddonsProps) {
  const [loading, setLoading] = useState(false);
  const [managerPhone, setManagerPhone] = useState(groupAutomationManagerPhone);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  useEffect(() => {
    setManagerPhone(groupAutomationManagerPhone);
  }, [groupAutomationManagerPhone]);

  const handleToggle = async (checked: boolean) => {
    try {
      setLoading(true);
      setMessage(null);
      await onToggleGroupAppointments(checked);
      setMessage({
        type: 'success',
        text: checked ? 'تم تفعيل إضافة مواعيد المجموعات بنجاح.' : 'تم إلغاء تفعيل إضافة مواعيد المجموعات.'
      });
    } catch {
      setMessage({ type: 'error', text: 'فشل تعديل حالة الإضافة.' });
    } finally {
      setLoading(false);
    }
  };

  const handleToggleGroupAutomation = async (checked: boolean) => {
    try {
      setLoading(true);
      setMessage(null);
      await onToggleWhatsAppGroupAutomation(checked);
      setMessage({
        type: 'success',
        text: checked ? 'تم تفعيل أتمتة مجموعات الواتساب بنجاح.' : 'تم إلغاء تفعيل أتمتة مجموعات الواتساب.'
      });
    } catch {
      setMessage({ type: 'error', text: 'فشل تعديل حالة الإضافة.' });
    } finally {
      setLoading(false);
    }
  };

  const handleUpdateManagerPhone = async (phone: string) => {
    try {
      setLoading(true);
      setMessage(null);
      await onUpdateGroupAutomationManagerPhone(phone);
      setMessage({
        type: 'success',
        text: 'تم تحديث رقم هاتف المدير بنجاح.'
      });
    } catch {
      setMessage({ type: 'error', text: 'فشل تحديث رقم هاتف المدير.' });
    } finally {
      setLoading(false);
    }
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-md)', width: '100%' }}>
      {message && (
        <div className="glass-panel" style={{ 
          padding: 'var(--space-md)', 
          border: `1px solid ${message.type === 'success' ? 'rgba(16, 185, 129, 0.2)' : 'rgba(239, 68, 68, 0.2)'}`,
          backgroundColor: message.type === 'success' ? 'rgba(16, 185, 129, 0.04)' : 'rgba(239, 68, 68, 0.04)',
          borderRadius: 'var(--radius-md)',
          fontSize: '0.85rem',
          fontWeight: 600
        }}>
          {message.text}
        </div>
      )}

      <div style={{ 
        display: 'grid', 
        gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))', 
        gap: 'var(--space-lg)' 
      }}>
        {/* Group Appointments Card */}
        <div className="glass-panel" style={{ 
          padding: 'var(--space-xl)', 
          borderRadius: 'var(--radius-md)',
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'space-between',
          minHeight: '220px',
          gap: 'var(--space-md)'
        }}>
          <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 'var(--space-md)' }}>
              <div style={{ 
                width: '40px', 
                height: '40px', 
                borderRadius: '8px', 
                backgroundColor: 'var(--accent-soft)', 
                display: 'flex', 
                alignItems: 'center', 
                justifyContent: 'center',
                color: 'var(--accent)'
              }}>
                <Calendar size={22} />
              </div>

              <label className={styles.checkboxGroup} style={{ cursor: loading ? 'not-allowed' : 'pointer' }}>
                <input
                  type="checkbox"
                  checked={isGroupAppointmentsEnabled}
                  disabled={loading}
                  onChange={(e) => handleToggle(e.target.checked)}
                  className={styles.checkbox}
                />
              </label>
            </div>

            <h3 style={{ fontSize: '1.05rem', fontWeight: 700, marginBottom: '8px', color: 'var(--text-strong)' }}>
              مواعيد المجموعات (Group Appointments)
            </h3>
            <p style={{ fontSize: '0.825rem', color: 'hsl(var(--text-secondary))', lineHeight: '1.5' }}>
              تفعيل صفحة حجز عامة لحجز مواعيد دورية للمجموعات مع تحديد السعة القصوى لكل مجموعة لتجنب الحجوزات الزائدة.
            </p>
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 'var(--space-sm)' }}>
            <button
              onClick={onManageGroups}
              disabled={!isGroupAppointmentsEnabled}
              className={`${styles.btn} ${isGroupAppointmentsEnabled ? styles.btnPrimary : styles.btnSecondary}`}
              style={{ padding: '6px 16px', fontSize: '0.8rem', opacity: isGroupAppointmentsEnabled ? 1 : 0.5 }}
            >
              <Settings size={14} />
              إدارة المجموعات والاشتراكات
            </button>
          </div>
        </div>

        {/* WhatsApp Group Automation Card */}
        <div className="glass-panel" style={{ 
          padding: 'var(--space-xl)', 
          borderRadius: 'var(--radius-md)',
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'space-between',
          minHeight: '220px',
          gap: 'var(--space-md)'
        }}>
          <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 'var(--space-md)' }}>
              <div style={{ 
                width: '40px', 
                height: '40px', 
                borderRadius: '8px', 
                backgroundColor: 'rgba(16, 185, 129, 0.1)', 
                display: 'flex', 
                alignItems: 'center', 
                justifyContent: 'center',
                color: 'rgba(16, 185, 129, 1)'
              }}>
                <Zap size={22} />
              </div>

              <label className={styles.checkboxGroup} style={{ cursor: loading ? 'not-allowed' : 'pointer' }}>
                <input
                  type="checkbox"
                  checked={isWhatsAppGroupAutomationEnabled}
                  disabled={loading}
                  onChange={(e) => handleToggleGroupAutomation(e.target.checked)}
                  className={styles.checkbox}
                />
              </label>
            </div>

            <h3 style={{ fontSize: '1.05rem', fontWeight: 700, marginBottom: '8px', color: 'var(--text-strong)' }}>
              أتمتة مجموعات الواتساب (Group Automation)
            </h3>
            <p style={{ fontSize: '0.825rem', color: 'hsl(var(--text-secondary))', lineHeight: '1.5' }}>
              إنشاء مجموعات واتساب مؤمنة آلياً قبل الجلسات وإرسال روابط الدعوة للطلاب ومتابعتهم بعد الحضور.
            </p>

            {isWhatsAppGroupAutomationEnabled && (
              <div style={{ marginTop: 'var(--space-md)' }}>
                <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 600, color: 'var(--text-soft)', marginBottom: '4px' }}>
                  رقم هاتف المدير للتنبيهات والمجموعات
                </label>
                <input
                  type="text"
                  value={managerPhone}
                  onChange={(e) => setManagerPhone(e.target.value)}
                  onBlur={() => handleUpdateManagerPhone(managerPhone)}
                  placeholder="+201068690092"
                  style={{
                    width: '100%',
                    padding: '8px 12px',
                    fontSize: '0.8rem',
                    borderRadius: 'var(--radius-sm)',
                    border: '1px solid var(--border)',
                    backgroundColor: 'var(--bg-card)',
                    color: 'var(--text-strong)',
                    outline: 'none'
                  }}
                />
              </div>
            )}
          </div>
        </div>

        {/* Placeholder Addon 3 */}
        <div className="glass-panel" style={{ 
          padding: 'var(--space-xl)', 
          borderRadius: 'var(--radius-md)',
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'space-between',
          minHeight: '220px',
          gap: 'var(--space-md)',
          opacity: 0.5
        }}>
          <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 'var(--space-md)' }}>
              <div style={{ 
                width: '40px', 
                height: '40px', 
                borderRadius: '8px', 
                backgroundColor: 'rgba(245, 158, 11, 0.1)', 
                display: 'flex', 
                alignItems: 'center', 
                justifyContent: 'center',
                color: 'hsl(var(--accent-warning))'
              }}>
                <Sparkles size={22} />
              </div>
              <span style={{ fontSize: '0.75rem', fontWeight: 600, color: 'var(--text-soft)' }}>قريباً</span>
            </div>

            <h3 style={{ fontSize: '1.05rem', fontWeight: 700, marginBottom: '8px', color: 'var(--text-strong)' }}>
              حملات التسويق الذكية (AI Campaigns)
            </h3>
            <p style={{ fontSize: '0.825rem', color: 'hsl(var(--text-secondary))', lineHeight: '1.5' }}>
              صناعة وإرسال حملات تسويق مخصصة بالذكاء الاصطناعي بناءً على تصنيفات واهتمامات العملاء ومتابعتها تلقائياً.
            </p>
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 'var(--space-sm)' }}>
            <button disabled className={`${styles.btn} ${styles.btnSecondary}`} style={{ padding: '6px 16px', fontSize: '0.8rem' }}>
              غير متوفر
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
