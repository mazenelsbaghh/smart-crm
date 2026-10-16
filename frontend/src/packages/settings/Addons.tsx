'use client';

import React, { useState, useEffect, useCallback, useRef } from 'react';
import { Activity, Calendar, CheckCircle2, ExternalLink, RefreshCw, Settings, Users, Zap, User, ShieldCheck, MessageCircle, Save } from 'lucide-react';
import { api } from '../../services/api';
import { useAuth } from '../../context/auth-context';
import styles from './settings.module.css';
import layout from './Addons.module.css';
import ScheduleDemandAddon from './ScheduleDemandAddon';

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
  messengerWhatsAppTransitionEnabled: boolean;
  onMessengerBookingRouteChange: (enabled: boolean) => Promise<void>;
  onManageGroups: (courseType: 'regular' | 'intensive') => void;
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
  isTalkTipsTrialGateEnabled: boolean;
  onToggleTalkTipsTrialGate: (enabled: boolean) => Promise<void>;
  timezone: string;
  onDirtyChange: (dirty: boolean) => void;
}

const isValidTimezone = (timezone: string) => {
  try { new Intl.DateTimeFormat('en', { timeZone: timezone }).format(); return true; }
  catch { return false; }
};

const isValidOperationalPhone = (phone: string, allowEmpty: boolean) => {
  if (!phone) return allowEmpty;
  const normalized = phone.replace(/[\s()-]/g, '');
  return /^(?:\+?[1-9]\d{7,14}|01\d{9})$/.test(normalized);
};

export default function Addons({
  messengerWhatsAppTransitionEnabled,
  onMessengerBookingRouteChange,
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
  onUpdateHumanTransferPhone,
  isTalkTipsTrialGateEnabled,
  onToggleTalkTipsTrialGate,
  timezone,
  onDirtyChange,
}: AddonsProps) {
  const { activeProject } = useAuth();
  const managerPhoneInputRef = useRef<HTMLInputElement>(null);
  const humanPhoneInputRef = useRef<HTMLInputElement>(null);
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [automationOverview, setAutomationOverview] = useState<WhatsAppGroupAutomationOverview | null>(null);
  const [automationOverviewLoading, setAutomationOverviewLoading] = useState(true);
  const [humanTransferOverview, setHumanTransferOverview] = useState<HumanTransferOverview | null>(null);
  const [humanTransferOverviewLoading, setHumanTransferOverviewLoading] = useState(true);
  const [managerPhoneDraft, setManagerPhoneDraft] = useState(groupAutomationManagerPhone);
  const [humanPhoneDraft, setHumanPhoneDraft] = useState(humanTransferPhone);
  const hasUnsavedPhoneChanges = managerPhoneDraft.trim() !== groupAutomationManagerPhone.trim()
    || humanPhoneDraft.trim() !== humanTransferPhone.trim();

  useEffect(() => onDirtyChange(hasUnsavedPhoneChanges), [hasUnsavedPhoneChanges, onDirtyChange]);
  useEffect(() => () => onDirtyChange(false), [onDirtyChange]);

  const fetchAutomationOverview = useCallback(async () => {
    if (!activeProject) return;
    try {
      setAutomationOverviewLoading(true);
      const response = await api.get<WhatsAppGroupAutomationOverview>('/api/group-appointments/automation-overview');
      setAutomationOverview(response.data);
    } catch (e) {
      console.error('Failed to load WhatsApp group automation overview', e);
      setMessage({ type: 'error', text: 'تعذّر تحديث حالة أتمتة مجموعات واتساب. قد تكون الأرقام المعروضة قديمة.' });
    } finally {
      setAutomationOverviewLoading(false);
    }
  }, [activeProject]);

  useEffect(() => {
    const timer = window.setTimeout(() => void fetchAutomationOverview(), 0);
    return () => window.clearTimeout(timer);
  }, [fetchAutomationOverview, isWhatsAppGroupAutomationEnabled]);

  const fetchHumanTransferOverview = useCallback(async () => {
    if (!activeProject) return;
    try {
      setHumanTransferOverviewLoading(true);
      const response = await api.get<HumanTransferOverview>(`/api/projects/${activeProject.id}/human-transfer-overview`);
      setHumanTransferOverview(response.data);
    } catch (e) {
      console.error('Failed to load human transfer overview', e);
      setMessage({ type: 'error', text: 'تعذّر تحديث حالة التحويل لمشرف. قد تكون البيانات المعروضة قديمة.' });
    } finally {
      setHumanTransferOverviewLoading(false);
    }
  }, [activeProject]);

  useEffect(() => {
    const timer = window.setTimeout(() => void fetchHumanTransferOverview(), 0);
    return () => window.clearTimeout(timer);
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
    const hasUnsavedManagerPhone = managerPhoneDraft.trim() !== groupAutomationManagerPhone.trim();
    if (checked && hasUnsavedManagerPhone) {
      setMessage({ type: 'error', text: 'احفظ رقم المدير المعدّل أولًا، ثم فعّل أتمتة مجموعات واتساب.' });
      managerPhoneInputRef.current?.focus();
      return;
    }
    if (checked && !isValidOperationalPhone(groupAutomationManagerPhone.trim(), false)) {
      setMessage({ type: 'error', text: 'احفظ رقم مدير صالحًا أولًا، ثم فعّل أتمتة مجموعات واتساب.' });
      managerPhoneInputRef.current?.focus();
      return;
    }
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

  const displayTimezone = isValidTimezone(timezone) ? timezone : 'UTC';
  const formatAppointmentDate = (isoString: string) => {
    return new Date(isoString).toLocaleString('ar-EG', {
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      timeZone: displayTimezone,
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
      minute: '2-digit',
      timeZone: displayTimezone,
    });
  };

  const handleToggleHumanTransfer = async (checked: boolean) => {
    const hasUnsavedHumanPhone = humanPhoneDraft.trim() !== humanTransferPhone.trim();
    if (checked && hasUnsavedHumanPhone) {
      setMessage({ type: 'error', text: 'احفظ رقم المشرف المعدّل أولًا، ثم فعّل التحويل لشخص حقيقي.' });
      humanPhoneInputRef.current?.focus();
      return;
    }
    if (checked && !isValidOperationalPhone(humanTransferPhone.trim(), false)) {
      setMessage({ type: 'error', text: 'احفظ رقم مشرف صالحًا أولًا، ثم فعّل التحويل لشخص حقيقي.' });
      humanPhoneInputRef.current?.focus();
      return;
    }
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

  const saveManagerPhone = () => {
    const phone = managerPhoneDraft.trim();
    if (!isValidOperationalPhone(phone, !isWhatsAppGroupAutomationEnabled)) {
      setMessage({ type: 'error', text: 'اكتب رقم مدير صالحًا (رقم مصري 11 خانة أو صيغة دولية) قبل الحفظ.' });
      managerPhoneInputRef.current?.focus();
      return;
    }
    void handleUpdateManagerPhone(phone);
  };

  const saveHumanTransferPhone = () => {
    const phone = humanPhoneDraft.trim();
    if (!isValidOperationalPhone(phone, !humanTransferEnabled)) {
      setMessage({ type: 'error', text: 'اكتب رقم مشرف صالحًا (رقم مصري 11 خانة أو صيغة دولية) قبل الحفظ.' });
      humanPhoneInputRef.current?.focus();
      return;
    }
    void handleUpdatePhone(phone);
  };

  const openGroupManagement = (courseType: 'regular' | 'intensive') => {
    if (hasUnsavedPhoneChanges) {
      setMessage({ type: 'error', text: 'احفظ أرقام المدير والمشرف المعدّلة قبل فتح إدارة المجموعات.' });
      (managerPhoneDraft.trim() !== groupAutomationManagerPhone.trim()
        ? managerPhoneInputRef.current
        : humanPhoneInputRef.current)?.focus();
      return;
    }
    onManageGroups(courseType);
  };

  const handleToggleTalkTipsTrialGate = async (checked: boolean) => {
    try {
      setLoading(true);
      setMessage(null);
      await onToggleTalkTipsTrialGate(checked);
      setMessage({
        type: 'success',
        text: checked ? 'تم تفعيل بوابة تجربة TalkTips.' : 'تم إلغاء تفعيل بوابة تجربة TalkTips.'
      });
    } catch {
      setMessage({ type: 'error', text: 'فشل تعديل بوابة تجربة TalkTips.' });
    } finally {
      setLoading(false);
    }
  };

  const changeMessengerBookingRoute = async (enabled: boolean) => {
    setLoading(true);
    setMessage(null);
    try {
      await onMessengerBookingRouteChange(enabled);
      setMessage({ type: 'success', text: 'تم حفظ طريقة حجز عملاء الماسنجر.' });
    } catch {
      setMessage({ type: 'error', text: 'تعذر حفظ طريقة الحجز. حاول مرة أخرى.' });
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className={layout.workspace}>
      {message && <div role={message.type === 'error' ? 'alert' : 'status'} className={message.type === 'error' ? layout.error : layout.notice}>{message.text}</div>}
      <nav className={layout.jumpLinks} aria-label="أقسام الإضافات">
        <a href="#addon-booking"><Calendar size={17} /> الحجز والمواعيد</a>
        <a href="#addon-automation"><Zap size={17} /> أتمتة واتساب</a>
        <a href="#addon-human"><User size={17} /> المشرف البشري</a>
        <a href="#addon-demand"><Activity size={17} /> طلبات المواعيد</a>
        <a href="#addon-trial"><ShieldCheck size={17} /> تجربة TalkTips</a>
      </nav>

      <section id="addon-booking" className={layout.panel} aria-labelledby="addon-booking-title">
        <header className={layout.heading}>
          <span className={layout.icon}><Calendar size={23} /></span>
          <div><h2 id="addon-booking-title">الحجز والمواعيد</h2><p>حدد مسار الحجز، ثم تابع مواعيد كل كورس وسعته.</p></div>
          <label className={layout.toggle}><input type="checkbox" role="switch" aria-label="تفعيل حجز مواعيد الكورسات" checked={isGroupAppointmentsEnabled} disabled={loading} onChange={e => handleToggle(e.target.checked)} className={styles.checkbox} /><span>{isGroupAppointmentsEnabled ? 'الحجز مفعّل' : 'الحجز متوقف'}</span></label>
        </header>
        <div className={layout.bookingBody}>
          <div className={layout.field}>
            <h3 id="messenger-booking-title"><MessageCircle size={18} /> حجز عملاء الماسنجر</h3>
            <label htmlFor="messenger-booking-route">مكان إكمال الحجز</label>
            <select id="messenger-booking-route" className={styles.input} value={messengerWhatsAppTransitionEnabled ? 'WhatsApp' : 'Messenger'} disabled={loading} aria-describedby="messenger-booking-help" onChange={event => void changeMessengerBookingRoute(event.target.value === 'WhatsApp')}>
              <option value="Messenger">إكمال الحجز من الماسنجر</option><option value="WhatsApp">التحويل للواتساب لإكمال الحجز</option>
            </select>
            <p id="messenger-booking-help">{messengerWhatsAppTransitionEnabled ? 'يرد في الماسنجر، ثم يطلب رقم العميل للانتقال إلى واتساب وإكمال الحجز.' : 'يجمع الاسم ورقم الموبايل ويكمل الحجز في الماسنجر، حسب المواعيد المتاحة وإعدادات الحجز.'}</p>
          </div>
          <div className={layout.courseList}>
            <div><div><h3>مواعيد الكورس العادي</h3><p>المجموعات والسعة والحجوزات.</p></div><button type="button" onClick={() => openGroupManagement('regular')} disabled={!isGroupAppointmentsEnabled} className={`${styles.btn} ${styles.btnSecondary}`}><Settings size={16} /> إدارة مواعيد الكورس العادي</button></div>
            <div><div><h3>مواعيد الكورس المكثف</h3><p>السيشنات والإنستراكتورز والحجوزات.</p></div><button type="button" onClick={() => openGroupManagement('intensive')} className={`${styles.btn} ${styles.btnSecondary}`}><Settings size={16} /> إدارة مواعيد الكورس المكثف</button></div>
            {!isGroupAppointmentsEnabled && <p className={layout.hint}>فعّل حجز مواعيد الكورسات لفتح الحجز للعملاء.</p>}
          </div>
        </div>
      </section>

      <section id="addon-automation" className={layout.panel} aria-labelledby="addon-automation-title">
        <header className={layout.heading}>
          <span className={`${layout.icon} ${layout.green}`}><Zap size={23} /></span>
          <div><h2 id="addon-automation-title">أتمتة مجموعات واتساب</h2><p>إنشاء المجموعات وإرسال الدعوات ومتابعة الطلاب بعد الحضور.</p></div>
          <label className={layout.toggle}><input type="checkbox" role="switch" aria-label="تفعيل أتمتة مجموعات واتساب" checked={isWhatsAppGroupAutomationEnabled} disabled={loading} onChange={e => handleToggleGroupAutomation(e.target.checked)} className={styles.checkbox} /><span>{isWhatsAppGroupAutomationEnabled ? 'مفعّلة' : 'متوقفة'}</span></label>
        </header>
        <div className={layout.phoneRow}>
          <div className={layout.field}><label htmlFor="addon-group-manager-phone">رقم هاتف المدير للتنبيهات والمجموعات</label><input ref={managerPhoneInputRef} id="addon-group-manager-phone" type="tel" inputMode="tel" autoComplete="tel" dir="ltr" className={styles.input} value={managerPhoneDraft} onChange={event => setManagerPhoneDraft(event.target.value)} disabled={loading} placeholder="+20xxxxxxxxxx" /></div>
          <button type="button" className={`${styles.btn} ${styles.btnSecondary}`} disabled={loading || managerPhoneDraft.trim() === groupAutomationManagerPhone.trim()} onClick={saveManagerPhone}><Save size={16} /> حفظ رقم المدير</button>
          {managerPhoneDraft.trim() !== groupAutomationManagerPhone.trim() && <small role="status">رقم المدير لم يُحفظ بعد.</small>}
        </div>
        {isWhatsAppGroupAutomationEnabled && <>
          <div className={layout.sectionHeading}><h3><Activity size={18} /> حالة المجموعات</h3><button type="button" onClick={() => fetchAutomationOverview()} disabled={automationOverviewLoading} className={`${styles.btn} ${styles.btnSecondary}`}><RefreshCw size={16} /> تحديث</button></div>
          <dl className={layout.stats}>
            {([['جروبات واتساب', automationOverview?.whatsAppGroupsCreated], ['طلاب داخلها', automationOverview?.totalBookingsInWhatsAppGroups], ['متابعات معلقة', automationOverview?.pendingFollowUps], ['كل الحجوزات', automationOverview?.totalBookings]] as const).map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value ?? '—'}</dd></div>)}
          </dl>
          {automationOverviewLoading ? <p role="status">جاري تحميل حالة المجموعات...</p> : !automationOverview ? <p>بيانات الأتمتة غير متاحة الآن. استخدم «تحديث» لإعادة المحاولة.</p> : visibleAutomationGroups.length === 0 ? <p className={layout.hint}>لا توجد مجموعات بحجوزات أو جروبات واتساب حتى الآن.</p> : <ul className={layout.rows}>{visibleAutomationGroups.map(group => <li key={group.id}>
            <div><strong>{group.name || (group.mode === 'online' ? 'أونلاين' : 'في السنتر')}</strong><span className={layout.badge} data-state={group.followUpStatus}>{getFollowUpStatusLabel(group.followUpStatus).text}</span><p><Users size={15} /> {group.bookedCount} طالب · {formatAppointmentDate(group.dateTime)} · {group.isActive ? 'نشطة' : 'معطلة'} {group.hasWhatsAppGroup && <span><CheckCircle2 size={15} /> الجروب اتعمل</span>}</p></div>
            {group.whatsAppGroupInviteLink && <a href={group.whatsAppGroupInviteLink} target="_blank" rel="noreferrer" className={`${styles.btn} ${styles.btnSecondary}`}><ExternalLink size={16} /> رابط المجموعة</a>}
          </li>)}</ul>}
        </>}
      </section>

      <section id="addon-human" className={layout.panel} aria-labelledby="addon-human-title">
        <header className={layout.heading}><span className={`${layout.icon} ${layout.blue}`}><User size={23} /></span><div><h2 id="addon-human-title">التواصل مع مشرف بشري</h2><p>إرسال رقم المشرف عندما يطلب العميل التحدث مع شخص حقيقي أو مسؤول.</p></div><label className={layout.toggle}><input type="checkbox" role="switch" aria-label="تفعيل التواصل مع مشرف بشري" checked={humanTransferEnabled} disabled={loading} onChange={e => handleToggleHumanTransfer(e.target.checked)} className={styles.checkbox} /><span>{humanTransferEnabled ? 'مفعّل' : 'متوقف'}</span></label></header>
        <div className={layout.phoneRow}><div className={layout.field}><label htmlFor="addon-human-transfer-phone">رقم هاتف المشرف للتحويل</label><input ref={humanPhoneInputRef} id="addon-human-transfer-phone" type="tel" inputMode="tel" autoComplete="tel" dir="ltr" className={styles.input} value={humanPhoneDraft} onChange={event => setHumanPhoneDraft(event.target.value)} disabled={loading} placeholder="+20xxxxxxxxxx" /></div><button type="button" className={`${styles.btn} ${styles.btnSecondary}`} disabled={loading || humanPhoneDraft.trim() === humanTransferPhone.trim()} onClick={saveHumanTransferPhone}><Save size={16} /> حفظ رقم المشرف</button>{humanPhoneDraft.trim() !== humanTransferPhone.trim() && <small role="status">رقم المشرف لم يُحفظ بعد.</small>}</div>
        <div className={layout.sectionHeading}><span className={layout.badge} data-state={!humanTransferOverview ? undefined : humanTransferOverview.isReady ? 'active' : humanTransferEnabled ? 'waiting' : 'inactive'}>{!humanTransferOverview ? 'الحالة غير متاحة' : humanTransferOverview.isReady ? 'جاهز للتحويل' : humanTransferEnabled ? 'أكمل رقم المشرف لتجهيز التحويل' : 'التحويل متوقف'}</span><button type="button" onClick={() => fetchHumanTransferOverview()} disabled={humanTransferOverviewLoading} className={`${styles.btn} ${styles.btnSecondary}`}><RefreshCw size={16} /> تحديث</button></div>
        <p className={layout.hint}>الرقم المستخدم: <bdi dir="ltr">{humanTransferOverview?.humanTransferPhone || humanTransferPhone || 'لم يتم تحديد رقم'}</bdi></p>
        <dl className={layout.stats}>{([['كل الطلبات', humanTransferOverview?.totalRequests], ['طلبات اليوم', humanTransferOverview?.todayRequests], ['غير مقروءة', humanTransferOverview?.unreadRequests]] as const).map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value ?? '—'}</dd></div>)}</dl>
        {humanTransferOverviewLoading ? <p role="status">جاري تحميل طلبات التواصل...</p> : !humanTransferOverview ? <p>بيانات طلبات التواصل غير متاحة الآن. استخدم «تحديث» لإعادة المحاولة.</p> : humanTransferOverview.recentRequests.length === 0 ? <p className={layout.hint}>لا توجد طلبات تواصل مع مشرف حتى الآن.</p> : <ul className={layout.rows}>{humanTransferOverview.recentRequests.map(item => <li key={item.id}><p>{item.message}</p><time dateTime={item.createdAt}>{formatRequestDate(item.createdAt)}</time></li>)}</ul>}
      </section>

      <div id="addon-demand" className={layout.demand}><ScheduleDemandAddon timezone={displayTimezone} onMessage={setMessage} /></div>

      <section id="addon-trial" className={layout.panel} aria-labelledby="addon-trial-title">
        <header className={layout.heading}><span className={`${layout.icon} ${layout.violet}`}><ShieldCheck size={23} /></span><div><h2 id="addon-trial-title">دعوة تجربة TalkTips</h2><p>يقترح تجربة التطبيق أثناء النقاش عن الإنجليزي والسعر والحجز، دون تكرار أو تعطيل خطوات الحجز.</p></div><label className={layout.toggle}><input type="checkbox" role="switch" aria-label="تفعيل بوابة تجربة TalkTips" checked={isTalkTipsTrialGateEnabled} disabled={loading} onChange={e => handleToggleTalkTipsTrialGate(e.target.checked)} className={styles.checkbox} /><span>{isTalkTipsTrialGateEnabled ? 'مفعّلة' : 'متوقفة'}</span></label></header>
        <a href="https://talktips-academy.com/ar/try" target="_blank" rel="noreferrer" className={`${styles.btn} ${styles.btnSecondary}`}><ExternalLink size={16} /> فتح تجربة TalkTips</a>
      </section>
    </div>
  );
}
