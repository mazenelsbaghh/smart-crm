'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import axios from 'axios';
import { Clock3, Link2, RefreshCw, Send } from 'lucide-react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import { automationDateTime } from './automation-time';
import styles from './quran-challenge.module.css';

type VerseSelection = { surahNumber: number; ayahNumber: number; hiddenWordIndex: number };
type PageOption = { pageId: string; pageName: string };
type OAuthPage = { pageId: string; pageName: string; accessToken: string };
type FacebookOAuthMessage = { type?: unknown; projectId?: unknown; error?: unknown; pages?: unknown; userAccessToken?: unknown };
type FacebookSettings = {
  appConfigured: boolean;
  connectedPages: PageOption[];
  facebookPageId: string | null;
  pageName: string | null;
  isEnabled: boolean;
  intervalHours: number;
  captionTemplate: string;
  nextPublishAtUtc: string | null;
  lastPublishedAtUtc: string | null;
  lastReelId: string | null;
  lastError: string | null;
};

export function FacebookAutomationPanel({ selection, timezone }: { selection: VerseSelection; timezone: string | null }) {
  const { user, activeProject, loading: authLoading } = useAuth();
  const [settings, setSettings] = useState<FacebookSettings | null>(null);
  const [oauthPages, setOauthPages] = useState<OAuthPage[]>([]);
  const [userAccessToken, setUserAccessToken] = useState('');
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [publishing, setPublishing] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [confirmPublish, setConfirmPublish] = useState(false);
  const oauthPopupRef = useRef<Window | null>(null);

  const loadSettings = useCallback(async () => {
    setLoading(true);
    try {
      const response = await api.get<FacebookSettings>('/api/quran/facebook');
      setSettings(response.data);
    } catch (requestError) {
      setError(apiErrorMessage(requestError));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!user || !activeProject) return;
    const timer = window.setTimeout(() => void loadSettings(), 0);
    return () => window.clearTimeout(timer);
  }, [user, activeProject, loadSettings]);

  useEffect(() => {
    const receiveOAuth = (event: MessageEvent) => {
      if (!trustedOAuthOrigins().includes(event.origin) || event.source !== oauthPopupRef.current || !activeProject || !event.data) return;
      const payload = event.data as FacebookOAuthMessage;
      if (payload.type === 'facebook-oauth-error') {
        setError(typeof payload.error === 'string' ? payload.error : 'لم يكتمل ربط Facebook.');
        oauthPopupRef.current = null;
        return;
      }
      if (payload.type !== 'facebook-oauth-success' || payload.projectId !== activeProject.id) return;
      const pages = (Array.isArray(payload.pages) ? payload.pages : []).flatMap((candidate) => {
        const page = candidate as Partial<OAuthPage>;
        return typeof page.pageId === 'string' && typeof page.pageName === 'string' && typeof page.accessToken === 'string'
          ? [{ pageId: page.pageId, pageName: page.pageName, accessToken: page.accessToken }]
          : [];
      });
      setOauthPages(pages);
      setUserAccessToken(typeof payload.userAccessToken === 'string' ? payload.userAccessToken : '');
      setMessage('تم تسجيل الدخول. اختر الصفحة التي ستُنشر عليها الفيديوهات.');
      oauthPopupRef.current = null;
    };
    window.addEventListener('message', receiveOAuth);
    return () => window.removeEventListener('message', receiveOAuth);
  }, [activeProject]);

  const connect = () => {
    if (!activeProject || busy || publishing) return;
    if (oauthPopupRef.current && !oauthPopupRef.current.closed) {
      oauthPopupRef.current.focus();
      return;
    }
    resetFeedback(setError, setMessage);
    const baseUrl = api.defaults.baseURL?.startsWith('http') ? api.defaults.baseURL : `${window.location.origin}${api.defaults.baseURL || ''}`;
    const oauthUrl = `${baseUrl.replace(/\/$/, '')}/api/facebook/oauth/login?projectId=${activeProject.id}`;
    const popup = window.open(oauthUrl, 'facebook-oauth-login', popupFeatures());
    if (!popup) setError('اسمح بالنوافذ المنبثقة ثم حاول الربط مرة أخرى.');
    else oauthPopupRef.current = popup;
  };

  const confirmPage = async (page: OAuthPage) => {
    if (!activeProject || !settings || busy || publishing) return;
    setBusy(true);
    resetFeedback(setError, setMessage);
    try {
      await api.post(`/api/projects/${activeProject.id}/facebook/pages/confirm`, {
        facebookPageId: page.pageId,
        pageName: page.pageName,
        pageAccessToken: page.accessToken,
        userAccessToken,
        facebookUserId: '',
      });
      const response = await api.put<FacebookSettings>('/api/quran/facebook', {
        isEnabled: false,
        intervalHours: settings.intervalHours,
        captionTemplate: settings.captionTemplate,
        facebookPageId: page.pageId,
      });
      setSettings(response.data);
      setOauthPages([]);
      setUserAccessToken('');
      setMessage(`تم ربط صفحة «${page.pageName}». راجع الجدولة ثم شغّلها.`);
    } catch (requestError) {
      setError(apiErrorMessage(requestError));
    } finally {
      setBusy(false);
    }
  };

  const save = async () => {
    if (!settings || busy || publishing) return;
    setBusy(true);
    resetFeedback(setError, setMessage);
    try {
      const response = await api.put<FacebookSettings>('/api/quran/facebook', settings);
      setSettings(response.data);
      setMessage('تم حفظ جدولة Facebook Reels، وستعمل حتى والمتصفح مغلق.');
    } catch (requestError) {
      setError(apiErrorMessage(requestError));
    } finally {
      setBusy(false);
    }
  };

  const publishNow = async () => {
    if (busy || publishing) return;
    setPublishing(true);
    resetFeedback(setError, setMessage);
    try {
      await api.post('/api/quran/facebook/publish-now', selection);
      setMessage('تمت إضافة الآية الحالية لطابور Facebook Reels.');
    } catch (requestError) {
      setError(apiErrorMessage(requestError));
    } finally {
      setPublishing(false);
    }
  };

  return <><section className={`${styles.youtubeStudio} ${styles.facebookStudio}`} aria-labelledby="facebook-heading">
    <div className={`${styles.youtubeIntro} ${styles.facebookIntro}`}>
      <div className={`${styles.youtubeMark} ${styles.facebookMark}`}><FacebookGlyph /></div>
      <p className={styles.eyebrow}>Facebook Reels</p>
      <h2 id="facebook-heading">اختار الصفحة،<br /><em>وسيب النشر علينا.</em></h2>
      <p>الفيديو يُنشأ من الآية بصوت ياسر الدوسري، ثم يُرفع Reel بالوصف والموعد اللذين تحددهما.</p>
      {settings?.lastReelId && <a href={`https://www.facebook.com/reel/${settings.lastReelId}`} target="_blank" rel="noreferrer">شاهد آخر Reel منشور</a>}
    </div>
    <div className={styles.youtubeControls}>
      {authLoading || loading ? <div className={styles.youtubeSkeleton} aria-label="جارٍ تحميل إعدادات Facebook" /> : !user || !activeProject ?
        <EmptyState title="سجّل الدخول أولاً" detail="ربط الصفحة والجدولة متاحان لصاحب المشروع فقط." /> : !settings ?
          <EmptyState title="تعذّر تحميل إعدادات Facebook" detail={error || 'أعد تحميل الصفحة وحاول مرة أخرى.'} /> : <>
            <div className={styles.channelRow}>
              <div><span className={`${styles.connectionDot} ${settings.facebookPageId ? styles.connected : ''}`} /><div><small>صفحة النشر</small><strong>{settings.pageName ?? 'غير محددة'}</strong></div></div>
              <button type="button" className={`${styles.connectButton} ${styles.facebookConnect}`} onClick={connect} disabled={!settings.appConfigured || busy || publishing}><RefreshCw size={17} />{settings.facebookPageId ? 'تحديث الصلاحيات' : 'ربط Facebook'}</button>
            </div>
            {!settings.appConfigured && <p className={styles.youtubeWarning}>بيانات Meta App غير مضبوطة على الخادم.</p>}
            <p className={styles.facebookPermissionNote}>عند الربط وافق على صلاحية إدارة منشورات الصفحة حتى يتمكن النظام من نشر Reels.</p>
            {oauthPages.length > 0 && <div className={styles.facebookPageChoices} aria-label="اختر صفحة Facebook">
              <strong>اختر صفحة النشر</strong>
              {oauthPages.map((page) => <button type="button" key={page.pageId} onClick={() => confirmPage(page)} disabled={busy || publishing}><FacebookGlyph small /><span>{page.pageName}</span><small>اختيار</small></button>)}
            </div>}
            {settings.connectedPages.length > 0 && <label className={styles.facebookPageSelect}><span>صفحة متصلة مسبقاً</span><select value={settings.facebookPageId ?? ''} onChange={(event) => { const page = settings.connectedPages.find((item) => item.pageId === event.target.value); setSettings({ ...settings, facebookPageId: event.target.value || null, pageName: page?.pageName ?? null, isEnabled: false }); }}><option value="">اختر الصفحة</option>{settings.connectedPages.map((page) => <option value={page.pageId} key={page.pageId}>{page.pageName}</option>)}</select></label>}
            <FacebookCountdown settings={settings} timezone={timezone} />
            <div className={styles.scheduleGrid}><label><span><Clock3 size={15} /> ينشر كل</span><div className={styles.intervalInput}><input type="number" min="1" max="168" value={settings.intervalHours} onChange={(event) => setSettings({ ...settings, intervalHours: Number(event.target.value) })} /><b>ساعة</b></div></label></div>
            <label className={styles.captionTemplate}><span>الوصف التلقائي</span><textarea rows={6} maxLength={5000} value={settings.captionTemplate} onChange={(event) => setSettings({ ...settings, captionTemplate: event.target.value })} /><small>المتغيرات: {'{surah}'}، {'{ayah}'}، {'{word}'}</small></label>
            <label className={styles.automationSwitch}><input type="checkbox" checked={settings.isEnabled} disabled={!settings.facebookPageId} onChange={(event) => setSettings({ ...settings, isEnabled: event.target.checked })} /><span /><div><strong>تشغيل النشر التلقائي على Facebook</strong><small>{settings.nextPublishAtUtc ? `الموعد القادم: ${automationDateTime(settings.nextPublishAtUtc, timezone)}` : 'لن يبدأ قبل اختيار الصفحة وحفظ الجدولة'}</small></div></label>
            <div className={styles.youtubeActions}><button type="button" className={styles.saveSchedule} onClick={save} disabled={busy || publishing}>{busy ? 'جارٍ الحفظ…' : 'حفظ الجدولة'}</button><button type="button" className={styles.publishNow} onClick={() => setConfirmPublish(true)} disabled={!settings.facebookPageId || busy || publishing}><Send size={17} />{publishing ? 'جارٍ الإضافة…' : 'انشر الآية الحالية الآن'}</button></div>
            {settings.lastPublishedAtUtc && <p className={styles.lastPublish}>آخر نشر: {automationDateTime(settings.lastPublishedAtUtc, timezone)}</p>}
            {(error || settings.lastError) && <p className={styles.youtubeError} role="alert">{error || settings.lastError}</p>}
            {message && <p className={styles.youtubeSuccess} role="status">{message}</p>}
          </>}
    </div>
  </section><ConfirmDialog
    isOpen={confirmPublish}
    title="نشر الآية على Facebook الآن؟"
    message={`ستُضاف الآية ${selection.ayahNumber} إلى طابور صفحة «${settings?.pageName ?? 'Facebook'}» فورًا.`}
    confirmLabel="إضافة للطابور"
    onCancel={() => setConfirmPublish(false)}
    onConfirm={() => { setConfirmPublish(false); void publishNow(); }}
  /></>;
}

function FacebookCountdown({ settings, timezone }: { settings: FacebookSettings; timezone: string | null }) {
  const [now, setNow] = useState<number | null>(null);
  useEffect(() => {
    if (!settings.isEnabled || !settings.nextPublishAtUtc) return;
    const initialTimer = window.setTimeout(() => setNow(Date.now()), 0);
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => {
      window.clearTimeout(initialTimer);
      window.clearInterval(timer);
    };
  }, [settings.isEnabled, settings.nextPublishAtUtc]);
  const remaining = settings.nextPublishAtUtc && now !== null ? new Date(settings.nextPublishAtUtc).getTime() - now : null;
  const active = Boolean(settings.facebookPageId && settings.isEnabled && remaining !== null);
  const label = !settings.facebookPageId ? 'اربط الصفحة لبدء الجدولة' : !settings.isEnabled ? 'النشر التلقائي متوقف' : remaining === null ? 'احفظ الجدولة لتحديد الموعد' : remaining <= 0 ? 'جارٍ تجهيز Reel التالي…' : `بعد ${formatCountdown(remaining)}`;
  return <div className={`${styles.nextPublish} ${active ? styles.nextPublishActive : ''}`}><div className={styles.nextPublishIcon}><Clock3 size={20} aria-hidden="true" /></div><div className={styles.nextPublishCopy}><span>الـReel القادم</span><strong>{label}</strong>{active && settings.nextPublishAtUtc && <time dateTime={settings.nextPublishAtUtc}>{automationDateTime(settings.nextPublishAtUtc, timezone)}</time>}</div></div>;
}

function EmptyState({ title, detail }: { title: string; detail: string }) {
  return <div className={styles.youtubeEmpty}><Link2 size={24} /><strong>{title}</strong><span>{detail}</span></div>;
}

function FacebookGlyph({ small = false }: { small?: boolean }) {
  return <span className={styles.facebookGlyph} style={{ fontSize: small ? '1rem' : '1.7rem' }} aria-hidden="true">f</span>;
}

function trustedOAuthOrigins() {
  const origins = new Set([window.location.origin]);
  if (api.defaults.baseURL?.startsWith('http')) origins.add(new URL(api.defaults.baseURL).origin);
  return [...origins];
}

function popupFeatures() {
  const width = 600;
  const height = 700;
  return `width=${width},height=${height},left=${window.screen.width / 2 - width / 2},top=${window.screen.height / 2 - height / 2},scrollbars=yes,status=yes`;
}

function resetFeedback(setError: (value: string) => void, setMessage: (value: string) => void) { setError(''); setMessage(''); }
function apiErrorMessage(error: unknown) { if (axios.isAxiosError<{ error?: string }>(error)) return error.response?.data?.error ?? 'تعذّر الاتصال بالخادم.'; return error instanceof Error ? error.message : 'حدث خطأ غير متوقع.'; }
function formatCountdown(milliseconds: number) { const seconds = Math.max(0, Math.floor(milliseconds / 1000)); const days = Math.floor(seconds / 86400); const time = [Math.floor((seconds % 86400) / 3600), Math.floor((seconds % 3600) / 60), seconds % 60].map((value) => String(value).padStart(2, '0')).join(':'); return days ? `${days} يوم و ${time}` : time; }
