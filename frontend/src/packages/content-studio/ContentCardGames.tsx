'use client';

import { useCallback, useEffect, useState } from 'react';
import Image from 'next/image';
import { Gamepad2, Lightbulb, LoaderCircle, Palette, Plus, Printer, Sparkles } from 'lucide-react';
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
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async (preferredId?: string) => {
    setBusy('load');
    setError(null);
    try {
      const response = await contentApi.listCardGames();
      setGames(response.games);
      const id = preferredId ?? response.games[0]?.id;
      setSelected(id ? await contentApi.getCardGame(id) : null);
    } catch (requestError) {
      setError(message(requestError));
    } finally {
      setBusy(null);
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  useEffect(() => {
    if (!selected || !['Queued', 'Generating', 'Stopping'].includes(selected.game.designStatus)) return;
    const timer = window.setTimeout(() => void load(selected.game.id), 5_000);
    return () => window.clearTimeout(timer);
  }, [load, selected]);

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
      {(notice || error) && <div className={error ? styles.alertError : styles.alertSuccess} role={error ? 'alert' : 'status'}>{error ?? notice}</div>}

      <section className={styles.gameReadiness} aria-label="جاهزية إنشاء الألعاب">
        <span><Sparkles size={16} /> Gemini <strong>{aiReady ? 'جاهز' : 'أضف المفتاح'}</strong></span>
        <span><Palette size={16} /> هوية المشروع <strong>{brandReady ? 'مربوطة' : 'ارفع اللوجو'}</strong></span>
        <span><Gamepad2 size={16} /> الألعاب <strong>{games.length}</strong></span>
      </section>

      <div className={styles.gameWorkspace}>
        <aside className={styles.gameComposer} aria-label="إنشاء لعبة جديدة">
          <div className={styles.panelHeading}><div><span>01</span><h2>لعبة جديدة</h2></div></div>
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
          <p className={styles.gameHint}>لعبة كوتشينة للـ English Club، بقواعد بالعربي وكروت بالإنجليزي. بعد مراجعة القواعد، ابدأ تصميم الوش والظهر كصور كاملة بالكلام والرموز ولوجو المشروع.</p>
          <button type="button" className={styles.btnPrimary} disabled={!canManage || !readiness || draft.brief.trim().length < 10 || draft.cardCount < 8 || draft.cardCount > 60 || Boolean(busy)} onClick={() => void create()}>
            {busy === 'create' ? <LoaderCircle className={styles.spin} size={17} /> : <Plus size={17} />} أنشئ اللعبة وراجع القواعد
          </button>
        </aside>

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
            : <div className={styles.gameEmpty}><Gamepad2 size={38} /><h2>أول لعبة تبدأ من فكرة</h2><p>اكتب هدف الورشة، خلّي Gemini يقترح أفكارًا، ثم نفّذ اللعبة كاملة.</p></div>}
        </main>

        <aside className={styles.gameHistory} aria-label="الألعاب المحفوظة">
          <div className={styles.documentHistoryHeading}><h2>الألعاب</h2><span>{games.length}</span></div>
          {games.map((game) => <button type="button" key={game.id} className={selected?.game.id === game.id ? styles.gameHistoryActive : undefined} onClick={async () => {
            setBusy('load');
            setError(null);
            try { setSelected(await contentApi.getCardGame(game.id)); } catch (requestError) { setError(message(requestError)); }
            finally { setBusy(null); }
          }}><Gamepad2 size={16} /><span><strong>{game.title}</strong><small>{game.cardCount} كارت</small></span></button>)}
        </aside>
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
    <details className={styles.gameInstructions} open><summary>طريقة اللعب — شرح بالعربي</summary><p>{detail.game.instructions}</p></details>
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
