'use client';

import React, { useCallback, useEffect, useState } from 'react';
import type { AxiosError } from 'axios';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import { 
  QrCode, 
  Smartphone, 
  CheckCircle, 
  AlertCircle, 
  Settings as SettingsIcon,
  RefreshCw,
  LogOut,
  Zap
} from 'lucide-react';
import styles from './settings.module.css';

import Addons from './Addons';
import GroupAppointmentsManager from './GroupAppointmentsManager';

interface SessionStatusResponse {
  projectId: string;
  status: 'Disconnected' | 'Initializing' | 'Connected';
  phoneNumber: string | null;
  error?: string | null;
}

interface ApiErrorResponse {
  error?: string;
}

interface ProjectSettingsResponse {
  name?: string;
  settings?: {
    aiAutoReplyEnabled?: boolean;
    timezone?: string;
    geminiApiKey?: string;
    aiTonePreference?: string;
    aiTargetAudience?: string;
    replyDelay?: number;
    maxDailyMessages?: number;
    isGroupAppointmentsEnabled?: boolean;
  } | null;
}

const getApiErrorMessage = (error: unknown, fallback: string) => {
  const axiosError = error as AxiosError<ApiErrorResponse>;
  return axiosError.response?.data?.error || fallback;
};

export default function Settings() {
  const { activeProject, refreshProjects } = useAuth();
  
  const [status, setStatus] = useState<'Disconnected' | 'Initializing' | 'Connected'>('Disconnected');
  const [phoneNumber, setPhoneNumber] = useState<string | null>(null);
  const [qrString, setQrString] = useState<string | null>(null);
  const [qrError, setQrError] = useState<string | null>(null);
  
  const [loading, setLoading] = useState(true);
  const [actionLoading, setActionLoading] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error', text: string } | null>(null);

  // General settings state
  const [projectName, setProjectName] = useState('');
  const [autoReplyEnabled, setAutoReplyEnabled] = useState(true);
  const [geminiApiKey, setGeminiApiKey] = useState('');
  const [timezone, setTimezone] = useState('Africa/Cairo');
  const [aiTonePreference, setAiTonePreference] = useState('العامية المصرية الروشة والصايعة');
  const [aiTargetAudience, setAiTargetAudience] = useState('طلاب كورس كول سنتر يبحثون عن عمل');
  const [replyDelay, setReplyDelay] = useState(3);
  const [maxDailyMessages, setMaxDailyMessages] = useState(500);
  const [isGroupAppointmentsEnabled, setIsGroupAppointmentsEnabled] = useState(false);

  // Tabs / Navigation state
  const [activeTab, setActiveTab] = useState<'general' | 'addons'>('general');
  const [viewMode, setViewMode] = useState<'list' | 'manage-groups'>('list');

  const fetchProjectSettings = useCallback(async () => {
    if (!activeProject) return;
    try {
      const response = await api.get<ProjectSettingsResponse>(`/api/projects/${activeProject.id}`);
      setProjectName(response.data.name || '');
      const settings = response.data.settings;
      setAutoReplyEnabled(settings?.aiAutoReplyEnabled ?? true);
      setTimezone(settings?.timezone || 'Africa/Cairo');
      setGeminiApiKey(settings?.geminiApiKey || '');
      setAiTonePreference(settings?.aiTonePreference || 'العامية المصرية الروشة والصايعة');
      setAiTargetAudience(settings?.aiTargetAudience || 'طلاب كورس كول سنتر يبحثون عن عمل');
      setReplyDelay(settings?.replyDelay ?? 3);
      setMaxDailyMessages(settings?.maxDailyMessages ?? 500);
      setIsGroupAppointmentsEnabled(settings?.isGroupAppointmentsEnabled ?? false);
    } catch {
      setMessage({ type: 'error', text: 'تعذر تحميل إعدادات الرد الآلي.' });
    }
  }, [activeProject]);

  const fetchQR = useCallback(async () => {
    if (!activeProject) return;
    try {
      const response = await api.get<{ qr?: string; error?: string }>(
        `/api/whatsapp/session/qr?projectId=${activeProject.id}`,
        {
          validateStatus: (status) => status === 200 || status === 404
        }
      );
      if (response.status === 200 && response.data && response.data.qr) {
        setQrString(response.data.qr);
        setQrError(null);
      } else {
        setQrString(null);
        setQrError(response.data?.error || 'QR code is not ready yet. Retrying...');
      }
    } catch (e) {
      console.error('Failed to fetch QR code payload', e);
      setQrError('Unable to fetch QR code from WhatsApp gateway.');
    }
  }, [activeProject]);

  const fetchStatus = useCallback(async (showLoading = false) => {
    if (!activeProject) return;
    try {
      if (showLoading) setLoading(true);
      const response = await api.get<SessionStatusResponse>(`/api/whatsapp/session/status?projectId=${activeProject.id}`);
      setStatus(response.data.status);
      setPhoneNumber(response.data.phoneNumber);
      setQrError(response.data.error || null);
      
      // If it is initializing, fetch the QR code
      if (response.data.status === 'Initializing') {
        void fetchQR();
      } else {
        setQrString(null);
        setQrError(null);
      }
    } catch (e) {
      console.error('Failed to fetch WhatsApp session status', e);
    } finally {
      if (showLoading) setLoading(false);
    }
  }, [activeProject, fetchQR]);

  // Poll status every 5 seconds
  useEffect(() => {
    queueMicrotask(() => {
      void fetchStatus(true);
      void fetchProjectSettings();
    });
    
    const interval = setInterval(() => {
      void fetchStatus(false);
    }, 5000);

    return () => clearInterval(interval);
  }, [fetchStatus, fetchProjectSettings]);

  const handleStartSession = async () => {
    if (!activeProject) return;
    try {
      setActionLoading(true);
      setMessage(null);
      await api.post('/api/whatsapp/session/start', {
        projectId: activeProject.id
      });
      setStatus('Initializing');
      setQrError(null);
      // Fetch QR immediately
      setTimeout(() => void fetchQR(), 1000);
    } catch (e: unknown) {
      console.error('Failed to start WhatsApp session', e);
      setMessage({ type: 'error', text: getApiErrorMessage(e, 'تعذر بدء جلسة واتساب.') });
    } finally {
      setActionLoading(false);
    }
  };

  const handleMockConnect = async () => {
    if (!activeProject) return;
    try {
      setActionLoading(true);
      setMessage(null);
      await api.post('/api/whatsapp/session/mock', {
        projectId: activeProject.id,
        status: 'Connected',
        phoneNumber: '201099887766'
      });
      setStatus('Connected');
      setPhoneNumber('201099887766');
      setQrString(null);
      setQrError(null);
      setMessage({ type: 'success', text: 'تم توصيل واتساب التجريبي بنجاح.' });
    } catch (e: unknown) {
      console.error('Failed to mock connect', e);
      setMessage({ type: 'error', text: getApiErrorMessage(e, 'تعذر التوصيل التجريبي.') });
    } finally {
      setActionLoading(false);
    }
  };

  const handleDisconnect = async () => {
    if (!activeProject) return;
    try {
      setActionLoading(true);
      setMessage(null);
      await api.post('/api/whatsapp/session/disconnect', {
        projectId: activeProject.id
      });
      setStatus('Disconnected');
      setPhoneNumber(null);
      setQrString(null);
      setQrError(null);
      setMessage({ type: 'success', text: 'تم فصل جلسة واتساب. بيانات المحادثات محفوظة كما هي.' });
    } catch (e: unknown) {
      console.error('Failed to disconnect session', e);
      setMessage({ type: 'error', text: getApiErrorMessage(e, 'تعذر فصل الجلسة.') });
    } finally {
      setActionLoading(false);
    }
  };

  const handleSaveGeneralSettings = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!activeProject) return;
    try {
      await api.put(`/api/projects/${activeProject.id}/settings`, {
        projectName: projectName.trim(),
        aiAutoReplyEnabled: autoReplyEnabled,
        timezone,
        geminiApiKey: geminiApiKey.trim(),
        aiTonePreference: aiTonePreference.trim(),
        aiTargetAudience: aiTargetAudience.trim(),
        replyDelay,
        maxDailyMessages,
        isGroupAppointmentsEnabled,
      });
      setMessage({ type: 'success', text: 'تم حفظ إعدادات الرد الآلي بنجاح.' });
      void refreshProjects();
    } catch (e) {
      setMessage({ type: 'error', text: getApiErrorMessage(e, 'تعذر حفظ إعدادات الرد الآلي.') });
    }
  };

  const handleToggleGroupAppointments = async (enabled: boolean) => {
    if (!activeProject) return;
    try {
      await api.put(`/api/projects/${activeProject.id}/settings`, {
        projectName: projectName.trim(),
        aiAutoReplyEnabled: autoReplyEnabled,
        timezone,
        geminiApiKey: geminiApiKey.trim(),
        aiTonePreference: aiTonePreference.trim(),
        aiTargetAudience: aiTargetAudience.trim(),
        replyDelay,
        maxDailyMessages,
        isGroupAppointmentsEnabled: enabled
      });
      setIsGroupAppointmentsEnabled(enabled);
    } catch (e) {
      console.error(e);
      throw e;
    }
  };

  if (loading) {
    return (
      <div className={styles.qrLoading} style={{ padding: '5rem 0' }}>
        <div className={styles.spinner}></div>
        <p>جاري تحميل إعدادات المشروع...</p>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <h1 className={styles.pageTitle}>إعدادات المشروع</h1>
        <p className={styles.pageSubtitle}>إدارة اتصال واتساب ومفاتيح الذكاء الاصطناعي وتفضيلات الرد الآلي</p>
      </div>

      {/* Tabs / التبويبات */}
      <div style={{ display: 'flex', gap: 'var(--space-md)', borderBottom: '1px solid var(--border-subtle)', paddingBottom: 'var(--space-sm)' }}>
        <button 
          onClick={() => { setActiveTab('general'); setViewMode('list'); }}
          className={`${styles.btn} ${activeTab === 'general' ? styles.btnPrimary : styles.btnSecondary}`}
          style={{ padding: '6px 12px', fontSize: '0.85rem' }}
        >
          إعدادات المشروع
        </button>
        <button 
          onClick={() => { setActiveTab('addons'); }}
          className={`${styles.btn} ${activeTab === 'addons' ? styles.btnPrimary : styles.btnSecondary}`}
          style={{ padding: '6px 12px', fontSize: '0.85rem' }}
        >
          الاضافات (Add-ons)
        </button>
      </div>

      {message && (
        <div className={`glass-panel`} style={{ 
          padding: 'var(--space-md)', 
          border: `1px solid ${message.type === 'success' ? 'rgba(16, 185, 129, 0.2)' : 'rgba(239, 68, 68, 0.2)'}`,
          backgroundColor: message.type === 'success' ? 'rgba(16, 185, 129, 0.04)' : 'rgba(239, 68, 68, 0.04)',
          borderRadius: 'var(--radius-md)',
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--space-sm)'
        }}>
          {message.type === 'success' ? <CheckCircle size={18} style={{ color: 'hsl(var(--accent-success))' }} /> : <AlertCircle size={18} style={{ color: 'hsl(var(--accent-danger))' }} />}
          <span style={{ fontSize: '0.9rem', fontWeight: 600 }}>{message.text}</span>
        </div>
      )}

      {activeTab === 'general' ? (
        <div className={styles.grid}>
          {/* Left Side: WhatsApp Setup */}
          <div className={`glass-panel ${styles.card}`}>
            <h2 className={styles.cardTitle}>
              <Smartphone size={20} style={{ color: 'hsl(var(--accent-primary))' }} />
              لوحة اتصال واتساب
            </h2>

            <div className={styles.statusWrapper}>
              <span className={styles.statusLabel}>حالة الاتصال:</span>
              <div className={styles.statusIndicator}>
                <span className={`${styles.dot} ${
                  status === 'Connected' ? styles.dotConnected :
                  status === 'Initializing' ? styles.dotInitializing :
                  styles.dotDisconnected
                }`}></span>
                <span style={{
                  color: status === 'Connected' ? 'hsl(var(--accent-success))' :
                         status === 'Initializing' ? 'hsl(var(--accent-warning))' :
                         'hsl(var(--accent-danger))'
                }}>{status === 'Connected' ? 'متصل' : status === 'Initializing' ? 'جاري التجهيز' : 'غير متصل'}</span>
              </div>
            </div>

            {status === 'Disconnected' && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
                <p style={{ fontSize: '0.9rem', color: 'hsl(var(--text-secondary))', lineHeight: '1.5' }}>
                  رقم واتساب غير مربوط بهذا المشروع. اربطه حتى يتمكن البوت من الرد على العملاء ومزامنة الرسائل والوسائط.
                </p>
                <div className={styles.actions}>
                  <button 
                    onClick={handleStartSession} 
                    disabled={actionLoading}
                    className={`${styles.btn} ${styles.btnPrimary}`}
                  >
                    <QrCode size={18} />
                    {actionLoading ? 'جاري التجهيز...' : 'ربط واتساب'}
                  </button>

                  <button 
                    onClick={handleMockConnect} 
                    disabled={actionLoading}
                    className={`${styles.btn} ${styles.btnSecondary}`}
                    style={{ gap: '4px' }}
                  >
                    <Zap size={14} style={{ color: 'hsl(var(--accent-warning))' }} />
                    توصيل تجريبي
                  </button>
                </div>
              </div>
            )}

            {status === 'Initializing' && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
                <div className={styles.qrContainer}>
                  {qrString ? (
                    <>
                      <div className={styles.qrWrapper}>
                        <img 
                          src={`https://api.qrserver.com/v1/create-qr-code/?size=250x250&data=${encodeURIComponent(qrString)}`} 
                          alt="كود ربط واتساب" 
                          className={styles.qrImage}
                        />
                      </div>
                      <p className={styles.qrInstructions}>
                        افتح واتساب من الموبايل، ادخل على الأجهزة المرتبطة، ثم امسح الكود لتوصيل الرقم.
                      </p>
                    </>
                  ) : (
                    <div className={styles.qrLoading}>
                      <div className={styles.spinner}></div>
                      <p style={{ fontSize: '0.85rem' }}>جاري إنشاء كود الربط الآمن...</p>
                      {qrError && (
                        <p style={{ fontSize: '0.8rem', color: 'hsl(var(--accent-warning))', maxWidth: '18rem' }}>
                          {qrError}
                        </p>
                      )}
                    </div>
                  )}
                </div>

                <div className={styles.actions}>
                  <button 
                    onClick={() => void fetchQR()}
                    disabled={actionLoading}
                    className={`${styles.btn} ${styles.btnSecondary}`}
                  >
                    <RefreshCw size={16} />
                    تحديث الكود
                  </button>

                  <button 
                    onClick={handleMockConnect} 
                    disabled={actionLoading}
                    className={`${styles.btn} ${styles.btnSecondary}`}
                  >
                    <Zap size={14} style={{ color: 'hsl(var(--accent-warning))' }} />
                    توصيل تجريبي
                  </button>

                  <button 
                    onClick={handleDisconnect} 
                    disabled={actionLoading}
                    className={`${styles.btn} ${styles.btnDanger}`}
                  >
                    إلغاء
                  </button>
                </div>
              </div>
            )}

            {status === 'Connected' && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
                <ul className={styles.detailsList}>
                  <li className={styles.detailsItem}>
                    <span>الرقم المتصل:</span>
                    <span className={styles.detailsVal}>{phoneNumber ? `+${phoneNumber}` : 'غير معروف'}</span>
                  </li>
                  <li className={styles.detailsItem}>
                    <span>بوابة واتساب:</span>
                    <span className={styles.detailsVal}>docker-gateway-container</span>
                  </li>
                  <li className={styles.detailsItem}>
                    <span>مفتاح الجلسة:</span>
                    <span className={styles.detailsVal} style={{ fontFamily: 'monospace', fontSize: '0.75rem' }}>
                      {activeProject?.id?.substring(0, 8)}...session
                    </span>
                  </li>
                </ul>

                <div className={styles.actions}>
                  <button 
                    onClick={handleDisconnect} 
                    disabled={actionLoading}
                    className={`${styles.btn} ${styles.btnDanger}`}
                  >
                    <LogOut size={16} />
                    {actionLoading ? 'جاري الفصل...' : 'فصل واتساب'}
                  </button>
                </div>
              </div>
            )}
          </div>

          {/* Right Side: General Preferences */}
          <div className={`glass-panel ${styles.card}`}>
            <h2 className={styles.cardTitle}>
              <SettingsIcon size={20} style={{ color: 'hsl(var(--accent-secondary))' }} />
              إعدادات الرد الآلي
            </h2>

            <form onSubmit={handleSaveGeneralSettings} className={styles.form}>
              <div className={styles.formGroup}>
                <label className={styles.label}>اسم المشروع</label>
                <input
                  type="text"
                  value={projectName}
                  onChange={(e) => setProjectName(e.target.value)}
                  placeholder="اكتب اسم المشروع هنا"
                  className={styles.input}
                  required
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>نموذج الذكاء الاصطناعي</label>
                <select className={styles.select} defaultValue="gemini-3.5-flash" disabled>
                  <option value="gemini-3.5-flash">Gemini 3.5 Flash (المحرك الموحد)</option>
                </select>
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>مفتاح API للردود الذكية</label>
                <input
                  type="password"
                  autoComplete="off"
                  placeholder="ضع مفتاح Gemini API هنا"
                  value={geminiApiKey}
                  onChange={(e) => setGeminiApiKey(e.target.value)}
                  className={styles.input}
                />
                <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))' }}>
                  يتم استخدام المفتاح للمشروع الحالي فقط حتى يبدأ البوت في الرد التلقائي.
                </span>
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>المنطقة الزمنية</label>
                <input
                  type="text"
                  value={timezone}
                  onChange={(e) => setTimezone(e.target.value)}
                  className={styles.input}
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>لهجة وأسلوب الرد</label>
                <select 
                  className={styles.select}
                  value={
                    ['العامية المصرية الروشة والصايعة', 'العامية المصرية المهذبة والمحترمة', 'العربية الفصحى المبسطة والودودة', 'اللهجة الخليجية المهذبة'].includes(aiTonePreference)
                      ? aiTonePreference
                      : 'custom'
                  }
                  onChange={(e) => {
                    const val = e.target.value;
                    if (val !== 'custom') {
                      setAiTonePreference(val);
                    } else {
                      setAiTonePreference('');
                    }
                  }}
                >
                  <option value="العامية المصرية الروشة والصايعة">عامية مصرية روشة وصايعة (سيلز شاطر وصاحب العميل)</option>
                  <option value="العامية المصرية المهذبة والمحترمة">عامية مصرية مهذبة ومحترمة (دعم فني وخدمة عملاء)</option>
                  <option value="العربية الفصحى المبسطة والودودة">عربية فصحى مبسطة وودودة (أسلوب رسمي عام)</option>
                  <option value="اللهجة الخليجية المهذبة">لهجة خليجية مهذبة ومحترمة (لبق وراقي)</option>
                  <option value="custom">لهجة مخصصة...</option>
                </select>
                
                {!['العامية المصرية الروشة والصايعة', 'العامية المصرية المهذبة والمحترمة', 'العربية الفصحى المبسطة والودودة', 'اللهجة الخليجية المهذبة'].includes(aiTonePreference) && (
                  <input
                    type="text"
                    placeholder="اكتب اللهجة والأسلوب المطلوب بالتفصيل..."
                    value={aiTonePreference}
                    onChange={(e) => setAiTonePreference(e.target.value)}
                    className={styles.input}
                    style={{ marginTop: 'var(--space-xs)' }}
                  />
                )}
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>من يكلم الذكاء الاصطناعي؟ (الفئة المستهدفة)</label>
                <input
                  type="text"
                  placeholder="مثال: طلاب كورس كول سنتر يبحثون عن عمل"
                  value={aiTargetAudience}
                  onChange={(e) => setAiTargetAudience(e.target.value)}
                  className={styles.input}
                />
                <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))' }}>
                  تساعد البوت على تخصيص الحجج البيعية ونبرة الاهتمام المناسبة للعميل.
                </span>
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>تأخير الرد (بالثواني)</label>
                <input 
                  type="number" 
                  min={0}
                  max={60}
                  value={replyDelay}
                  onChange={(e) => setReplyDelay(Number(e.target.value))}
                  className={styles.input} 
                />
                <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))' }}>
                  يساعد على جعل الرد طبيعي وتقليل مخاطر حظر الرقم.
                </span>
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>الحد اليومي للرسائل الصادرة</label>
                <input 
                  type="number" 
                  min={10}
                  value={maxDailyMessages}
                  onChange={(e) => setMaxDailyMessages(Number(e.target.value))}
                  className={styles.input} 
                />
              </div>

              <div className={styles.formGroup} style={{ marginTop: 'var(--space-xs)' }}>
                <label className={styles.checkboxGroup}>
                  <input 
                    type="checkbox" 
                    checked={autoReplyEnabled}
                    onChange={(e) => setAutoReplyEnabled(e.target.checked)}
                    className={styles.checkbox} 
                  />
                  <span className={styles.label} style={{ userSelect: 'none' }}>تفعيل الرد الآلي بالذكاء الاصطناعي</span>
                </label>
              </div>

              <button type="submit" className={`${styles.btn} ${styles.btnPrimary}`} style={{ marginTop: 'var(--space-sm)' }}>
                حفظ الإعدادات
              </button>
            </form>
          </div>
        </div>
      ) : (
        <div style={{ width: '100%' }}>
          {viewMode === 'manage-groups' ? (
            <GroupAppointmentsManager onBack={() => setViewMode('list')} />
          ) : (
            <Addons 
              isGroupAppointmentsEnabled={isGroupAppointmentsEnabled} 
              onToggleGroupAppointments={handleToggleGroupAppointments} 
              onManageGroups={() => setViewMode('manage-groups')} 
            />
          )}
        </div>
      )}
    </div>
  );
}
