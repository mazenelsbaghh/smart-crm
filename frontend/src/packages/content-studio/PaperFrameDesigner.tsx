'use client';

import Image from 'next/image';
import { useEffect, useState } from 'react';
import { Check, LoaderCircle, RefreshCw, Sparkles, PanelsTopLeft, ImagePlus, UserRound, Palette, ScanEye } from 'lucide-react';
import { contentApi } from './content-api';
import type { OpenAiImageModel, PaperDesignOptions, PaperFrame } from './types';
import styles from './PaperFrameDesigner.module.css';
import ImageModelSelect from './ImageModelSelect';
import SingleImageTrial from './SingleImageTrial';

const referenceExamples = [
  { id: 'ribbons', title: 'شرائط وملاحظات', path: '/paper-references/ribbons.webp' },
  { id: 'stamps', title: 'طوابع وحكايات', path: '/paper-references/stamps.png' },
];

export default function PaperFrameDesigner({ canManage, aiReady, locked, onFrameChange, onDirtyChange }: {
  canManage: boolean; aiReady: boolean; locked: boolean;
  onFrameChange: (token: string | undefined) => void;
  onDirtyChange: (dirty: boolean) => void;
}) {
  const [options, setOptions] = useState<PaperDesignOptions | null>(null);
  const [imageProvider, setImageProvider] = useState<'Gemini' | 'OpenAI'>('Gemini');
  const [openAiImageModel, setOpenAiImageModel] = useState<OpenAiImageModel>('gpt-image-2.5-sunburst');
  const [trialBusy, setTrialBusy] = useState(false);
  const [openAiImageApiKey, setOpenAiImageApiKey] = useState('');
  const [styleId, setStyleId] = useState('ribbons');
  const [paletteId, setPaletteId] = useState('mint');
  const [brief, setBrief] = useState('');
  const [teacherName, setTeacherName] = useState('');
  const [includeTeacher, setIncludeTeacher] = useState(false);
  const [teacher, setTeacher] = useState<File | null>(null);
  const [reference, setReference] = useState<File | null>(null);
  const [exampleId, setExampleId] = useState('ribbons');
  const [frame, setFrame] = useState<PaperFrame | null>(null);
  const [approved, setApproved] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [retry, setRetry] = useState(0);
  const disabled = !canManage || busy || locked || trialBusy;
  const imageReady = imageProvider === 'Gemini' ? aiReady
    : Boolean(options?.openAiImageApiKeyConfigured || openAiImageApiKey.trim());

  useEffect(() => {
    const controller = new AbortController();
    contentApi.getPaperOptions(controller.signal).then(setOptions).catch(reason => {
      if (!controller.signal.aborted) setError(messageOf(reason));
    });
    return () => controller.abort();
  }, [retry]);

  const invalidate = () => {
    setFrame(null); setApproved(false); setError(null);
    onFrameChange(undefined); onDirtyChange(true);
  };

  const generate = async () => {
    setBusy(true); setFrame(null); setError(null); setApproved(false); onFrameChange(undefined); onDirtyChange(true);
    try {
      const form = new FormData();
      form.append('imageProvider', imageProvider);
      form.append('openAiImageModel', openAiImageModel);
      if (imageProvider === 'OpenAI' && openAiImageApiKey.trim()) form.append('openAiImageApiKey', openAiImageApiKey.trim());
      form.append('styleId', styleId); form.append('paletteId', paletteId);
      form.append('brief', brief); form.append('teacherName', teacherName);
      form.append('includeTeacher', String(includeTeacher));
      if (includeTeacher && teacher) form.append('teacher', teacher);
      if (reference) form.append('references', reference);
      const example = referenceExamples.find(example => example.id === exampleId);
      if (example) {
        const response = await fetch(example.path);
        if (!response.ok) throw new Error('تعذر تحميل المرجع. جرّب مرة أخرى.');
        form.append('references', await response.blob(), example.path.split('/').at(-1));
      }
      const generated = await contentApi.generatePaperFrame(form);
      setFrame(generated); setOpenAiImageApiKey('');
      setOptions(current => current && { ...current, openAiImageApiKeyConfigured: generated.openAiImageApiKeyConfigured ?? current.openAiImageApiKeyConfigured });
      onDirtyChange(true);
    } catch (reason) { setError(messageOf(reason)); }
    finally { setBusy(false); }
  };

  return <section className={styles.designer} aria-labelledby="paper-frame-title">
    <div className={styles.setup}>
      <header><span className={styles.step}><PanelsTopLeft size={17} aria-hidden="true" /> هوية الورق</span><h2 id="paper-frame-title">صمّم إطار صفحاتك</h2><p>اختار الأسلوب والألوان، ثم راجع الفريم قبل إضافة الشرح.</p></header>
      {error && <p className={styles.error} role="alert">{error}</p>}
      <ImageModelSelect label="موديل صور الورق" provider={imageProvider} model={openAiImageModel} disabled={disabled} className={styles.field}
        onChange={selection => { invalidate(); setImageProvider(selection.imageProvider); setOpenAiImageModel(selection.openAiImageModel); }} />
      <small>الموديل المختار يستخدم للفريم وكل صفحات الورق بعد اعتماد الفريم.</small>
      {imageProvider === 'OpenAI' && <label className={styles.field}>مفتاح OpenAI للصور
        <input type="password" autoComplete="new-password" dir="ltr" disabled={disabled} maxLength={2048}
          value={openAiImageApiKey} placeholder={options?.openAiImageApiKeyConfigured ? 'المفتاح محفوظ. اكتب مفتاحًا جديدًا لاستبداله' : 'أدخل مفتاح OpenAI API'}
          onChange={event => { invalidate(); setOpenAiImageApiKey(event.target.value); }} />
        <small>{options?.openAiImageApiKeyConfigured ? 'يستخدم نفس المفتاح المحفوظ لصور المشروع.' : 'يحفظ المفتاح مشفّرًا للمشروع عند تصميم الفريم. يحتاج رصيدًا وصلاحية لموديل الصور.'}</small>
      </label>}
      {!options ? <div role="status">جارٍ تحميل الاختيارات… {error && <button type="button" onClick={() => { setError(null); setRetry(retry + 1); }}>إعادة المحاولة</button>}</div> : <>
        <fieldset disabled={disabled} className={styles.options}><legend><PanelsTopLeft size={17} aria-hidden="true" /> أسلوب التصميم</legend>
          {options.styles.map(style => <label key={style.id} className={styles.option}>
            <input type="radio" name="paper-style" value={style.id} checked={styleId === style.id} onChange={() => { invalidate(); setStyleId(style.id); }} />
            <span><strong>{style.name}</strong><small>{style.description}</small></span>
          </label>)}
        </fieldset>
        <fieldset disabled={disabled} className={styles.options}><legend><Palette size={17} aria-hidden="true" /> مجموعة الألوان</legend>
          <div className={styles.palettes}>{options.palettes.map(palette => <label key={palette.id} className={styles.palette}>
            <input type="radio" name="paper-palette" checked={paletteId === palette.id} onChange={() => { invalidate(); setPaletteId(palette.id); }} />
            <span>{palette.name}<span className={styles.swatches} role="img" aria-label={palette.colors.join('، ')}>{palette.colors.map(color => <i key={color} style={{ backgroundColor: color }} />)}</span></span>
          </label>)}</div>
        </fieldset>
      </>}
      <details className={styles.optionalSection}><summary><ImagePlus size={17} aria-hidden="true" /> مراجع الشكل والصور</summary>
      <fieldset disabled={disabled} className={styles.options}><legend className="sr-only">ريفرنس للشكل</legend>
        <div className={styles.examples}>{referenceExamples.map(example => <label key={example.id} className={styles.example}>
          <Image src={example.path} width={160} height={180} alt={`مثال ورق ${example.title}`} />
          <span><input type="radio" name="paper-reference" checked={exampleId === example.id} onChange={() => { invalidate(); setExampleId(example.id); }} />{example.title}</span>
        </label>)}</div>
        <label className={styles.inline}><input type="radio" name="paper-reference" checked={!exampleId} onChange={() => { invalidate(); setExampleId(''); }} />بدون مرجع جاهز</label>
        <label className={styles.field}>مرجع إضافي من جهازك (اختياري)<input type="file" accept="image/png,image/jpeg,image/webp" onChange={event => { invalidate(); setReference(event.target.files?.[0] ?? null); }} /></label>
        <small>نستفيد من التوزيع والزخارف. الأسماء والنصوص والصور الموجودة في الريفرنس لا تُستخدم في ورقك.</small>
      </fieldset>
      </details>
      <details className={styles.optionalSection}><summary><UserRound size={17} aria-hidden="true" /> اسم المدرّس وصورته</summary>
      <fieldset disabled={disabled} className={styles.options}><legend className="sr-only">هوية المدرّس</legend>
        <label className={styles.field}>اسم المدرّس (اختياري)<input maxLength={150} value={teacherName} onChange={event => { invalidate(); setTeacherName(event.target.value); }} /></label>
        <div className={styles.teacherChoice}>
          <label><input type="radio" name="paper-teacher" checked={!includeTeacher} onChange={() => { invalidate(); setIncludeTeacher(false); }} />بدون صورة</label>
          <label><input type="radio" name="paper-teacher" checked={includeTeacher} onChange={() => { invalidate(); setIncludeTeacher(true); }} />بصورة المدرّس</label>
        </div>
        {includeTeacher && <label className={styles.field}>صورة المدرّس<input type="file" accept="image/png,image/jpeg,image/webp" onChange={event => { invalidate(); setTeacher(event.target.files?.[0] ?? null); }} /><small>{teacher?.name ?? 'ارفع صورة واضحة. هنضيفها مع النص بعد اعتماد الفريم.'}</small></label>}
      </fieldset>
      </details>
      <label className={styles.field}>المادة وتوجيهات الشكل (اختياري)<textarea rows={3} maxLength={2000} disabled={disabled} value={brief} placeholder="مثال: مذكرة نحو للثانوية، عناوين واضحة وملاحظات جانبية خفيفة" onChange={event => { invalidate(); setBrief(event.target.value); }} /></label>
      <button className={styles.primary} type="button" disabled={disabled || !imageReady || !options || (includeTeacher && !teacher)} onClick={() => void generate()}>{busy ? <LoaderCircle size={18} className={styles.spin} /> : frame ? <RefreshCw size={18} /> : <Sparkles size={18} />}{busy ? 'جارٍ تصميم الفريم…' : frame ? 'جرّب فريم تاني بنفس الاختيارات' : 'صمّم الفريم'}</button>
      {!imageReady && <p className={styles.error}>{imageProvider === 'OpenAI' ? 'أضف مفتاح OpenAI للصور لتصميم الفريم.' : 'أضف مفتاح Gemini من إعدادات المشروع لتصميم الورق.'}</p>}
    </div>
    <div className={styles.preview} aria-busy={busy}>
      <span className={styles.step}><ScanEye size={17} aria-hidden="true" /> معاينة الفريم</span>
      {frame ? <>
        <Image className={styles.frame} src={frame.image} alt="معاينة الفريم المقترح بدون نص الدرس" width={600} height={800} unoptimized />
        <p>راجع الهوامش والمساحة المتاحة للشرح. صورة المدرّس والنص هيتضافوا في الخطوة التالية.</p>
        <button type="button" className={styles.primary} disabled={disabled || approved} onClick={() => { setApproved(true); onFrameChange(frame.token); }}><Check size={18} />{approved ? 'الفريم معتمد، أضف المحتوى تحت' : 'اعتمد الفريم واستخدمه للورق'}</button>
        <SingleImageTrial key={frame.token} title="جرب ورقة واحدة" promptLabel="محتوى الورقة التجريبية" portrait ready={approved}
          disabled={!canManage || busy || locked} onBusyChange={setTrialBusy} onDirtyChange={() => onDirtyChange(true)}
          generate={(content, signal) => contentApi.previewPaperPage({ frameToken: frame.token, content }, signal)} />

      </> : <div className={styles.empty}><div className={styles.blankSheet}><span /><span /><span /></div><h3>{busy ? 'بنجهّز فريمك…' : 'هنا هيظهر فريمك'}</h3><p>هامش مميز ومساحة هادئة للكلام، بنفس الروح في كل صفحة.</p></div>}
    </div>
  </section>;
}

function messageOf(reason: unknown): string {
  const failure = reason as { response?: { data?: { error?: string } }; message?: string };
  return failure.response?.data?.error ?? failure.message ?? 'تعذر تصميم الفريم. حاول مرة أخرى.';
}
