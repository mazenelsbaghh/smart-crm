'use client';

import { useCallback, useEffect, useMemo, useState, type CSSProperties } from 'react';
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
            <span>الورشة أو الهدف</span>
            <textarea rows={5} maxLength={2000} value={draft.brief} placeholder="مثال: ورشة لفريق خدمة العملاء لكسر الجليد والتدريب على الردود..." onChange={(event) => updateDraft({ ...draft, brief: event.target.value })} />
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
          <p className={styles.gameHint}>الظهر ثابت داخل اللعبة، والوجه مختلف لكل كارت. التصميم بزوايا مستقيمة ويستخدم لوجو وألوان المشروع تلقائيًا.</p>
          <button type="button" className={styles.btnPrimary} disabled={!canManage || !readiness || draft.brief.trim().length < 10 || draft.cardCount < 8 || draft.cardCount > 60 || Boolean(busy)} onClick={() => void create()}>
            {busy === 'create' ? <LoaderCircle className={styles.spin} size={17} /> : <Plus size={17} />} نفّذ اللعبة والكروت
          </button>
        </aside>

        <main className={styles.gameCanvas}>
          {busy === 'load' && !selected ? <div className={styles.gameEmpty}><LoaderCircle className={styles.spin} /><p>بنحمّل الألعاب...</p></div>
            : selected ? <GamePreview detail={selected} />
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

function GamePreview({ detail }: { detail: ContentCardGameDetail }) {
  const palette = useMemo(() => paletteFor(detail.game.brandColors), [detail.game.brandColors]);
  const style = {
    '--game-ink': palette.ink,
    '--game-surface': palette.surface,
    '--game-accent': palette.accent,
    '--game-muted': palette.muted,
  } as CSSProperties;
  return <div className={styles.gamePreview} style={style}>
    <header className={styles.gamePreviewHeader}>
      <div><span>لعبة جاهزة</span><h2>{detail.game.title}</h2><p>{detail.game.mechanic}</p></div>
      <button type="button" className={styles.btnSecondary} onClick={() => window.print()}><Printer size={17} /> طباعة / حفظ PDF</button>
    </header>
    <details className={styles.gameInstructions}><summary>طريقة اللعب</summary><p>{detail.game.instructions}</p></details>
    <section className={styles.gamePrintArea} aria-label={`كروت ${detail.game.title}`}>
      <div className={styles.gameBackSample}><GameLogo url={detail.game.logoUrl} alt="لوجو المشروع" /><strong>{detail.game.title}</strong><span>WORKSHOP CARDS</span></div>
      {detail.cards.map((card) => <article className={styles.gameCardFace} key={card.id}>
        <header><span>{card.category || 'تحدّي'}</span><b>{String(card.cardIndex + 1).padStart(2, '0')}</b></header>
        <div><h3>{card.title}</h3><p>{card.prompt}</p></div>
        {card.instruction && <footer>{card.instruction}</footer>}
      </article>)}
    </section>
    <section className={styles.gameBackPrint} aria-label="ظهور الكروت للطباعة">
      {detail.cards.map((card) => <div className={styles.gameBackSample} key={`back-${card.id}`}><GameLogo url={detail.game.logoUrl} alt="" /><strong>{detail.game.title}</strong><span>WORKSHOP CARDS</span></div>)}
    </section>
  </div>;
}

function GameLogo({ url, alt }: { url: string; alt: string }) {
  const [source, setSource] = useState<string | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    let objectUrl: string | null = null;
    void contentApi.downloadAsset(url, controller.signal).then((blob) => {
      objectUrl = URL.createObjectURL(blob);
      setSource(objectUrl);
    }).catch(() => undefined);
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [url]);
  return source ? <Image src={source} alt={alt} width={180} height={90} unoptimized /> : <Gamepad2 aria-hidden="true" />;
}

function paletteFor(colors: string[]) {
  const valid = colors.filter((color) => /^#[0-9a-f]{6}$/i.test(color));
  return {
    ink: valid[0] ?? '#140D2E',
    surface: valid[2] ?? '#FCFCFC',
    accent: valid[3] ?? valid[1] ?? '#22E9D4',
    muted: valid[4] ?? '#B8B2C8',
  };
}

function message(error: unknown) {
  if (typeof error === 'object' && error && 'response' in error) {
    const response = (error as { response?: { data?: { error?: string } } }).response;
    if (response?.data?.error) return response.data.error;
  }
  return error instanceof Error ? error.message : 'تعذر تنفيذ الطلب. حاول مرة أخرى.';
}
