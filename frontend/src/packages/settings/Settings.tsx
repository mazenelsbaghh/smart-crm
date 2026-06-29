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
  Zap,
  PlusCircle
} from 'lucide-react';

const FacebookIcon = ({ size = 20 }: { size?: number }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="#1877F2">
    <path d="M24 12.073c0-6.627-5.373-12-12-12s-12 5.373-12 12c0 5.99 4.388 10.954 10.125 11.854v-8.385H7.078v-3.47h3.047V9.43c0-3.007 1.792-4.669 4.533-4.669 1.312 0 2.686.235 2.686.235v2.953H15.83c-1.491 0-1.956.925-1.956 1.874v2.25h3.328l-.532 3.47h-2.796v8.385C19.612 23.027 24 18.062 24 12.073z"/>
  </svg>
);
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
    geminiModel?: string;
    aiTonePreference?: string;
    aiTargetAudience?: string;
    replyDelay?: number;
    maxDailyMessages?: number;
    isGroupAppointmentsEnabled?: boolean;
    isWhatsAppGroupAutomationEnabled?: boolean;
    groupAutomationManagerPhone?: string;
    messengerAiAutoReplyEnabled?: boolean;
    messengerReplyDelay?: number;
    commentsAiAutoReplyEnabled?: boolean;
    commentsReplyDelay?: number;
    systemPrompt?: string;
    aiBehavior?: AIBehaviorSettings;
  } | null;
}

type ChannelName = 'WhatsApp' | 'Messenger' | 'FacebookComment';

interface AIBehaviorSettings {
  identity: {
    agentNames: string[];
    nameSelectionMode: string;
    signatureEnabled: boolean;
    signatureTemplate: string;
    complaintSignatureTemplate: string;
  };
  tone: {
    tonePreset: string;
    customTone?: string | null;
    targetAudience: string;
    allowedPhrases: string[];
    prohibitedPhrases: string[];
    businessInstructions?: string | null;
  };
  reactions: {
    enabled: boolean;
    allowedReactions: string[];
    useAiSuggestedReaction: boolean;
    rules?: string | null;
  };
  fallbacks: {
    aiError: string;
    invalidAiOutput: string;
    genericCustomerService: string;
    facebookPublicComment: string;
    whatsAppTransitionSuccess: string;
    whatsAppTransitionFailure: string;
    whatsAppTransitionMessage: string;
    followUpDefault: string;
  };
  channels: Partial<Record<ChannelName, {
    additionalInstructions?: string | null;
  }>>;
  advancedInstructions?: string | null;
}

const getApiErrorMessage = (error: unknown, fallback: string) => {
  const axiosError = error as AxiosError<ApiErrorResponse>;
  return axiosError.response?.data?.error || fallback;
};

const defaultAiBehavior = (): AIBehaviorSettings => ({
  identity: {
    agentNames: ['ساجي', 'لارا', 'مادلين', 'شاهي', 'ساندي'],
    nameSelectionMode: 'HourlyRotation',
    signatureEnabled: true,
    signatureTemplate: '- {agentName} ✨',
    complaintSignatureTemplate: '- {agentName}',
  },
  tone: {
    tonePreset: 'egyptian-slang-sales',
    customTone: 'العامية المصرية الروشة والصايعة',
    targetAudience: 'طلاب كورس كول سنتر يبحثون عن عمل',
    allowedPhrases: [],
    prohibitedPhrases: [],
    businessInstructions: '',
  },
  reactions: {
    enabled: true,
    allowedReactions: ['👍', '❤️', '💖', '😢', '😂', '😮'],
    useAiSuggestedReaction: true,
    rules: '',
  },
  fallbacks: {
    aiError: 'أهلاً بك! سنقوم بالرد عليك في أقرب وقت ممكن.',
    invalidAiOutput: 'أهلاً بك! سنقوم بالرد عليك في أقرب وقت ممكن.',
    genericCustomerService: 'أهلاً بك! سنقوم بالرد عليك في أقرب وقت ممكن.',
    facebookPublicComment: 'تم الرد في الخاص يا فندم! ❤️',
    whatsAppTransitionSuccess: 'أنا بعتلك رسالة على الواتساب، خلينا نتواصل هناك. ✨',
    whatsAppTransitionFailure: 'حاولنا نبعتلك على الواتساب بس غالباً الرقم غلط أو مش عليه واتساب. يا ريت تبعتلي الرقم الصح هنا عشان نتواصل هناك.',
    whatsAppTransitionMessage: 'أهلاً يا {customerName}، منورنا يا فندم! 😊 معاك {agentName}.. زي ما اتفقنا على ماسنجر، هنكمل كلامنا هنا على واتساب.',
    followUpDefault: 'مرحباً يا فندم، حابين نطمن على تفاصيل الحجز ونعرف لو في أي استفسار آخر؟',
  },
  channels: {
    WhatsApp: { additionalInstructions: '' },
    Messenger: { additionalInstructions: '' },
    FacebookComment: { additionalInstructions: '' },
  },
  advancedInstructions: '',
});

const linesToArray = (value: string) => value.split('\n').map(item => item.trim()).filter(Boolean);
const arrayToLines = (value?: string[]) => (value || []).join('\n');

export default function Settings() {
  const { activeProject, refreshProjects, switchProject } = useAuth();
  
  const [status, setStatus] = useState<'Disconnected' | 'Initializing' | 'Connected'>('Disconnected');
  const [phoneNumber, setPhoneNumber] = useState<string | null>(null);
  const [qrString, setQrString] = useState<string | null>(null);
  const [qrError, setQrError] = useState<string | null>(null);
  
  const [loading, setLoading] = useState(true);
  const [actionLoading, setActionLoading] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error', text: string } | null>(null);

  // General settings state
  const [projectName, setProjectName] = useState('');
  const [geminiApiKey, setGeminiApiKey] = useState('');
  const [geminiModel, setGeminiModel] = useState('gemini-3.5-flash');
  const [timezone, setTimezone] = useState('Africa/Cairo');
  const [aiTonePreference, setAiTonePreference] = useState('العامية المصرية الروشة والصايعة');
  const [aiTargetAudience, setAiTargetAudience] = useState('طلاب كورس كول سنتر يبحثون عن عمل');
  const [replyDelay, setReplyDelay] = useState(3);
  const [maxDailyMessages, setMaxDailyMessages] = useState(500);
  const [isGroupAppointmentsEnabled, setIsGroupAppointmentsEnabled] = useState(false);
  const [isWhatsAppGroupAutomationEnabled, setIsWhatsAppGroupAutomationEnabled] = useState(false);
  const [groupAutomationManagerPhone, setGroupAutomationManagerPhone] = useState('+201068690092');
  const [autoReplyEnabled, setAutoReplyEnabled] = useState(false);
  const [messengerAutoReplyEnabled, setMessengerAutoReplyEnabled] = useState(false);
  const [messengerReplyDelay, setMessengerReplyDelay] = useState(3);
  const [commentsAutoReplyEnabled, setCommentsAutoReplyEnabled] = useState(false);
  const [commentsReplyDelay, setCommentsReplyDelay] = useState(3);
  const [systemPrompt, setSystemPrompt] = useState('');
  const [aiBehavior, setAiBehavior] = useState<AIBehaviorSettings>(() => defaultAiBehavior());
  const [newProjectName, setNewProjectName] = useState('');

  // Facebook Pages state
  const [connectedPages, setConnectedPages] = useState<Array<{ id: string; pageId: string; pageName: string; connectedAt: string }>>([]);
  const [fbLoading, setFbLoading] = useState(false);
  const [pagesToConnect, setPagesToConnect] = useState<Array<{ id: string; name: string; access_token: string }>>([]);
  const [showPagesModal, setShowPagesModal] = useState(false);
  const [userAccessToken, setUserAccessToken] = useState('');

  // Tabs / Navigation state
  const [activeTab, setActiveTab] = useState<'general' | 'addons'>('general');
  const [viewMode, setViewMode] = useState<'list' | 'manage-groups'>('list');

  const updateAiBehavior = <K extends keyof AIBehaviorSettings>(section: K, value: AIBehaviorSettings[K]) => {
    setAiBehavior(prev => ({ ...prev, [section]: value }));
  };

  const updateChannelInstructions = (channel: ChannelName, value: string) => {
    setAiBehavior(prev => ({
      ...prev,
      channels: {
        ...prev.channels,
        [channel]: {
          ...(prev.channels[channel] || {}),
          additionalInstructions: value,
        },
      },
    }));
  };

  // Listen for message events from the popup
  useEffect(() => {
    const handleOAuthMessage = (event: MessageEvent) => {
      if (!event.data) return;

      if (event.data.type === 'facebook-oauth-success') {
        const { projectId, userAccessToken: uToken, pages } = event.data;
        if (activeProject && projectId === activeProject.id) {
          const mapped = (pages || []).map((p: any) => ({
            id: p.pageId,
            name: p.pageName,
            access_token: p.accessToken
          }));
          setPagesToConnect(mapped);
          setUserAccessToken(uToken);
          setShowPagesModal(true);
          setFbLoading(false);
          setMessage({ type: 'success', text: 'تم تسجيل الدخول بنجاح. الرجاء تحديد الصفحة لربطها.' });
        }
      } else if (event.data.type === 'facebook-oauth-error') {
        setFbLoading(false);
        setMessage({ type: 'error', text: event.data.error || 'حدث خطأ أثناء الاتصال بفيسبوك.' });
      }
    };

    window.addEventListener('message', handleOAuthMessage);
    return () => {
      window.removeEventListener('message', handleOAuthMessage);
    };
  }, [activeProject]);

  const fetchProjectSettings = useCallback(async () => {
    if (!activeProject) return;
    try {
      const response = await api.get<ProjectSettingsResponse>(`/api/projects/${activeProject.id}`);
      setProjectName(response.data.name || '');
      const settings = response.data.settings;
      setTimezone(settings?.timezone || 'Africa/Cairo');
      setGeminiApiKey(settings?.geminiApiKey || '');
      setGeminiModel(settings?.geminiModel || 'gemini-3.5-flash');
      setAiTonePreference(settings?.aiTonePreference || 'العامية المصرية الروشة والصايعة');
      setAiTargetAudience(settings?.aiTargetAudience || 'طلاب كورس كول سنتر يبحثون عن عمل');
      setReplyDelay(settings?.replyDelay ?? 3);
      setMaxDailyMessages(settings?.maxDailyMessages ?? 500);
      setIsGroupAppointmentsEnabled(settings?.isGroupAppointmentsEnabled ?? false);
      setIsWhatsAppGroupAutomationEnabled(settings?.isWhatsAppGroupAutomationEnabled ?? false);
      setGroupAutomationManagerPhone(settings?.groupAutomationManagerPhone ?? '+201068690092');
      setAutoReplyEnabled(settings?.aiAutoReplyEnabled ?? false);
      setMessengerAutoReplyEnabled(settings?.messengerAiAutoReplyEnabled ?? false);
      setMessengerReplyDelay(settings?.messengerReplyDelay ?? 3);
      setCommentsAutoReplyEnabled(settings?.commentsAiAutoReplyEnabled ?? false);
      setCommentsReplyDelay(settings?.commentsReplyDelay ?? 3);
      setSystemPrompt(settings?.systemPrompt || '');
      setAiBehavior(settings?.aiBehavior || defaultAiBehavior());
    } catch {
      setMessage({ type: 'error', text: 'تعذر تحميل إعدادات الرد الآلي.' });
    }
  }, [activeProject]);

  const fetchConnectedPages = useCallback(async () => {
    if (!activeProject) return;
    try {
      const res = await api.get<Array<{ id: string; facebookPageId: string; pageName: string; isActive: boolean; createdAt: string }>>(
        `/api/projects/${activeProject.id}/facebook/pages`
      );
      const mapped = (res.data || []).map(p => ({
        id: p.id,
        pageId: p.facebookPageId,
        pageName: p.pageName,
        connectedAt: p.createdAt
      }));
      setConnectedPages(mapped);
    } catch {
      // Silently handle - no pages connected yet
      setConnectedPages([]);
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
      void fetchConnectedPages();
    });
    
    const interval = setInterval(() => {
      void fetchStatus(false);
    }, 5000);

    return () => clearInterval(interval);
  }, [fetchStatus, fetchProjectSettings, fetchConnectedPages]);

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

  const handleConnectFacebook = () => {
    if (!activeProject) return;

    setFbLoading(true);
    setMessage(null);

    let baseUrl = api.defaults.baseURL || '';
    if (!baseUrl.startsWith('http')) {
      baseUrl = window.location.origin + baseUrl;
    }
    const cleanedBaseUrl = baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl;
    const oauthUrl = `${cleanedBaseUrl}/api/facebook/oauth/login?projectId=${activeProject.id}`;

    const width = 600;
    const height = 700;
    const left = window.screen.width / 2 - width / 2;
    const top = window.screen.height / 2 - height / 2;

    const popup = window.open(
      oauthUrl,
      'facebook-oauth-login',
      `width=${width},height=${height},top=${top},left=${left},scrollbars=yes,status=yes`
    );

    if (!popup) {
      setFbLoading(false);
      setMessage({ type: 'error', text: 'تم حظر النافذة المنبثقة. يرجى تفعيل النوافذ المنبثقة في متصفحك والمحاولة مرة أخرى.' });
    }
  };

  const handleConfirmPage = async (page: { id: string; name: string; access_token: string }) => {
    if (!activeProject) return;
    setActionLoading(true);
    try {
      await api.post(`/api/projects/${activeProject.id}/facebook/pages/confirm`, {
        facebookPageId: page.id,
        pageName: page.name,
        pageAccessToken: page.access_token,
        userAccessToken: userAccessToken,
        facebookUserId: ''
      });
      setMessage({ type: 'success', text: `تم ربط الصفحة بنجاح: ${page.name}` });
      setShowPagesModal(false);
      void fetchConnectedPages();
    } catch (e: unknown) {
      console.error(e);
      setMessage({ type: 'error', text: getApiErrorMessage(e, 'تعذر ربط هذه الصفحة.') });
    } finally {
      setActionLoading(false);
    }
  };

  const handleDisconnectPage = async (pageDbId: string) => {
    if (!activeProject) return;
    try {
      await api.delete(`/api/projects/${activeProject.id}/facebook/pages/${pageDbId}`);
      setConnectedPages(prev => prev.filter(p => p.id !== pageDbId));
      setMessage({ type: 'success', text: 'تم فصل الصفحة بنجاح.' });
    } catch {
      setMessage({ type: 'error', text: 'تعذر فصل الصفحة.' });
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
        geminiModel,
        aiTonePreference: aiTonePreference.trim(),
        aiTargetAudience: aiTargetAudience.trim(),
        replyDelay,
        maxDailyMessages,
        isGroupAppointmentsEnabled,
        isWhatsAppGroupAutomationEnabled,
        groupAutomationManagerPhone: groupAutomationManagerPhone.trim(),
        messengerAiAutoReplyEnabled: messengerAutoReplyEnabled,
        messengerReplyDelay,
        commentsAiAutoReplyEnabled: commentsAutoReplyEnabled,
        commentsReplyDelay,
        systemPrompt: systemPrompt.trim(),
        aiBehavior,
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
        geminiModel,
        aiTonePreference: aiTonePreference.trim(),
        aiTargetAudience: aiTargetAudience.trim(),
        replyDelay,
        maxDailyMessages,
        isGroupAppointmentsEnabled: enabled,
        messengerAiAutoReplyEnabled: messengerAutoReplyEnabled,
        messengerReplyDelay,
        commentsAiAutoReplyEnabled: commentsAutoReplyEnabled,
        commentsReplyDelay,
        systemPrompt: systemPrompt.trim(),
        aiBehavior,
      });
      setIsGroupAppointmentsEnabled(enabled);
    } catch (e) {
      console.error(e);
      throw e;
    }
  };

  const handleToggleWhatsAppGroupAutomation = async (enabled: boolean) => {
    if (!activeProject) return;
    try {
      await api.put(`/api/projects/${activeProject.id}/settings`, {
        projectName: projectName.trim(),
        aiAutoReplyEnabled: autoReplyEnabled,
        timezone,
        geminiApiKey: geminiApiKey.trim(),
        geminiModel,
        aiTonePreference: aiTonePreference.trim(),
        aiTargetAudience: aiTargetAudience.trim(),
        replyDelay,
        maxDailyMessages,
        isGroupAppointmentsEnabled,
        isWhatsAppGroupAutomationEnabled: enabled,
        groupAutomationManagerPhone: groupAutomationManagerPhone.trim(),
        messengerAiAutoReplyEnabled: messengerAutoReplyEnabled,
        messengerReplyDelay,
        commentsAiAutoReplyEnabled: commentsAutoReplyEnabled,
        commentsReplyDelay,
        systemPrompt: systemPrompt.trim(),
        aiBehavior,
      });
      setIsWhatsAppGroupAutomationEnabled(enabled);
    } catch (e) {
      console.error(e);
      throw e;
    }
  };

  const handleUpdateGroupAutomationManagerPhone = async (phone: string) => {
    if (!activeProject) return;
    try {
      await api.put(`/api/projects/${activeProject.id}/settings`, {
        projectName: projectName.trim(),
        aiAutoReplyEnabled: autoReplyEnabled,
        timezone,
        geminiApiKey: geminiApiKey.trim(),
        geminiModel,
        aiTonePreference: aiTonePreference.trim(),
        aiTargetAudience: aiTargetAudience.trim(),
        replyDelay,
        maxDailyMessages,
        isGroupAppointmentsEnabled,
        isWhatsAppGroupAutomationEnabled,
        groupAutomationManagerPhone: phone.trim(),
        messengerAiAutoReplyEnabled: messengerAutoReplyEnabled,
        messengerReplyDelay,
        commentsAiAutoReplyEnabled: commentsAutoReplyEnabled,
        commentsReplyDelay,
        systemPrompt: systemPrompt.trim(),
        aiBehavior,
      });
      setGroupAutomationManagerPhone(phone);
    } catch (e) {
      console.error(e);
      throw e;
    }
  };

  const handleCreateNewProject = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newProjectName.trim()) return;
    setActionLoading(true);
    setMessage(null);
    try {
      const response = await api.post<{ id: string; name: string }>('/api/projects', {
        name: newProjectName.trim()
      });
      setMessage({ type: 'success', text: `تم إنشاء المشروع "${newProjectName.trim()}" بنجاح وجاري الانتقال إليه.` });
      setNewProjectName('');
      await refreshProjects();
      if (response.data && response.data.id) {
        switchProject(response.data.id);
      }
    } catch (err: unknown) {
      console.error('Failed to create new project', err);
      setMessage({ type: 'error', text: getApiErrorMessage(err, 'تعذر إنشاء المشروع الجديد.') });
    } finally {
      setActionLoading(false);
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

          {/* Facebook Page Connection Card */}
          <div className={`glass-panel ${styles.card}`}>
            <h2 className={styles.cardTitle}>
              <FacebookIcon size={20} />
              ربط صفحة فيسبوك
            </h2>

            {connectedPages.length > 0 ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-xs)' }}>
                  <CheckCircle size={16} style={{ color: 'hsl(var(--accent-success))' }} />
                  <span style={{ fontSize: '0.85rem', fontWeight: 700, color: 'hsl(var(--accent-success))' }}>
                    {connectedPages.length} صفحة مربوطة
                  </span>
                </div>

                {connectedPages.map(page => (
                  <div key={page.pageId} style={{
                    display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                    padding: '10px 12px', borderRadius: 'var(--radius-md)',
                    background: 'var(--surface-muted)', border: '1px solid var(--border-subtle)'
                  }}>
                    <div>
                      <div style={{ fontWeight: 700, fontSize: '0.9rem', color: 'var(--text-strong)' }}>{page.pageName}</div>
                      <div style={{ fontSize: '0.75rem', color: 'var(--text-soft)' }}>ID: {page.pageId}</div>
                    </div>
                    <button
                      onClick={() => void handleDisconnectPage(page.id)}
                      className={`${styles.btn} ${styles.btnDanger}`}
                      style={{ padding: '4px 10px', fontSize: '0.78rem' }}
                    >
                      <LogOut size={14} />
                      فصل
                    </button>
                  </div>
                ))}

                <button
                  onClick={handleConnectFacebook}
                  disabled={fbLoading}
                  className={`${styles.btn} ${styles.btnSecondary}`}
                  style={{ gap: '6px' }}
                >
                  <FacebookIcon size={16} />
                  {fbLoading ? 'جاري الربط...' : 'ربط صفحة أخرى'}
                </button>
              </div>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
                <p style={{ fontSize: '0.9rem', color: 'hsl(var(--text-secondary))', lineHeight: '1.5' }}>
                  اربط صفحة فيسبوك عشان تقدر ترد على رسائل الماسنجر والتعليقات تلقائياً.
                </p>
                <button
                  onClick={handleConnectFacebook}
                  disabled={fbLoading}
                  className={`${styles.btn} ${styles.btnPrimary}`}
                  style={{ gap: '6px', backgroundColor: '#1877F2' }}
                >
                  <FacebookIcon size={18} />
                  {fbLoading ? 'جاري فتح نافذة فيسبوك...' : 'ربط صفحة فيسبوك'}
                </button>
              </div>
            )}
          </div>

          {/* Create New Project Card */}
          <div className={`glass-panel ${styles.card}`}>
            <h2 className={styles.cardTitle}>
              <PlusCircle size={20} style={{ color: 'hsl(var(--accent-success))' }} />
              إنشاء مشروع جديد
            </h2>
            <p style={{ fontSize: '0.9rem', color: 'hsl(var(--text-secondary))', lineHeight: '1.5', marginBottom: 'var(--space-md)' }}>
              أنشئ مشروعاً مستقلاً تماماً لإدارة رقم واتساب آخر، قاعدة معرفية جديدة، وإعدادات ذكاء اصطناعي منفصلة.
            </p>
            <form onSubmit={handleCreateNewProject} className={styles.form}>
              <div className={styles.formGroup}>
                <input
                  type="text"
                  placeholder="اسم المشروع الجديد..."
                  value={newProjectName}
                  onChange={(e) => setNewProjectName(e.target.value)}
                  className={styles.input}
                  required
                />
              </div>
              <button
                type="submit"
                disabled={actionLoading || !newProjectName.trim()}
                className={`${styles.btn} ${styles.btnPrimary}`}
                style={{ alignSelf: 'flex-start' }}
              >
                إنشاء مشروع جديد
              </button>
            </form>
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
                <select
                  className={styles.select}
                  value={geminiModel}
                  onChange={(e) => setGeminiModel(e.target.value)}
                >
                  <option value="gemini-3.5-flash">Gemini 3.5 Flash (المحرك الموحد)</option>
                  <option value="gemini-3.1-flash-lite">Gemini 3.1 Flash-Lite (أرخص من 3.5)</option>
                  <option value="gemini-2.5-flash-lite">Gemini 2.5 Flash-Lite (الأوفر)</option>
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

              <div className={styles.settingsSection}>
                <h3 className={styles.sectionTitle}>الهوية والتوقيع</h3>
                <div className={styles.formGroup}>
                  <label className={styles.label}>أسماء الموظفين</label>
                  <textarea
                    value={arrayToLines(aiBehavior.identity.agentNames)}
                    onChange={(e) => updateAiBehavior('identity', { ...aiBehavior.identity, agentNames: linesToArray(e.target.value) })}
                    className={styles.input}
                    rows={4}
                    placeholder="اسم كل موظف في سطر"
                  />
                </div>
                <div className={styles.inlineGrid}>
                  <div className={styles.formGroup}>
                    <label className={styles.label}>طريقة اختيار الاسم</label>
                    <select
                      className={styles.select}
                      value={aiBehavior.identity.nameSelectionMode}
                      onChange={(e) => updateAiBehavior('identity', { ...aiBehavior.identity, nameSelectionMode: e.target.value })}
                    >
                      <option value="HourlyRotation">تدوير حسب الساعة</option>
                      <option value="First">أول اسم دائماً</option>
                    </select>
                  </div>
                  <div className={styles.formGroup}>
                    <label className={styles.checkboxGroup}>
                      <input
                        type="checkbox"
                        checked={aiBehavior.identity.signatureEnabled}
                        onChange={(e) => updateAiBehavior('identity', { ...aiBehavior.identity, signatureEnabled: e.target.checked })}
                        className={styles.checkbox}
                      />
                      <span className={styles.label} style={{ userSelect: 'none' }}>إضافة توقيع في الرد</span>
                    </label>
                  </div>
                </div>
                <div className={styles.inlineGrid}>
                  <div className={styles.formGroup}>
                    <label className={styles.label}>قالب التوقيع</label>
                    <input
                      value={aiBehavior.identity.signatureTemplate}
                      onChange={(e) => updateAiBehavior('identity', { ...aiBehavior.identity, signatureTemplate: e.target.value })}
                      className={styles.input}
                      placeholder="- {agentName}"
                    />
                  </div>
                  <div className={styles.formGroup}>
                    <label className={styles.label}>توقيع الشكاوى</label>
                    <input
                      value={aiBehavior.identity.complaintSignatureTemplate}
                      onChange={(e) => updateAiBehavior('identity', { ...aiBehavior.identity, complaintSignatureTemplate: e.target.value })}
                      className={styles.input}
                      placeholder="- {agentName}"
                    />
                  </div>
                </div>
              </div>

              <div className={styles.settingsSection}>
                <h3 className={styles.sectionTitle}>النبرة والجمهور</h3>
                <div className={styles.formGroup}>
                  <label className={styles.label}>لهجة وأسلوب الرد</label>
                  <select
                    className={styles.select}
                    value={aiBehavior.tone.tonePreset}
                    onChange={(e) => {
                      const preset = e.target.value;
                      const presetTone = preset === 'custom' ? '' :
                        preset === 'egyptian-polite' ? 'العامية المصرية المهذبة والمحترمة' :
                        preset === 'msa-friendly' ? 'العربية الفصحى المبسطة والودودة' :
                        preset === 'gulf-polite' ? 'اللهجة الخليجية المهذبة' :
                        'العامية المصرية الروشة والصايعة';
                      setAiTonePreference(presetTone);
                      updateAiBehavior('tone', { ...aiBehavior.tone, tonePreset: preset, customTone: presetTone });
                    }}
                  >
                    <option value="egyptian-slang-sales">عامية مصرية روشة وصايعة</option>
                    <option value="egyptian-polite">عامية مصرية مهذبة</option>
                    <option value="msa-friendly">عربية فصحى مبسطة</option>
                    <option value="gulf-polite">لهجة خليجية مهذبة</option>
                    <option value="custom">نبرة مخصصة</option>
                  </select>
                  <input
                    type="text"
                    placeholder="اكتب النبرة المخصصة أو عدل وصف النبرة المختارة"
                    value={aiBehavior.tone.customTone || ''}
                    onChange={(e) => {
                      setAiTonePreference(e.target.value);
                      updateAiBehavior('tone', { ...aiBehavior.tone, customTone: e.target.value });
                    }}
                    className={styles.input}
                  />
                </div>

                <div className={styles.formGroup}>
                  <label className={styles.label}>الجمهور المستهدف</label>
                  <input
                    type="text"
                    placeholder="مثال: طلاب كورس كول سنتر يبحثون عن عمل"
                    value={aiBehavior.tone.targetAudience}
                    onChange={(e) => {
                      setAiTargetAudience(e.target.value);
                      updateAiBehavior('tone', { ...aiBehavior.tone, targetAudience: e.target.value });
                    }}
                    className={styles.input}
                  />
                </div>

                <div className={styles.inlineGrid}>
                  <div className={styles.formGroup}>
                    <label className={styles.label}>عبارات مسموحة أو مفضلة</label>
                    <textarea
                      value={arrayToLines(aiBehavior.tone.allowedPhrases)}
                      onChange={(e) => updateAiBehavior('tone', { ...aiBehavior.tone, allowedPhrases: linesToArray(e.target.value) })}
                      className={styles.input}
                      rows={4}
                      placeholder="عبارة في كل سطر"
                    />
                  </div>
                  <div className={styles.formGroup}>
                    <label className={styles.label}>عبارات ممنوعة</label>
                    <textarea
                      value={arrayToLines(aiBehavior.tone.prohibitedPhrases)}
                      onChange={(e) => updateAiBehavior('tone', { ...aiBehavior.tone, prohibitedPhrases: linesToArray(e.target.value) })}
                      className={styles.input}
                      rows={4}
                      placeholder="عبارة في كل سطر"
                    />
                  </div>
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label}>تعليمات بيزنس إضافية</label>
                  <textarea
                    value={aiBehavior.tone.businessInstructions || ''}
                    onChange={(e) => updateAiBehavior('tone', { ...aiBehavior.tone, businessInstructions: e.target.value })}
                    className={styles.input}
                    rows={4}
                    placeholder="أي قواعد بيع أو خدمة عملاء عامة"
                  />
                </div>
              </div>

              <div className={styles.settingsSection}>
                <h3 className={styles.sectionTitle}>القنوات</h3>
                {(['WhatsApp', 'Messenger', 'FacebookComment'] as ChannelName[]).map(channel => (
                  <div className={styles.formGroup} key={channel}>
                    <label className={styles.label}>{channel === 'FacebookComment' ? 'تعليقات فيسبوك' : channel} - تعليمات زيادة فوق الإعدادات العامة</label>
                    <textarea
                      value={aiBehavior.channels[channel]?.additionalInstructions || ''}
                      onChange={(e) => updateChannelInstructions(channel, e.target.value)}
                      className={styles.input}
                      rows={3}
                      placeholder="سيتم تطبيقها على هذه القناة فقط"
                    />
                  </div>
                ))}
              </div>

              <div className={styles.settingsSection}>
                <h3 className={styles.sectionTitle}>الريأكشن</h3>
                <div className={styles.inlineGrid}>
                  <label className={styles.checkboxGroup}>
                    <input
                      type="checkbox"
                      checked={aiBehavior.reactions.enabled}
                      onChange={(e) => updateAiBehavior('reactions', { ...aiBehavior.reactions, enabled: e.target.checked })}
                      className={styles.checkbox}
                    />
                    <span className={styles.label} style={{ userSelect: 'none' }}>تفعيل إرسال الريأكشن</span>
                  </label>
                  <label className={styles.checkboxGroup}>
                    <input
                      type="checkbox"
                      checked={aiBehavior.reactions.useAiSuggestedReaction}
                      onChange={(e) => updateAiBehavior('reactions', { ...aiBehavior.reactions, useAiSuggestedReaction: e.target.checked })}
                      className={styles.checkbox}
                    />
                    <span className={styles.label} style={{ userSelect: 'none' }}>استخدام اقتراح الذكاء الاصطناعي</span>
                  </label>
                </div>
                <div className={styles.reactionGrid}>
                  {['👍', '❤️', '💖', '😢', '😂', '😮'].map(reaction => (
                    <label className={styles.reactionOption} key={reaction}>
                      <input
                        type="checkbox"
                        checked={aiBehavior.reactions.allowedReactions.includes(reaction)}
                        onChange={(e) => {
                          const next = e.target.checked
                            ? [...aiBehavior.reactions.allowedReactions, reaction]
                            : aiBehavior.reactions.allowedReactions.filter(item => item !== reaction);
                          updateAiBehavior('reactions', { ...aiBehavior.reactions, allowedReactions: Array.from(new Set(next)) });
                        }}
                      />
                      <span>{reaction}</span>
                    </label>
                  ))}
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label}>قواعد اختيار الريأكشن</label>
                  <textarea
                    value={aiBehavior.reactions.rules || ''}
                    onChange={(e) => updateAiBehavior('reactions', { ...aiBehavior.reactions, rules: e.target.value })}
                    className={styles.input}
                    rows={3}
                    placeholder="مثال: استخدم ❤️ عند الشكر أو الموافقة، و😮 عند الاستفسار المهم"
                  />
                </div>
              </div>

              <div className={styles.settingsSection}>
                <h3 className={styles.sectionTitle}>رسائل fallback</h3>
                <div className={styles.formGroup}>
                  <label className={styles.label}>خطأ الذكاء الاصطناعي</label>
                  <textarea value={aiBehavior.fallbacks.aiError} onChange={(e) => updateAiBehavior('fallbacks', { ...aiBehavior.fallbacks, aiError: e.target.value })} className={styles.input} rows={3} />
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label}>خروج الذكاء الاصطناعي بصيغة غير صحيحة</label>
                  <textarea value={aiBehavior.fallbacks.invalidAiOutput} onChange={(e) => updateAiBehavior('fallbacks', { ...aiBehavior.fallbacks, invalidAiOutput: e.target.value })} className={styles.input} rows={3} />
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label}>رسالة خدمة العملاء العامة</label>
                  <textarea value={aiBehavior.fallbacks.genericCustomerService} onChange={(e) => updateAiBehavior('fallbacks', { ...aiBehavior.fallbacks, genericCustomerService: e.target.value })} className={styles.input} rows={3} />
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label}>رد التعليق العام على فيسبوك</label>
                  <textarea value={aiBehavior.fallbacks.facebookPublicComment} onChange={(e) => updateAiBehavior('fallbacks', { ...aiBehavior.fallbacks, facebookPublicComment: e.target.value })} className={styles.input} rows={3} />
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label}>رسالة الانتقال إلى واتساب</label>
                  <textarea value={aiBehavior.fallbacks.whatsAppTransitionMessage} onChange={(e) => updateAiBehavior('fallbacks', { ...aiBehavior.fallbacks, whatsAppTransitionMessage: e.target.value })} className={styles.input} rows={4} />
                </div>
                <div className={styles.inlineGrid}>
                  <div className={styles.formGroup}>
                    <label className={styles.label}>نجاح الانتقال إلى واتساب</label>
                    <textarea value={aiBehavior.fallbacks.whatsAppTransitionSuccess} onChange={(e) => updateAiBehavior('fallbacks', { ...aiBehavior.fallbacks, whatsAppTransitionSuccess: e.target.value })} className={styles.input} rows={3} />
                  </div>
                  <div className={styles.formGroup}>
                    <label className={styles.label}>فشل الانتقال إلى واتساب</label>
                    <textarea value={aiBehavior.fallbacks.whatsAppTransitionFailure} onChange={(e) => updateAiBehavior('fallbacks', { ...aiBehavior.fallbacks, whatsAppTransitionFailure: e.target.value })} className={styles.input} rows={3} />
                  </div>
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label}>رسالة المتابعة الافتراضية</label>
                  <textarea value={aiBehavior.fallbacks.followUpDefault} onChange={(e) => updateAiBehavior('fallbacks', { ...aiBehavior.fallbacks, followUpDefault: e.target.value })} className={styles.input} rows={3} />
                </div>
                <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))' }}>
                  المتغيرات المدعومة: {'{customerName}'}, {'{agentName}'}, {'{projectName}'}, {'{phoneNumber}'}, {'{channel}'}.
                </span>
              </div>

              <div className={styles.settingsSection}>
                <h3 className={styles.sectionTitle}>Advanced</h3>
                <div className={styles.formGroup}>
                  <label className={styles.label}>تعليمات إضافية متقدمة</label>
                  <textarea
                    placeholder="تعليمات إضافية فقط. القواعد المحمية والإعدادات المنظمة أعلى أولوية."
                    value={aiBehavior.advancedInstructions || ''}
                    onChange={(e) => updateAiBehavior('advancedInstructions', e.target.value)}
                    className={styles.input}
                    rows={6}
                  />
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label}>Legacy System Prompt</label>
                  <textarea
                    placeholder="تعليمات قديمة محفوظة للتوافق فقط"
                    value={systemPrompt}
                    onChange={(e) => setSystemPrompt(e.target.value)}
                    className={styles.input}
                    rows={4}
                    style={{ direction: 'ltr', textAlign: 'left', fontFamily: 'monospace', fontSize: '0.85rem' }}
                  />
                </div>
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
                  <span className={styles.label} style={{ userSelect: 'none' }}>تفعيل الرد الآلي بالذكاء الاصطناعي (واتساب)</span>
                </label>
              </div>

              {/* Messenger AI Settings */}
              <div style={{ borderTop: '1px solid var(--border-subtle)', marginTop: 'var(--space-md)', paddingTop: 'var(--space-md)' }}>
                <h3 style={{ fontSize: '0.95rem', fontWeight: 700, marginBottom: 'var(--space-sm)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                  💬 إعدادات ماسنجر
                </h3>
                <div className={styles.formGroup}>
                  <label className={styles.checkboxGroup}>
                    <input 
                      type="checkbox" 
                      checked={messengerAutoReplyEnabled}
                      onChange={(e) => setMessengerAutoReplyEnabled(e.target.checked)}
                      className={styles.checkbox} 
                    />
                    <span className={styles.label} style={{ userSelect: 'none' }}>تفعيل الرد الآلي على الماسنجر</span>
                  </label>
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label}>تأخير الرد (ماسنجر)</label>
                  <input 
                    type="number" 
                    min={0}
                    max={60}
                    value={messengerReplyDelay}
                    onChange={(e) => setMessengerReplyDelay(Number(e.target.value))}
                    className={styles.input} 
                  />
                </div>
              </div>

              {/* Comments AI Settings */}
              <div style={{ borderTop: '1px solid var(--border-subtle)', marginTop: 'var(--space-md)', paddingTop: 'var(--space-md)' }}>
                <h3 style={{ fontSize: '0.95rem', fontWeight: 700, marginBottom: 'var(--space-sm)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                  📝 إعدادات التعليقات
                </h3>
                <div className={styles.formGroup}>
                  <label className={styles.checkboxGroup}>
                    <input 
                      type="checkbox" 
                      checked={commentsAutoReplyEnabled}
                      onChange={(e) => setCommentsAutoReplyEnabled(e.target.checked)}
                      className={styles.checkbox} 
                    />
                    <span className={styles.label} style={{ userSelect: 'none' }}>تفعيل الرد الآلي على التعليقات</span>
                  </label>
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label}>تأخير الرد (تعليقات)</label>
                  <input 
                    type="number" 
                    min={0}
                    max={60}
                    value={commentsReplyDelay}
                    onChange={(e) => setCommentsReplyDelay(Number(e.target.value))}
                    className={styles.input} 
                  />
                </div>
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
              isWhatsAppGroupAutomationEnabled={isWhatsAppGroupAutomationEnabled}
              onToggleWhatsAppGroupAutomation={handleToggleWhatsAppGroupAutomation}
              groupAutomationManagerPhone={groupAutomationManagerPhone}
              onUpdateGroupAutomationManagerPhone={handleUpdateGroupAutomationManagerPhone}
              onManageGroups={() => setViewMode('manage-groups')} 
            />
          )}
        </div>
      )}

      {/* Facebook Pages Selection Modal */}
      {showPagesModal && (
        <div className={styles.overlay}>
          <div className={`glass-panel ${styles.modal}`}>
            <div className={styles.modalHeader}>
              <h3 className={styles.modalTitle}>ربط صفحة فيسبوك</h3>
              <div onClick={() => setShowPagesModal(false)} className={styles.closeBtn} style={{ fontSize: '1.5rem', cursor: 'pointer' }}>
                &times;
              </div>
            </div>

            <p style={{ fontSize: '0.9rem', color: 'var(--text-soft)' }}>
              تم العثور على الصفحات التالية. يرجى اختيار الصفحة التي تريد ربطها بالمشروع لتفعيل الماسنجر والتعليقات:
            </p>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-sm)', maxHeight: '300px', overflowY: 'auto', paddingRight: '4px' }}>
              {pagesToConnect.length === 0 ? (
                <p style={{ fontSize: '0.85rem', color: 'hsl(var(--accent-warning))', textAlign: 'center' }}>
                  لم يتم العثور على أي صفحات فيسبوك مسؤولة في هذا الحساب.
                </p>
              ) : (
                pagesToConnect.map(page => (
                  <div key={page.id} style={{
                    display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                    padding: '12px', borderRadius: 'var(--radius-md)',
                    background: 'var(--surface-muted)', border: '1px solid var(--border-subtle)',
                    gap: 'var(--space-md)'
                  }}>
                    <div style={{ minWidth: 0, flex: 1 }}>
                      <div style={{ fontWeight: 700, fontSize: '0.9rem', color: 'var(--text-strong)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                        {page.name}
                      </div>
                      <div style={{ fontSize: '0.75rem', color: 'var(--text-soft)' }}>ID: {page.id}</div>
                    </div>
                    <button
                      onClick={() => void handleConfirmPage(page)}
                      disabled={actionLoading}
                      className={`${styles.btn} ${styles.btnPrimary}`}
                      style={{ padding: '6px 12px', fontSize: '0.8rem', whiteSpace: 'nowrap' }}
                    >
                      {actionLoading ? 'جاري الربط...' : 'ربط الصفحة'}
                    </button>
                  </div>
                ))
              )}
            </div>

            <div className={styles.formActions} style={{ marginTop: 'var(--space-md)' }}>
              <button
                type="button"
                onClick={() => setShowPagesModal(false)}
                className={`${styles.btn} ${styles.btnSecondary}`}
              >
                إغلاق
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
