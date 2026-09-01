'use client';

import { useEffect, useState } from 'react';
import axios from 'axios';
import { Clock3, Link2, PlaySquare, Send, Unlink } from 'lucide-react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import { automationDateTime } from './automation-time';
import styles from './quran-challenge.module.css';

type YouTubeSettings = {
  oauthConfigured: boolean;
  connected: boolean;
  channelId: string | null;
  channelTitle: string | null;
  isEnabled: boolean;
  intervalHours: number;
  privacyStatus: 'public' | 'unlisted' | 'private';
  captionTemplate: string;
  nextPublishAtUtc: string | null;
  lastPublishedAtUtc: string | null;
  lastVideoId: string | null;
  lastError: string | null;
};

type VerseSelection = {
  surahNumber: number;
  ayahNumber: number;
  hiddenWordIndex: number;
};

export function YouTubeAutomationPanel({ selection, timezone }: { selection: VerseSelection; timezone: string | null }) {
  const { user, activeProject, loading: authLoading } = useAuth();
  const [settings, setSettings] = useState<YouTubeSettings | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [publishing, setPublishing] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [confirmAction, setConfirmAction] = useState<'publish' | 'disconnect' | null>(null);

  useEffect(() => {
    if (!user || !activeProject) return;
    const loadSettings = async () => {
      setLoading(true);
      resetFeedback(setError, setMessage);
      try {
        const response = await api.get<YouTubeSettings>('/api/quran/youtube');
        setSettings(response.data);
        applyOAuthFeedback(setError, setMessage);
      } catch (requestError) {
        setError(apiErrorMessage(requestError));
      } finally {
        setLoading(false);
      }
    };
    void loadSettings();
  }, [user, activeProject]);

  const connect = async () => {
    setError('');
    try {
      const response = await api.get<{ authorizationUrl: string }>('/api/quran/youtube/connect');
      window.location.assign(response.data.authorizationUrl);
    } catch (requestError) {
      setError(apiErrorMessage(requestError));
    }
  };

  const save = async () => {
    if (!settings) return;
    setSaving(true);
    resetFeedback(setError, setMessage);
    try {
      const response = await api.put<YouTubeSettings>('/api/quran/youtube', settings);
      setSettings(response.data);
      setMessage('تم حفظ الجدولة. ستعمل حتى والمتصفح مغلق.');
    } catch (requestError) {
      setError(apiErrorMessage(requestError));
    } finally {
      setSaving(false);
    }
  };

  const publishNow = async () => {
    setPublishing(true);
    resetFeedback(setError, setMessage);
    try {
      await api.post('/api/quran/youtube/publish-now', selection);
      setMessage('تمت إضافة الآية الحالية لطابور YouTube، وسيبدأ رفعها الآن.');
    } catch (requestError) {
      setError(apiErrorMessage(requestError));
    } finally {
      setPublishing(false);
    }
  };

  const disconnect = async () => {
    setError('');
    try {
      await api.post('/api/quran/youtube/disconnect');
      setSettings((current) => current ? disconnectedSettings(current) : current);
      setMessage('تم فصل قناة YouTube وإيقاف الجدولة.');
    } catch (requestError) {
      setError(apiErrorMessage(requestError));
    }
  };

  return (
    <>
    <section className={styles.youtubeStudio} aria-labelledby="youtube-heading">
      <YouTubeIntroduction settings={settings} />
      <div className={styles.youtubeControls}>
        {authLoading || loading ? <div className={styles.youtubeSkeleton} aria-label="جارٍ تحميل إعدادات YouTube" /> : !user || !activeProject ? (
          <EmptyYouTubeState title="سجّل الدخول أولاً" detail="ربط القناة والجدولة متاحان لصاحب المشروع فقط." />
        ) : !settings ? (
          <EmptyYouTubeState title="تعذّر تحميل إعدادات YouTube" detail={error || 'أعد تحميل الصفحة وحاول مرة أخرى.'} />
        ) : (
          <>
            <ChannelConnection settings={settings} connect={connect} disconnect={() => setConfirmAction('disconnect')} />
            {!settings.oauthConfigured && <p className={styles.youtubeWarning}>يلزم إضافة Client ID وClient Secret الخاصين بـ Google OAuth على الخادم لتفعيل زر الربط.</p>}
            <NextPublishCountdown settings={settings} timezone={timezone} />
            <ScheduleFields settings={settings} setSettings={setSettings} />
            <label className={styles.captionTemplate}><span>الوصف التلقائي</span><textarea rows={6} maxLength={5000} value={settings.captionTemplate} onChange={(event) => setSettings({ ...settings, captionTemplate: event.target.value })} /><small>المتغيرات المتاحة: {'{surah}'}، {'{ayah}'}، {'{word}'}</small></label>
            <AutomationSwitch settings={settings} setSettings={setSettings} timezone={timezone} />
            <div className={styles.youtubeActions}>
              <button type="button" className={styles.saveSchedule} onClick={save} disabled={saving}>{saving ? 'جارٍ الحفظ…' : 'حفظ الجدولة'}</button>
              <button type="button" className={styles.publishNow} onClick={() => setConfirmAction('publish')} disabled={!settings.connected || publishing}><Send size={17} />{publishing ? 'جارٍ الإضافة…' : 'انشر الآية الحالية الآن'}</button>
            </div>
            {settings.lastPublishedAtUtc && <p className={styles.lastPublish}>آخر نشر: {automationDateTime(settings.lastPublishedAtUtc, timezone)}</p>}
            {(error || settings.lastError) && <p className={styles.youtubeError} role="alert">{error || settings.lastError}</p>}
            {message && <p className={styles.youtubeSuccess} role="status">{message}</p>}
          </>
        )}
      </div>
    </section>
    <ConfirmDialog
      isOpen={confirmAction !== null}
      title={confirmAction === 'disconnect' ? 'فصل قناة YouTube؟' : 'نشر الآية الآن؟'}
      message={confirmAction === 'disconnect'
        ? `سيتم فصل «${settings?.channelTitle ?? 'القناة الحالية'}» وإيقاف الجدولة التلقائية.`
        : `ستُضاف الآية ${selection.ayahNumber} إلى طابور قناة «${settings?.channelTitle ?? 'YouTube'}» فورًا.`}
      confirmLabel={confirmAction === 'disconnect' ? 'فصل القناة' : 'إضافة للطابور'}
      onCancel={() => setConfirmAction(null)}
      onConfirm={() => {
        const action = confirmAction;
        setConfirmAction(null);
        if (action === 'disconnect') void disconnect();
        if (action === 'publish') void publishNow();
      }}
    />
    </>
  );
}

function NextPublishCountdown({ settings, timezone }: { settings: YouTubeSettings; timezone: string | null }) {
  const [now, setNow] = useState(() => Date.now());
  const nextPublishAtUtc = settings.nextPublishAtUtc;

  useEffect(() => {
    if (!settings.connected || !settings.isEnabled || !nextPublishAtUtc) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [settings.connected, settings.isEnabled, nextPublishAtUtc]);

  const target = nextPublishAtUtc ? new Date(nextPublishAtUtc).getTime() : null;
  const remaining = target === null ? null : target - now;
  const isActive = settings.connected && settings.isEnabled && target !== null;

  return <div className={`${styles.nextPublish} ${isActive ? styles.nextPublishActive : ''}`}>
    <div className={styles.nextPublishIcon}><Clock3 size={20} aria-hidden="true" /></div>
    <div className={styles.nextPublishCopy}>
      <span>الفيديو القادم</span>
      <strong>{nextPublishLabel(settings, remaining)}</strong>
      {isActive && nextPublishAtUtc && <time dateTime={nextPublishAtUtc}>{automationDateTime(nextPublishAtUtc, timezone)}</time>}
    </div>
  </div>;
}

function nextPublishLabel(settings: YouTubeSettings, remaining: number | null) {
  if (!settings.connected) return 'اربط القناة لبدء الجدولة';
  if (!settings.isEnabled) return 'النشر التلقائي متوقف';
  if (remaining === null) return 'احفظ الجدولة لتحديد الموعد';
  if (remaining <= 0) return 'جارٍ تجهيز الفيديو التالي…';
  return `بعد ${formatCountdown(remaining)}`;
}

function YouTubeIntroduction({ settings }: { settings: YouTubeSettings | null }) {
  return <div className={styles.youtubeIntro}>
    <div className={styles.youtubeMark}><PlaySquare size={28} /></div>
    <p className={styles.eyebrow}>النشر التلقائي</p>
    <h2 id="youtube-heading">اربط قناتك،<br /><em>والفيديو ينزل في معاده.</em></h2>
    <p>المهمة تعمل من السيرفر كل دقيقة، وتختار آية مناسبة وتولّد الفيديو وتضيف العنوان والوصف تلقائياً.</p>
    {settings?.lastVideoId && <a href={`https://youtu.be/${settings.lastVideoId}`} target="_blank" rel="noreferrer">شاهد آخر فيديو منشور</a>}
  </div>;
}

function ChannelConnection({ settings, connect, disconnect }: { settings: YouTubeSettings; connect: () => void; disconnect: () => void }) {
  return <div className={styles.channelRow}>
    <div><span className={`${styles.connectionDot} ${settings.connected ? styles.connected : ''}`} /><div><small>القناة</small><strong>{settings.channelTitle ?? 'غير مرتبطة'}</strong></div></div>
    {settings.connected
      ? <button type="button" className={styles.disconnectButton} onClick={disconnect}><Unlink size={16} /> فصل القناة</button>
      : <button type="button" className={styles.connectButton} onClick={connect} disabled={!settings.oauthConfigured}><PlaySquare size={18} /> ربط YouTube</button>}
  </div>;
}

function ScheduleFields({ settings, setSettings }: { settings: YouTubeSettings; setSettings: (settings: YouTubeSettings) => void }) {
  return <>
    <div className={styles.scheduleGrid}>
      <label><span><Clock3 size={15} /> ينشر كل</span><div className={styles.intervalInput}><input type="number" min="1" max="168" value={settings.intervalHours} onChange={(event) => setSettings({ ...settings, intervalHours: Number(event.target.value) })} /><b>ساعة</b></div></label>
      <label><span>خصوصية الفيديو</span><select value={settings.privacyStatus} onChange={(event) => setSettings({ ...settings, privacyStatus: event.target.value as YouTubeSettings['privacyStatus'] })}><option value="public">عام</option><option value="unlisted">غير مدرج</option><option value="private">خاص</option></select></label>
    </div>
    {settings.privacyStatus === 'public' && <p className={styles.privacyNote}>قد تُبقي Google الفيديو «خاصًا» إذا كان مشروع YouTube API لم يجتز مراجعة الامتثال بعد.</p>}
  </>;
}

function AutomationSwitch({ settings, setSettings, timezone }: { settings: YouTubeSettings; setSettings: (settings: YouTubeSettings) => void; timezone: string | null }) {
  return <label className={styles.automationSwitch}><input type="checkbox" checked={settings.isEnabled} disabled={!settings.connected} onChange={(event) => setSettings({ ...settings, isEnabled: event.target.checked })} /><span /><div><strong>تشغيل النشر التلقائي</strong><small>{settings.nextPublishAtUtc ? `الموعد القادم: ${automationDateTime(settings.nextPublishAtUtc, timezone)}` : 'سيبدأ فور حفظ الجدولة'}</small></div></label>;
}

function EmptyYouTubeState({ title, detail }: { title: string; detail: string }) {
  return <div className={styles.youtubeEmpty}><Link2 size={24} /><strong>{title}</strong><span>{detail}</span></div>;
}

function disconnectedSettings(settings: YouTubeSettings): YouTubeSettings {
  return { ...settings, connected: false, channelId: null, channelTitle: null, isEnabled: false, nextPublishAtUtc: null };
}

function resetFeedback(setError: (message: string) => void, setMessage: (message: string) => void) {
  setError('');
  setMessage('');
}

function applyOAuthFeedback(setError: (message: string) => void, setMessage: (message: string) => void) {
  const oauthStatus = new URLSearchParams(window.location.search).get('youtube');
  if (oauthStatus === 'connected') setMessage('تم ربط قناة YouTube بنجاح.');
  if (oauthStatus && oauthStatus !== 'connected') setError('لم يكتمل ربط YouTube. جرّب الربط مرة أخرى.');
  if (oauthStatus) window.history.replaceState({}, '', window.location.pathname);
}

function apiErrorMessage(error: unknown) {
  if (axios.isAxiosError<{ error?: string }>(error)) return error.response?.data?.error ?? 'تعذّر الاتصال بالخادم.';
  return error instanceof Error ? error.message : 'حدث خطأ غير متوقع.';
}

function formatCountdown(milliseconds: number) {
  const totalSeconds = Math.max(0, Math.floor(milliseconds / 1000));
  const days = Math.floor(totalSeconds / 86400);
  const hours = Math.floor((totalSeconds % 86400) / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  const time = [hours, minutes, seconds].map((value) => value.toString().padStart(2, '0')).join(':');
  return days > 0 ? `${days} يوم و ${time}` : time;
}
