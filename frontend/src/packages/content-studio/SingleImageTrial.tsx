'use client';

import Image from 'next/image';
import { useEffect, useId, useRef, useState } from 'react';
import { Download, LoaderCircle, Sparkles } from 'lucide-react';
import type { ContentImageTrial } from './types';
import styles from './SingleImageTrial.module.css';

export default function SingleImageTrial({ title, promptLabel, ready, disabled, portrait = false, generate, onDirtyChange, onBusyChange }: {
  title: string;
  promptLabel: string;
  ready: boolean;
  disabled: boolean;
  portrait?: boolean;
  generate: (prompt: string, signal: AbortSignal) => Promise<ContentImageTrial>;
  onDirtyChange?: (dirty: boolean) => void;
  onBusyChange?: (busy: boolean) => void;
}) {
  const headingId = useId();
  const [prompt, setPrompt] = useState('');
  const [preview, setPreview] = useState<ContentImageTrial | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const controller = useRef<AbortController | null>(null);
  useEffect(() => () => controller.current?.abort(), []);

  const tryImage = async () => {
    controller.current = new AbortController();
    const signal = controller.current.signal;
    setBusy(true); setPreview(null); setError(null); onBusyChange?.(true);
    try { const generated = await generate(prompt.trim(), signal); if (!signal.aborted) setPreview(generated); }
    catch (reason) {
      if (!signal.aborted) {
        const failure = reason as { response?: { data?: { error?: string } }; message?: string };
        setError(failure.response?.data?.error ?? failure.message ?? 'تعذر توليد الصورة. جرّب مرة أخرى.');
      }
    } finally { setBusy(false); onBusyChange?.(false); }
  };

  return <section className={styles.trial} aria-labelledby={headingId} aria-busy={busy}>
    <h3 id={headingId}><Sparkles size={20} aria-hidden="true" />{title}</h3>
    <p>معاينة واحدة بدقة أخف للتجربة. تقدر تنزّل الصورة بعد ما تجهز.</p>
    <label>{promptLabel}<textarea rows={3} maxLength={6000} value={prompt} disabled={disabled || busy}
      onChange={event => { setPrompt(event.target.value); setPreview(null); setError(null); onDirtyChange?.(Boolean(event.target.value.trim())); }} /></label>
    <div className={styles.actions}>
      <button type="button" disabled={disabled || busy || !ready || !prompt.trim()} onClick={() => void tryImage()}>
        {busy ? <LoaderCircle className={styles.spin} size={17} /> : <Sparkles size={17} />}{busy ? 'جارٍ توليد الصورة…' : 'جرّب صورة واحدة'}
      </button>
      {(prompt || preview) && <button type="button" className={styles.clear} disabled={busy || disabled}
        onClick={() => { setPrompt(''); setPreview(null); setError(null); onDirtyChange?.(false); }}>مسح التجربة</button>}
    </div>
    {!ready && <small>اختار الموديل وجهّز مفتاحه للتجربة{portrait ? '، واعتمد الفريم أولاً' : ''}.</small>}
    {error && <p role="alert" className={styles.error}>{error}</p>}
    {preview && <figure>
      <Image src={preview.image} alt={`معاينة ${title}`} width={portrait ? 768 : 1024} height={1024} unoptimized />
      <figcaption>{preview.model} · {preview.size}</figcaption>
      <a href={preview.image} download={`trial-${preview.model}.png`}><Download size={17} />تنزيل الصورة</a>
    </figure>}
  </section>;
}
