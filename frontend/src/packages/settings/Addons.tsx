'use client';

import React, { useEffect, useState } from 'react';
import { api } from '../../services/api';
import { useAuth } from '../../context/auth-context';
import { Calendar, Settings, ShieldAlert, Sparkles } from 'lucide-react';
import styles from './settings.module.css';

interface AddonsProps {
  onManageGroups: () => void;
  isGroupAppointmentsEnabled: boolean;
  onToggleGroupAppointments: (enabled: boolean) => Promise<void>;
}

export default function Addons({ 
  onManageGroups, 
  isGroupAppointmentsEnabled, 
  onToggleGroupAppointments 
}: AddonsProps) {
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

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
                backgroundColor: 'rgba(0, 243, 255, 0.1)', 
                display: 'flex', 
                alignItems: 'center', 
                justifyContent: 'center',
                color: 'hsl(var(--accent-primary))'
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

        {/* Placeholder Addon 2 */}
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
