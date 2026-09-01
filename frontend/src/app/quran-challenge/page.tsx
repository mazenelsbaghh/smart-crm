'use client';

import { FormEvent, useEffect, useRef, useState } from 'react';
import Image from 'next/image';
import { Check, Copy, Download, Pause, Play, RotateCcw, Sparkles, Volume2 } from 'lucide-react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import styles from './quran-challenge.module.css';
import { YouTubeAutomationPanel } from './youtube-automation';
import { FacebookAutomationPanel } from './facebook-automation';
import { TikTokPublishingPanel } from './tiktok-publishing';

type Verse = {
  surahNumber: number;
  ayahNumber: number;
  surah: string;
  text: string;
  words: string[];
  audioUrl: string | null;
  source?: string;
};

const defaultVerse: Verse = {
  surahNumber: 2,
  ayahNumber: 3,
  surah: 'سُورَةُ البَقَرَةِ',
  text: 'الَّذِينَ يُؤْمِنُونَ بِالْغَيْبِ وَيُقِيمُونَ الصَّلَاةَ وَمِمَّا رَزَقْنَاهُمْ يُنْفِقُونَ',
  words: ['الَّذِينَ', 'يُؤْمِنُونَ', 'بِالْغَيْبِ', 'وَيُقِيمُونَ', 'الصَّلَاةَ', 'وَمِمَّا', 'رَزَقْنَاهُمْ', 'يُنْفِقُونَ'],
  audioUrl: 'https://everyayah.com/data/Yasser_Ad-Dussary_128kbps/002003.mp3',
  source: 'Al Quran Cloud، إصدار quran-simple',
};

const PREVIEW_PAGE_SIZE = 7;

function createVersePages(words: string[]) {
  return Array.from({ length: Math.ceil(words.length / PREVIEW_PAGE_SIZE) }, (_, pageIndex) => {
    const startIndex = pageIndex * PREVIEW_PAGE_SIZE;
    return { startIndex, words: words.slice(startIndex, startIndex + PREVIEW_PAGE_SIZE) };
  });
}

export default function QuranChallengePage() {
  const { user, activeProject, loading: authLoading } = useAuth();
  const [verse, setVerse] = useState(defaultVerse);
  const [surahNumber, setSurahNumber] = useState(2);
  const [ayahNumber, setAyahNumber] = useState(3);
  const [hiddenWordIndex, setHiddenWordIndex] = useState(Math.floor(verse.words.length / 2));
  const [selected, setSelected] = useState<number | null>(null);
  const [revealed, setRevealed] = useState(false);
  const [playing, setPlaying] = useState(false);
  const [copied, setCopied] = useState(false);
  const [copyError, setCopyError] = useState('');
  const [audioError, setAudioError] = useState('');
  const [loadingVerse, setLoadingVerse] = useState(false);
  const [renderingVideo, setRenderingVideo] = useState(false);
  const [verseError, setVerseError] = useState('');
  const [renderError, setRenderError] = useState('');
  const [loadConfirmation, setLoadConfirmation] = useState('');
  const [activePageIndex, setActivePageIndex] = useState(0);
  const [timezoneState, setTimezoneState] = useState<{ projectId: string; timezone: string | null; error: string | null } | null>(null);
  const audioRef = useRef<HTMLAudioElement>(null);
  const canManageAutomation = user?.role === 'Owner' || user?.role === 'Admin';
  const projectTimezone = timezoneState && timezoneState.projectId === activeProject?.id ? timezoneState.timezone : null;
  const automationTimezoneReady = Boolean(
    timezoneState
    && timezoneState.projectId === activeProject?.id
    && (timezoneState.timezone || timezoneState.error),
  );

  useEffect(() => {
    if (!activeProject) return;
    const projectId = activeProject.id;
    const controller = new AbortController();
    const frame = window.requestAnimationFrame(async () => {
      setTimezoneState({ projectId, timezone: null, error: null });
      try {
        const response = await api.get<{ settings?: { timezone?: string } | null }>(`/api/projects/${projectId}`, { signal: controller.signal });
        const timezone = response.data.settings?.timezone?.trim();
        if (!timezone) throw new Error('PROJECT_TIMEZONE_MISSING');
        new Intl.DateTimeFormat('en', { timeZone: timezone }).format();
        setTimezoneState({ projectId, timezone, error: null });
      } catch (error) {
        if (controller.signal.aborted) return;
        console.error('Failed to load Quran automation timezone', error);
        setTimezoneState({ projectId, timezone: null, error: 'تعذّر تحميل توقيت المشروع. سنعرض المواعيد بتوقيت UTC بوضوح ولن نخمن توقيتًا محليًا.' });
      }
    });
    return () => { controller.abort(); window.cancelAnimationFrame(frame); };
  }, [activeProject]);

  const versePages = createVersePages(verse.words);
  const activePage = versePages[Math.min(activePageIndex, versePages.length - 1)];

  const correctAnswer = verse.words[hiddenWordIndex];
  const answers = [
    correctAnswer,
    ...Array.from(new Set(verse.words.filter((word, index) => index !== hiddenWordIndex && word !== correctAnswer))).slice(0, 2),
  ].sort((left, right) => left.localeCompare(right, 'ar'));
  const answer = answers.indexOf(verse.words[hiddenWordIndex]);

  const resetChallenge = () => {
    setSelected(null);
    setRevealed(false);
    setPlaying(false);
    setAudioError('');
    setActivePageIndex(0);
    audioRef.current?.pause();
    if (audioRef.current) audioRef.current.currentTime = 0;
  };

  const loadVerse = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setLoadingVerse(true);
    setVerseError('');
    setLoadConfirmation('');

    try {
      const response = await fetch(`/api/quran/verses/${surahNumber}/${ayahNumber}`);
      const payload = await response.json();
      if (!response.ok) throw new Error(payload.error ?? 'تعذر تحميل الآية.');

      setVerse(payload);
      setHiddenWordIndex(Math.floor(payload.words.length / 2));
      resetChallenge();
      setLoadConfirmation(`تم تحميل ${payload.surah}، الآية ${payload.ayahNumber}.`);
    } catch (error) {
      setVerseError(error instanceof Error ? error.message : 'تعذر تحميل الآية.');
    } finally {
      setLoadingVerse(false);
    }
  };

  const toggleRecitation = async () => {
    const audio = audioRef.current;
    if (!audio || !verse.audioUrl) {
      setAudioError('لا يتوفر تسجيل صوتي لهذه الآية.');
      return;
    }
    if (audio.paused) {
      try {
        setAudioError('');
        await audio.play();
        setPlaying(true);
      } catch {
        setPlaying(false);
        setAudioError('تعذر تشغيل التلاوة. تحقق من الاتصال ثم أعد المحاولة.');
      }
      return;
    }
    audio.pause();
    setPlaying(false);
  };

  const syncPreviewPage = () => {
    const audio = audioRef.current;
    if (!audio?.duration || versePages.length <= 1) return;
    setActivePageIndex(Math.min(versePages.length - 1, Math.floor((audio.currentTime / audio.duration) * versePages.length)));
  };

  const downloadVideo = async () => {
    setRenderingVideo(true);
    setRenderError('');
    try {
      const response = await fetch('/quran-video/render', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ surahNumber: verse.surahNumber, ayahNumber: verse.ayahNumber, hiddenWordIndex }),
      });
      if (!response.ok) {
        const payload = await response.json().catch(() => null) as { error?: string } | null;
        throw new Error(payload?.error ?? 'تعذّر إنشاء الفيديو.');
      }

      const videoUrl = URL.createObjectURL(await response.blob());
      const downloadLink = document.createElement('a');
      downloadLink.href = videoUrl;
      downloadLink.download = `akmel-alaya-${verse.surahNumber}-${verse.ayahNumber}.mp4`;
      document.body.appendChild(downloadLink);
      downloadLink.click();
      downloadLink.remove();
      window.setTimeout(() => URL.revokeObjectURL(videoUrl), 1000);
    } catch (error) {
      setRenderError(error instanceof Error ? error.message : 'تعذّر إنشاء الفيديو.');
    } finally {
      setRenderingVideo(false);
    }
  };

  const copyCaption = async () => {
    setCopyError('');
    try {
      if (!navigator.clipboard) throw new Error('clipboard-unavailable');
      await navigator.clipboard.writeText(`هل عرفت الكلمة الناقصة من ${verse.surah}؟ ✨\nاكتب إجابتك قبل ما تظهر النتيجة.\n#أكمل_الآية #القرآن_الكريم #تدبر`);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1800);
    } catch {
      setCopied(false);
      setCopyError('تعذر النسخ تلقائيًا. حدّد الوصف وانسخه يدويًا.');
    }
  };

  return (
    <main className={styles.page}>
      <section className={styles.intro}>
        <a className={styles.brand} href="#studio" aria-label="أكمل الآية">
          <Image
            className={styles.brandLogo}
            src="/quran-challenge/icon.png"
            alt="شعار أكمل الآية للقرآن الكريم"
            width={1024}
            height={1024}
            priority
          />
          <span>أكمل الآية</span>
        </a>
        <div className={styles.introCopy}>
          <p className={styles.eyebrow}>استوديو تجارب الحلقات</p>
          <h1>اختر آية،<br /><em>واخفِ كلمة واحدة.</em></h1>
          <p className={styles.lede}>نص عربي مبسّط من مصدر الآيات الموضح أدناه، وكلمة وسطية فقط للاختبار. الآيات الأقل من ثلاث كلمات لا تدخل في التحدي.</p>
        </div>
        <div className={styles.manifesto}><Sparkles size={18} /><span>التجربة أولاً، ثم إخراج الفيديو.</span></div>
      </section>

      <section className={styles.studio} id="studio" aria-label="معاينة قالب الفيديو">
        <div className={styles.previewColumn}>
          <div className={styles.previewHeader}><span>المعاينة الحية</span><span className={styles.liveDot}>قالب 09:16</span></div>
          <div className={styles.phoneFrame}>
            <div className={styles.reel}>
              <div className={styles.orbitOne} /><div className={styles.orbitTwo} />
              <div className={styles.reelTop}><span className={styles.series}>أكمل الآية</span><span className={styles.episode}>تجربة</span></div>
              <div className={styles.reelCore}>
                <p className={styles.prompt}>ما الكلمة الناقصة؟</p>
                <div className={styles.ayah}>
                  {activePage.words.map((word, localIndex) => {
                    const wordIndex = activePage.startIndex + localIndex;
                    return wordIndex === hiddenWordIndex
                      ? <strong key={`${word}-${wordIndex}`}>{revealed ? word : 'ــــــــــــ'}</strong>
                      : <span key={`${word}-${wordIndex}`}>{word}</span>;
                  })}
                </div>
                {versePages.length > 1 && <div className={styles.pageStepper} aria-label={`الجزء ${activePageIndex + 1} من ${versePages.length}`}>
                  {versePages.map((_, pageIndex) => <button type="button" key={pageIndex} className={pageIndex === activePageIndex ? styles.activePage : ''} onClick={() => setActivePageIndex(pageIndex)} aria-label={`عرض الجزء ${pageIndex + 1}`} />)}
                </div>}
                <div className={styles.options}>
                  {answers.map((option, index) => {
                    const state = revealed ? index === answer ? styles.correct : index === selected ? styles.wrong : '' : index === selected ? styles.selected : '';
                    return <button type="button" className={`${styles.option} ${state}`} key={`${option}-${index}`} onClick={() => !revealed && setSelected(index)} aria-pressed={index === selected}><span>{['أ', 'ب', 'ج'][index]}</span>{option}{revealed && index === answer && <Check size={17} aria-label="الإجابة الصحيحة" />}</button>;
                  })}
                </div>
              </div>
              <div className={styles.reelBottom}><div className={styles.timer}><i /><i /><i /><i /><i /></div><div className={styles.source}>{verse.surah} · الآية {verse.ayahNumber}</div>{revealed && <div className={styles.answerNote}>الإجابة: {verse.words[hiddenWordIndex]}</div>}</div>
            </div>
          </div>
        </div>

        <aside className={styles.controls}>
          <div className={styles.controlHeading}><p className={styles.eyebrow}>إعداد التحدي</p><h2>جرّب آيتك</h2></div>
          <form className={styles.verseForm} onSubmit={loadVerse}>
            <label>رقم السورة<input type="number" min="1" max="114" value={surahNumber} onChange={(event) => setSurahNumber(Number(event.target.value))} /></label>
            <label>رقم الآية<input type="number" min="1" value={ayahNumber} onChange={(event) => setAyahNumber(Number(event.target.value))} /></label>
            <button className={styles.loadButton} disabled={loadingVerse}>{loadingVerse ? 'جارٍ تحميل الآية…' : 'عرض الآية'}</button>
          </form>
          {verseError && <p className={styles.error} role="alert">{verseError}</p>}
          {loadConfirmation && <p className={styles.success} role="status">{loadConfirmation}</p>}
          <label className={styles.wordPicker}>الكلمة المخفية<select value={hiddenWordIndex} onChange={(event) => { setHiddenWordIndex(Number(event.target.value)); resetChallenge(); }}>{verse.words.slice(1, -1).map((word, index) => <option value={index + 1} key={`${word}-${index}`}>{word}</option>)}</select></label>
          <dl className={styles.details}><div><dt>إصدار النص</dt><dd>quran-simple</dd></div><div><dt>مصدر النص</dt><dd>{verse.source ?? 'Al Quran Cloud'}</dd></div><div><dt>مصدر الصوت</dt><dd>EveryAyah، ياسر الدوسري</dd></div><div><dt>الكلمات</dt><dd>{verse.words.length}</dd></div></dl>
          <div className={styles.actions}>
            <button className={styles.primaryAction} onClick={revealed ? resetChallenge : () => selected !== null && setRevealed(true)} disabled={selected === null && !revealed}>{revealed ? <RotateCcw size={19} /> : <Check size={19} />}{revealed ? 'ابدأ من جديد' : 'أظهر الإجابة'}</button>
            <button type="button" className={styles.secondaryAction} onClick={toggleRecitation} disabled={!verse.audioUrl}>{playing ? <Pause size={18} /> : <Play size={18} />}{playing ? 'إيقاف التلاوة' : 'استمع للتلاوة'}<Volume2 size={16} className={styles.volume} /></button>
            <button type="button" className={styles.downloadAction} onClick={downloadVideo} disabled={renderingVideo}><Download size={18} />{renderingVideo ? 'جارٍ تجهيز الفيديو…' : 'تنزيل فيديو الآية الآن'}</button>
          </div>
          {audioError && <p className={styles.error} role="alert">{audioError}</p>}
          {renderError && <p className={styles.error} role="alert">{renderError}</p>}
          {renderingVideo && <p className={styles.renderStatus} role="status">يتم الآن بناء فيديو جديد بالنص والصوت المختارين. قد يستغرق نحو دقيقة.</p>}
          <audio ref={audioRef} src={verse.audioUrl ?? undefined} onTimeUpdate={syncPreviewPage} onError={() => { setPlaying(false); setAudioError('تعذر تحميل ملف التلاوة.'); }} onEnded={() => { setPlaying(false); setActivePageIndex(versePages.length - 1); }} />
          <div className={styles.captionBox}><span>وصف جاهز للنشر</span><p>هل عرفت الكلمة الناقصة؟ ✨<br />اكتب إجابتك قبل ما تظهر النتيجة.</p><button type="button" onClick={copyCaption}>{copied ? <><Check size={16} /> تم النسخ</> : <><Copy size={16} /> نسخ الوصف</>}</button></div>
          {copyError && <p className={styles.error} role="alert">{copyError}</p>}
          <p className={styles.note}>الصوت في الفيديو متصل دون قطع. الآيات الطويلة تُقسَّم تلقائياً إلى أجزاء متتابعة، ثم تظهر الإجابة الصحيحة.</p>
        </aside>
      </section>

      {!authLoading && user && !activeProject && (
        <section className={styles.projectScopeNotice} aria-labelledby="automation-project-title">
          <div>
            <h2 id="automation-project-title">تعذر تحميل مساحة العمل</h2>
            <p>لا توجد مساحة مرتبطة متاحة الآن. أعد تحميل الصفحة أو تواصل مع المدير قبل إعداد النشر.</p>
          </div>
        </section>
      )}

      {!authLoading && user && activeProject && !canManageAutomation && (
        <section className={styles.projectScopeNotice} role="status"><div><h2>أتمتة النشر للعرض الإداري فقط</h2><p>ربط الحسابات أو تغيير الجدولة أو النشر يحتاج دور مالك أو مدير للمشروع.</p></div></section>
      )}
      {!authLoading && user && activeProject && canManageAutomation && !automationTimezoneReady && (
        <section className={styles.projectScopeNotice} role="status"><div><h2>جارٍ تحميل توقيت المشروع</h2><p>سنعرض إعدادات النشر بعد تحديد التوقيت، حتى لا نعرض موعدًا مضللًا.</p></div></section>
      )}
      {timezoneState && timezoneState.projectId === activeProject?.id && timezoneState.error && <p className={styles.error} role="alert">{timezoneState.error}</p>}

      {user && activeProject && canManageAutomation && automationTimezoneReady && <>
        <YouTubeAutomationPanel key={`youtube-${activeProject?.id ?? 'guest'}`} timezone={projectTimezone} selection={{ surahNumber: verse.surahNumber, ayahNumber: verse.ayahNumber, hiddenWordIndex }} />
        <FacebookAutomationPanel key={`facebook-${activeProject?.id ?? 'guest'}`} timezone={projectTimezone} selection={{ surahNumber: verse.surahNumber, ayahNumber: verse.ayahNumber, hiddenWordIndex }} />
        <TikTokPublishingPanel key={`tiktok-${activeProject?.id ?? 'guest'}`} timezone={projectTimezone} selection={{ surahNumber: verse.surahNumber, ayahNumber: verse.ayahNumber, hiddenWordIndex, surahName: verse.surah }} />
      </>}
    </main>
  );
}
