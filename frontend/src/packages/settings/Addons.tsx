'use client';

import React, { useState, useEffect, useCallback } from 'react';
import { Activity, Calendar, CheckCircle2, ExternalLink, RefreshCw, Settings, Sparkles, Users, Zap, User } from 'lucide-react';
import { api } from '../../services/api';
import { useAuth } from '../../context/auth-context';
import styles from './settings.module.css';

interface WhatsAppGroupAutomationRow {
  id: string;
  name: string;
  mode: string;
  dateTime: string;
  isActive: boolean;
  capacity: number;
  whatsAppGroupJid?: string | null;
  whatsAppGroupInviteLink?: string | null;
  bookedCount: number;
  hasWhatsAppGroup: boolean;
  pendingFollowUpCount: number;
  followUpStatus: 'disabled' | 'inactive' | 'active' | 'created-no-pending' | 'waiting';
}

interface WhatsAppGroupAutomationOverview {
  isEnabled: boolean;
  managerPhone: string;
  totalGroups: number;
  activeGroups: number;
  inactiveGroups: number;
  whatsAppGroupsCreated: number;
  totalBookings: number;
  totalBookingsInWhatsAppGroups: number;
  pendingFollowUps: number;
  groups: WhatsAppGroupAutomationRow[];
}

interface HumanTransferRequestRow {
  id: string;
  message: string;
  createdAt: string;
  isRead: boolean;
}

interface HumanTransferOverview {
  humanTransferEnabled: boolean;
  humanTransferPhone?: string | null;
  isReady: boolean;
  totalRequests: number;
  todayRequests: number;
  unreadRequests: number;
  recentRequests: HumanTransferRequestRow[];
}

interface AddonsProps {
  onManageGroups: () => void;
  isGroupAppointmentsEnabled: boolean;
  onToggleGroupAppointments: (enabled: boolean) => Promise<void>;
  isWhatsAppGroupAutomationEnabled: boolean;
  onToggleWhatsAppGroupAutomation: (enabled: boolean) => Promise<void>;
  groupAutomationManagerPhone: string;
  onUpdateGroupAutomationManagerPhone: (phone: string) => Promise<void>;
  humanTransferEnabled: boolean;
  onToggleHumanTransfer: (enabled: boolean) => Promise<void>;
  humanTransferPhone: string;
  onUpdateHumanTransferPhone: (phone: string) => Promise<void>;
}

export default function Addons({
  onManageGroups,
  isGroupAppointmentsEnabled,
  onToggleGroupAppointments,
  isWhatsAppGroupAutomationEnabled,
  onToggleWhatsAppGroupAutomation,
  groupAutomationManagerPhone,
  onUpdateGroupAutomationManagerPhone,
  humanTransferEnabled,
  onToggleHumanTransfer,
  humanTransferPhone,
  onUpdateHumanTransferPhone
}: AddonsProps) {
  const { activeProject } = useAuth();
  const [loading, setLoading] = useState(false);
  const [managerPhone, setManagerPhone] = useState(groupAutomationManagerPhone);
  const [phoneInput, setPhoneInput] = useState(humanTransferPhone);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [automationOverview, setAutomationOverview] = useState<WhatsAppGroupAutomationOverview | null>(null);
  const [automationOverviewLoading, setAutomationOverviewLoading] = useState(false);
  const [humanTransferOverview, setHumanTransferOverview] = useState<HumanTransferOverview | null>(null);
  const [humanTransferOverviewLoading, setHumanTransferOverviewLoading] = useState(false);

  useEffect(() => {
    setManagerPhone(groupAutomationManagerPhone);
  }, [groupAutomationManagerPhone]);

  useEffect(() => {
    setPhoneInput(humanTransferPhone);
  }, [humanTransferPhone]);

  const fetchAutomationOverview = useCallback(async () => {
    if (!activeProject) return;
    try {
      setAutomationOverviewLoading(true);
      const response = await api.get<WhatsAppGroupAutomationOverview>('/api/group-appointments/automation-overview');
      setAutomationOverview(response.data);
    } catch (e) {
      console.error('Failed to load WhatsApp group automation overview', e);
    } finally {
      setAutomationOverviewLoading(false);
    }
  }, [activeProject]);

  useEffect(() => {
    void fetchAutomationOverview();
  }, [fetchAutomationOverview, isWhatsAppGroupAutomationEnabled]);

  const fetchHumanTransferOverview = useCallback(async () => {
    if (!activeProject) return;
    try {
      setHumanTransferOverviewLoading(true);
      const response = await api.get<HumanTransferOverview>(`/api/projects/${activeProject.id}/human-transfer-overview`);
      setHumanTransferOverview(response.data);
    } catch (e) {
      console.error('Failed to load human transfer overview', e);
    } finally {
      setHumanTransferOverviewLoading(false);
    }
  }, [activeProject]);

  useEffect(() => {
    void fetchHumanTransferOverview();
  }, [fetchHumanTransferOverview, humanTransferEnabled]);

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
      void fetchAutomationOverview();
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
      void fetchAutomationOverview();
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

  const formatAppointmentDate = (isoString: string) => {
    return new Date(isoString).toLocaleString('ar-EG', {
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  };

  const getFollowUpStatusLabel = (status: WhatsAppGroupAutomationRow['followUpStatus']) => {
    switch (status) {
      case 'active':
        return { text: 'المتابعة شغالة', color: 'rgb(16, 185, 129)', background: 'rgba(16, 185, 129, 0.1)' };
      case 'created-no-pending':
        return { text: 'الجروب اتعمل، لا توجد متابعات معلقة', color: 'rgb(245, 158, 11)', background: 'rgba(245, 158, 11, 0.1)' };
      case 'waiting':
        return { text: 'منتظر إنشاء الجروب', color: 'hsl(var(--text-secondary))', background: 'var(--surface-muted)' };
      case 'inactive':
        return { text: 'المجموعة معطلة', color: 'hsl(var(--text-secondary))', background: 'var(--surface-muted)' };
      default:
        return { text: 'الأتمتة متوقفة', color: 'hsl(var(--accent-danger))', background: 'rgba(239, 68, 68, 0.1)' };
    }
  };

  const visibleAutomationGroups = automationOverview?.groups
    .filter(group => group.hasWhatsAppGroup || group.isActive || group.bookedCount > 0)
    .slice(0, 8) ?? [];

  const formatRequestDate = (isoString: string) => {
    return new Date(isoString).toLocaleString('ar-EG', {
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  };

  const handleToggleHumanTransfer = async (checked: boolean) => {
    try {
      setLoading(true);
      setMessage(null);
      await onToggleHumanTransfer(checked);
      void fetchHumanTransferOverview();
      setMessage({
        type: 'success',
        text: checked ? 'تم تفعيل ميزة التواصل مع شخص حقيقي بنجاح.' : 'تم إلغاء تفعيل ميزة التواصل مع شخص حقيقي.'
      });
    } catch {
      setMessage({ type: 'error', text: 'فشل تعديل حالة الإضافة.' });
    } finally {
      setLoading(false);
    }
  };

  const handleUpdatePhone = async (phone: string) => {
    try {
      setLoading(true);
      setMessage(null);
      await onUpdateHumanTransferPhone(phone);
      void fetchHumanTransferOverview();
      setMessage({
        type: 'success',
        text: 'تم تحديث رقم هاتف المشرف بنجاح.'
      });
    } catch {
      setMessage({ type: 'error', text: 'فشل تحديث رقم هاتف المشرف.' });
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
              <div style={{ marginTop: 'var(--space-md)', display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
                <div>
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

                <div style={{ borderTop: '1px solid var(--border-subtle)', paddingTop: 'var(--space-md)' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 'var(--space-sm)', marginBottom: 'var(--space-sm)' }}>
                    <span style={{ fontSize: '0.82rem', fontWeight: 700, color: 'var(--text-strong)', display: 'flex', alignItems: 'center', gap: '6px' }}>
                      <Activity size={14} />
                      إدارة الأتمتة
                    </span>
                    <button
                      type="button"
                      onClick={() => fetchAutomationOverview()}
                      disabled={automationOverviewLoading}
                      className={`${styles.btn} ${styles.btnSecondary}`}
                      style={{ padding: '4px 8px', fontSize: '0.72rem' }}
                    >
                      <RefreshCw size={12} />
                      تحديث
                    </button>
                  </div>

                  <div style={{
                    display: 'grid',
                    gridTemplateColumns: 'repeat(4, minmax(0, 1fr))',
                    gap: '8px',
                    marginBottom: 'var(--space-sm)'
                  }}>
                    {[
                      ['جروبات واتساب', automationOverview?.whatsAppGroupsCreated ?? 0],
                      ['طلاب داخلها', automationOverview?.totalBookingsInWhatsAppGroups ?? 0],
                      ['متابعات معلقة', automationOverview?.pendingFollowUps ?? 0],
                      ['كل الحجوزات', automationOverview?.totalBookings ?? 0]
                    ].map(([label, value]) => (
                      <div key={label} style={{ padding: '8px 0', borderTop: '1px solid var(--border-subtle)' }}>
                        <div style={{ fontSize: '1.05rem', fontWeight: 800, color: 'var(--text-strong)', lineHeight: 1.1 }}>{value}</div>
                        <div style={{ fontSize: '0.66rem', color: 'hsl(var(--text-secondary))', marginTop: '3px' }}>{label}</div>
                      </div>
                    ))}
                  </div>

                  {automationOverviewLoading ? (
                    <div style={{ fontSize: '0.76rem', color: 'hsl(var(--text-secondary))', padding: '8px 0' }}>
                      جاري تحميل حالة المجموعات...
                    </div>
                  ) : visibleAutomationGroups.length === 0 ? (
                    <div style={{ fontSize: '0.76rem', color: 'hsl(var(--text-secondary))', padding: '8px 0' }}>
                      لا توجد مجموعات بحجوزات أو جروبات واتساب حتى الآن.
                    </div>
                  ) : (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '6px', maxHeight: '260px', overflowY: 'auto' }}>
                      {visibleAutomationGroups.map((group) => {
                        const status = getFollowUpStatusLabel(group.followUpStatus);
                        return (
                          <div
                            key={group.id}
                            style={{
                              display: 'grid',
                              gridTemplateColumns: '1fr auto',
                              gap: '10px',
                              padding: '9px 0',
                              borderTop: '1px solid var(--border-subtle)'
                            }}
                          >
                            <div style={{ minWidth: 0 }}>
                              <div style={{ display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
                                <span style={{ fontSize: '0.8rem', fontWeight: 700, color: 'var(--text-strong)' }}>
                                  {group.name || (group.mode === 'online' ? 'أونلاين' : 'في السنتر')}
                                </span>
                                <span style={{
                                  fontSize: '0.66rem',
                                  color: status.color,
                                  background: status.background,
                                  padding: '2px 6px',
                                  borderRadius: '6px',
                                  fontWeight: 700
                                }}>
                                  {status.text}
                                </span>
                              </div>
                              <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap', marginTop: '5px', color: 'hsl(var(--text-secondary))', fontSize: '0.72rem' }}>
                                <span style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                                  <Users size={11} />
                                  {group.bookedCount} طالب
                                </span>
                                <span>{formatAppointmentDate(group.dateTime)}</span>
                                <span>{group.isActive ? 'نشطة' : 'معطلة'}</span>
                                {group.hasWhatsAppGroup && (
                                  <span style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', color: 'rgb(16, 185, 129)' }}>
                                    <CheckCircle2 size={11} />
                                    الجروب اتعمل
                                  </span>
                                )}
                              </div>
                            </div>
                            {group.whatsAppGroupInviteLink && (
                              <a
                                href={group.whatsAppGroupInviteLink}
                                target="_blank"
                                rel="noreferrer"
                                className={`${styles.btn} ${styles.btnSecondary}`}
                                style={{ padding: '4px 8px', fontSize: '0.7rem', alignSelf: 'start' }}
                              >
                                <ExternalLink size={11} />
                                الرابط
                              </a>
                            )}
                          </div>
                        );
                      })}
                    </div>
                  )}
                </div>
              </div>
            )}
          </div>
        </div>

        {/* Human Agent Transfer Card */}
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
                backgroundColor: 'rgba(59, 130, 246, 0.1)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                color: 'rgba(59, 130, 246, 1)'
              }}>
                <User size={22} />
              </div>

              <label className={styles.checkboxGroup} style={{ cursor: loading ? 'not-allowed' : 'pointer' }}>
                <input
                  type="checkbox"
                  checked={humanTransferEnabled}
                  disabled={loading}
                  onChange={(e) => handleToggleHumanTransfer(e.target.checked)}
                  className={styles.checkbox}
                />
              </label>
            </div>

            <h3 style={{ fontSize: '1.05rem', fontWeight: 700, marginBottom: '8px', color: 'var(--text-strong)' }}>
              التواصل مع شخص حقيقي (Human Transfer)
            </h3>
            <p style={{ fontSize: '0.825rem', color: 'hsl(var(--text-secondary))', lineHeight: '1.5' }}>
              إرسال رقم المشرف فقط عندما يطلب العميل صراحة التحدث مع شخص حقيقي أو رقم مسؤول.
            </p>

            <div style={{
              marginTop: 'var(--space-md)',
              padding: '10px 0',
              borderTop: '1px solid var(--border-subtle)',
              borderBottom: '1px solid var(--border-subtle)',
              display: 'flex',
              flexDirection: 'column',
              gap: '8px'
            }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 'var(--space-sm)', flexWrap: 'wrap' }}>
                <span style={{
                  fontSize: '0.76rem',
                  fontWeight: 800,
                  color: humanTransferOverview?.isReady ? 'rgb(16, 185, 129)' : humanTransferEnabled ? 'rgb(245, 158, 11)' : 'hsl(var(--accent-danger))',
                  background: humanTransferOverview?.isReady ? 'rgba(16, 185, 129, 0.1)' : humanTransferEnabled ? 'rgba(245, 158, 11, 0.1)' : 'rgba(239, 68, 68, 0.1)',
                  padding: '3px 8px',
                  borderRadius: '6px'
                }}>
                  {humanTransferOverview?.isReady ? 'شغالة بالرقم الحالي' : humanTransferEnabled ? 'مفعلة لكن الرقم ناقص' : 'متوقفة'}
                </span>
                <button
                  type="button"
                  onClick={() => fetchHumanTransferOverview()}
                  disabled={humanTransferOverviewLoading}
                  className={`${styles.btn} ${styles.btnSecondary}`}
                  style={{ padding: '4px 8px', fontSize: '0.72rem' }}
                >
                  <RefreshCw size={12} />
                  تحديث
                </button>
              </div>
              <div style={{ fontSize: '0.76rem', color: 'hsl(var(--text-secondary))' }}>
                الرقم المستخدم: <span style={{ color: 'var(--text-strong)', fontWeight: 700 }}>{humanTransferOverview?.humanTransferPhone || humanTransferPhone || 'غير محدد'}</span>
              </div>
            </div>

            {humanTransferEnabled && (
              <div style={{ marginTop: 'var(--space-md)', display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
                <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 600, color: 'var(--text-soft)', marginBottom: '4px' }}>
                  رقم هاتف المشرف للتواصل (مثال: 010xxxxxxxx)
                </label>
                <input
                  type="text"
                  value={phoneInput}
                  onChange={(e) => setPhoneInput(e.target.value)}
                  onBlur={() => handleUpdatePhone(phoneInput)}
                  placeholder="010xxxxxxxx"
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

                <div>
                  <div style={{
                    display: 'grid',
                    gridTemplateColumns: 'repeat(3, minmax(0, 1fr))',
                    gap: '8px',
                    marginBottom: 'var(--space-sm)'
                  }}>
                    {[
                      ['كل الطلبات', humanTransferOverview?.totalRequests ?? 0],
                      ['طلبات اليوم', humanTransferOverview?.todayRequests ?? 0],
                      ['غير مقروءة', humanTransferOverview?.unreadRequests ?? 0]
                    ].map(([label, value]) => (
                      <div key={label} style={{ padding: '8px 0', borderTop: '1px solid var(--border-subtle)' }}>
                        <div style={{ fontSize: '1.05rem', fontWeight: 800, color: 'var(--text-strong)', lineHeight: 1.1 }}>{value}</div>
                        <div style={{ fontSize: '0.66rem', color: 'hsl(var(--text-secondary))', marginTop: '3px' }}>{label}</div>
                      </div>
                    ))}
                  </div>

                  {humanTransferOverviewLoading ? (
                    <div style={{ fontSize: '0.76rem', color: 'hsl(var(--text-secondary))', padding: '8px 0' }}>
                      جاري تحميل طلبات التواصل...
                    </div>
                  ) : !humanTransferOverview?.recentRequests?.length ? (
                    <div style={{ fontSize: '0.76rem', color: 'hsl(var(--text-secondary))', padding: '8px 0' }}>
                      لا توجد طلبات تواصل بشخص حقيقي حتى الآن.
                    </div>
                  ) : (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '6px', maxHeight: '220px', overflowY: 'auto' }}>
                      {humanTransferOverview.recentRequests.map((request) => (
                        <div
                          key={request.id}
                          style={{
                            display: 'flex',
                            flexDirection: 'column',
                            gap: '4px',
                            padding: '8px 0',
                            borderTop: '1px solid var(--border-subtle)'
                          }}
                        >
                          <span style={{ fontSize: '0.77rem', color: 'var(--text-strong)', lineHeight: 1.5 }}>
                            {request.message}
                          </span>
                          <span style={{ fontSize: '0.68rem', color: 'hsl(var(--text-secondary))' }}>
                            {formatRequestDate(request.createdAt)}
                          </span>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
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
