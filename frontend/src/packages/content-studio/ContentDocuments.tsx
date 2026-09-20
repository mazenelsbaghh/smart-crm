'use client';

import Image from 'next/image';
import { useCallback, useEffect, useRef, useState } from 'react';
import { ArrowDown, ArrowUp, ChevronsUp, Download, FileText, LoaderCircle, MonitorPlay, Plus, RefreshCw, Save, Sparkles } from 'lucide-react';
import { contentApi } from './content-api';
import type { ContentDocumentCountSuggestion, ContentDocumentDetail, ContentDocumentKind, ContentDocumentPreview, ContentDocumentSummary } from './types';
import styles from './ContentStudio.module.css';

export default function ContentDocuments({ canManage, brandReady, aiReady, onDraftDirtyChange }: {
  canManage: boolean;
  brandReady: boolean;
  aiReady: boolean;
  onDraftDirtyChange: (dirty: boolean) => void;
}) {
  const [documents, setDocuments] = useState<ContentDocumentSummary[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [detail, setDetail] = useState<ContentDocumentDetail | null>(null);
  const [kind, setKind] = useState<ContentDocumentKind>('Presentation');
  const [content, setContent] = useState('');
  const [suggestion, setSuggestion] = useState<(ContentDocumentCountSuggestion & { kind: ContentDocumentKind; content: string }) | null>(null);
  const [countOverride, setCountOverride] = useState<string | null>(null);
  const [coverTitle, setCoverTitle] = useState('');
  const [preview, setPreview] = useState<{ key: string; value: ContentDocumentPreview } | null>(null);
  const draftVersion = useRef(0);
  const previewHeading = useRef<HTMLHeadingElement>(null);
  const previewController = useRef<AbortController | null>(null);
  const [suggestionError, setSuggestionError] = useState<string | null>(null);
  const [suggestionRetry, setSuggestionRetry] = useState(0);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pageDrafts, setPageDrafts] = useState<Record<string, { title: string; body: string }>>({});
  const [addingPage, setAddingPage] = useState(false);
  const [newPage, setNewPage] = useState({ title: '', body: '' });
  const [beforePageId, setBeforePageId] = useState('');
  const detailRequest = useRef(0);
  const dirtyPages = new Set(Object.keys(pageDrafts));
  const editableDetail = detail && { ...detail, pages: detail.pages.map(page => ({ ...page, ...pageDrafts[page.id] })) };
  const activeGeneration = documents.some(document => ['Planning', 'GeneratingImages'].includes(document.status));
  const pageDraftDirty = dirtyPages.size > 0 || Boolean(newPage.title.trim() || newPage.body.trim());
  const draftDirty = content.trim().length > 0 || pageDraftDirty;
  const validContent = content.trim().length >= 80 && content.length <= 60000;
  const currentSuggestion = suggestion?.kind === kind && suggestion.content === content ? suggestion : null;
  const sections = currentSuggestion?.fileSections ?? currentSuggestion?.sections ?? [];
  const separateSessions = Boolean(currentSuggestion?.sessionCount && sections.length);
  const minimumCount = separateSessions ? Math.max(...sections.map(section => section.minPageCount + 1)) : currentSuggestion?.minPageCount ?? 2;
  const maximumCount = separateSessions ? Math.min(Math.floor(200 / sections.length), ...sections.map(section => section.maxPageCount + 1)) : currentSuggestion?.maxPageCount ?? 200;
  const recommendedCount = separateSessions ? Math.min(maximumCount, Math.max(...sections.map(section => section.pageCount + 1))) : currentSuggestion?.pageCount ?? 0;
  const selectedCount = countOverride === null ? recommendedCount : Number(countOverride);
  const validCount = Boolean(currentSuggestion && Number.isInteger(selectedCount) && selectedCount >= minimumCount
    && selectedCount <= maximumCount && (!separateSessions || sections.length <= 50));
  const pageCount = separateSessions ? 0 : selectedCount;
  const previewRequest = { kind, content, pageCount, coverTitle: separateSessions ? '' : coverTitle,
    ...(separateSessions ? { pagesPerSession: selectedCount } : {}) };
  const draftKey = JSON.stringify(previewRequest);
  const currentPreview = preview?.key === draftKey ? preview.value : null;

  const invalidatePreview = () => { previewController.current?.abort(); draftVersion.current++; setPreview(null); setError(null); setNotice(null); };
  const changeKind = (value: ContentDocumentKind) => {
    if (value === kind) return;
    invalidatePreview(); setKind(value); setCountOverride(null); setSuggestionError(null);
  };

  useEffect(() => {
    if (!validContent || !canManage) return;
    const controller = new AbortController();
    const timer = window.setTimeout(async () => {
      try {
        const response = await contentApi.suggestDocumentPageCount({ kind, content }, controller.signal);
        if (!controller.signal.aborted) setSuggestion({ kind, content, ...response });
      } catch (requestError) {
        if (!controller.signal.aborted) setSuggestionError(messageOf(requestError));
      }
    }, 350);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [canManage, content, kind, suggestionRetry, validContent]);

  useEffect(() => () => previewController.current?.abort(), []);

  useEffect(() => { if (currentPreview) previewHeading.current?.focus(); }, [currentPreview]);

  useEffect(() => onDraftDirtyChange(draftDirty), [draftDirty, onDraftDirtyChange]);

  const loadDocuments = useCallback(async (signal?: AbortSignal) => {
    try {
      const response = await contentApi.getDocuments(signal);
      if (signal?.aborted) return;
      setDocuments(response.documents);
      setSelectedId(current => current ?? response.documents[0]?.id ?? null);
    } catch (requestError) {
      if (!signal?.aborted) setError(messageOf(requestError));
    }
  }, []);

  const loadDetail = useCallback(async (id: string, signal?: AbortSignal) => {
    const request = ++detailRequest.current;
    try {
      const response = await contentApi.getDocument(id, signal);
      if (!signal?.aborted && request === detailRequest.current) setDetail(response);
    } catch (requestError) {
      if (!signal?.aborted && request === detailRequest.current) setError(messageOf(requestError));
    }
  }, []);

  useEffect(() => { const controller = new AbortController(); const timer = window.setTimeout(() => void loadDocuments(controller.signal), 0); return () => { window.clearTimeout(timer); controller.abort(); }; }, [loadDocuments]);
  useEffect(() => { if (!selectedId) return; const controller = new AbortController(); const timer = window.setTimeout(() => void loadDetail(selectedId, controller.signal), 0); return () => { window.clearTimeout(timer); controller.abort(); }; }, [loadDetail, selectedId]);
  useEffect(() => {
    if (busy) return;
    if (!activeGeneration && !detail?.pages.some(page => ['Queued', 'GeneratingImage'].includes(page.status))) return;
    const controller = new AbortController();
    const interval = window.setInterval(async () => {
      await loadDocuments(controller.signal);
      if (!controller.signal.aborted && selectedId) await loadDetail(selectedId, controller.signal);
    }, 4000);
    return () => { window.clearInterval(interval); controller.abort(); };
  }, [activeGeneration, busy, detail?.pages, loadDetail, loadDocuments, selectedId]);

  const showPreview = async () => {
    if (!validContent || !validCount || !aiReady || pageDraftDirty) return;
    const controller = new AbortController();
    previewController.current = controller;
    const version = draftVersion.current;
    setBusy('preview'); setError(null); setNotice(null);
    try {
      const response = await contentApi.previewDocument(previewRequest, controller.signal);
      if (!controller.signal.aborted && version === draftVersion.current) setPreview({ key: draftKey, value: response });
    } catch (requestError) { if (!controller.signal.aborted && version === draftVersion.current) setError(messageOf(requestError)); }
    finally { setBusy(null); }
  };

  const create = async () => {
    if (!currentPreview || !validCount || pageDraftDirty) return;
    setBusy('create'); setError(null); setNotice(null);
    try {
      const response = await contentApi.createDocument({ ...previewRequest, previewFingerprint: currentPreview.fingerprint });
      setContent(''); setCoverTitle(''); setCountOverride(null); setPreview(null);
      setSelectedId(response.id);
      setNotice(response.message);
      await loadDocuments();
    } catch (requestError) { setError(messageOf(requestError)); }
    finally { setBusy(null); }
  };

  const updatePage = (pageId: string, field: 'title' | 'body', value: string) => {
    const page = detail?.pages.find(candidate => candidate.id === pageId);
    if (!page) return;
    setPageDrafts(current => ({ ...current, [pageId]: { ...(current[pageId] ?? { title: page.title, body: page.body }), [field]: value } }));
  };

  const savePage = async (pageId: string) => {
    const page = editableDetail?.pages.find(item => item.id === pageId);
    if (!detail || !page) return;
    setBusy(`save-${pageId}`); setError(null); setNotice(null);
    try {
      await contentApi.updateDocumentPage(detail.document.id, page.id, page.title, page.body);
      setDetail(current => current && { ...current, document: { ...current.document, status: 'AwaitingDesign' },
        pages: current.pages.map(saved => saved.id === page.id ? { ...saved, title: page.title, body: page.body, status: 'Planned' } : saved) });
      setPageDrafts(current => { const remaining = { ...current }; delete remaining[pageId]; return remaining; });
      await contentApi.regenerateDocumentPageImage(detail.document.id, page.id);
      setNotice(`تم حفظ النص وبدأ Gemini إعادة تصميم الصفحة ${page.pageIndex + 1}.`);
    } catch (requestError) { setError(messageOf(requestError)); }
    finally { await loadDetail(detail.document.id); setBusy(null); }
  };

  const addPage = async () => {
    if (!detail) return;
    const documentId = detail.document.id;
    setBusy('add-page'); setError(null); setNotice(null);
    try {
      const added = await contentApi.addDocumentPage(documentId, { ...newPage, beforePageId: beforePageId || null });
      setDetail(current => {
        if (!current) return current;
        const pages = [...current.pages];
        pages.splice(added.pageIndex, 0, { ...newPage, id: added.id, pageIndex: added.pageIndex, status: 'Planned' });
        return { document: { ...current.document, status: 'AwaitingDesign', requestedPageCount: pages.length },
          pages: pages.map((page, pageIndex) => ({ ...page, pageIndex })) };
      });
      setNewPage({ title: '', body: '' }); setAddingPage(false); setBeforePageId('');
      setNotice('تمت إضافة السلايد وحفظ محتواه.');
      const response = await contentApi.regenerateDocumentPageImage(documentId, added.id);
      setNotice(response.message);
    } catch (requestError) { setError(messageOf(requestError)); }
    finally { await loadDocuments(); await loadDetail(documentId); setBusy(null); }
  };

  const movePage = async (pageId: string, to: number) => {
    if (!detail) return;
    const pageIds = detail.pages.map(page => page.id);
    const from = pageIds.indexOf(pageId);
    if (from < 0 || to < 0 || to >= pageIds.length) return;
    pageIds.splice(from, 1);
    pageIds.splice(to, 0, pageId);
    setBusy(`move-${pageId}`); setError(null); setNotice(null);
    try {
      await contentApi.reorderDocumentPages(detail.document.id, pageIds);
      setDetail(current => current && { ...current, pages: pageIds.map((id, pageIndex) => ({ ...current.pages.find(page => page.id === id)!, pageIndex })) });
      setNotice(`اتحفظ الترتيب. السلايد بقى رقم ${to + 1}.`);
    } catch (requestError) { setError(messageOf(requestError)); }
    finally { await loadDetail(detail.document.id); setBusy(null); }
  };

  const regenerate = async (pageId: string) => {
    if (!detail) return;
    setBusy(`image-${pageId}`); setError(null);
    try {
      const response = await contentApi.regenerateDocumentPageImage(detail.document.id, pageId);
      setNotice(response.message);
      await loadDetail(detail.document.id);
    } catch (requestError) { setError(messageOf(requestError)); }
    finally { setBusy(null); }
  };

  const regenerateAll = async () => {
    if (!detail) return;
    setBusy('images-all'); setError(null);
    try {
      const response = await contentApi.regenerateDocumentImages(detail.document.id);
      setNotice(response.message);
      await loadDocuments();
      await loadDetail(detail.document.id);
    } catch (requestError) { setError(messageOf(requestError)); }
    finally { setBusy(null); }
  };

  const exportFile = async (format: 'pdf' | 'pptx' | 'docx') => {
    if (!detail || dirtyPages.size) return;
    setBusy(`export-${format}`); setError(null);
    try {
      const exporters = await import('./document-export');
      if (format === 'pdf') await exporters.exportPdf(detail);
      else if (format === 'pptx') await exporters.exportPresentationPptx(detail);
      else await exporters.exportDocumentDocx(detail);
    } catch (requestError) { setError(messageOf(requestError)); }
    finally { setBusy(null); }
  };

  const readinessMessage = !aiReady ? 'أضف مفتاح Gemini من إعدادات المشروع.' : !brandReady ? 'ارفع اللوجو من تبويب الصور والمنشورات أولاً.' : null;
  return <div id="content-panel-documents" role="tabpanel" aria-labelledby="content-tab-documents" className={styles.documentStudio}>
    {(error || notice) && <div className={error ? styles.alertError : styles.alertSuccess} role={error ? 'alert' : 'status'}>{error ?? notice}</div>}
    <div className={styles.documentWorkspace}>
      <aside className={styles.documentComposer} aria-label="إنشاء عرض أو مستند">
        <div className={styles.panelHeading}><div><span>01</span><h2>ملف جديد</h2></div></div>
        <div className={styles.kindSwitch} role="radiogroup" aria-label="نوع الملف">
          <button type="button" role="radio" aria-checked={kind === 'Presentation'} className={kind === 'Presentation' ? styles.kindActive : undefined} disabled={busy === 'create'} onClick={() => changeKind('Presentation')}><MonitorPlay size={18} /><span><strong>عرض تقديمي</strong><small>16:9 · PPTX وPDF</small></span></button>
          <button type="button" role="radio" aria-checked={kind === 'A4'} className={kind === 'A4' ? styles.kindActive : undefined} disabled={busy === 'create'} onClick={() => changeKind('A4')}><FileText size={18} /><span><strong>مستند A4</strong><small>DOCX وPDF</small></span></button>
        </div>
        <label className={styles.field}><span>المحتوى الكامل</span><textarea rows={13} maxLength={60000} placeholder="الصق محتواك كاملًا. لأكثر من سيشن، اكتب Session 1 أو سيشن ١ في سطر مستقل، ثم محتواه، وبعده Session 2 ومحتواه." value={content} disabled={busy === 'create'} onChange={event => { invalidatePreview(); setContent(event.target.value); setCountOverride(null); setSuggestionError(null); }} /><small>{content.length.toLocaleString('ar-EG')} من 60,000 حرف</small></label>
        <div className={styles.field}>
          <span>{separateSessions ? 'ملفات السيشنات' : `العدد المقترح ${kind === 'Presentation' ? 'للسلايدز' : 'للصفحات'}`}</span>
          <output aria-live="polite" aria-label="العدد المقترح">{currentSuggestion ? separateSessions
            ? `${sections.length} ملفات مستقلة، كل ملف بغلاف باسم السيشن`
            : `${currentSuggestion.pageCount.toLocaleString('ar-EG')} ${kind === 'Presentation' ? 'سلايد' : 'صفحة'}، شامل الغلاف التعريفي`
            : !validContent ? 'اكتب 80 حرفًا على الأقل لاقتراح العدد.' : suggestionError ? 'تعذر اقتراح العدد.' : canManage ? 'جارٍ حساب العدد المناسب للمحتوى…' : 'إنشاء الملفات متاح لمديري المشروع.'}</output>
          {suggestionError && <><small role="alert">{suggestionError}</small><button type="button" onClick={() => { setSuggestionError(null); setSuggestionRetry(current => current + 1); }}>أعد اقتراح العدد</button></>}
        </div>
        <label className={styles.field}><span id="document-count-label">{separateSessions ? `عدد ${kind === 'Presentation' ? 'السلايدات' : 'الصفحات'} لكل سيشن شامل الغلاف` : `عدد ${kind === 'Presentation' ? 'السلايدز' : 'الصفحات'} شامل الغلاف`}</span>
          <input type="number" aria-labelledby="document-count-label" aria-describedby={separateSessions ? 'document-count-hint' : undefined} min={minimumCount} max={maximumCount} step={1} value={countOverride ?? (recommendedCount || '')} disabled={!currentSuggestion || busy === 'create'} onChange={event => { invalidatePreview(); setCountOverride(event.target.value); }} />
          {separateSessions && <small id="document-count-hint">حدد العدد مرة واحدة وهيتطبق على كل السيشنات. مثال: 10 يعني غلاف + 9 {kind === 'Presentation' ? 'سلايدات' : 'صفحات'} محتوى في كل ملف.</small>}
          {currentSuggestion && minimumCount <= maximumCount && <small>متاح من {minimumCount} إلى {maximumCount}، مع الحفاظ على المحتوى كاملًا.</small>}
          {currentSuggestion && !validCount && <small role="alert">{minimumCount > maximumCount ? 'مفيش عدد واحد يناسب طول كل السيشنات. راجع طول المحتوى قبل التقسيم.' : sections.length > 50 ? 'الحد الأقصى 50 ملفًا في الدفعة.' : 'اختر عددًا صحيحًا داخل النطاق المتاح.'}</small>}
        </label>
        {separateSessions ? <>
          <ul className={styles.documentSessionSummary} aria-label="ملفات السيشنات">
            {sections.map(section => <li key={section.section}><strong dir="auto">{section.title}</strong><span>{validCount ? `${selectedCount} ${kind === 'Presentation' ? 'سلايد' : 'صفحة'} شامل الغلاف` : 'ملف مستقل'}</span></li>)}
          </ul>
          {validCount && <p className={styles.documentPreviewHint}>الإجمالي {sections.length * selectedCount} صفحة في {sections.length} ملفات، بحد أقصى 200 صفحة في الدفعة.</p>}
        </> : <label className={styles.field}><span>عنوان الغلاف (اختياري)</span><input value={coverTitle} maxLength={300} dir="auto" placeholder="لو سيبته فاضي هنستخدم أول سطر من كلامك" disabled={busy === 'create'} onChange={event => { invalidatePreview(); setCoverTitle(event.target.value); }} /></label>}
        <button type="button" className={styles.btnPrimary} disabled={!canManage || !aiReady || !validContent || !validCount || Boolean(busy) || pageDraftDirty} onClick={() => void showPreview()}>{busy === 'preview' ? <LoaderCircle className={styles.spin} size={18} /> : <FileText size={18} />} {busy === 'preview' ? 'جارٍ تنسيق المحتوى…' : 'اعرض التقسيم'}</button>
        {pageDraftDirty && <p className={styles.documentRequirement}>احفظ تعديلات العرض الحالي قبل تجهيز ملف جديد.</p>}
        <p className={styles.documentPreviewHint}>بنحافظ على كلماتك، مع إزالة الإيموجي وعلامات التنظيم زي Title وSlide 1. كل سيشن بيتحفظ في ملف مستقل بغلاف باسمه. توليد الصور يبدأ بعد اعتمادك للتقسيم.</p>
        {readinessMessage && <p className={styles.documentRequirement}>{readinessMessage}</p>}
      </aside>

      <main className={styles.documentCanvas} aria-busy={busy === 'preview'}>
        {busy === 'preview' && <div className={styles.documentProgress} role="status"><LoaderCircle className={styles.spin} size={20} /><span>{separateSessions ? `الـAI بينظم كل سيشن في ملف مستقل من ${selectedCount} صفحات شامل الغلاف…` : `الـAI بيرتب الأفكار ويوزع الفقرات والنقط على ${pageCount} ${kind === 'Presentation' ? 'سلايد' : 'صفحة'}…`}</span></div>}
        {currentPreview ? <>
          <header className={styles.documentToolbar}><div><span>02 · مراجعة قبل التصميم</span><h2 ref={previewHeading} tabIndex={-1}>{currentPreview.documents?.length ? `${currentPreview.documents.length} ملفات مستقلة، ${currentPreview.pageCount} ${kind === 'Presentation' ? 'سلايد' : 'صفحة'} شامل الأغلفة` : `${currentPreview.pageCount} ${kind === 'Presentation' ? 'سلايد' : 'صفحة'}، شامل الغلاف`}</h2></div></header>
          <p className={styles.documentPreviewHint}>راجع المحتوى والترتيب قبل التصميم. كل غلاف ظاهر هنا هو أول صفحة في ملفه، والعدد شامل الغلاف.</p>
          {currentPreview.documents?.length ? currentPreview.documents.map(file => <section key={file.section} className={styles.documentFilePreview} aria-label={`معاينة ملف ${file.title}`}>
            <header><h3 dir="auto">{file.title}</h3><span>{file.pageCount} {kind === 'Presentation' ? 'سلايد' : 'صفحة'} · ملف مستقل</span></header>
            <PreviewPages pages={file.pages} kind={kind} />
          </section>) : <PreviewPages pages={currentPreview.pages} kind={kind} />}
          <div className={styles.documentApproval}>
            <p>نفس كلامك كاملًا، بترتيب وتنسيق مقترح من الـAI. التصميم هيستخدم التنظيم اللي بتعتمده هنا.</p>
            <button type="button" className={styles.btnPrimary} disabled={!canManage || Boolean(readinessMessage) || Boolean(busy)} onClick={() => void create()}>{busy === 'create' ? <LoaderCircle className={styles.spin} size={18} /> : <Sparkles size={18} />} اعتمد التقسيم وابدأ التصميم</button>
          </div>
        </> : !editableDetail || editableDetail.document.id !== selectedId ? <div className={styles.documentEmpty}><Sparkles size={36} /><h2>{selectedId ? 'جارٍ تحميل العرض…' : 'المحتوى يتحول لملف جاهز'}</h2><p>الصق كل السيشنات، حدد العدد مرة واحدة، وراجع ملف كل سيشن قبل التصميم.</p></div> : <DocumentEditor canManage={canManage} aiReady={aiReady} detail={editableDetail} busy={busy} dirtyPages={dirtyPages} onUpdate={updatePage} onSave={savePage} onRegenerate={regenerate} onRegenerateAll={regenerateAll} onExport={exportFile} onMove={movePage} onAdd={() => { setBeforePageId(''); setAddingPage(true); }}>
          {addingPage && <NewPageForm page={newPage} pages={editableDetail.pages} beforePageId={beforePageId} onPositionChange={setBeforePageId} disabled={!canManage || !aiReady || Boolean(busy) || ['Planning', 'GeneratingImages'].includes(editableDetail.document.status)} onChange={setNewPage} onSubmit={addPage} onCancel={() => { setAddingPage(false); setNewPage({ title: '', body: '' }); setBeforePageId(''); }} />}
        </DocumentEditor>}
      </main>

      <aside className={styles.documentHistory} aria-label="الملفات السابقة">
        <div className={styles.documentHistoryHeading}><h2>الملفات</h2><span>{documents.length}</span></div>
        {pageDraftDirty && <p className={styles.documentRequirement}>احفظ تعديلات السلايد قبل فتح ملف تاني.</p>}
        {documents.map(document => <button type="button" key={document.id} disabled={Boolean(busy) || (selectedId !== document.id && pageDraftDirty)} className={selectedId === document.id ? styles.documentHistoryActive : undefined} onClick={() => { invalidatePreview(); setSelectedId(document.id); if (document.id !== selectedId) setAddingPage(false); }}><span>{document.kind === 'Presentation' ? <MonitorPlay size={16} /> : <FileText size={16} />}</span><div><strong>{document.title || 'جارٍ تجهيز العنوان…'}</strong><small>{document.requestedPageCount} {document.kind === 'Presentation' ? 'سلايد' : 'صفحة'} · {statusLabel(document.status)}</small></div></button>)}
      </aside>
    </div>
  </div>;
}

function PreviewPages({ pages, kind }: { pages: ContentDocumentPreview['pages']; kind: ContentDocumentKind }) {
  return <div className={styles.documentPages}>
    {pages.map(page => <section className={styles.documentTextPreview} key={page.pageIndex} aria-label={`معاينة الصفحة ${page.pageIndex + 1}`}>
      <header>{page.pageIndex === 0 ? '1 · الغلاف التعريفي' : `${page.pageIndex + 1} · ${kind === 'Presentation' ? 'سلايد' : 'صفحة'}`}</header>
      {page.title && <h3 dir="auto">{page.title}</h3>}
      <PreviewBlocks blocks={page.blocks} />
    </section>)}
  </div>;
}

function PreviewBlocks({ blocks }: { blocks: ContentDocumentPreview['pages'][number]['blocks'] }) {
  return <div className={styles.documentPreviewBlocks}>
    {blocks.map((block, index) => block.type === 'heading'
      ? <h3 key={index} dir="auto">{block.items[0]}</h3>
      : block.type === 'bullets'
        ? <ul key={index} dir="auto">{block.items.map((text, itemIndex) => <li key={itemIndex} dir="auto" data-source-marker={/^(?:[-*•]\s|[0-9٠-٩]+[.)]\s)/.test(text) || undefined}>{text}</li>)}</ul>
        : <p className={styles.documentPreviewBody} key={index} dir="auto">{block.items.join('\n')}</p>)}
  </div>;
}

function DocumentEditor({ canManage, aiReady, detail, busy, dirtyPages, onUpdate, onSave, onRegenerate, onRegenerateAll, onExport, onMove, onAdd, children }: {
  canManage: boolean; aiReady: boolean;
  detail: ContentDocumentDetail; busy: string | null; dirtyPages: Set<string>;
  onUpdate: (id: string, field: 'title' | 'body', value: string) => void;
  onSave: (id: string) => void; onRegenerate: (id: string) => void;
  onRegenerateAll: () => void;
  onExport: (format: 'pdf' | 'pptx' | 'docx') => void;
  onMove: (id: string, position: number) => void; onAdd: () => void; children: React.ReactNode;
}) {
  const generating = ['Planning', 'GeneratingImages'].includes(detail.document.status);
  const readyCount = detail.pages.filter(page => page.status === 'Ready').length;
  const allImagesReady = detail.pages.length > 0 && readyCount === detail.pages.length;
  const canExport = allImagesReady && !generating && dirtyPages.size === 0;
  const editDisabled = !canManage || generating || Boolean(busy);
  const designDisabled = editDisabled || !aiReady;
  return <>
    <header className={styles.documentToolbar}><div><span>{detail.document.kind === 'Presentation' ? 'عرض تقديمي' : 'مستند A4'}</span><h2>{detail.document.title || 'Gemini يجهز التصميم…'}</h2></div><div className={styles.exportActions}><button type="button" disabled={designDisabled || dirtyPages.size > 0} onClick={onRegenerateAll}>{busy === 'images-all' ? <LoaderCircle className={styles.spin} size={16} /> : <RefreshCw size={16} />} أعد تصميم الكل</button><button type="button" disabled={!canExport || Boolean(busy)} onClick={() => onExport('pdf')}><Download size={16} /> PDF</button><button type="button" disabled={!canExport || Boolean(busy)} onClick={() => onExport(detail.document.kind === 'Presentation' ? 'pptx' : 'docx')}><Download size={16} /> {detail.document.kind === 'Presentation' ? 'PPTX' : 'DOCX'}</button></div></header>
    <div className={styles.documentEditToolbar}>
      <p>{detail.pages.length} سلايد · حرّك السلايدات لفوق أو لتحت، والترتيب بيتحفظ تلقائيًا.</p>
      <button type="button" className={styles.btnPrimary} disabled={designDisabled || detail.pages.length >= 200 || Boolean(children)} onClick={onAdd}><Plus size={17} /> إضافة سلايد</button>
    </div>
    {children}
    {detail.document.error && <div className={styles.alertError} role="alert">{detail.document.error}</div>}
    {generating && !detail.document.error && <div className={styles.documentProgress} role="status"><LoaderCircle className={styles.spin} size={20} /><span>{detail.document.status === 'Planning' ? 'جارٍ تجهيز التصميم بالتقسيم المعتمد…' : `يولّد الصور بالهوية الحالية، اكتمل ${detail.pages.filter(page => page.status === 'Ready').length} من ${detail.document.requestedPageCount}`}</span></div>}
    {!generating && !allImagesReady && <p className={styles.documentProgress} role="status">اكتمل {readyCount} من {detail.pages.length}. اختار السلايد واضغط «تصميم السلايد».</p>}
    <div className={styles.documentPages} data-kind={detail.document.kind}>
      {detail.pages.map(page => <section className={styles.documentPageShell} key={page.id} aria-label={`السلايد ${page.pageIndex + 1}`}>
        <header className={styles.documentSlideHeading}>
          <strong>سلايد {page.pageIndex + 1}</strong>
          <div className={styles.documentPageActions}>
            <button type="button" aria-label={`تحريك السلايد ${page.pageIndex + 1} لأعلى`} disabled={editDisabled || page.pageIndex === 0} onClick={() => onMove(page.id, page.pageIndex - 1)}><ArrowUp size={16} /> لفوق</button>
            <button type="button" aria-label={`نقل السلايد ${page.pageIndex + 1} لأول العرض`} disabled={editDisabled || page.pageIndex === 0} onClick={() => onMove(page.id, 0)}><ChevronsUp size={16} /> فوق خالص</button>
            <button type="button" aria-label={`تحريك السلايد ${page.pageIndex + 1} لأسفل`} disabled={editDisabled || page.pageIndex === detail.pages.length - 1} onClick={() => onMove(page.id, page.pageIndex + 1)}><ArrowDown size={16} /> لتحت</button>
          </div>
        </header>
        <article className={detail.document.kind === 'Presentation' ? styles.presentationPage : styles.a4Page}>
          <div className={styles.documentPageVisual}>{page.imageUrl ? <AssetImage url={page.imageUrl} alt={`تصميم السلايد ${page.pageIndex + 1}`} /> : <div className={styles.documentUndesigned}>
            {['Queued', 'GeneratingImage'].includes(page.status) ? <LoaderCircle className={styles.spin} size={24} /> : <FileText size={28} />}
            <strong>{page.status === 'Queued' ? 'في انتظار بدء التصميم' : page.status === 'GeneratingImage' ? 'جاري تصميم السلايد' : page.status === 'ImageFailed' ? 'تعذر التصميم، تقدر تحاول تاني' : 'جاهز للتصميم'}</strong>
            {page.error && <span>{page.error}</span>}
          </div>}</div>
        </article>
        <div className={styles.documentPageEditor}>
          <input disabled={editDisabled} aria-label={`عنوان الصفحة ${page.pageIndex + 1}`} value={page.title} maxLength={300} dir="auto" onChange={event => onUpdate(page.id, 'title', event.target.value)} />
          <textarea disabled={editDisabled} aria-label={`نص الصفحة ${page.pageIndex + 1}`} value={page.body} maxLength={4000} dir="auto" onChange={event => onUpdate(page.id, 'body', event.target.value)} />
        </div>
        <div className={styles.documentPageActions}><button type="button" disabled={!dirtyPages.has(page.id) || designDisabled} onClick={() => onSave(page.id)}>{busy === `save-${page.id}` ? <LoaderCircle className={styles.spin} size={15} /> : <Save size={15} />} حفظ وإعادة التصميم</button><button type="button" disabled={designDisabled || dirtyPages.has(page.id)} onClick={() => onRegenerate(page.id)}>{busy === `image-${page.id}` ? <LoaderCircle className={styles.spin} size={15} /> : <Sparkles size={15} />} {page.imageUrl ? 'إعادة تصميم السلايد' : 'تصميم السلايد'}</button></div>
      </section>)}
    </div>
    {!allImagesReady && <p className={styles.documentRequirement}>التنزيل يتاح لما يكتمل تصميم كل السلايدات.</p>}
  </>;
}

function NewPageForm({ page, pages, beforePageId, onPositionChange, disabled, onChange, onSubmit, onCancel }: {
  page: { title: string; body: string }; disabled: boolean;
  pages: ContentDocumentDetail['pages']; beforePageId: string; onPositionChange: (id: string) => void;
  onChange: (page: { title: string; body: string }) => void; onSubmit: () => void; onCancel: () => void;
}) {
  const titleInput = useRef<HTMLInputElement>(null);
  const insertionIndex = beforePageId ? pages.findIndex(candidate => candidate.id === beforePageId) : pages.length;
  useEffect(() => { titleInput.current?.focus(); }, []);
  return <form className={styles.documentNewPage} aria-label="سلايد جديد" onSubmit={event => { event.preventDefault(); onSubmit(); }}>
    <h3>إضافة سلايد جديد</h3>
    <p>اكتب محتواه واختار مكانه، وهيتصمم بنفس هوية العرض.</p>
    <label className={styles.field}><span>مكان السلايد الجديد</span>
      <select aria-label="مكان السلايد الجديد" aria-describedby="new-slide-position" value={beforePageId} disabled={disabled} onChange={event => onPositionChange(event.target.value)}>
        {pages.map((candidate, index) => <option key={candidate.id} value={candidate.id}>{index === 0 ? 'في أول العرض' : `قبل السلايد ${index + 1}${candidate.title.trim() ? `: ${candidate.title.slice(0, 50)}` : ''}`}</option>)}
        <option value="">في آخر العرض</option>
      </select>
      <small id="new-slide-position" aria-live="polite">{insertionIndex < 0 ? 'اختار مكان السلايد تاني.' : `هيكون السلايد رقم ${insertionIndex + 1} من ${pages.length + 1}.`}</small>
    </label>
    <label className={styles.field}><span>عنوان السلايد الجديد</span><input ref={titleInput} dir="auto" maxLength={300} disabled={disabled} value={page.title} onChange={event => onChange({ ...page, title: event.target.value })} /></label>
    <label className={styles.field}><span>محتوى السلايد الجديد</span><textarea aria-label="محتوى السلايد الجديد" aria-describedby="new-slide-character-count" dir="auto" rows={5} maxLength={4000} disabled={disabled} value={page.body} onChange={event => onChange({ ...page, body: event.target.value })} /><small id="new-slide-character-count">{page.body.length.toLocaleString('ar-EG')} من 4,000 حرف</small></label>
    <div className={styles.documentPageActions}>
      <button type="button" disabled={disabled} onClick={onCancel}>إلغاء</button>
      <button type="submit" disabled={disabled || insertionIndex < 0 || !(page.title.trim() || page.body.trim())}><Sparkles size={16} /> إضافة وتصميم السلايد</button>
    </div>
  </form>;
}

function AssetImage({ url, alt }: { url: string; alt: string }) {
  const [loaded, setLoaded] = useState<{ url: string; source: string } | null>(null);
  const [failedUrl, setFailedUrl] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    let objectUrl: string | null = null;
    void contentApi.downloadAsset(url, controller.signal).then(blob => {
      if (controller.signal.aborted) return;
      objectUrl = URL.createObjectURL(blob);
      setLoaded({ url, source: objectUrl });
    }).catch(() => { if (!controller.signal.aborted) setFailedUrl(url); });
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [url, attempt]);
  if (loaded?.url === url) return <Image src={loaded.source} alt={alt} fill sizes="(max-width: 820px) 100vw, 45vw" unoptimized />;
  if (failedUrl === url) return <div className={styles.documentUndesigned}><span>تعذر تحميل الصورة المحفوظة.</span><button type="button" onClick={() => { setFailedUrl(null); setAttempt(current => current + 1); }}>تحميل الصورة تاني</button></div>;
  return <LoaderCircle className={styles.spin} size={24} />;
}

function statusLabel(status: ContentDocumentSummary['status']) { return status === 'Ready' ? 'جاهز' : status === 'Failed' ? 'تعثر' : status === 'Planning' ? 'تقسيم المحتوى' : status === 'AwaitingDesign' ? 'جاهز لتصميم السلايدات' : 'توليد الصور'; }
function messageOf(error: unknown) { const candidate = error as { response?: { data?: { error?: string } }; message?: string }; return candidate.response?.data?.error ?? candidate.message ?? 'تعذر تنفيذ الطلب.'; }
