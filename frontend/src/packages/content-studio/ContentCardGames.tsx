'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import Image from 'next/image';
import { Gamepad2, FolderOpen, Lightbulb, LoaderCircle, Palette, Plus, Printer, RefreshCw, Sparkles } from 'lucide-react';
import { contentApi } from './content-api';
import type { CardGameIdea, ContentCardGameDetail, ContentCardGameSummary, CreateContentCardGame } from './types';
import styles from './ContentStudio.module.css';

const emptyDraft: CreateContentCardGame = { title: '', brief: '', mechanic: '', cardCount: 20 };

export default function ContentCardGames({
  canManage,
  aiReady,
  brandReady,
  onDraftDirtyChange,
}: {
  canManage: boolean;
  aiReady: boolean;
  brandReady: boolean;
  onDraftDirtyChange: (dirty: boolean) => void;
}) {
  const [games, setGames] = useState<ContentCardGameSummary[]>([]);
  const [selected, setSelected] = useState<ContentCardGameDetail | null>(null);
  const [draft, setDraft] = useState<CreateContentCardGame>(emptyDraft);
  const [ideas, setIdeas] = useState<CardGameIdea[]>([]);
  const [busy, setBusy] = useState<'load' | 'ideas' | 'create' | null>('load');
  const [error, setError] = useState<string | null>(null);
  const [loadFailure, setLoadFailure] = useState<{ message: string; gameId?: string } | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const loadRequest = useRef(0);

  const load = useCallback(async (preferredId?: string) => {
    const request = ++loadRequest.current;
    setBusy('load');
    setError(null);
    setLoadFailure(null);
    let requestedId = preferredId;
    try {
      const response = await contentApi.listCardGames();
      if (request !== loadRequest.current) return;
      setGames(response.games);
      requestedId ??= response.games[0]?.id;
      const detail = requestedId ? await contentApi.getCardGame(requestedId) : null;
      if (request === loadRequest.current) setSelected(detail);
    } catch (requestError) {
      if (request === loadRequest.current) setLoadFailure({ message: message(requestError), gameId: requestedId });
    } finally {
      if (request === loadRequest.current) setBusy(null);
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  useEffect(() => {
    if (busy || !selected || !['Queued', 'Generating', 'Stopping'].includes(selected.game.designStatus)) return;
    const timer = window.setTimeout(() => void load(selected.game.id), 5_000);
    return () => window.clearTimeout(timer);
  }, [busy, load, selected]);

  const selectGame = async (id: string) => {
    const request = ++loadRequest.current;
    setBusy('load');
    setError(null);
    setLoadFailure(null);
    try {
      const detail = await contentApi.getCardGame(id);
      if (request === loadRequest.current) setSelected(detail);
    } catch (requestError) {
      if (request === loadRequest.current) setLoadFailure({ message: message(requestError), gameId: id });
    } finally {
      if (request === loadRequest.current) setBusy(null);
    }
  };

  const updateDraft = (next: CreateContentCardGame) => {
    setDraft(next);
    onDraftDirtyChange(Boolean(next.title?.trim() || next.brief.trim() || next.mechanic?.trim()));
  };

  const suggest = async () => {
    setBusy('ideas');
    setError(null);
    setNotice(null);
    try {
      const response = await contentApi.suggestCardGames(draft.brief);
      setIdeas(response.ideas);
      setNotice('جهّزنا أفكارًا مناسبة لهوية المشروع. اختار واحدة أو اكتب فكرتك.');
    } catch (requestError) {
      setError(message(requestError));
    } finally {
      setBusy(null);
    }
  };

  const chooseIdea = (idea: CardGameIdea) => updateDraft({
    title: idea.title,
    brief: idea.summary,
    mechanic: idea.mechanic,
    cardCount: idea.recommendedCardCount,
  });

  const create = async () => {
    setBusy('create');
    setError(null);
    setNotice(null);
    try {
      const response = await contentApi.createCardGame(draft);
      setDraft(emptyDraft);
      setIdeas([]);
      onDraftDirtyChange(false);
      setNotice(response.message);
      await load(response.id);
    } catch (requestError) {
      setError(message(requestError));
    } finally {
      setBusy(null);
    }
  };

  const readiness = aiReady && brandReady;

  return (
    <div id="content-panel-games" role="tabpanel" aria-labelledby="content-tab-games" className={styles.gameStudio}>
      {(notice || error || loadFailure) && <div className={error || loadFailure ? styles.alertError : styles.alertSuccess} role={error || loadFailure ? 'alert' : 'status'}>
        {error ?? loadFailure?.message ?? notice}
        {loadFailure && <button type="button" className={styles.btnSecondary} disabled={Boolean(busy)} onClick={() => void load(loadFailure.gameId)}><RefreshCw size={17} aria-hidden="true" /> إعادة تحميل الألعاب</button>}
      </div>}

      <section className={styles.gameReadiness} aria-label="جاهزية إنشاء الألعاب">
        <span data-ready={aiReady}><Sparkles size={16} aria-hidden="true" /> Gemini <strong>{aiReady ? 'جاهز' : 'أضف المفتاح'}</strong></span>
        <span data-ready={brandReady}><Palette size={16} aria-hidden="true" /> هوية المشروع <strong>{brandReady ? 'مربوطة' : 'ارفع اللوجو'}</strong></span>
        <span><Gamepad2 size={16} /> الألعاب في القائمة <strong>{games.length}</strong></span>
      </section>

      <div className={styles.gameWorkspace}>
        <aside className={styles.gameHistory} aria-label="الألعاب المحفوظة">
          <div className={styles.documentHistoryHeading}><h2><FolderOpen size={18} aria-hidden="true" /> الألعاب المحفوظة</h2><span>{games.length} في أحدث 50</span></div>
          {games.map((game) => <button type="button" key={game.id} className={selected?.game.id === game.id ? styles.gameHistoryActive : undefined} onClick={() => void selectGame(game.id)}><Gamepad2 size={16} /><span><strong>{game.title}</strong><small>{game.cardCount} كارت</small></span></button>)}
        </aside>
        <details className={styles.mediaCreateDisclosure} open={games.length === 0}>
          <summary><Plus size={18} aria-hidden="true" /> إنشاء لعبة جديدة<span>الفكرة وقواعد اللعب</span></summary>
        <aside className={styles.gameComposer} aria-label="إنشاء لعبة جديدة">
          <div className={styles.mediaTaskHeading}><Gamepad2 size={20} aria-hidden="true" /><h2>صمّم تجربة اللعب</h2></div>
          <label className={styles.field}>
            <span>هدف جلسة الـ English Club</span>
            <textarea rows={5} maxLength={2000} value={draft.brief} placeholder="مثال: English Club للمستوى المتوسط؛ نريد لعبة فرق تدرب المشاركين على الكلام بثقة ورواية قصص قصيرة..." onChange={(event) => updateDraft({ ...draft, brief: event.target.value })} />
          </label>
          <button type="button" className={styles.btnSecondary} disabled={!canManage || !readiness || Boolean(busy)} onClick={() => void suggest()}>
            {busy === 'ideas' ? <LoaderCircle className={styles.spin} size={17} /> : <Lightbulb size={17} />} اقترح أفكار بالذكاء الاصطناعي
          </button>

          {ideas.length > 0 && <div className={styles.gameIdeas}>
            {ideas.map((idea) => <button type="button" key={`${idea.title}-${idea.recommendedCardCount}`} onClick={() => chooseIdea(idea)}>
              <strong>{idea.title}</strong><span>{idea.summary}</span><small>{idea.recommendedCardCount} كارت</small>
            </button>)}
          </div>}

          <label className={styles.field}><span>اسم اللعبة</span><input maxLength={200} value={draft.title ?? ''} onChange={(event) => updateDraft({ ...draft, title: event.target.value })} /></label>
          <label className={styles.field}><span>طريقة اللعب</span><textarea rows={4} maxLength={600} value={draft.mechanic ?? ''} onChange={(event) => updateDraft({ ...draft, mechanic: event.target.value })} /></label>
          <label className={styles.field}><span>عدد الكروت</span><input type="number" min={8} max={60} value={draft.cardCount} onChange={(event) => updateDraft({ ...draft, cardCount: Number(event.target.value) })} /></label>
          <p className={styles.gameHint}>راجع القواعد بالعربي أولًا، ثم صمّم وجوه الكروت بالإنجليزي وظهورها بهوية المشروع.</p>
          <button type="button" className={styles.btnPrimary} disabled={!canManage || !readiness || draft.brief.trim().length < 10 || draft.cardCount < 8 || draft.cardCount > 60 || Boolean(busy)} onClick={() => void create()}>
            {busy === 'create' ? <LoaderCircle className={styles.spin} size={17} /> : <Plus size={17} />} أنشئ اللعبة وراجع القواعد
          </button>
        </aside>
        </details>

        <main className={styles.gameCanvas}>
          {busy === 'load' && !selected ? <div className={styles.gameEmpty}><LoaderCircle className={styles.spin} /><p>بنحمّل الألعاب...</p></div>
            : selected ? <GamePreview canManage={canManage} detail={selected} onDesignStop={async () => {
              try {
                const response = await contentApi.stopCardGameDesign(selected.game.id);
                setNotice(response.message);
                await load(selected.game.id);
              } catch (requestError) { setError(message(requestError)); }
            }} onDesignRetry={async () => {
              setBusy('create');
              setError(null);
              try {
                const response = await contentApi.generateCardGameDesign(selected.game.id);
                setNotice(response.message);
                await load(selected.game.id);
              } catch (requestError) { setError(message(requestError)); }
              finally { setBusy(null); }
            }} />
            : loadFailure ? <div className={styles.gameEmpty}><Gamepad2 size={38} aria-hidden="true" /><h2>تعذّر عرض الألعاب المحفوظة</h2><p>أعد التحميل من رسالة الخطأ لعرض الألعاب وقواعدها.</p></div>
            : <div className={styles.gameEmpty}><Gamepad2 size={38} /><h2>أول لعبة تبدأ من فكرة</h2><p>اكتب هدف الورشة، خلّي Gemini يقترح أفكارًا، ثم نفّذ اللعبة كاملة.</p></div>}
        </main>


      </div>
    </div>
  );
}

function GamePreview({
  detail,
  canManage,
  onDesignRetry,
  onDesignStop,
}: {
  detail: ContentCardGameDetail;
  canManage: boolean;
  onDesignRetry: () => Promise<void>;
  onDesignStop: () => Promise<void>;
}) {
  const isDesigning = ['Queued', 'Generating', 'Stopping'].includes(detail.game.designStatus);
  const printReady = detail.game.designStatus === 'Ready' && Boolean(detail.game.backImageUrl) && detail.cards.every(card => Boolean(card.imageUrl));
  return <div className={styles.gamePreview}>
    <header className={styles.gamePreviewHeader}>
      <div><span>{isDesigning ? 'جارٍ تنفيذ طلب التصميم' : printReady ? 'صور الكروت جاهزة للمراجعة' : 'قواعد محفوظة، التصميم غير مكتمل'}</span><h2 dir="ltr">{detail.game.title}</h2><p>{detail.game.mechanic}</p></div>
      <button type="button" disabled={!printReady} className={styles.btnSecondary} onClick={() => window.print()}><Printer size={17} /> طباعة / حفظ PDF</button>
    </header>
    {!printReady && <div className={styles.gameDesignStatus} role="status">
      {isDesigning ? <><LoaderCircle className={styles.spin} size={16} /> {detail.game.designStatus === 'Stopping' ? 'جارٍ الإيقاف بعد طلب الصورة الحالي.' : 'جارٍ تصميم الكروت كاملة، بما فيها الكتابة والرموز والظهر.'}
        {canManage && <button type="button" className={styles.btnSecondary} disabled={detail.game.designStatus === 'Stopping'} onClick={() => void onDesignStop()}>إيقاف التصميم</button>}</>
        : <>{detail.game.designError ?? (detail.game.designStatus === 'Cancelled' ? 'التصميم متوقف. الصور المكتملة محفوظة.' : 'التصميم الجديد صورة كاملة، وليس خلفية مع كلام مضاف. راجع النصوص واللوجو بعد التوليد.')}
          {canManage && <button type="button" className={styles.btnSecondary} onClick={() => void onDesignRetry()}>تصميم الكروت كاملة</button>}</>}
    </div>}
    <details className={styles.gameInstructions} open><summary>طريقة اللعب بالعربي</summary><p>{detail.game.instructions}</p></details>
    <section className={styles.gamePrintArea} aria-label={`كروت ${detail.game.title}`}>
      <GameArtwork url={detail.game.backImageUrl} alt={`ظهر ${detail.game.title}`} />
      {detail.cards.map((card) => <GameArtwork key={card.id} url={card.imageUrl} alt={`${card.cardIndex + 1}. ${card.category}: ${card.title}. ${card.prompt} ${card.instruction}`} />)}
    </section>
    <section className={styles.gameBackPrint} aria-label="ظهور الكروت للطباعة">
      {detail.cards.map((card) => <GameArtwork key={`back-${card.id}`} url={detail.game.backImageUrl} alt={`ظهر ${detail.game.title}`} />)}
    </section>
  </div>;
}

function GameArtwork({ url, alt }: { url: string | null; alt: string }) {
  const source = useAssetSource(url);
  return <article className={styles.gameCompleteCard}>
    {source ? <AssetImage className={styles.gameCompleteImage} source={source} alt={alt} />
      : <span className={styles.gameImagePlaceholder}>{url ? 'تحميل الصورة…' : 'بانتظار التصميم'}</span>}
  </article>;
}

function AssetImage({
  source,
  alt,
  className,
  width = 840,
  height = 1200,
}: {
  source: string;
  alt: string;
  className: string;
  width?: number;
  height?: number;
}) {
  return <Image className={className} src={source} alt={alt} width={width} height={height} unoptimized />;
}

function useAssetSource(url: string | null) {
  const [asset, setAsset] = useState<{ url: string; source: string } | null>(null);
  useEffect(() => {
    if (!url) return;
    const controller = new AbortController();
    let objectUrl: string | null = null;
    void contentApi.downloadAsset(url, controller.signal).then((blob) => {
      objectUrl = URL.createObjectURL(blob);
      setAsset({ url, source: objectUrl });
    }).catch(() => undefined);
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [url]);
  return asset?.url === url ? asset.source : null;
}

function message(error: unknown) {
  if (typeof error === 'object' && error && 'response' in error) {
    const response = (error as { response?: { data?: { error?: string } } }).response;
    if (response?.data?.error) return response.data.error;
  }
  return error instanceof Error ? error.message : 'تعذر تنفيذ الطلب. حاول مرة أخرى.';
}
