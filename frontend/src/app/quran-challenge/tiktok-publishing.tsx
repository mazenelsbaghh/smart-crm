'use client';

import { useEffect, useMemo, useState } from 'react';
import axios from 'axios';
import { CheckCircle2, Link2, RefreshCw, Send, Unlink } from 'lucide-react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
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
  lastPublishedAtUtc: string | null;
  lastPublishId: string | null;
  lastPublishStatus: string | null;
  lastError: string | null;
};

type CreatorInfo = {
  creatorAvatarUrl: string | null;
  creatorUsername: string | null;
  creatorNickname: string;
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

export function TikTokPublishingPanel({ selection }: { selection: VerseSelection }) {
  const { user, activeProject, loading: authLoading } = useAuth();
  const [settings, setSettings] = useState<TikTokSettings | null>(null);
  const [creator, setCreator] = useState<CreatorInfo | null>(null);
  const [title, setTitle] = useState(defaultTitle(selection));
  const [privacyLevel, setPrivacyLevel] = useState<PrivacyLevel | ''>('');
  const [allowComment, setAllowComment] = useState(false);
  const [allowDuet, setAllowDuet] = useState(false);
  const [allowStitch, setAllowStitch] = useState(false);
  const [consent, setConsent] = useState(false);
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');

  useEffect(() => setTitle(defaultTitle(selection)), [
    selection.surahNumber,
    selection.ayahNumber,
    selection.surahName,
  ]);

  const load = async () => {
    setLoading(true);
    setError('');
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
    if (user && activeProject) void load();
  }, [user, activeProject]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (!settings?.lastPublishStatus || !isPending(settings.lastPublishStatus)) return;
    const timer = window.setInterval(() => void pollStatus(), 10000);
    return () => window.clearInterval(timer);
  }, [settings?.lastPublishStatus]); // eslint-disable-line react-hooks/exhaustive-deps

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

  const disconnect = async () => {
    setBusy(true);
    resetFeedback(setError, setMessage);
    try {
      await api.post('/api/quran/tiktok/disconnect');
      setSettings((current) => current ? {
        ...current,
        connected: false,
        displayName: null,
      } : current);
      setCreator(null);
      setMessage('تم فصل حساب TikTok.');
    } catch (requestError) {
      setError(apiErrorMessage(requestError));
    } finally {
      setBusy(false);
    }
  };

  const publish = async () => {
    if (!privacyLevel) {
      setError('اختر خصوصية الفيديو قبل النشر.');
      return;
    }
    setBusy(true);
    resetFeedback(setError, setMessage);
    try {
      await api.post('/api/quran/tiktok/publish-now', {
        surahNumber: selection.surahNumber,
        ayahNumber: selection.ayahNumber,
        hiddenWordIndex: selection.hiddenWordIndex,
        title,
        privacyLevel,
        allowComment,
        allowDuet,
        allowStitch,
        consent,
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
      if (showFeedback) setMessage(statusLabel(response.data.status));
    } catch (requestError) {
      if (showFeedback) setError(apiErrorMessage(requestError));
    }
  };

  const pollStatus = async () => {
    try {
      const settingsResponse = await api.get<TikTokSettings>('/api/quran/tiktok');
      setSettings(settingsResponse.data);
      if (settingsResponse.data.lastPublishId && isPending(settingsResponse.data.lastPublishStatus ?? '')) {
        await refreshStatus(false);
      }
    } catch (requestError) {
      if (!axios.isAxiosError(requestError)) throw requestError;
    }
  };

  const canPublish = useMemo(() => Boolean(
    settings?.connected
    && creator
    && title.trim()
    && privacyLevel
    && consent
    && !busy
  ), [busy, consent, creator, privacyLevel, settings?.connected, title]);

  return <section className={`${styles.youtubeStudio} ${styles.tiktokStudio}`} aria-labelledby="tiktok-heading">
    <div className={`${styles.youtubeIntro} ${styles.tiktokIntro}`}>
      <div className={`${styles.youtubeMark} ${styles.tiktokMark}`}><TikTokGlyph /></div>
      <p className={styles.eyebrow}>TikTok</p>
      <h2 id="tiktok-heading">راجع الفيديو،<br /><em>وانشره على حسابك.</em></h2>
      <p>اختر الخصوصية والتفاعلات بنفسك في كل مرة. بعد الضغط، يتولّد فيديو الآية ويُرسل إلى TikTok وتظهر حالة المعالجة هنا.</p>
      <p className={styles.tiktokPolicy}>TikTok يشترط موافقتك وقت كل نشر، لذلك لا يعمل هذا القسم بجدولة صامتة.</p>
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
                  <div><small>حساب TikTok</small><strong>{creator?.creatorNickname ?? settings.displayName ?? 'غير مرتبط'}</strong></div>
                </div>
                {settings.connected
                  ? <button type="button" className={styles.disconnectButton} onClick={disconnect} disabled={busy}><Unlink size={16} /> فصل الحساب</button>
                  : <button type="button" className={`${styles.connectButton} ${styles.tiktokConnect}`} onClick={connect} disabled={!settings.appConfigured || busy}><TikTokGlyph small /> ربط TikTok</button>}
              </div>

              {!settings.appConfigured && <p className={styles.youtubeWarning}>TikTok App غير مضبوط على الخادم. أضف Client Key وClient Secret أولاً.</p>}

              {settings.connected && creator && <>
                <div className={styles.tiktokAccount}>
                  {creator.creatorAvatarUrl
                    ? <img src={creator.creatorAvatarUrl} alt="" />
                    : <span><TikTokGlyph /></span>}
                  <div><small>سيُنشر على</small><strong>{creator.creatorNickname}</strong>{creator.creatorUsername && <b>@{creator.creatorUsername}</b>}</div>
                  <em>الحد الأقصى {creator.maxVideoPostDurationSeconds} ث</em>
                </div>

                <label className={styles.captionTemplate}>
                  <span>وصف فيديو TikTok</span>
                  <textarea rows={6} maxLength={2200} value={title} onChange={(event) => setTitle(event.target.value)} />
                  <small>{title.length}/2200، عدّل الوصف والهاشتاجات قبل كل نشر.</small>
                </label>

                <label className={styles.tiktokPrivacy}>
                  <span>خصوصية الفيديو</span>
                  <select value={privacyLevel} onChange={(event) => setPrivacyLevel(event.target.value as PrivacyLevel | '')}>
                    <option value="">اختر بنفسك</option>
                    {creator.privacyLevelOptions.map((level) => <option value={level} key={level}>{privacyLabel(level)}</option>)}
                  </select>
                </label>

                <fieldset className={styles.tiktokInteractions}>
                  <legend>السماح بالتفاعلات</legend>
                  <TikTokCheck label="التعليقات" checked={allowComment} disabled={creator.commentDisabled} onChange={setAllowComment} />
                  <TikTokCheck label="Duet" checked={allowDuet} disabled={creator.duetDisabled} onChange={setAllowDuet} />
                  <TikTokCheck label="Stitch" checked={allowStitch} disabled={creator.stitchDisabled} onChange={setAllowStitch} />
                </fieldset>

                <label className={styles.tiktokConsent}>
                  <input type="checkbox" checked={consent} onChange={(event) => setConsent(event.target.checked)} />
                  <span><CheckCircle2 size={18} /><b>بنشر الفيديو، أوافق على تأكيد استخدام الموسيقى الخاص بـ TikTok.</b><small>By posting, you agree to TikTok&apos;s Music Usage Confirmation.</small></span>
                </label>

                <div className={styles.youtubeActions}>
                  <button type="button" className={styles.publishNow} onClick={() => void refreshStatus()} disabled={!settings.lastPublishId || busy}><RefreshCw size={17} /> تحديث الحالة</button>
                  <button type="button" className={`${styles.saveSchedule} ${styles.tiktokPublish}`} onClick={publish} disabled={!canPublish}><Send size={17} />{busy ? 'جارٍ البدء…' : 'انشر فيديو الآية على TikTok'}</button>
                </div>
              </>}

              {settings.lastPublishStatus && <div className={styles.tiktokStatus}><span className={isPending(settings.lastPublishStatus) ? styles.statusPulse : ''} /><div><small>حالة آخر فيديو</small><strong>{statusLabel(settings.lastPublishStatus)}</strong></div></div>}
              {settings.lastPublishedAtUtc && <p className={styles.lastPublish}>بدأ آخر نشر: {cairoDateTime(settings.lastPublishedAtUtc)}</p>}
              {(error || settings.lastError) && <p className={styles.youtubeError}>{error || settings.lastError}</p>}
              {message && <p className={styles.youtubeSuccess}>{message}</p>}
            </>}
    </div>
  </section>;
}

function TikTokCheck({ label, checked, disabled, onChange }: {
  label: string;
  checked: boolean;
  disabled: boolean;
  onChange: (checked: boolean) => void;
}) {
  return <label><input type="checkbox" checked={checked} disabled={disabled} onChange={(event) => onChange(event.target.checked)} /><span>{label}</span>{disabled && <small>معطّل في TikTok</small>}</label>;
}

function TikTokGlyph({ small = false }: { small?: boolean }) {
  return <span className={styles.tiktokGlyph} style={{ fontSize: small ? '1rem' : '1.45rem' }} aria-hidden="true">♪</span>;
}

function EmptyState({ title, detail }: { title: string; detail: string }) {
  return <div className={styles.youtubeEmpty}><Link2 size={24} /><strong>{title}</strong><span>{detail}</span></div>;
}

function defaultTitle(selection: VerseSelection) {
  return `هل عرفت الكلمة الناقصة من ${selection.surahName}، الآية ${selection.ayahNumber}؟ ✨\nاكتب إجابتك قبل ظهور النتيجة.\nصلِّ على النبي ﷺ ولا تنسَ المتابعة والإعجاب.\n#أكمل_الآية #القرآن_الكريم #ياسر_الدوسري`;
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
  if (oauthStatus === 'connected') setMessage('تم ربط حساب TikTok بنجاح.');
  if (oauthStatus && oauthStatus !== 'connected') setError('لم يكتمل ربط TikTok. راجع صلاحية video.publish ثم حاول مرة أخرى.');
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

function cairoDateTime(date: string) {
  return new Intl.DateTimeFormat('ar-EG', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'Africa/Cairo',
  }).format(new Date(date));
}
