'use client';

import { useEffect, useMemo, useState } from 'react';
import Image from 'next/image';
import axios from 'axios';
import { Clock3, ExternalLink, Link2, RefreshCw, Send } from 'lucide-react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import { automationDateTime } from './automation-time';
import styles from './quran-challenge.module.css';

type VerseSelection = {
  surahNumber: number;
  ayahNumber: number;
  hiddenWordIndex: number;
  surahName: string;
};

type TikTokSettings = {
  appConfigured: boolean;
  connected: boolean;
  displayName: string | null;
  isEnabled: boolean;
  intervalHours: number;
  privacyLevel: PrivacyLevel;
  allowComment: boolean;
  allowDuet: boolean;
  allowStitch: boolean;
  captionTemplate: string;
  nextPublishAtUtc: string | null;
  lastPublishedAtUtc: string | null;
  lastPublishId: string | null;
  lastPublishStatus: string | null;
  lastError: string | null;
};

type CreatorInfo = {
  creatorAvatarUrl: string | null;
  creatorUsername: string | null;
  creatorNickname: string | null;
  privacyLevelOptions: PrivacyLevel[];
  commentDisabled: boolean;
  duetDisabled: boolean;
  stitchDisabled: boolean;
  maxVideoPostDurationSeconds: number;
};

type PrivacyLevel =
  | 'PUBLIC_TO_EVERYONE'
  | 'MUTUAL_FOLLOW_FRIENDS'
  | 'FOLLOWER_OF_CREATOR'
  | 'SELF_ONLY';

export function TikTokPublishingPanel({ selection, timezone }: { selection: VerseSelection; timezone: string | null }) {
  const { user, activeProject, loading: authLoading } = useAuth();
  const [settings, setSettings] = useState<TikTokSettings | null>(null);
  const [creator, setCreator] = useState<CreatorInfo | null>(null);
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [pollError, setPollError] = useState('');
  const [message, setMessage] = useState('');
  const [confirmPublish, setConfirmPublish] = useState(false);

  const load = async () => {
    setLoading(true);
    setError('');
    setPollError('');
    setCreator(null);
    try {
      const response = await api.get<TikTokSettings>('/api/quran/tiktok');
      setSettings(response.data);
      applyOAuthFeedback(setError, setMessage);
      if (response.data.connected) {
        const creatorResponse = await api.get<CreatorInfo>('/api/quran/tiktok/creator-info');
        setCreator(creatorResponse.data);
      } else {
        setCreator(null);
      }
    } catch (requestError) {
      setError(apiErrorMessage(requestError));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (!user || !activeProject) return;
    const frame = window.requestAnimationFrame(() => void load());
    return () => window.cancelAnimationFrame(frame);
  }, [user, activeProject]);

  const connect = async () => {
    setBusy(true);
    resetFeedback(setError, setMessage);
    try {
      const response = await api.get<{ authorizationUrl: string }>('/api/quran/tiktok/connect');
      window.location.assign(response.data.authorizationUrl);
    } catch (requestError) {
      setError(apiErrorMessage(requestError));
      setBusy(false);
    }
  };

  const verifyConnection = async () => {
    setBusy(true);
    resetFeedback(setError, setMessage);
    try {
      const creatorResponse = await api.post<CreatorInfo>('/api/quran/tiktok/verify-connection');
      const settingsResponse = await api.get<TikTokSettings>('/api/quran/tiktok');
      setCreator(creatorResponse.data);
      setSettings(settingsResponse.data);
      setMessage('تم التحقق من حساب TikTok وحفظ هوية الناشر.');
    } catch (requestError) {
      setCreator(null);
      setError(apiErrorMessage(requestError));
    } finally {
      setBusy(false);
    }
  };

  const publish = async () => {
    setBusy(true);
    resetFeedback(setError, setMessage);
    try {
      await api.post('/api/quran/tiktok/publish-now', {
        surahNumber: selection.surahNumber,
        ayahNumber: selection.ayahNumber,
        hiddenWordIndex: selection.hiddenWordIndex,
      });
      setSettings((current) => current ? {
        ...current,
        lastPublishStatus: 'GENERATING',
        lastError: null,
      } : current);
      setMessage('بدأ تجهيز الفيديو. سنحدّث حالة TikTok تلقائيًا هنا.');
    } catch (requestError) {
      setError(apiErrorMessage(requestError));
    } finally {
      setBusy(false);
    }
  };

  const save = async () => {
    if (!settings) return;
    setBusy(true);
    resetFeedback(setError, setMessage);
    try {
      const response = await api.put<TikTokSettings>('/api/quran/tiktok', settings);
      setSettings(response.data);
      setMessage('تم حفظ الجدولة. سيستمر النشر حتى والمتصفح مغلق.');
    } catch (requestError) {
      setError(apiErrorMessage(requestError));
    } finally {
      setBusy(false);
    }
  };

  const refreshStatus = async (showFeedback = true) => {
    try {
      const response = await api.post<{
        status: string;
        failReason: string | null;
      }>('/api/quran/tiktok/refresh-status');
      setSettings((current) => current ? {
        ...current,
        lastPublishStatus: response.data.status,
        lastError: response.data.failReason,
      } : current);
      setPollError('');
      if (showFeedback) setMessage(statusLabel(response.data.status));
    } catch (requestError) {
      if (showFeedback) setError(apiErrorMessage(requestError));
      else setPollError(apiErrorMessage(requestError));
    }
  };

  const pollStatus = async () => {
    try {
      const settingsResponse = await api.get<TikTokSettings>('/api/quran/tiktok');
      setSettings(settingsResponse.data);
      setPollError('');
      if (settingsResponse.data.lastPublishId && isPending(settingsResponse.data.lastPublishStatus ?? '')) {
        await refreshStatus(false);
      }
    } catch (requestError) {
      setPollError(apiErrorMessage(requestError));
    }
  };

  useEffect(() => {
    if (!settings?.lastPublishStatus || !isPending(settings.lastPublishStatus)) return;
    const timer = window.setInterval(() => void pollStatus(), 10000);
    return () => window.clearInterval(timer);
  }, [settings?.lastPublishStatus]); // eslint-disable-line react-hooks/exhaustive-deps

  const canPublish = useMemo(() => Boolean(settings?.connected && creator && !busy), [busy, creator, settings?.connected]);
  const creatorLabel = creator?.creatorNickname || creator?.creatorUsername || settings?.displayName || 'حساب TikTok';

  return <><section className={`${styles.youtubeStudio} ${styles.tiktokStudio}`} aria-labelledby="tiktok-heading">
    <div className={`${styles.youtubeIntro} ${styles.tiktokIntro}`}>
      <div className={`${styles.youtubeMark} ${styles.tiktokMark}`}><TikTokLabel /></div>
      <p className={styles.eyebrow}>النشر التلقائي عبر Zernio</p>
      <h2 id="tiktok-heading">اضبط الجدولة،<br /><em>وTikTok ينشر وحده.</em></h2>
      <p>المهمة تعمل من السيرفر كل دقيقة، وتختار آية مناسبة وتولّد الفيديو وتنشره تلقائيًا على الحساب المتصل.</p>
    </div>
    <div className={styles.youtubeControls}>
      {authLoading || loading
        ? <div className={styles.youtubeSkeleton} aria-label="جارٍ تحميل إعدادات TikTok" />
        : !user || !activeProject
          ? <EmptyState title="سجّل الدخول أولاً" detail="ربط TikTok والنشر متاحان لصاحب المشروع فقط." />
          : !settings
            ? <EmptyState title="تعذّر تحميل إعدادات TikTok" detail={error || 'أعد تحميل الصفحة وحاول مرة أخرى.'} />
            : <>
              <div className={styles.channelRow}>
                <div>
                  <span className={`${styles.connectionDot} ${settings.connected ? styles.connected : ''}`} />
                  <div><small>حساب TikTok</small><strong>{settings.connected ? creatorLabel : 'غير متحقق منه'}</strong></div>
                </div>
                <button type="button" className={styles.disconnectButton} onClick={connect} disabled={busy || !settings.appConfigured}><ExternalLink size={16} /> إدارة الربط في Zernio</button>
              </div>

              {!settings.appConfigured && <p className={styles.youtubeWarning}>Zernio غير مكتمل على الخادم. أضف ZERNIO_API_KEY أولاً.</p>}
              {settings.appConfigured && !settings.connected && <div className={styles.youtubeWarning} role="status">
                <p>إعداد Zernio موجود، لكننا لن نعرض حسابًا متصلًا قبل قراءة هوية الناشر من TikTok.</p>
                <button type="button" className={styles.publishNow} onClick={verifyConnection} disabled={busy}>
                  <RefreshCw size={17} />{busy ? 'جارٍ التحقق…' : 'تحقق من الربط'}
                </button>
              </div>}

              {settings.connected && !creator && <div className={styles.youtubeWarning} role="alert">
                <p>الحساب مسجل كمتصل، لكن تعذر قراءة هوية الناشر. تحقق من الربط قبل النشر.</p>
                <button type="button" className={styles.publishNow} onClick={verifyConnection} disabled={busy}>
                  <RefreshCw size={17} />{busy ? 'جارٍ التحقق…' : 'إعادة التحقق'}
                </button>
              </div>}

              {settings.connected && creator && <>
                <div className={styles.tiktokAccount}>
                  {creator.creatorAvatarUrl
                    ? <Image unoptimized src={creator.creatorAvatarUrl} width={48} height={48} alt={`صورة حساب ${creatorLabel}`} />
                    : <span><TikTokLabel /></span>}
                  <div><small>سيُنشر على</small><strong>{creatorLabel}</strong>{creator.creatorUsername && <b>@{creator.creatorUsername}</b>}</div>
                  <em>الحد الأقصى {creator.maxVideoPostDurationSeconds} ث</em>
                </div>

                <TikTokCountdown settings={settings} timezone={timezone} />

                <div className={styles.scheduleGrid}>
                  <label><span><Clock3 size={15} /> ينشر كل</span><div className={styles.intervalInput}><input type="number" min="1" max="168" value={settings.intervalHours} onChange={(event) => setSettings({ ...settings, intervalHours: Number(event.target.value) })} /><b>ساعة</b></div></label>
                  <label className={styles.tiktokPrivacy}><span>خصوصية الفيديو</span><select value={settings.privacyLevel} onChange={(event) => setSettings({ ...settings, privacyLevel: event.target.value as PrivacyLevel })}>{creator.privacyLevelOptions.map((level) => <option value={level} key={level}>{privacyLabel(level)}</option>)}</select></label>
                </div>

                <label className={styles.captionTemplate}>
                  <span>الوصف التلقائي</span>
                  <textarea rows={6} maxLength={2200} value={settings.captionTemplate} onChange={(event) => setSettings({ ...settings, captionTemplate: event.target.value })} />
                  <small>{settings.captionTemplate.length}/2200، المتغيرات: {'{surah}'}، {'{ayah}'}، {'{word}'}</small>
                </label>

                <fieldset className={styles.tiktokInteractions}>
                  <legend>السماح بالتفاعلات</legend>
                  <TikTokCheck label="التعليقات" checked={settings.allowComment} disabled={creator.commentDisabled} onChange={(checked) => setSettings({ ...settings, allowComment: checked })} />
                  <TikTokCheck label="Duet" checked={settings.allowDuet} disabled={creator.duetDisabled} onChange={(checked) => setSettings({ ...settings, allowDuet: checked })} />
                  <TikTokCheck label="Stitch" checked={settings.allowStitch} disabled={creator.stitchDisabled} onChange={(checked) => setSettings({ ...settings, allowStitch: checked })} />
                </fieldset>

                <label className={styles.automationSwitch}><input type="checkbox" checked={settings.isEnabled} onChange={(event) => setSettings({ ...settings, isEnabled: event.target.checked })} /><span /><div><strong>تشغيل النشر التلقائي على TikTok</strong><small>{settings.nextPublishAtUtc ? `الموعد القادم: ${automationDateTime(settings.nextPublishAtUtc, timezone)}` : 'سيبدأ بعد حفظ الجدولة'}</small></div></label>

                <div className={`${styles.youtubeActions} ${styles.tiktokActions}`}>
                  <button type="button" className={styles.publishNow} onClick={() => void refreshStatus()} disabled={!settings.lastPublishId || busy}><RefreshCw size={17} /> تحديث الحالة</button>
                  <button type="button" className={styles.saveSchedule} onClick={save} disabled={busy}>{busy ? 'جارٍ الحفظ…' : 'حفظ الجدولة'}</button>
                  <button type="button" className={`${styles.saveSchedule} ${styles.tiktokPublish}`} onClick={() => setConfirmPublish(true)} disabled={!canPublish}><Send size={17} />{busy ? 'جارٍ الإضافة…' : 'انشر الآية الحالية الآن'}</button>
                </div>
              </>}

              {settings.lastPublishStatus && <div className={styles.tiktokStatus}><span className={isPending(settings.lastPublishStatus) ? styles.statusPulse : ''} /><div><small>حالة آخر فيديو</small><strong>{statusLabel(settings.lastPublishStatus)}</strong></div></div>}
              {settings.lastPublishedAtUtc && <p className={styles.lastPublish}>بدأ آخر نشر: {automationDateTime(settings.lastPublishedAtUtc, timezone)}</p>}
              {(error || pollError || settings.lastError) && <p className={styles.youtubeError} role="alert">{error || pollError || settings.lastError}</p>}
              {message && <p className={styles.youtubeSuccess} role="status">{message}</p>}
            </>}
    </div>
  </section><ConfirmDialog
    isOpen={confirmPublish}
    title="نشر الآية على TikTok الآن؟"
    message={`ستُرسل الآية ${selection.ayahNumber} إلى حساب «${creatorLabel}» فورًا وفق إعدادات الخصوصية الحالية.`}
    confirmLabel="ابدأ النشر"
    onCancel={() => setConfirmPublish(false)}
    onConfirm={() => { setConfirmPublish(false); void publish(); }}
  /></>;
}

function TikTokCheck({ label, checked, disabled, onChange }: {
  label: string;
  checked: boolean;
  disabled: boolean;
  onChange: (checked: boolean) => void;
}) {
  return <label><input type="checkbox" checked={checked} disabled={disabled} onChange={(event) => onChange(event.target.checked)} /><span>{label}</span>{disabled && <small>معطّل في TikTok</small>}</label>;
}

function TikTokCountdown({ settings, timezone }: { settings: TikTokSettings; timezone: string | null }) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!settings.isEnabled || !settings.nextPublishAtUtc) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [settings.isEnabled, settings.nextPublishAtUtc]);
  const remaining = settings.nextPublishAtUtc
    ? new Date(settings.nextPublishAtUtc).getTime() - now
    : null;
  const active = settings.connected && settings.isEnabled && remaining !== null;
  return <div className={`${styles.nextPublish} ${active ? styles.nextPublishActive : ''}`}>
    <div className={styles.nextPublishIcon}><Clock3 size={20} /></div>
    <div className={styles.nextPublishCopy}>
      <span>فيديو TikTok القادم</span>
      <strong>{nextPublishLabel(settings, remaining)}</strong>
      {active && settings.nextPublishAtUtc && <time dateTime={settings.nextPublishAtUtc}>{automationDateTime(settings.nextPublishAtUtc, timezone)}</time>}
    </div>
  </div>;
}

function nextPublishLabel(settings: TikTokSettings, remaining: number | null) {
  if (!settings.isEnabled) return 'النشر التلقائي متوقف';
  if (remaining === null) return 'احفظ الجدولة لتحديد الموعد';
  if (remaining <= 0) return 'جارٍ تجهيز الفيديو التالي…';
  return `بعد ${formatCountdown(remaining)}`;
}

function TikTokLabel() {
  return <span className={styles.tiktokGlyph} aria-hidden="true">TikTok</span>;
}

function EmptyState({ title, detail }: { title: string; detail: string }) {
  return <div className={styles.youtubeEmpty}><Link2 size={24} /><strong>{title}</strong><span>{detail}</span></div>;
}

function privacyLabel(level: PrivacyLevel) {
  return {
    PUBLIC_TO_EVERYONE: 'عام للجميع',
    MUTUAL_FOLLOW_FRIENDS: 'الأصدقاء المتبادلون',
    FOLLOWER_OF_CREATOR: 'المتابعون',
    SELF_ONLY: 'خاص بي فقط',
  }[level];
}

function statusLabel(status: string) {
  return {
    GENERATING: 'جارٍ توليد الفيديو',
    PROCESSING: 'أُرسل إلى TikTok وجارٍ المعالجة',
    PROCESSING_UPLOAD: 'جارٍ رفع الفيديو',
    PROCESSING_DOWNLOAD: 'TikTok يحمّل الفيديو',
    SEND_TO_USER_INBOX: 'وصل إلى صندوق TikTok',
    PUBLISH_COMPLETE: 'تم النشر بنجاح',
    FAILED: 'فشل النشر',
  }[status] ?? status;
}

function isPending(status: string) {
  return ['GENERATING', 'PROCESSING', 'PROCESSING_UPLOAD', 'PROCESSING_DOWNLOAD'].includes(status);
}

function applyOAuthFeedback(setError: (value: string) => void, setMessage: (value: string) => void) {
  const oauthStatus = new URLSearchParams(window.location.search).get('tiktok');
  if (oauthStatus === 'connected') setMessage('عدت من Zernio. اضغط «تحقق من الربط» لقراءة هوية حساب TikTok وحفظها.');
  if (oauthStatus && oauthStatus !== 'connected') setError('لم يكتمل ربط TikTok في Zernio.');
  if (oauthStatus) window.history.replaceState({}, '', window.location.pathname);
}

function resetFeedback(setError: (value: string) => void, setMessage: (value: string) => void) {
  setError('');
  setMessage('');
}

function apiErrorMessage(error: unknown) {
  if (axios.isAxiosError<{ error?: string }>(error)) return error.response?.data?.error ?? 'تعذّر الاتصال بالخادم.';
  return error instanceof Error ? error.message : 'حدث خطأ غير متوقع.';
}

function formatCountdown(milliseconds: number) {
  const seconds = Math.max(0, Math.floor(milliseconds / 1000));
  const days = Math.floor(seconds / 86400);
  const time = [
    Math.floor((seconds % 86400) / 3600),
    Math.floor((seconds % 3600) / 60),
    seconds % 60,
  ].map((value) => String(value).padStart(2, '0')).join(':');
  return days ? `${days} يوم و ${time}` : time;
}
