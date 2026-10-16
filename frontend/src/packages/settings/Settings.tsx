'use client';

import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import PageHeader from '../../components/ui/PageHeader';
import StatusMessage from '../../components/ui/StatusMessage';
import FormSection from '../../components/ui/FormSection';
import Tabs from '../../components/ui/Tabs';
import type { AxiosError } from 'axios';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import {
  CheckCircle,
  ChevronDown,
  Save,
  AlertCircle,
  Bot,
  BrainCircuit,
  Building2,
  Link2,
  MessageCircle,
  MessageSquareText,
  RefreshCw,
  LogOut,
  X
} from 'lucide-react';

const FacebookIcon = ({ size = 20 }: { size?: number }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="#1877F2">
    <path d="M24 12.073c0-6.627-5.373-12-12-12s-12 5.373-12 12c0 5.99 4.388 10.954 10.125 11.854v-8.385H7.078v-3.47h3.047V9.43c0-3.007 1.792-4.669 4.533-4.669 1.312 0 2.686.235 2.686.235v2.953H15.83c-1.491 0-1.956.925-1.956 1.874v2.25h3.328l-.532 3.47h-2.796v8.385C19.612 23.027 24 18.062 24 12.073z"/>
  </svg>
);
import styles from './settings.module.css';
import workspace from './SettingsWorkspace.module.css';

import Addons from './Addons';
import WhatsAppAccountsPanel from './WhatsAppAccountsPanel';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import { useDialogViewport } from '../../components/shared/useDialogViewport';
import { lockBodyScroll } from '../../components/shared/modal-accessibility';
import { useUnsavedNavigationGuard } from '../../hooks/use-unsaved-navigation-guard';

interface ApiErrorResponse {
  error?: string;
}

interface FacebookOAuthPageMessage {
  pageId?: unknown;
  pageName?: unknown;
  accessToken?: unknown;
}

interface FacebookOAuthMessage {
  type?: unknown;
  projectId?: unknown;
  userAccessToken?: unknown;
  pages?: unknown;
  error?: unknown;
}

interface ProjectSettingsResponse {
  name?: string;
  settings?: {
    aiAutoReplyEnabled?: boolean;
    timezone?: string;
    geminiApiKeyConfigured?: boolean;
    geminiAgentPlatformApiKeyConfigured?: boolean;
    geminiModel?: string;
    temporaryGeminiModel?: string | null;
    temporaryGeminiModelExpiresAtUtc?: string | null;
    effectiveGeminiModel?: string;
    geminiEnterpriseProjectId?: string;
    customerReplyProvider?: CustomerReplyProvider;
    customerReplyOpenAiApiKeyConfigured?: boolean;
    customerReplyXaiApiKeyConfigured?: boolean;
    customerReplyModel?: string;
    aiTonePreference?: string;
    aiTargetAudience?: string;
    replyDelay?: number;
    maxDailyMessages?: number;
    isGroupAppointmentsEnabled?: boolean;
    isWhatsAppGroupAutomationEnabled?: boolean;
    groupAutomationManagerPhone?: string;
    humanTransferEnabled?: boolean;
    humanTransferPhone?: string;
    isTalkTipsTrialGateEnabled?: boolean;
    messengerAiAutoReplyEnabled?: boolean;
    messengerReplyDelay?: number;
    commentsAiAutoReplyEnabled?: boolean;
    commentsReplyDelay?: number;
    systemPrompt?: string;
    aiBehavior?: AIBehaviorSettings;
  } | null;
}

type ChannelName = 'WhatsApp' | 'Messenger' | 'FacebookComment';
type CustomerReplyProvider = 'Gemini' | 'OpenAI' | 'xAI';

interface AIBehaviorSettings {
  messengerWhatsAppTransitionEnabled: boolean;
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
  cta: {
    enabled: boolean;
    instructions?: string | null;
    topics: string[];
  };
  followUps: {
    nurturingEnabled: boolean;
    appointmentRemindersEnabled: boolean;
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
  messengerWhatsAppTransitionEnabled: true,
  identity: {
    agentNames: ['فريق خدمة العملاء'],
    nameSelectionMode: 'First',
    signatureEnabled: false,
    signatureTemplate: '- {agentName}',
    complaintSignatureTemplate: '- {agentName}',
  },
  tone: {
    tonePreset: 'egyptian-polite',
    customTone: 'العامية المصرية المهذبة والمحترمة',
    targetAudience: '',
    allowedPhrases: [],
    prohibitedPhrases: [],
    businessInstructions: '',
  },
  cta: {
    enabled: false,
    instructions: 'أضف CTA واحداً فقط عندما يتوافق مع اهتمام العميل الأخير، ولا تضفه في الردود العادية أو الشكاوى.',
    topics: [],
  },
  followUps: {
    nurturingEnabled: true,
    appointmentRemindersEnabled: true,
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
    facebookPublicComment: 'تم إرسال التفاصيل في رسالة خاصة.',
    whatsAppTransitionSuccess: 'تم إرسال رسالة على واتساب ويمكننا استكمال المحادثة هناك.',
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
const GEMINI_MODELS = [
  'gemini-3.5-flash',
  'gemini-3.6-flash',
  'gemini-3.5-flash-lite',
  'gemini-3.1-flash-lite',
  'gemini-2.5-flash-lite',
  'gemini-flash-latest',
  'gemini-flash-lite-latest',
] as const;
const OPENAI_CUSTOMER_REPLY_MODELS = [
  { value: 'gpt-6-luna', label: 'GPT-6 Luna' },
  { value: 'gpt-6-sol', label: 'GPT-6 Sol' },
  { value: 'gpt-5.6', label: 'GPT-5.6: أعلى جودة (موصى به)' },
  { value: 'gpt-5.6-terra', label: 'GPT-5.6 Terra: متوازن' },
  { value: 'gpt-5.6-luna', label: 'GPT-5.6 Luna: أسرع وأوفر' },
] as const;
const XAI_CUSTOMER_REPLY_MODELS = [
  { value: 'grok-4.6', label: 'Grok 4.6' },
  { value: 'grok-4.3', label: 'Grok 4.3' },
] as const;
const FALLBACK_TIMEZONES = ['Africa/Cairo', 'Asia/Riyadh', 'Asia/Dubai', 'Africa/Casablanca', 'Europe/London'];
const IANA_TIMEZONES = (() => {
  const intl = Intl as typeof Intl & { supportedValuesOf?: (key: 'timeZone') => string[] };
  return intl.supportedValuesOf?.('timeZone') ?? FALLBACK_TIMEZONES;
})();
const isSupportedGeminiModel = (model: string) => (GEMINI_MODELS as readonly string[]).includes(model);
const isSupportedOpenAiCustomerReplyModel = (model: string) =>
  OPENAI_CUSTOMER_REPLY_MODELS.some((option) => option.value === model);
const isSupportedXaiCustomerReplyModel = (model: string) =>
  XAI_CUSTOMER_REPLY_MODELS.some((option) => option.value === model);
const parseCustomerReplyProvider = (provider?: string): CustomerReplyProvider => {
  if (provider === 'OpenAI' || provider === 'xAI') return provider;
  return 'Gemini';
};
const isValidTimezone = (timezone: string) => {
  try { new Intl.DateTimeFormat('en', { timeZone: timezone }).format(); return true; }
  catch { return false; }
};
const trustedOAuthOrigins = () => {
  const origins = new Set([window.location.origin]);
  if (api.defaults.baseURL?.startsWith('http')) origins.add(new URL(api.defaults.baseURL).origin);
  return origins;
};

const SETTINGS_SECTIONS = [
  { key: 'replies', label: 'تشغيل الرد الآلي', hint: 'التفعيل والتأخير وحدود الإرسال', icon: Bot, tone: 'intelligence' },
  { key: 'behavior', label: 'طريقة الرد', hint: 'الهوية والنبرة والرسائل', icon: MessageSquareText, tone: 'intelligence' },
  { key: 'connections', label: 'القنوات المتصلة', hint: 'واتساب وصفحات فيسبوك', icon: Link2, tone: 'info' },
  { key: 'models', label: 'نماذج الذكاء الاصطناعي', hint: 'المزود والمفاتيح والنماذج', icon: BrainCircuit, tone: 'intelligence' },
  { key: 'project', label: 'بيانات المشروع', hint: 'الاسم والمنطقة الزمنية', icon: Building2, tone: 'accent' },
] as const;
type SettingsSection = typeof SETTINGS_SECTIONS[number]['key'];

export default function Settings() {
  const { activeProject } = useAuth();
  return <SettingsProjectView key={activeProject?.id ?? 'no-active-project'} />;
}

function SettingsProjectView() {
  const { user, activeProject, refreshProjects } = useAuth();
  const canManageSettings = user?.role === 'Owner' || user?.role === 'Admin';

  const [pagesLoadError, setPagesLoadError] = useState<string | null>(null);

  const [projectSettingsState, setProjectSettingsState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [actionLoading, setActionLoading] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error', text: string } | null>(null);

  // General settings state
  const [projectName, setProjectName] = useState('');
  const [geminiApiKey, setGeminiApiKey] = useState('');
  const [geminiApiKeyConfigured, setGeminiApiKeyConfigured] = useState(false);
  const [geminiAgentPlatformApiKey, setGeminiAgentPlatformApiKey] = useState('');
  const [geminiAgentPlatformApiKeyConfigured, setGeminiAgentPlatformApiKeyConfigured] = useState(false);
  const [clearGeminiAgentPlatformApiKey, setClearGeminiAgentPlatformApiKey] = useState(false);
  const [geminiModel, setGeminiModel] = useState('gemini-3.5-flash');
  const [temporaryGeminiModel, setTemporaryGeminiModel] = useState('gemini-flash-latest');
  const [temporaryGeminiModelExpiresAtUtc, setTemporaryGeminiModelExpiresAtUtc] = useState<string | null>(null);
  const [temporaryGeminiDurationMinutes, setTemporaryGeminiDurationMinutes] = useState(120);
  const [temporaryModelActionLoading, setTemporaryModelActionLoading] = useState(false);
  const [geminiEnterpriseProjectId, setGeminiEnterpriseProjectId] = useState('');
  const [customerReplyProvider, setCustomerReplyProvider] = useState<CustomerReplyProvider>('Gemini');
  const [customerReplyOpenAiApiKey, setCustomerReplyOpenAiApiKey] = useState('');
  const [customerReplyOpenAiApiKeyConfigured, setCustomerReplyOpenAiApiKeyConfigured] = useState(false);
  const [clearCustomerReplyOpenAiApiKey, setClearCustomerReplyOpenAiApiKey] = useState(false);
  const [customerReplyXaiApiKey, setCustomerReplyXaiApiKey] = useState('');
  const [customerReplyXaiApiKeyConfigured, setCustomerReplyXaiApiKeyConfigured] = useState(false);
  const [clearCustomerReplyXaiApiKey, setClearCustomerReplyXaiApiKey] = useState(false);
  const [customerReplyModel, setCustomerReplyModel] = useState('gpt-5.6');
  const [timezone, setTimezone] = useState('Africa/Cairo');
  const [aiTonePreference, setAiTonePreference] = useState('العامية المصرية المهذبة والمحترمة');
  const [aiTargetAudience, setAiTargetAudience] = useState('');
  const [replyDelay, setReplyDelay] = useState(3);
  const [maxDailyMessages, setMaxDailyMessages] = useState(500);
  const [isGroupAppointmentsEnabled, setIsGroupAppointmentsEnabled] = useState(false);
  const [isWhatsAppGroupAutomationEnabled, setIsWhatsAppGroupAutomationEnabled] = useState(false);
  const [groupAutomationManagerPhone, setGroupAutomationManagerPhone] = useState('');
  const [humanTransferEnabled, setHumanTransferEnabled] = useState(false);
  const [humanTransferPhone, setHumanTransferPhone] = useState('');
  const [isTalkTipsTrialGateEnabled, setIsTalkTipsTrialGateEnabled] = useState(false);
  const [autoReplyEnabled, setAutoReplyEnabled] = useState(false);
  const [messengerAutoReplyEnabled, setMessengerAutoReplyEnabled] = useState(false);
  const [messengerReplyDelay, setMessengerReplyDelay] = useState(3);
  const [commentsAutoReplyEnabled, setCommentsAutoReplyEnabled] = useState(false);
  const [commentsReplyDelay, setCommentsReplyDelay] = useState(3);
  const [expandedReplyChannels, setExpandedReplyChannels] = useState<string[]>([]);

  useEffect(() => {
    if (!temporaryGeminiModelExpiresAtUtc) return;
    const remainingMilliseconds = new Date(temporaryGeminiModelExpiresAtUtc).getTime() - Date.now();
    const timeoutId = window.setTimeout(
      () => setTemporaryGeminiModelExpiresAtUtc(null),
      Math.max(0, remainingMilliseconds),
    );
    return () => window.clearTimeout(timeoutId);
  }, [temporaryGeminiModelExpiresAtUtc]);
  const [systemPrompt, setSystemPrompt] = useState('');
  const [aiBehavior, setAiBehavior] = useState<AIBehaviorSettings>(() => defaultAiBehavior());

  const replyChannels = [
    { id: 'whatsapp', name: 'واتساب', icon: MessageCircle, enabled: autoReplyEnabled, setEnabled: setAutoReplyEnabled,
      delay: replyDelay, setDelay: setReplyDelay, delayId: 'settings-reply-delay', delayLabel: 'تأخير الرد (بالثواني)',
      activationLabel: 'تفعيل الرد الآلي بالذكاء الاصطناعي (واتساب)' },
    { id: 'messenger', name: 'ماسنجر', icon: MessageCircle, enabled: messengerAutoReplyEnabled, setEnabled: setMessengerAutoReplyEnabled,
      delay: messengerReplyDelay, setDelay: setMessengerReplyDelay, delayId: 'settings-messenger-delay', delayLabel: 'تأخير الرد (ماسنجر)',
      activationLabel: 'تفعيل الرد الآلي على الماسنجر' },
    { id: 'comments', name: 'تعليقات فيسبوك', icon: MessageSquareText, enabled: commentsAutoReplyEnabled, setEnabled: setCommentsAutoReplyEnabled,
      delay: commentsReplyDelay, setDelay: setCommentsReplyDelay, delayId: 'settings-comments-delay', delayLabel: 'تأخير الرد (تعليقات)',
      activationLabel: 'تفعيل الرد الآلي على التعليقات' },
  ];

  // Facebook Pages state
  const [connectedPages, setConnectedPages] = useState<Array<{ id: string; pageId: string; pageName: string; connectedAt: string }>>([]);
  const [fbLoading, setFbLoading] = useState(false);
  const [pagesToConnect, setPagesToConnect] = useState<Array<{ id: string; name: string; access_token: string }>>([]);
  const [selectedPageId, setSelectedPageId] = useState<string | null>(null);
  const [pageConnectionError, setPageConnectionError] = useState<string | null>(null);
  const [showPagesModal, setShowPagesModal] = useState(false);
  const [userAccessToken, setUserAccessToken] = useState('');
  const [pendingDisconnect, setPendingDisconnect] = useState<{ id: string; name?: string } | null>(null);
  const oauthPopupRef = useRef<Window | null>(null);
  const pagesOverlayRef = useRef<HTMLDivElement>(null);
  const pagesModalRef = useRef<HTMLDivElement>(null);
  const modalCloseRef = useRef<HTMLButtonElement>(null);
  const previouslyFocusedRef = useRef<HTMLElement | null>(null);
  const generalFormRef = useRef<HTMLFormElement>(null);
  const settingsTaskNavRef = useRef<HTMLElement>(null);
  const pendingInvalidFieldRef = useRef<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement | null>(null);
  const settingsMutationInFlightRef = useRef(false);
  const [generalDirty, setGeneralDirty] = useState(false);
  const [addonsDirty, setAddonsDirty] = useState(false);
  const navigationGuard = useUnsavedNavigationGuard(generalDirty || addonsDirty);
  useDialogViewport(pagesOverlayRef, showPagesModal);

  // Tabs / Navigation state
  const router = useRouter();
  const searchParams = useSearchParams();
  const activeTab = searchParams.get('tab') === 'addons' ? 'addons' : 'general';
  const settingsSection = SETTINGS_SECTIONS.find(section => section.key === searchParams.get('section')) ?? SETTINGS_SECTIONS[0];
  const setSettingsSection = (section: SettingsSection) => {
    const query = new URLSearchParams(window.location.search);
    query.set('section', section);
    window.history.replaceState(null, '', `/settings?${query}`);
  };
  // URL-backed section selection can commit after the validation message renders.
  useEffect(() => {
    const field = pendingInvalidFieldRef.current;
    if (!field || field.closest('[hidden]')) return;
    pendingInvalidFieldRef.current = null;
    settingsTaskNavRef.current?.querySelector('[aria-current="true"]')?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    field.focus();
    field.reportValidity();
  });
  const setActiveTab = (tab: 'general' | 'addons') => {
    const query = new URLSearchParams(searchParams.toString());
    query.set('tab', tab);
    query.delete('view');
    window.history.replaceState(null, '', `/settings?${query}`);
  };

  const openAddonsTab = () => {
    if (generalDirty) {
      setMessage({ type: 'error', text: 'احفظ تغييرات إعدادات المشروع أولًا قبل الانتقال للإضافات.' });
      generalFormRef.current?.querySelector<HTMLButtonElement>('button[type="submit"]')?.focus();
      return false;
    }
    setActiveTab('addons');
    return true;
  };

  const openGeneralTab = () => {
    if (addonsDirty) {
      setMessage({ type: 'error', text: 'احفظ تغييرات التشغيل وفريق العمل أولًا قبل الانتقال لإعدادات المشروع.' });
      return false;
    }
    setActiveTab('general');
    return true;
  };

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

  const handleCustomerReplyProviderChange = (provider: CustomerReplyProvider) => {
    setCustomerReplyProvider(provider);
    if (provider === 'OpenAI' && !isSupportedOpenAiCustomerReplyModel(customerReplyModel)) {
      setCustomerReplyModel('gpt-5.6');
    }
    if (provider === 'xAI' && !isSupportedXaiCustomerReplyModel(customerReplyModel)) {
      setCustomerReplyModel('grok-4.6');
    }
  };

  const handleGeminiAgentPlatformApiKeyChange = (apiKey: string) => {
    setGeminiAgentPlatformApiKey(apiKey);
    if (apiKey.trim()) setClearGeminiAgentPlatformApiKey(false);
  };

  const toggleClearGeminiAgentPlatformApiKey = () => {
    if (!geminiAgentPlatformApiKeyConfigured || geminiAgentPlatformApiKey.trim()) return;
    setClearGeminiAgentPlatformApiKey(shouldClear => !shouldClear);
    setGeneralDirty(true);
  };

  const handleCustomerReplyOpenAiApiKeyChange = (value: string) => {
    setCustomerReplyOpenAiApiKey(value);
    if (value.trim()) setClearCustomerReplyOpenAiApiKey(false);
  };

  const handleCustomerReplyXaiApiKeyChange = (value: string) => {
    setCustomerReplyXaiApiKey(value);
    if (value.trim()) setClearCustomerReplyXaiApiKey(false);
  };

  const toggleClearCustomerReplyOpenAiApiKey = () => {
    if (!customerReplyOpenAiApiKeyConfigured || customerReplyOpenAiApiKey.trim()) return;
    setClearCustomerReplyOpenAiApiKey(clear => !clear);
    setGeneralDirty(true);
  };

  const toggleClearCustomerReplyXaiApiKey = () => {
    if (!customerReplyXaiApiKeyConfigured || customerReplyXaiApiKey.trim()) return;
    setClearCustomerReplyXaiApiKey(clear => !clear);
    setGeneralDirty(true);
  };

  const closePagesModal = useCallback(() => {
    setShowPagesModal(false);
    setPagesToConnect([]);
    setSelectedPageId(null);
    setPageConnectionError(null);
    setUserAccessToken('');
  }, []);

  // Listen for message events from the popup
  useEffect(() => {
    const handleOAuthMessage = (event: MessageEvent) => {
      if (!trustedOAuthOrigins().has(event.origin) || event.source !== oauthPopupRef.current || !event.data) return;
      const oauthMessage = event.data as FacebookOAuthMessage;

      if (oauthMessage.type === 'facebook-oauth-success') {
        const { projectId, userAccessToken: uToken, pages } = oauthMessage;
        if (activeProject && projectId === activeProject.id) {
          const mapped = (Array.isArray(pages) ? pages : []).flatMap((candidate) => {
            const page = candidate as FacebookOAuthPageMessage;
            return typeof page.pageId === 'string' && typeof page.pageName === 'string' && typeof page.accessToken === 'string'
              ? [{ id: page.pageId, name: page.pageName, access_token: page.accessToken }]
              : [];
          });
          if (mapped.length === 0) {
            setFbLoading(false);
            setMessage({ type: 'error', text: 'لم يرجع Facebook أي صفحة قابلة للربط لهذا الحساب.' });
            oauthPopupRef.current = null;
            return;
          }
          setPagesToConnect(mapped);
          setSelectedPageId(null);
          setPageConnectionError(null);
          setUserAccessToken(typeof uToken === 'string' ? uToken : '');
          setShowPagesModal(true);
          setFbLoading(false);
          setMessage({ type: 'success', text: 'تم تسجيل الدخول بنجاح. الرجاء تحديد الصفحة لربطها.' });
        }
        oauthPopupRef.current = null;
      } else if (oauthMessage.type === 'facebook-oauth-error') {
        setFbLoading(false);
        setMessage({ type: 'error', text: typeof oauthMessage.error === 'string' ? oauthMessage.error : 'حدث خطأ أثناء الاتصال بفيسبوك.' });
        oauthPopupRef.current = null;
      }
    };

    window.addEventListener('message', handleOAuthMessage);
    return () => {
      window.removeEventListener('message', handleOAuthMessage);
    };
  }, [activeProject]);

  useEffect(() => {
    if (!fbLoading) return;
    const popupMonitor = window.setInterval(() => {
      const popup = oauthPopupRef.current;
      if (!popup?.closed) return;
      oauthPopupRef.current = null;
      setFbLoading(false);
      setMessage({ type: 'error', text: 'أُغلقت نافذة Facebook قبل اكتمال الربط.' });
    }, 500);
    return () => window.clearInterval(popupMonitor);
  }, [fbLoading]);

  useEffect(() => {
    if (!showPagesModal) return;
    const releaseScrollLock = lockBodyScroll();
    const focusTimer = window.setTimeout(() => modalCloseRef.current?.focus(), 0);
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        closePagesModal();
        return;
      }
      if (event.key !== 'Tab') return;
      const focusable = Array.from(pagesModalRef.current?.querySelectorAll<HTMLElement>(
        'button:not(:disabled), [href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])',
      ) ?? []);
      if (!focusable.length) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      window.clearTimeout(focusTimer);
      document.removeEventListener('keydown', handleKeyDown);
      releaseScrollLock();
      previouslyFocusedRef.current?.focus();
    };
  }, [closePagesModal, showPagesModal]);

  const fetchProjectSettings = useCallback(async () => {
    if (!activeProject) return;
    setProjectSettingsState('loading');
    try {
      const response = await api.get<ProjectSettingsResponse>(`/api/projects/${activeProject.id}`);
      setProjectName(response.data.name || '');
      const settings = response.data.settings;
      setTimezone(settings?.timezone || 'Africa/Cairo');
      setGeminiApiKey('');
      setGeminiApiKeyConfigured(settings?.geminiApiKeyConfigured ?? false);
      setGeminiAgentPlatformApiKey('');
      setGeminiAgentPlatformApiKeyConfigured(settings?.geminiAgentPlatformApiKeyConfigured ?? false);
      setClearGeminiAgentPlatformApiKey(false);
      setGeminiModel(settings?.geminiModel || 'gemini-3.5-flash');
      setTemporaryGeminiModel(settings?.temporaryGeminiModel || 'gemini-flash-latest');
      setTemporaryGeminiModelExpiresAtUtc(settings?.temporaryGeminiModelExpiresAtUtc ?? null);
      setGeminiEnterpriseProjectId(settings?.geminiEnterpriseProjectId ?? '');
      const replyProvider = parseCustomerReplyProvider(settings?.customerReplyProvider);
      setCustomerReplyProvider(replyProvider);
      setCustomerReplyOpenAiApiKey('');
      setCustomerReplyOpenAiApiKeyConfigured(settings?.customerReplyOpenAiApiKeyConfigured ?? false);
      setClearCustomerReplyOpenAiApiKey(false);
      setCustomerReplyXaiApiKey('');
      setCustomerReplyXaiApiKeyConfigured(settings?.customerReplyXaiApiKeyConfigured ?? false);
      setClearCustomerReplyXaiApiKey(false);
      setCustomerReplyModel(settings?.customerReplyModel || (replyProvider === 'xAI' ? 'grok-4.6' : 'gpt-5.6'));
      setAiTonePreference(settings?.aiTonePreference || 'العامية المصرية المهذبة والمحترمة');
      setAiTargetAudience(settings?.aiTargetAudience || '');
      setReplyDelay(settings?.replyDelay ?? 3);
      setMaxDailyMessages(settings?.maxDailyMessages ?? 500);
      setIsGroupAppointmentsEnabled(settings?.isGroupAppointmentsEnabled ?? false);
      setIsWhatsAppGroupAutomationEnabled(settings?.isWhatsAppGroupAutomationEnabled ?? false);
      setGroupAutomationManagerPhone(settings?.groupAutomationManagerPhone ?? '');
      setHumanTransferEnabled(settings?.humanTransferEnabled ?? false);
      setHumanTransferPhone(settings?.humanTransferPhone || '');
      setIsTalkTipsTrialGateEnabled(settings?.isTalkTipsTrialGateEnabled ?? false);
      setAutoReplyEnabled(settings?.aiAutoReplyEnabled ?? false);
      setMessengerAutoReplyEnabled(settings?.messengerAiAutoReplyEnabled ?? false);
      setMessengerReplyDelay(settings?.messengerReplyDelay ?? 3);
      setCommentsAutoReplyEnabled(settings?.commentsAiAutoReplyEnabled ?? false);
      setCommentsReplyDelay(settings?.commentsReplyDelay ?? 3);
      setSystemPrompt(settings?.systemPrompt || '');
      setAiBehavior(settings?.aiBehavior || defaultAiBehavior());
      setGeneralDirty(false);
      setProjectSettingsState('ready');
    } catch {
      setMessage({ type: 'error', text: 'تعذر تحميل إعدادات الرد الآلي.' });
      setProjectSettingsState('error');
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
      setPagesLoadError(null);
    } catch {
      setPagesLoadError('تعذر تحديث حالة صفحات Facebook. البيانات المعروضة قد تكون قديمة.');
    }
  }, [activeProject]);

  // Load project resources once when the selected project changes.
  useEffect(() => {
    if (!activeProject) return;
    queueMicrotask(() => {
      void fetchProjectSettings();
      void fetchConnectedPages();
    });

  }, [activeProject, fetchProjectSettings, fetchConnectedPages]);

  const handleConnectFacebook = async (event: React.MouseEvent<HTMLButtonElement>) => {
    if (!activeProject || fbLoading) return;

    previouslyFocusedRef.current = event.currentTarget;
    setFbLoading(true);
    setMessage(null);

    const width = 600;
    const height = 700;
    const left = window.screen.width / 2 - width / 2;
    const top = window.screen.height / 2 - height / 2;

    const popup = window.open(
      'about:blank',
      'facebook-oauth-login',
      `width=${width},height=${height},top=${top},left=${left},scrollbars=yes,status=yes`
    );

    if (!popup) {
      setFbLoading(false);
      setMessage({ type: 'error', text: 'تم حظر النافذة المنبثقة. يرجى تفعيل النوافذ المنبثقة في متصفحك والمحاولة مرة أخرى.' });
      return;
    }
    oauthPopupRef.current = popup;
    try {
      const response = await api.get<{ authorizationUrl: string }>('/api/facebook/oauth/login', {
        params: { projectId: activeProject.id },
      });
      if (!popup.closed) popup.location.replace(response.data.authorizationUrl);
    } catch (error) {
      popup.close();
      oauthPopupRef.current = null;
      setFbLoading(false);
      setMessage({ type: 'error', text: getApiErrorMessage(error, 'تعذر بدء ربط Facebook. حاول مرة أخرى.') });
    }
  };

  const handleConfirmPage = async (page: { id: string; name: string; access_token: string }) => {
    if (!activeProject) return;
    setActionLoading(true);
    setPageConnectionError(null);
    try {
      await api.post(`/api/projects/${activeProject.id}/facebook/pages/confirm`, {
        facebookPageId: page.id,
        pageName: page.name,
        pageAccessToken: page.access_token,
        userAccessToken: userAccessToken,
        facebookUserId: ''
      });
      setMessage({ type: 'success', text: `تم ربط الصفحة بنجاح: ${page.name}` });
      closePagesModal();
      void fetchConnectedPages();
    } catch (e: unknown) {
      console.error(e);
      const errorMessage = getApiErrorMessage(e, 'تعذر ربط هذه الصفحة. حاول مرة أخرى.');
      setPageConnectionError(errorMessage);
      setMessage({ type: 'error', text: errorMessage });
    } finally {
      setActionLoading(false);
    }
  };

  const handleDisconnectPage = async (pageDbId: string) => {
    if (!activeProject) return;
    try {
      setActionLoading(true);
      setMessage(null);
      await api.delete(`/api/projects/${activeProject.id}/facebook/pages/${pageDbId}`);
      setConnectedPages(prev => prev.filter(p => p.id !== pageDbId));
      setMessage({ type: 'success', text: 'تم فصل الصفحة بنجاح.' });
    } catch {
      setMessage({ type: 'error', text: 'تعذر فصل الصفحة.' });
    } finally {
      setActionLoading(false);
    }
  };

  const confirmDisconnect = () => {
    const pending = pendingDisconnect;
    setPendingDisconnect(null);
    if (!pending) return;
    void handleDisconnectPage(pending.id);
  };

  const buildSettingsPayload = (overrides: Record<string, unknown> = {}) => ({
    projectName: projectName.trim(),
    aiAutoReplyEnabled: autoReplyEnabled,
    timezone,
    ...(geminiApiKey.trim() ? { geminiApiKey: geminiApiKey.trim() } : {}),
    ...(geminiAgentPlatformApiKey.trim()
      ? { geminiAgentPlatformApiKey: geminiAgentPlatformApiKey.trim() }
      : {}),
    clearGeminiAgentPlatformApiKey,
    ...(isSupportedGeminiModel(geminiModel) ? { geminiModel } : {}),
    geminiEnterpriseProjectId: geminiEnterpriseProjectId.trim(),
    customerReplyProvider,
    ...(customerReplyOpenAiApiKey.trim()
      ? { customerReplyOpenAiApiKey: customerReplyOpenAiApiKey.trim() }
      : {}),
    clearCustomerReplyOpenAiApiKey,
    ...(customerReplyXaiApiKey.trim()
      ? { customerReplyXaiApiKey: customerReplyXaiApiKey.trim() }
      : {}),
    clearCustomerReplyXaiApiKey,
    ...(
      (customerReplyProvider === 'OpenAI' && isSupportedOpenAiCustomerReplyModel(customerReplyModel)) ||
      (customerReplyProvider === 'xAI' && isSupportedXaiCustomerReplyModel(customerReplyModel))
        ? { customerReplyModel }
        : {}
    ),
    aiTonePreference: aiTonePreference.trim(),
    aiTargetAudience: aiTargetAudience.trim(),
    replyDelay,
    maxDailyMessages,
    isGroupAppointmentsEnabled,
    isWhatsAppGroupAutomationEnabled,
    groupAutomationManagerPhone: groupAutomationManagerPhone.trim(),
    humanTransferEnabled,
    humanTransferPhone: humanTransferPhone.trim(),
    isTalkTipsTrialGateEnabled,
    messengerAiAutoReplyEnabled: messengerAutoReplyEnabled,
    messengerReplyDelay,
    commentsAiAutoReplyEnabled: commentsAutoReplyEnabled,
    commentsReplyDelay,
    systemPrompt: systemPrompt.trim(),
    aiBehavior,
    ...overrides,
  });

  const updateProjectSettings = async (overrides: Record<string, unknown> = {}) => {
    if (!activeProject) throw new Error('NO_ACTIVE_PROJECT');
    if (settingsMutationInFlightRef.current) throw new Error('SETTINGS_UPDATE_IN_PROGRESS');
    settingsMutationInFlightRef.current = true;
    try {
      await api.put(`/api/projects/${activeProject.id}/settings`, buildSettingsPayload(overrides));
    } finally {
      settingsMutationInFlightRef.current = false;
    }
  };

  const activateTemporaryGeminiModel = async () => {
    if (!activeProject || !isSupportedGeminiModel(temporaryGeminiModel)) return;
    setTemporaryModelActionLoading(true);
    setMessage(null);
    try {
      const response = await api.put<{ temporaryGeminiModelExpiresAtUtc: string }>(
        `/api/projects/${activeProject.id}/settings/gemini-model-override`,
        { model: temporaryGeminiModel, durationMinutes: temporaryGeminiDurationMinutes },
      );
      setTemporaryGeminiModelExpiresAtUtc(response.data.temporaryGeminiModelExpiresAtUtc);
      setMessage({ type: 'success', text: 'تم تشغيل الموديل المؤقت، وسيعود النظام للموديل الأساسي تلقائيًا في الموعد.' });
    } catch (error) {
      const apiError = error as AxiosError<ApiErrorResponse>;
      setMessage({ type: 'error', text: apiError.response?.data?.error || 'تعذر تشغيل الموديل المؤقت.' });
    } finally {
      setTemporaryModelActionLoading(false);
    }
  };

  const cancelTemporaryGeminiModel = async () => {
    if (!activeProject) return;
    setTemporaryModelActionLoading(true);
    setMessage(null);
    try {
      await api.delete(`/api/projects/${activeProject.id}/settings/gemini-model-override`);
      setTemporaryGeminiModelExpiresAtUtc(null);
      setMessage({ type: 'success', text: 'تم إيقاف الموديل المؤقت والرجوع للموديل الأساسي.' });
    } catch (error) {
      const apiError = error as AxiosError<ApiErrorResponse>;
      setMessage({ type: 'error', text: apiError.response?.data?.error || 'تعذر إيقاف الموديل المؤقت.' });
    } finally {
      setTemporaryModelActionLoading(false);
    }
  };

  const handleSaveGeneralSettings = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!activeProject) return;
    const invalidField = generalFormRef.current?.querySelector<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>('input:invalid, select:invalid, textarea:invalid');
    if (invalidField) {
      const section = invalidField.closest<HTMLElement>('[data-settings-section]')?.dataset.settingsSection as SettingsSection;
      pendingInvalidFieldRef.current = invalidField;
      const replyChannel = invalidField.closest<HTMLElement>('[data-reply-channel]')?.dataset.replyChannel;
      if (replyChannel) setExpandedReplyChannels(channels => channels.includes(replyChannel) ? channels : [...channels, replyChannel]);
      setSettingsSection(section);
      const disclosure = invalidField.closest('details');
      if (disclosure) disclosure.open = true;
      setMessage({ type: 'error', text: 'راجع الحقول المعلّمة قبل الحفظ.' });
      return;
    }
    if (!isSupportedGeminiModel(geminiModel)) {
      setSettingsSection('models');
      setMessage({ type: 'error', text: 'اختر نموذج Gemini مدعومًا قبل الحفظ؛ لن نغيّر الإعداد القديم تلقائيًا.' });
      return;
    }
    if (customerReplyProvider === 'OpenAI' && !isSupportedOpenAiCustomerReplyModel(customerReplyModel)) {
      setSettingsSection('models');
      setMessage({ type: 'error', text: 'اختر موديل OpenAI مدعومًا لردود العملاء.' });
      return;
    }
    if (customerReplyProvider === 'xAI' && !isSupportedXaiCustomerReplyModel(customerReplyModel)) {
      setSettingsSection('models');
      setMessage({ type: 'error', text: 'اختر موديل Grok مدعومًا لردود العملاء.' });
      return;
    }
    if (
      customerReplyProvider === 'OpenAI' &&
      !customerReplyOpenAiApiKey.trim() &&
      (!customerReplyOpenAiApiKeyConfigured || clearCustomerReplyOpenAiApiKey)
    ) {
      setSettingsSection('models');
      setMessage({ type: 'error', text: 'أدخل مفتاح OpenAI API أو ألغِ طلب مسحه قبل تشغيل OpenAI لردود العملاء.' });
      return;
    }
    if (
      customerReplyProvider === 'xAI' &&
      !customerReplyXaiApiKey.trim() &&
      (!customerReplyXaiApiKeyConfigured || clearCustomerReplyXaiApiKey)
    ) {
      setSettingsSection('models');
      setMessage({ type: 'error', text: 'أدخل مفتاح xAI API أو ألغِ طلب مسحه قبل تشغيل Grok لردود العملاء.' });
      return;
    }
    if (!isValidTimezone(timezone)) {
      setSettingsSection('project');
      setMessage({ type: 'error', text: 'اكتب اسم منطقة زمنية صالحًا من قاعدة IANA، مثل Africa/Cairo.' });
      return;
    }
    setActionLoading(true);
    try {
      await updateProjectSettings();
      if (geminiApiKey.trim()) {
        setGeminiApiKeyConfigured(true);
        setGeminiApiKey('');
      }
      if (geminiAgentPlatformApiKey.trim()) {
        setGeminiAgentPlatformApiKeyConfigured(true);
        setGeminiAgentPlatformApiKey('');
      } else if (clearGeminiAgentPlatformApiKey) {
        setGeminiAgentPlatformApiKeyConfigured(false);
      }
      setClearGeminiAgentPlatformApiKey(false);
      if (customerReplyOpenAiApiKey.trim()) {
        setCustomerReplyOpenAiApiKeyConfigured(true);
        setCustomerReplyOpenAiApiKey('');
      } else if (clearCustomerReplyOpenAiApiKey) {
        setCustomerReplyOpenAiApiKeyConfigured(false);
      }
      setClearCustomerReplyOpenAiApiKey(false);
      if (customerReplyXaiApiKey.trim()) {
        setCustomerReplyXaiApiKeyConfigured(true);
        setCustomerReplyXaiApiKey('');
      } else if (clearCustomerReplyXaiApiKey) {
        setCustomerReplyXaiApiKeyConfigured(false);
      }
      setClearCustomerReplyXaiApiKey(false);
      setMessage({ type: 'success', text: 'تم حفظ إعدادات الرد الآلي بنجاح.' });
      setGeneralDirty(false);
      void refreshProjects();
    } catch (e) {
      setMessage({ type: 'error', text: getApiErrorMessage(e, 'تعذر حفظ إعدادات الرد الآلي.') });
    } finally {
      setActionLoading(false);
    }
  };

  const handleToggleGroupAppointments = async (enabled: boolean) => {
    if (!activeProject) return;
    try {
      await updateProjectSettings({ isGroupAppointmentsEnabled: enabled });
      setIsGroupAppointmentsEnabled(enabled);
    } catch (e) {
      console.error(e);
      throw e;
    }
  };

  const handleMessengerBookingRouteChange = async (enabled: boolean) => {
    const nextBehavior = { ...aiBehavior, messengerWhatsAppTransitionEnabled: enabled };
    await updateProjectSettings({ aiBehavior: nextBehavior });
    setAiBehavior(nextBehavior);
  };

  const handleToggleWhatsAppGroupAutomation = async (enabled: boolean) => {
    if (!activeProject) return;
    try {
      await updateProjectSettings({ isWhatsAppGroupAutomationEnabled: enabled });
      setIsWhatsAppGroupAutomationEnabled(enabled);
    } catch (e) {
      console.error(e);
      throw e;
    }
  };

  const handleUpdateGroupAutomationManagerPhone = async (phone: string) => {
    if (!activeProject) return;
    try {
      await updateProjectSettings({ groupAutomationManagerPhone: phone.trim() });
      setGroupAutomationManagerPhone(phone);
    } catch (e) {
      console.error(e);
      throw e;
    }
  };

  const handleToggleHumanTransfer = async (enabled: boolean) => {
    if (!activeProject) return;
    try {
      await updateProjectSettings({ humanTransferEnabled: enabled });
      setHumanTransferEnabled(enabled);
    } catch (e) {
      console.error(e);
      throw e;
    }
  };

  const handleUpdateHumanTransferPhone = async (phone: string) => {
    if (!activeProject) return;
    try {
      await updateProjectSettings({ humanTransferPhone: phone.trim() });
      setHumanTransferPhone(phone);
    } catch (e) {
      console.error(e);
      throw e;
    }
  };

  const handleToggleTalkTipsTrialGate = async (enabled: boolean) => {
    if (!activeProject) return;
    try {
      await updateProjectSettings({ isTalkTipsTrialGateEnabled: enabled });
      setIsTalkTipsTrialGateEnabled(enabled);
    } catch (error) {
      console.error(error);
      throw error;
    }
  };

  if (activeProject && projectSettingsState === 'loading') {
    return (
      <div className={styles.qrLoading} style={{ padding: '5rem 0' }}>
        <div className={styles.spinner}></div>
        <p>جاري تحميل إعدادات المشروع...</p>
      </div>
    );
  }

  if (!activeProject) {
    return (
      <div className={styles.qrLoading} role="status" style={{ padding: '5rem 0' }}>
        <AlertCircle size={24} />
        <p>تعذر تحميل مساحة العمل. أعد المحاولة أو تواصل مع المدير.</p>
        <button type="button" className={`${styles.btn} ${styles.btnPrimary}`} onClick={() => void refreshProjects()}>
          <RefreshCw size={16} aria-hidden="true" /> إعادة المحاولة
        </button>
      </div>
    );
  }

  if (!canManageSettings) {
    return (
      <div className={styles.qrLoading} role="status" style={{ padding: '5rem 0' }}>
        <AlertCircle size={24} />
        <p>إعدادات الاتصال والمفاتيح متاحة لمالك المشروع أو المدير فقط.</p>
      </div>
    );
  }

  if (projectSettingsState === 'error') {
    return (
      <div className={styles.qrLoading} role="alert" style={{ padding: '5rem 0' }}>
        <AlertCircle size={24} aria-hidden="true" />
        <p>تعذّر تحميل إعدادات هذا المشروع، لذلك أوقفنا التعديل حتى لا تُحفظ قيم ناقصة أو تخص مشروعًا آخر.</p>
        <button type="button" className={`${styles.btn} ${styles.btnPrimary}`} onClick={() => void fetchProjectSettings()}>
          <RefreshCw size={16} aria-hidden="true" /> إعادة المحاولة
        </button>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <PageHeader title="إعدادات المشروع" icon={<Building2 size={24} aria-hidden="true" />} description="القنوات المتصلة، مزود الذكاء الاصطناعي، وطريقة رد فريقك" />

      <Tabs idPrefix="settings" label="أقسام الإعدادات" selected={activeTab}
        tabs={[{ key: 'general', label: 'إعدادات المشروع' }, { key: 'addons', label: 'الإضافات والتشغيل' }]}
        onChange={tab => tab === 'general' ? openGeneralTab() : openAddonsTab()} />

      {message && <StatusMessage tone={message.type === 'error' ? 'danger' : 'success'}>{message.text}</StatusMessage>}

      {activeTab === 'general' ? (
        <div id="settings-panel-general" role="tabpanel" aria-labelledby="settings-tab-general" className={styles.settingsWorkspace}>
          <label className={workspace.sectionPicker}>
            <span>قسم الإعدادات</span>
            <select value={settingsSection.key} aria-controls={`settings-task-${settingsSection.key}`}
              onChange={event => setSettingsSection(event.target.value as SettingsSection)}>
              {SETTINGS_SECTIONS.map(section => <option key={section.key} value={section.key}>{section.label}</option>)}
            </select>
          </label>
          <nav ref={settingsTaskNavRef} className={`${styles.settingsTaskNav} ${workspace.sectionNav}`} aria-label="مهام إعدادات المشروع">
            {SETTINGS_SECTIONS.map(section => <button key={section.key} type="button" aria-label={section.label}
              data-tone={section.tone} aria-current={settingsSection.key === section.key ? 'true' : undefined}
              aria-controls={`settings-task-${section.key}`} onClick={() => setSettingsSection(section.key)}>
              <span className={styles.settingsTaskLabel}><section.icon size={19} aria-hidden="true" /><span>{section.label}</span></span><small>{section.hint}</small>
            </button>)}
          </nav>
          <div className={styles.settingsContent}>
          <div id="settings-task-connections" hidden={settingsSection.key !== 'connections'} className={styles.settingsTaskPanel}>
          <WhatsAppAccountsPanel projectId={activeProject.id} />

          {/* Facebook Page Connection Card */}
          <div className={styles.card}>
            <h2 className={styles.cardTitle}>
              <FacebookIcon size={20} />
              ربط صفحة فيسبوك
            </h2>

            {pagesLoadError && <p role="alert" className={styles.inlineError}>{pagesLoadError}</p>}

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
                      type="button"
                      onClick={() => setPendingDisconnect({ id: page.id, name: page.pageName })}
                      disabled={actionLoading}
                      className={`${styles.btn} ${styles.btnDanger}`}
                      style={{ padding: '4px 10px', fontSize: '0.78rem' }}
                    >
                      <LogOut size={14} />
                      فصل
                    </button>
                  </div>
                ))}

              </div>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
                <p style={{ fontSize: '0.9rem', color: 'hsl(var(--text-secondary))', lineHeight: '1.5' }}>
                  اربط صفحة فيسبوك عشان تقدر ترد على رسائل الماسنجر والتعليقات تلقائياً.
                </p>
              </div>
            )}
            <button
              type="button"
              onClick={handleConnectFacebook}
              disabled={fbLoading}
              className={`${styles.btn} ${connectedPages.length > 0 ? styles.btnSecondary : styles.btnPrimary}`}
              style={{ gap: '6px' }}
            >
              <FacebookIcon size={connectedPages.length > 0 ? 16 : 18} />
              {connectedPages.length > 0
                ? fbLoading ? 'جاري الربط...' : 'ربط صفحة أخرى'
                : fbLoading ? 'جاري فتح نافذة فيسبوك...' : 'ربط صفحة فيسبوك'}
            </button>
          </div>

          </div>
          <div className={settingsSection.key === 'connections' ? styles.settingsConnectionSave : `${styles.card} ${styles.settingsEditor} ${workspace.editor}`} data-tone={settingsSection.tone} hidden={settingsSection.key === 'connections' && !generalDirty}>
            {settingsSection.key !== 'connections' && <h2 className={styles.cardTitle}>
              <settingsSection.icon size={21} aria-hidden="true" />{settingsSection.label}
            </h2>}

            <form ref={generalFormRef} noValidate onSubmit={handleSaveGeneralSettings} onChangeCapture={() => setGeneralDirty(true)} className={`${styles.form} ${workspace.form}`}>
              <div className={workspace.saveBar} data-dirty={generalDirty}>
                <span role="status" aria-live="polite">
                  {generalDirty ? <AlertCircle size={17} aria-hidden="true" /> : <CheckCircle size={17} aria-hidden="true" />}
                  {generalDirty ? 'توجد تغييرات غير محفوظة' : 'كل التغييرات محفوظة'}
                </span>
                <button type="submit" disabled={actionLoading || !generalDirty} className={`${styles.btn} ${styles.btnPrimary}`}>
                  <Save size={17} aria-hidden="true" />{actionLoading ? 'جارٍ الحفظ…' : 'حفظ الإعدادات'}
                </button>
              </div>
              <div id="settings-task-replies" data-settings-section="replies" hidden={settingsSection.key !== 'replies'} className={styles.settingsTaskPanel}>
                <p className={styles.replySettingsHint}>فعّل الرد وحدّد تأخيره لكل قناة. تُطبّق التغييرات عند الحفظ.</p>
                <div className={styles.replyChannelList}>
                  {replyChannels.map(channel => <section key={channel.id} className={workspace.channel} data-enabled={channel.enabled} aria-labelledby={`reply-channel-${channel.id}`}>
                    <div className={workspace.channelHeader}>
                      <div className={workspace.channelIdentity}>
                        <channel.icon size={21} aria-hidden="true" />
                        <h3 id={`reply-channel-${channel.id}`}>{channel.name}</h3>
                        <span className={styles.replyChannelState} data-enabled={channel.enabled}>{channel.enabled ? 'مفعّل' : 'متوقف'}</span>
                      </div>
                      <label className={workspace.channelToggle}>
                        <input type="checkbox" role="switch" className={styles.checkbox} checked={channel.enabled}
                          aria-label={channel.activationLabel} onChange={event => {
                            channel.setEnabled(event.target.checked);
                            setExpandedReplyChannels(channels => channels.filter(id => id !== channel.id));
                          }} />
                        <span>الرد الآلي</span>
                      </label>
                    </div>
                    {!channel.enabled && <button type="button" className={workspace.channelDisclosure}
                      aria-expanded={expandedReplyChannels.includes(channel.id)} aria-controls={`reply-fields-${channel.id}`}
                      onClick={() => setExpandedReplyChannels(channels => channels.includes(channel.id)
                        ? channels.filter(id => id !== channel.id) : [...channels, channel.id])}>
                      <ChevronDown size={16} aria-hidden="true" />إعدادات {channel.name}
                    </button>}
                    <div id={`reply-fields-${channel.id}`} data-reply-channel={channel.id} className={workspace.channelFields}
                      hidden={!channel.enabled && !expandedReplyChannels.includes(channel.id)}>
                      <div className={styles.formGroup}>
                        <label className={styles.label} htmlFor={channel.delayId}>{channel.delayLabel}</label>
                        <div className={styles.numberWithUnit}>
                          <input id={channel.delayId} type="number" min={0} max={60} value={channel.delay}
                            onChange={event => channel.setDelay(Number(event.target.value))} className={styles.input} />
                          <span aria-hidden="true">ثانية</span>
                        </div>
                      </div>
                      {channel.id === 'whatsapp' && <div className={styles.formGroup}>
                        <label className={styles.label} htmlFor="settings-daily-message-limit">الحد اليومي للرسائل الصادرة</label>
                        <div className={styles.numberWithUnit}>
                          <input id="settings-daily-message-limit" type="number" min={10} value={maxDailyMessages}
                            onChange={event => setMaxDailyMessages(Number(event.target.value))} className={styles.input} />
                          <span aria-hidden="true">رسالة</span>
                        </div>
                      </div>}
                    </div>
                  </section>)}
                </div>
              </div>
              <div id="settings-task-behavior" data-settings-section="behavior" hidden={settingsSection.key !== 'behavior'} className={styles.settingsTaskPanel}>
              <FormSection id="settings-section-3" title="الهوية والتوقيع">

                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-agent-names">أسماء الموظفين</label>
                  <textarea
                    id="settings-agent-names"
                    value={arrayToLines(aiBehavior.identity.agentNames)}
                    onChange={(e) => updateAiBehavior('identity', { ...aiBehavior.identity, agentNames: linesToArray(e.target.value) })}
                    className={styles.input}
                    rows={4}
                    placeholder="اسم كل موظف في سطر"
                  />
                </div>
                <div className={styles.inlineGrid}>
                  <div className={styles.formGroup}>
                    <label className={styles.label} htmlFor="settings-name-mode">طريقة اختيار الاسم</label>
                    <select
                      id="settings-name-mode"
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
                    <label className={styles.label} htmlFor="settings-signature-template">قالب التوقيع</label>
                    <input
                      id="settings-signature-template"
                      value={aiBehavior.identity.signatureTemplate}
                      onChange={(e) => updateAiBehavior('identity', { ...aiBehavior.identity, signatureTemplate: e.target.value })}
                      className={styles.input}
                      placeholder="- {agentName}"
                    />
                  </div>
                  <div className={styles.formGroup}>
                    <label className={styles.label} htmlFor="settings-complaint-signature">توقيع الشكاوى</label>
                    <input
                      id="settings-complaint-signature"
                      value={aiBehavior.identity.complaintSignatureTemplate}
                      onChange={(e) => updateAiBehavior('identity', { ...aiBehavior.identity, complaintSignatureTemplate: e.target.value })}
                      className={styles.input}
                      placeholder="- {agentName}"
                    />
                  </div>
                </div>
              </FormSection>

              <FormSection id="settings-section-4" title="النبرة والجمهور">

                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-tone-preset">لهجة وأسلوب الرد</label>
                  <select
                    id="settings-tone-preset"
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
                    id="settings-custom-tone"
                    aria-label="وصف النبرة المخصصة"
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
                  <label className={styles.label} htmlFor="settings-target-audience">الجمهور المستهدف</label>
                  <input
                    id="settings-target-audience"
                    type="text"
                    placeholder="مثال: عملاء المتجر المهتمون بمنتجات العناية الشخصية"
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
                    <label className={styles.label} htmlFor="settings-allowed-phrases">عبارات مسموحة أو مفضلة</label>
                    <textarea
                      id="settings-allowed-phrases"
                      value={arrayToLines(aiBehavior.tone.allowedPhrases)}
                      onChange={(e) => updateAiBehavior('tone', { ...aiBehavior.tone, allowedPhrases: linesToArray(e.target.value) })}
                      className={styles.input}
                      rows={4}
                      placeholder="عبارة في كل سطر"
                    />
                  </div>
                  <div className={styles.formGroup}>
                    <label className={styles.label} htmlFor="settings-prohibited-phrases">عبارات ممنوعة</label>
                    <textarea
                      id="settings-prohibited-phrases"
                      value={arrayToLines(aiBehavior.tone.prohibitedPhrases)}
                      onChange={(e) => updateAiBehavior('tone', { ...aiBehavior.tone, prohibitedPhrases: linesToArray(e.target.value) })}
                      className={styles.input}
                      rows={4}
                      placeholder="عبارة في كل سطر"
                    />
                  </div>
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-business-instructions">تعليمات عمل إضافية</label>
                  <textarea
                    id="settings-business-instructions"
                    value={aiBehavior.tone.businessInstructions || ''}
                    onChange={(e) => updateAiBehavior('tone', { ...aiBehavior.tone, businessInstructions: e.target.value })}
                    className={styles.input}
                    rows={4}
                    placeholder="أي قواعد بيع أو خدمة عملاء عامة"
                  />
                </div>
              </FormSection>

              <FormSection id="settings-section-5" title="القنوات">

                {(['WhatsApp', 'Messenger', 'FacebookComment'] as ChannelName[]).map(channel => (
                  <div className={styles.formGroup} key={channel}>
                    <label className={styles.label} htmlFor={`settings-channel-${channel}`}>{channel === 'FacebookComment' ? 'تعليقات فيسبوك' : channel} — تعليمات إضافية للقناة</label>
                    <textarea
                      id={`settings-channel-${channel}`}
                      value={aiBehavior.channels[channel]?.additionalInstructions || ''}
                      onChange={(e) => updateChannelInstructions(channel, e.target.value)}
                      className={styles.input}
                      rows={3}
                      placeholder="سيتم تطبيقها على هذه القناة فقط"
                    />
                  </div>
                ))}
              </FormSection>

              <FormSection id="settings-section-6" title="CTA ذكي">

                <p className={styles.sectionHint}>اختياري، يختار الذكاء الاصطناعي دعوة إجراء واحدة تناسب آخر اهتمام للعميل، بدلاً من تكرار عرض ثابت في كل رسالة.</p>
                <label className={styles.checkboxGroup}>
                  <input
                    type="checkbox"
                    checked={aiBehavior.cta.enabled}
                    onChange={(e) => updateAiBehavior('cta', { ...aiBehavior.cta, enabled: e.target.checked })}
                    className={styles.checkbox}
                  />
                  <span className={styles.label} style={{ userSelect: 'none' }}>تفعيل CTA ديناميكي</span>
                </label>
                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-cta-instructions">متى تظهر دعوة الإجراء؟</label>
                  <textarea
                    id="settings-cta-instructions"
                    value={aiBehavior.cta.instructions || ''}
                    onChange={(e) => updateAiBehavior('cta', { ...aiBehavior.cta, instructions: e.target.value })}
                    className={styles.input}
                    rows={3}
                    placeholder="مثال: عند السؤال عن المنصة أو الباقات، اقترح الخطوة التالية المناسبة فقط"
                  />
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-cta-topics">موضوعات دعوة الإجراء المتاحة</label>
                  <textarea
                    id="settings-cta-topics"
                    value={arrayToLines(aiBehavior.cta.topics)}
                    onChange={(e) => updateAiBehavior('cta', { ...aiBehavior.cta, topics: linesToArray(e.target.value) })}
                    className={styles.input}
                    rows={4}
                    placeholder={'ابدأ تجربة البحث المجانية\nاطلع على الباقات\nشاهد الفيديو التوضيحي'}
                  />
                  <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))' }}>اكتب موضوعاً واحداً في كل سطر.</span>
                </div>
              </FormSection>

              <FormSection id="settings-section-7" title="الريأكشن">

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
                  <label className={styles.label} htmlFor="settings-reaction-rules">قواعد اختيار التفاعل</label>
                  <textarea
                    id="settings-reaction-rules"
                    value={aiBehavior.reactions.rules || ''}
                    onChange={(e) => updateAiBehavior('reactions', { ...aiBehavior.reactions, rules: e.target.value })}
                    className={styles.input}
                    rows={3}
                    placeholder="مثال: استخدم ❤️ عند الشكر أو الموافقة، و😮 عند الاستفسار المهم"
                  />
                </div>
              </FormSection>

              <FormSection id="settings-section-8" title="رسائل fallback">

                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-fallback-ai-error">خطأ الذكاء الاصطناعي</label>
                  <textarea id="settings-fallback-ai-error" value={aiBehavior.fallbacks.aiError} onChange={(e) => updateAiBehavior('fallbacks', { ...aiBehavior.fallbacks, aiError: e.target.value })} className={styles.input} rows={3} />
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-fallback-invalid-ai">صيغة رد غير صحيحة</label>
                  <textarea id="settings-fallback-invalid-ai" value={aiBehavior.fallbacks.invalidAiOutput} onChange={(e) => updateAiBehavior('fallbacks', { ...aiBehavior.fallbacks, invalidAiOutput: e.target.value })} className={styles.input} rows={3} />
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-fallback-customer-service">رسالة خدمة العملاء العامة</label>
                  <textarea id="settings-fallback-customer-service" value={aiBehavior.fallbacks.genericCustomerService} onChange={(e) => updateAiBehavior('fallbacks', { ...aiBehavior.fallbacks, genericCustomerService: e.target.value })} className={styles.input} rows={3} />
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-fallback-facebook-comment">رد التعليق العام على فيسبوك</label>
                  <textarea id="settings-fallback-facebook-comment" value={aiBehavior.fallbacks.facebookPublicComment} onChange={(e) => updateAiBehavior('fallbacks', { ...aiBehavior.fallbacks, facebookPublicComment: e.target.value })} className={styles.input} rows={3} />
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-fallback-whatsapp-message">رسالة الانتقال إلى واتساب</label>
                  <textarea id="settings-fallback-whatsapp-message" value={aiBehavior.fallbacks.whatsAppTransitionMessage} onChange={(e) => updateAiBehavior('fallbacks', { ...aiBehavior.fallbacks, whatsAppTransitionMessage: e.target.value })} className={styles.input} rows={4} />
                </div>
                <div className={styles.inlineGrid}>
                  <div className={styles.formGroup}>
                    <label className={styles.label} htmlFor="settings-fallback-whatsapp-success">نجاح الانتقال إلى واتساب</label>
                    <textarea id="settings-fallback-whatsapp-success" value={aiBehavior.fallbacks.whatsAppTransitionSuccess} onChange={(e) => updateAiBehavior('fallbacks', { ...aiBehavior.fallbacks, whatsAppTransitionSuccess: e.target.value })} className={styles.input} rows={3} />
                  </div>
                  <div className={styles.formGroup}>
                    <label className={styles.label} htmlFor="settings-fallback-whatsapp-failure">فشل الانتقال إلى واتساب</label>
                    <textarea id="settings-fallback-whatsapp-failure" value={aiBehavior.fallbacks.whatsAppTransitionFailure} onChange={(e) => updateAiBehavior('fallbacks', { ...aiBehavior.fallbacks, whatsAppTransitionFailure: e.target.value })} className={styles.input} rows={3} />
                  </div>
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-fallback-followup">رسالة المتابعة الافتراضية</label>
                  <textarea id="settings-fallback-followup" value={aiBehavior.fallbacks.followUpDefault} onChange={(e) => updateAiBehavior('fallbacks', { ...aiBehavior.fallbacks, followUpDefault: e.target.value })} className={styles.input} rows={3} />
                </div>
                <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-muted))' }}>
                  المتغيرات المدعومة: {'{customerName}'}, {'{agentName}'}, {'{projectName}'}, {'{phoneNumber}'}, {'{channel}'}.
                </span>
              </FormSection>

              <FormSection id="settings-section-9" title="إعدادات متقدمة">

                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-advanced-instructions">تعليمات إضافية متقدمة</label>
                  <textarea
                    id="settings-advanced-instructions"
                    placeholder="تعليمات إضافية فقط. القواعد المحمية والإعدادات المنظمة أعلى أولوية."
                    value={aiBehavior.advancedInstructions || ''}
                    onChange={(e) => updateAiBehavior('advancedInstructions', e.target.value)}
                    className={styles.input}
                    rows={6}
                  />
                </div>
                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-legacy-prompt">تعليمات النظام القديمة للتوافق</label>
                  <textarea
                    id="settings-legacy-prompt"
                    placeholder="تعليمات قديمة محفوظة للتوافق فقط"
                    value={systemPrompt}
                    onChange={(e) => setSystemPrompt(e.target.value)}
                    className={styles.input}
                    rows={4}
                    style={{ direction: 'ltr', textAlign: 'left', fontFamily: 'monospace', fontSize: '0.85rem' }}
                  />
                </div>
              </FormSection>

              </div>
              <div id="settings-task-models" data-settings-section="models" hidden={settingsSection.key !== 'models'} className={styles.settingsTaskPanel}>
              <FormSection id="settings-section-1" title="موديل ردود العملاء">
                <div>

                  <p className={styles.sectionHint}>
                    خاص فقط بردود واتساب وماسنجر وتعليقات فيسبوك. المحتوى والصور والإعلانات تفضل على إعداد Gemini المنفصل بالأسفل.
                  </p>
                </div>

                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-customer-reply-provider">مزود الشات</label>
                  <select
                    id="settings-customer-reply-provider"
                    name="customerReplyProvider"
                    className={styles.select}
                    value={customerReplyProvider}
                    onChange={(e) => handleCustomerReplyProviderChange(parseCustomerReplyProvider(e.target.value))}
                  >
                    <option value="xAI">xAI (Grok): مستقل لردود العملاء</option>
                    <option value="OpenAI">OpenAI: مستقل لردود العملاء</option>
                    <option value="Gemini">Gemini: الإعداد الحالي</option>
                  </select>
                </div>

                {customerReplyProvider === 'OpenAI' && (
                  <div className={styles.inlineGrid}>
                    <div className={styles.formGroup}>
                      <label className={styles.label} htmlFor="settings-customer-reply-model">موديل OpenAI</label>
                      <select
                        id="settings-customer-reply-model"
                        name="customerReplyModel"
                        className={styles.select}
                        value={customerReplyModel}
                        onChange={(e) => setCustomerReplyModel(e.target.value)}
                        aria-describedby={!isSupportedOpenAiCustomerReplyModel(customerReplyModel) ? 'settings-customer-reply-openai-model-warning' : undefined}
                      >
                        {!isSupportedOpenAiCustomerReplyModel(customerReplyModel) && (
                          <option value={customerReplyModel} disabled>{customerReplyModel} (إعداد قديم، اختر موديلًا مدعومًا)</option>
                        )}
                        {OPENAI_CUSTOMER_REPLY_MODELS.map((model) => (
                          <option key={model.value} value={model.value}>{model.label}</option>
                        ))}
                      </select>
                      {!isSupportedOpenAiCustomerReplyModel(customerReplyModel) && (
                        <span id="settings-customer-reply-openai-model-warning" role="alert" className={styles.inlineError}>
                          اختر موديل OpenAI مدعومًا قبل الحفظ.
                        </span>
                      )}
                    </div>

                    <div className={styles.formGroup}>
                      <label className={styles.label} htmlFor="settings-customer-reply-openai-key">مفتاح OpenAI API</label>
                      <input
                        id="settings-customer-reply-openai-key"
                        name="customerReplyOpenAiApiKey"
                        type="password"
                        autoComplete="new-password"
                        placeholder={customerReplyOpenAiApiKeyConfigured ? 'المفتاح محفوظ. اكتب مفتاحًا جديدًا لاستبداله' : 'ضع مفتاح OpenAI API هنا'}
                        value={customerReplyOpenAiApiKey}
                        onChange={(e) => handleCustomerReplyOpenAiApiKeyChange(e.target.value)}
                        className={styles.input}
                        aria-describedby="settings-customer-reply-openai-key-help"
                      />
                      <span id="settings-customer-reply-openai-key-help" className={styles.fieldHint}>
                        {clearCustomerReplyOpenAiApiKey
                          ? 'سيُمسح المفتاح عند الحفظ. اكتب مفتاحًا جديدًا لإلغاء المسح واستبداله.'
                          : customerReplyOpenAiApiKeyConfigured
                          ? 'المفتاح محفوظ ومشفّر. اترك الحقل فارغًا للاحتفاظ به.'
                          : 'مفتاح مستقل؛ مفتاح Gemini لا يعمل مع موديلات OpenAI.'}
                      </span>
                      {customerReplyOpenAiApiKeyConfigured && (
                        <div className={styles.actions}>
                          <button
                            type="button"
                            className={`${styles.btn} ${clearCustomerReplyOpenAiApiKey ? styles.btnSecondary : styles.btnDanger}`}
                            onClick={toggleClearCustomerReplyOpenAiApiKey}
                            disabled={actionLoading || Boolean(customerReplyOpenAiApiKey.trim())}
                          >
                            {clearCustomerReplyOpenAiApiKey ? 'إلغاء مسح مفتاح OpenAI' : 'مسح مفتاح OpenAI عند الحفظ'}
                          </button>
                        </div>
                      )}
                    </div>
                  </div>
                )}

                {customerReplyProvider === 'xAI' && (
                  <div className={styles.inlineGrid}>
                    <div className={styles.formGroup}>
                      <label className={styles.label} htmlFor="settings-customer-reply-xai-model">موديل Grok</label>
                      <select
                        id="settings-customer-reply-xai-model"
                        name="customerReplyModel"
                        className={styles.select}
                        value={customerReplyModel}
                        onChange={(e) => setCustomerReplyModel(e.target.value)}
                        aria-describedby={!isSupportedXaiCustomerReplyModel(customerReplyModel) ? 'settings-customer-reply-xai-model-warning' : undefined}
                      >
                        {!isSupportedXaiCustomerReplyModel(customerReplyModel) && (
                          <option value={customerReplyModel} disabled>{customerReplyModel} (إعداد قديم، اختر موديلًا مدعومًا)</option>
                        )}
                        {XAI_CUSTOMER_REPLY_MODELS.map((model) => (
                          <option key={model.value} value={model.value}>{model.label}</option>
                        ))}
                      </select>
                      {!isSupportedXaiCustomerReplyModel(customerReplyModel) && (
                        <span id="settings-customer-reply-xai-model-warning" role="alert" className={styles.inlineError}>
                          اختر موديل Grok مدعومًا قبل الحفظ.
                        </span>
                      )}
                    </div>

                    <div className={styles.formGroup}>
                      <label className={styles.label} htmlFor="settings-customer-reply-xai-key">مفتاح xAI API</label>
                      <input
                        id="settings-customer-reply-xai-key"
                        name="customerReplyXaiApiKey"
                        type="password"
                        autoComplete="new-password"
                        placeholder={customerReplyXaiApiKeyConfigured ? 'المفتاح محفوظ. اكتب مفتاحًا جديدًا لاستبداله' : 'ضع مفتاح xAI API هنا'}
                        value={customerReplyXaiApiKey}
                        onChange={(e) => handleCustomerReplyXaiApiKeyChange(e.target.value)}
                        className={styles.input}
                        aria-describedby="settings-customer-reply-xai-key-help"
                      />
                      <span id="settings-customer-reply-xai-key-help" className={styles.fieldHint}>
                        {clearCustomerReplyXaiApiKey
                          ? 'سيُمسح المفتاح عند الحفظ. اكتب مفتاحًا جديدًا لإلغاء المسح واستبداله.'
                          : customerReplyXaiApiKeyConfigured
                          ? 'المفتاح محفوظ ومشفّر. اترك الحقل فارغًا للاحتفاظ به.'
                          : 'مفتاح مستقل؛ مفاتيح Gemini وOpenAI لا تعمل مع موديلات Grok.'}
                      </span>
                      {customerReplyXaiApiKeyConfigured && (
                        <div className={styles.actions}>
                          <button
                            type="button"
                            className={`${styles.btn} ${clearCustomerReplyXaiApiKey ? styles.btnSecondary : styles.btnDanger}`}
                            onClick={toggleClearCustomerReplyXaiApiKey}
                            disabled={actionLoading || Boolean(customerReplyXaiApiKey.trim())}
                          >
                            {clearCustomerReplyXaiApiKey ? 'إلغاء مسح مفتاح xAI' : 'مسح مفتاح xAI عند الحفظ'}
                          </button>
                        </div>
                      )}
                    </div>
                  </div>
                )}
              </FormSection>

              <FormSection id="settings-section-2" title="Gemini لباقي النظام">
                <div>

                  <p className={styles.sectionHint}>مفتاح Gemini الأساسي يفهم قاعدة المعرفة ويقترح الأفكار والمحتوى والصور والإعلانات. توليد الفيديو عبر Agent Platform له مفتاح مستقل بالأسفل.</p>
                </div>

                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-gemini-model">موديل Gemini الأساسي</label>
                  <select
                    id="settings-gemini-model"
                    name="geminiModel"
                    className={styles.select}
                    value={geminiModel}
                    onChange={(e) => setGeminiModel(e.target.value)}
                    aria-describedby={!isSupportedGeminiModel(geminiModel) ? 'settings-gemini-model-warning' : undefined}
                  >
                    {!isSupportedGeminiModel(geminiModel) && (
                      <option value={geminiModel} disabled>{geminiModel} (إعداد قديم — اختر نموذجًا مدعومًا)</option>
                    )}
                    {GEMINI_MODELS.map((model) => <option key={model} value={model}>{model}</option>)}
                  </select>
                  {!isSupportedGeminiModel(geminiModel) && <span id="settings-gemini-model-warning" role="alert" className={styles.inlineError}>لن نبدّل النموذج القديم تلقائيًا. اختر نموذجًا مدعومًا قبل الحفظ.</span>}
                </div>

                <div className={styles.temporaryModelPanel}>
                  <div className={styles.temporaryModelHeader}>
                    <div>
                      <h4 className={styles.temporaryModelTitle}>تشغيل موديل مؤقت</h4>
                      <p className={styles.sectionHint}>جرّب موديلًا مختلفًا لمدة محددة، وبعدها يرجع المشروع تلقائيًا إلى <bdi dir="ltr">{geminiModel}</bdi>.</p>
                    </div>
                    {temporaryGeminiModelExpiresAtUtc && (
                      <span className={styles.activeOverrideBadge}>شغال الآن</span>
                    )}
                  </div>

                  {temporaryGeminiModelExpiresAtUtc && (
                    <div className={styles.temporaryModelStatus} role="status">
                      <span><bdi dir="ltr">{temporaryGeminiModel}</bdi> مستخدم حاليًا</span>
                      <span>الرجوع التلقائي: {new Intl.DateTimeFormat('ar-EG', {
                        dateStyle: 'medium',
                        timeStyle: 'short',
                        timeZone: timezone || 'Africa/Cairo',
                      }).format(new Date(temporaryGeminiModelExpiresAtUtc))}</span>
                    </div>
                  )}

                  <div className={styles.temporaryModelControls}>
                    <div className={styles.formGroup}>
                      <label className={styles.label} htmlFor="settings-temporary-gemini-model">الموديل المؤقت</label>
                      <select
                        id="settings-temporary-gemini-model"
                        className={styles.select}
                        value={temporaryGeminiModel}
                        onChange={(event) => setTemporaryGeminiModel(event.target.value)}
                        disabled={temporaryModelActionLoading}
                      >
                        {GEMINI_MODELS.map((model) => <option key={model} value={model}>{model}</option>)}
                      </select>
                    </div>
                    <div className={styles.formGroup}>
                      <label className={styles.label} htmlFor="settings-temporary-gemini-duration">المدة</label>
                      <select
                        id="settings-temporary-gemini-duration"
                        className={styles.select}
                        value={temporaryGeminiDurationMinutes}
                        onChange={(event) => setTemporaryGeminiDurationMinutes(Number(event.target.value))}
                        disabled={temporaryModelActionLoading}
                      >
                        <option value={30}>30 دقيقة</option>
                        <option value={60}>ساعة</option>
                        <option value={120}>ساعتان</option>
                        <option value={240}>4 ساعات</option>
                        <option value={480}>8 ساعات</option>
                        <option value={1440}>يوم</option>
                      </select>
                    </div>
                  </div>

                  <div className={styles.actions}>
                    <button
                      type="button"
                      className={`${styles.btn} ${styles.btnPrimary}`}
                      onClick={() => void activateTemporaryGeminiModel()}
                      disabled={temporaryModelActionLoading || !canManageSettings}
                    >
                      {temporaryModelActionLoading ? 'جارٍ التنفيذ…' : temporaryGeminiModelExpiresAtUtc ? 'تحديث الموديل والمدة' : 'تشغيل مؤقتًا'}
                    </button>
                    {temporaryGeminiModelExpiresAtUtc && (
                      <button
                        type="button"
                        className={`${styles.btn} ${styles.btnSecondary}`}
                        onClick={() => void cancelTemporaryGeminiModel()}
                        disabled={temporaryModelActionLoading || !canManageSettings}
                      >
                        الرجوع الآن للموديل الأساسي
                      </button>
                    )}
                  </div>
                </div>

                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-gemini-key">مفتاح Gemini API للأفكار والمعرفة</label>
                  <input
                    id="settings-gemini-key"
                    name="geminiApiKey"
                    type="password"
                    autoComplete="new-password"
                    placeholder={geminiApiKeyConfigured ? 'مفتاح الأفكار محفوظ، اكتب مفتاحًا جديدًا لاستبداله' : 'ضع مفتاح Gemini للأفكار والمعرفة هنا'}
                    value={geminiApiKey}
                    onChange={(e) => setGeminiApiKey(e.target.value)}
                    className={styles.input}
                    aria-describedby="settings-gemini-key-help"
                  />
                  <span id="settings-gemini-key-help" className={styles.fieldHint}>
                    {geminiApiKeyConfigured
                      ? 'مفتاح الأفكار وفهم قاعدة المعرفة محفوظ. اترك الحقل فارغًا للاحتفاظ به.'
                      : 'يُستخدم لفهم قاعدة المعرفة واقتراح الأفكار والمشاهد، وليس لتوليد ملفات الفيديو.'}
                  </span>
                </div>

                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-gemini-agent-platform-key">مفتاح Agent Platform API لتوليد الفيديو</label>
                  <input
                    id="settings-gemini-agent-platform-key"
                    name="geminiAgentPlatformApiKey"
                    type="password"
                    autoComplete="new-password"
                    placeholder={geminiAgentPlatformApiKeyConfigured ? 'مفتاح الفيديو محفوظ، اكتب مفتاحًا جديدًا لاستبداله' : 'ضع مفتاح Agent Platform المستقل هنا'}
                    value={geminiAgentPlatformApiKey}
                    onChange={(event) => handleGeminiAgentPlatformApiKeyChange(event.target.value)}
                    className={styles.input}
                    aria-describedby="settings-gemini-agent-platform-key-help"
                  />
                  <span id="settings-gemini-agent-platform-key-help" className={styles.fieldHint} aria-live="polite">
                    {clearGeminiAgentPlatformApiKey
                      ? 'سيُمسح مفتاح توليد الفيديو من الخادم عند الحفظ. اكتب مفتاحًا جديدًا لإلغاء المسح.'
                      : geminiAgentPlatformApiKeyConfigured
                        ? 'مفتاح مستقل محفوظ كسر خادمي ولا يُعاد عرضه. اترك الحقل فارغًا للاحتفاظ به.'
                        : 'مفتاح مستقل لتوليد الفيديو عبر Gemini Enterprise Agent Platform؛ مفتاح الأفكار لا يحل محله.'}
                  </span>
                  {geminiAgentPlatformApiKeyConfigured && (
                    <div className={styles.actions}>
                      <button
                        type="button"
                        className={`${styles.btn} ${clearGeminiAgentPlatformApiKey ? styles.btnSecondary : styles.btnDanger}`}
                        onClick={toggleClearGeminiAgentPlatformApiKey}
                        disabled={actionLoading || Boolean(geminiAgentPlatformApiKey.trim())}
                      >
                        {clearGeminiAgentPlatformApiKey ? 'إلغاء مسح مفتاح الفيديو' : 'مسح مفتاح الفيديو عند الحفظ'}
                      </button>
                    </div>
                  )}
                </div>

                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="settings-gemini-enterprise-project">Google Cloud Project ID للفيديو</label>
                  <input
                    id="settings-gemini-enterprise-project"
                    name="geminiEnterpriseProjectId"
                    type="text"
                    autoComplete="off"
                    placeholder="my-google-cloud-project"
                    maxLength={30}
                    pattern="[a-z][a-z0-9-]{4,28}[a-z0-9]"
                    spellCheck={false}
                    value={geminiEnterpriseProjectId}
                    onChange={(e) => setGeminiEnterpriseProjectId(e.target.value)}
                    className={styles.input}
                    dir="ltr"
                    aria-describedby="settings-gemini-enterprise-project-help"
                  />
                  <span id="settings-gemini-enterprise-project-help" className={styles.fieldHint}>
                    مشروع Google Cloud المرتبط بمفتاح Agent Platform لتوليد الفيديو. اكتب Project ID وليس رقم المشروع.
                  </span>
                </div>
              </FormSection>

              </div>
              <div id="settings-task-project" data-settings-section="project" hidden={settingsSection.key !== 'project'} className={styles.settingsTaskPanel}>
              <div className={styles.formGroup}>
                <label className={styles.label} htmlFor="settings-project-name">اسم المشروع</label>
                <input
                  id="settings-project-name"
                  name="projectName"
                  type="text"
                  value={projectName}
                  onChange={(e) => setProjectName(e.target.value)}
                  placeholder="اكتب اسم المشروع هنا"
                  className={styles.input}
                  required
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label} htmlFor="settings-timezone">المنطقة الزمنية</label>
                <input
                  id="settings-timezone"
                  name="timezone"
                  type="text"
                  list="settings-timezone-options"
                  value={timezone}
                  onChange={(e) => setTimezone(e.target.value)}
                  className={styles.input}
                  dir="ltr"
                  required
                  aria-describedby="settings-timezone-help"
                />
                <datalist id="settings-timezone-options">
                  {IANA_TIMEZONES.map((zone) => <option key={zone} value={zone} />)}
                </datalist>
                <span id="settings-timezone-help" className={styles.sectionHint}>اكتب اسم IANA كاملًا أو اختاره من القائمة، مثل Africa/Cairo.</span>
              </div>

              </div>
            </form>
          </div>
          </div>
        </div>
      ) : (
        <div id="settings-panel-addons" role="tabpanel" aria-labelledby="settings-tab-addons" style={{ width: '100%' }}>
          <Addons
            messengerWhatsAppTransitionEnabled={aiBehavior.messengerWhatsAppTransitionEnabled ?? true}
            onMessengerBookingRouteChange={handleMessengerBookingRouteChange}
            isGroupAppointmentsEnabled={isGroupAppointmentsEnabled}
            onToggleGroupAppointments={handleToggleGroupAppointments}
            isWhatsAppGroupAutomationEnabled={isWhatsAppGroupAutomationEnabled}
            onToggleWhatsAppGroupAutomation={handleToggleWhatsAppGroupAutomation}
            groupAutomationManagerPhone={groupAutomationManagerPhone}
            onUpdateGroupAutomationManagerPhone={handleUpdateGroupAutomationManagerPhone}
            humanTransferEnabled={humanTransferEnabled}
            onToggleHumanTransfer={handleToggleHumanTransfer}
            humanTransferPhone={humanTransferPhone}
            onUpdateHumanTransferPhone={handleUpdateHumanTransferPhone}
            isTalkTipsTrialGateEnabled={isTalkTipsTrialGateEnabled}
            onToggleTalkTipsTrialGate={handleToggleTalkTipsTrialGate}
            timezone={timezone}
            onDirtyChange={setAddonsDirty}
            onManageGroups={(courseType) => router.push(courseType === 'intensive' ? '/management/groups?course=intensive' : '/management/groups')}
          />
        </div>
      )}

      {/* Facebook Pages Selection Modal */}
      {showPagesModal && (
        <div ref={pagesOverlayRef} className={workspace.pagePickerOverlay} onMouseDown={(event) => { if (event.target === event.currentTarget) closePagesModal(); }}>
          <div ref={pagesModalRef} className={workspace.pagePicker} role="dialog" aria-modal="true" aria-labelledby="facebook-pages-modal-title" aria-describedby="facebook-pages-modal-description">
            <div className={workspace.pagePickerHeader}>
              <h3 id="facebook-pages-modal-title">ربط صفحة فيسبوك</h3>
              <button ref={modalCloseRef} type="button" aria-label="إغلاق نافذة اختيار الصفحة" onClick={closePagesModal} className={workspace.pagePickerClose}>
                <X size={20} aria-hidden="true" />
              </button>
            </div>

            <div className={workspace.pagePickerBody}>
              <p id="facebook-pages-modal-description">اختر صفحة لتفعيل رسائلها وتعليقاتها في هذا المشروع.</p>
              <fieldset className={workspace.pagePickerChoices} disabled={actionLoading}>
                <legend>الصفحات المتاحة ({pagesToConnect.length})</legend>
                {pagesToConnect.length === 0 ? (
                  <p>لا توجد صفحات متاحة للربط في هذا الحساب.</p>
                ) : (
                  pagesToConnect.map(page => (
                    <label key={page.id} className={workspace.pagePickerChoice}>
                      <input type="radio" name="facebook-page" value={page.id} checked={selectedPageId === page.id}
                        onChange={() => { setSelectedPageId(page.id); setPageConnectionError(null); }} />
                      <span><strong>{page.name}</strong><small>معرّف الصفحة: <bdi>{page.id}</bdi></small></span>
                    </label>
                  ))
                )}
              </fieldset>
            </div>

            <div className={workspace.pagePickerFooter}>
              {pageConnectionError && <StatusMessage tone="danger">{pageConnectionError}</StatusMessage>}
              <div className={workspace.pagePickerActions}>
                <button type="button" className={`${styles.btn} ${styles.btnPrimary}`}
                  disabled={!selectedPageId || actionLoading}
                  onClick={() => { const page = pagesToConnect.find(candidate => candidate.id === selectedPageId); if (page) void handleConfirmPage(page); }}>
                  <Link2 size={17} aria-hidden="true" />{actionLoading ? 'جاري الربط…' : 'ربط الصفحة المحددة'}
                </button>
                <button type="button" onClick={closePagesModal} className={`${styles.btn} ${styles.btnSecondary}`}>إغلاق</button>
              </div>
            </div>
          </div>
        </div>
      )}
      <ConfirmDialog
        isOpen={Boolean(pendingDisconnect)}
        title="فصل صفحة Facebook؟"
        message={`سيُوقف استقبال رسائل وتعليقات صفحة «${pendingDisconnect?.name ?? 'الصفحة'}» في هذا المشروع. لن تُحذف المحادثات السابقة.`}
        confirmLabel="تأكيد الفصل"
        onCancel={() => setPendingDisconnect(null)}
        onConfirm={confirmDisconnect}
      />
      <ConfirmDialog
        isOpen={navigationGuard.navigationBlocked}
        title="مغادرة الإعدادات دون حفظ؟"
        message="ستفقد التغييرات غير المحفوظة في إعدادات مساحة العمل."
        confirmLabel="مغادرة دون حفظ"
        onCancel={navigationGuard.cancelNavigation}
        onConfirm={navigationGuard.confirmNavigation}
      />
    </div>
  );
}
