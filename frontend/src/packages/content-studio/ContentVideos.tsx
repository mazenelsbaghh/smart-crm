'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  AlertTriangle,
  CheckCircle2,
  Clock3,
  Download,
  Film,
  LoaderCircle,
  Play,
  RefreshCw,
  ShieldCheck,
  Sparkles,
} from 'lucide-react';
import { contentApi } from './content-api';
import type {
  ContentVideo,
  ContentVideoAspectRatio,
  ContentVideoReadiness,
  ContentVideoScene,
  ContentVideoSceneRetryIntent,
  ContentVideoSceneStatus,
  ContentVideoStatus,
  ContentVideosData,
  CreateContentVideoPlan,
} from './types';
import styles from './ContentStudio.module.css';

const activeVideoStatuses: ContentVideoStatus[] = ['Planning', 'Generating', 'Assembling'];
const videoPollIntervalMs = 4_000;
const defaultVideoPlan: CreateContentVideoPlan = {
  sceneCount: 4,
  durationSeconds: 6,
  aspectRatio: '9:16',
  resolution: '720p',
};

const videoStatusLabels: Record<ContentVideoStatus, string> = {
  Planning: 'بنخطط الفكرة',
  AwaitingApproval: 'مستني موافقتك',
  Generating: 'بنولّد المشاهد',
  Assembling: 'بنركّب الفيديو',
  Ready: 'جاهز',
  PlanningFailed: 'التخطيط محتاج إعادة',
  GenerationFailed: 'مشهد محتاج تدخلك',
  AssemblyFailed: 'التركيب محتاج إعادة',
};

const sceneStatusLabels: Record<ContentVideoSceneStatus, string> = {
  Planned: 'مخطط',
  Queued: 'في الطابور',
  Submitting: 'جارٍ إرسال الطلب',
  Submitted: 'اترسل للموديل',
  Generating: 'بيتولّد',
  Completed: 'جاهز',
  RecoveryRequired: 'محتاج استكمال',
  SubmissionUncertain: 'نتيجة الإرسال غير مؤكدة',
  Failed: 'محتاج إعادة',
};

interface ContentVideosProps {
  projectId: string;
  canManage: boolean;
  onDraftDirtyChange: (dirty: boolean) => void;
}

export default function ContentVideos({ projectId, canManage, onDraftDirtyChange }: ContentVideosProps) {
  const [workspace, setWorkspace] = useState<ContentVideosData | null>(null);
  const [selectedVideoId, setSelectedVideoId] = useState<string>();
  const [selectedVideo, setSelectedVideo] = useState<ContentVideo | null>(null);
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [busy, setBusy] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [workspaceError, setWorkspaceError] = useState<string>();
  const [detailError, setDetailError] = useState<string>();
  const [actionError, setActionError] = useState<string>();
  const workspaceRequestRef = useRef(0);
  const detailRequestRef = useRef(0);
  const currentProjectRef = useRef(projectId);
  const mountedRef = useRef(true);

  useEffect(() => {
    currentProjectRef.current = projectId;
    mountedRef.current = true;
    return () => { mountedRef.current = false; };
  }, [projectId]);

  const loadWorkspace = useCallback(async (foreground: boolean, signal?: AbortSignal) => {
    const requestNumber = ++workspaceRequestRef.current;
    const requestProjectId = currentProjectRef.current;
    if (foreground) setLoading(true);
    try {
      const response = await contentApi.getVideos(signal);
      if (!mountedRef.current || currentProjectRef.current !== requestProjectId
        || signal?.aborted || requestNumber !== workspaceRequestRef.current) return;
      setWorkspace(response);
      if (response.videos.length === 0) setSelectedVideo(null);
      setSelectedVideoId((current) => current && response.videos.some((video) => video.id === current)
        ? current
        : response.videos[0]?.id);
      setWorkspaceError(undefined);
    } catch (requestError) {
      if (mountedRef.current && currentProjectRef.current === requestProjectId
        && !signal?.aborted && requestNumber === workspaceRequestRef.current) {
        setWorkspaceError(contentVideoError(requestError));
      }
    } finally {
      if (foreground && mountedRef.current && currentProjectRef.current === requestProjectId
        && !signal?.aborted && requestNumber === workspaceRequestRef.current) setLoading(false);
    }
  }, []);

  const loadVideo = useCallback(async (videoId: string, signal?: AbortSignal) => {
    const requestNumber = ++detailRequestRef.current;
    const requestProjectId = currentProjectRef.current;
    setDetailLoading(true);
    setDetailError(undefined);
    try {
      const response = await contentApi.getVideo(videoId, signal);
      if (!mountedRef.current || currentProjectRef.current !== requestProjectId
        || signal?.aborted || requestNumber !== detailRequestRef.current) return;
      setSelectedVideo(response);
      setDetailError(undefined);
    } catch (requestError) {
      if (mountedRef.current && currentProjectRef.current === requestProjectId
        && !signal?.aborted && requestNumber === detailRequestRef.current) {
        setDetailError(contentVideoError(requestError));
      }
    } finally {
      if (mountedRef.current && currentProjectRef.current === requestProjectId
        && !signal?.aborted && requestNumber === detailRequestRef.current) setDetailLoading(false);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => void loadWorkspace(true, controller.signal), 0);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [loadWorkspace]);

  useEffect(() => {
    if (!selectedVideoId) return;
    const controller = new AbortController();
    const timer = window.setTimeout(() => void loadVideo(selectedVideoId, controller.signal), 0);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [loadVideo, selectedVideoId]);

  const hasActiveVideo = Boolean(workspace?.videos.some((video) => activeVideoStatuses.includes(video.status)));
  useEffect(() => {
    if (!hasActiveVideo) return;
    return startSerialVideoPolling((signal) => loadWorkspace(false, signal));
  }, [hasActiveVideo, loadWorkspace]);

  useEffect(() => {
    if (!selectedVideo || !activeVideoStatuses.includes(selectedVideo.status)) return;
    return startSerialVideoPolling((signal) => loadVideo(selectedVideo.id, signal));
  }, [loadVideo, selectedVideo]);

  const refreshAfterMutation = async (videoId: string) => {
    await Promise.all([loadWorkspace(false), loadVideo(videoId)]);
  };

  const runMutation = async (key: string, videoId: string, mutation: () => Promise<{ message?: string }>) => {
    const mutationProjectId = projectId;
    setBusy(key);
    setNotice(undefined);
    setActionError(undefined);
    try {
      const response = await mutation();
      if (!mountedRef.current || currentProjectRef.current !== mutationProjectId) return false;
      setNotice(response.message ?? 'تم تنفيذ الإجراء.');
      setSelectedVideoId(videoId);
      await refreshAfterMutation(videoId);
      return true;
    } catch (requestError) {
      if (mountedRef.current && currentProjectRef.current === mutationProjectId) {
        setActionError(contentVideoError(requestError));
      }
      return false;
    } finally {
      if (mountedRef.current && currentProjectRef.current === mutationProjectId) setBusy(undefined);
    }
  };

  const createPlan = async (request: CreateContentVideoPlan) => {
    const mutationProjectId = projectId;
    setBusy('plan');
    setNotice(undefined);
    setActionError(undefined);
    try {
      const response = await contentApi.planVideo(request);
      if (!mountedRef.current || currentProjectRef.current !== mutationProjectId) return false;
      setNotice(response.message);
      setSelectedVideoId(response.id);
      await refreshAfterMutation(response.id);
      return true;
    } catch (requestError) {
      if (mountedRef.current && currentProjectRef.current === mutationProjectId) {
        setActionError(contentVideoError(requestError));
      }
      return false;
    } finally {
      if (mountedRef.current && currentProjectRef.current === mutationProjectId) setBusy(undefined);
    }
  };

  const error = actionError ?? detailError ?? workspaceError;
  if (loading && !workspace) return <VideoSkeleton />;
  if (!workspace) {
    return <div className={styles.blockingError} role="alert"><AlertTriangle size={20} /><span>{error ?? 'تعذر تحميل استوديو الفيديو.'}</span><button type="button" onClick={() => void loadWorkspace(true)}>إعادة المحاولة</button></div>;
  }

  return <section className={styles.videoStudio} aria-labelledby="content-video-studio-heading">
    {(notice || error) && <div className={error ? styles.alertError : styles.alertSuccess} role={error ? 'alert' : 'status'} aria-live={error ? 'assertive' : 'polite'}>
      {error ? <AlertTriangle size={18} /> : <CheckCircle2 size={18} />}
      <span>{error ?? notice}</span>
    </div>}

    <VideoReadiness readiness={workspace.readiness} />
    <div className={styles.quotaNote} role="note">
      <Clock3 size={18} aria-hidden="true" />
      <div><strong>Gemini Omni في Public Preview وبحصة ثابتة</strong><span>كل مشهد يدخل مهمة مستقلة؛ وقت الانتظار قد يختلف، والمشاهد المكتملة تبقى محفوظة إذا احتاج مشهد آخر تدخلك.</span></div>
    </div>

    <div className={styles.videoWorkspace}>
      <VideoPlanForm
        readiness={workspace.readiness}
        canManage={canManage}
        busy={Boolean(busy)}
        onCreate={createPlan}
        onDirtyChange={onDraftDirtyChange}
      />
      <VideoDetail
        key={selectedVideo?.id ?? 'empty-video'}
        video={selectedVideo}
        loading={detailLoading}
        busy={busy}
        canManage={canManage}
        agentPlatformApiKeyConfigured={workspace.readiness.geminiAgentPlatformApiKeyConfigured}
        enterpriseProjectIdConfigured={Boolean(workspace.readiness.enterpriseProjectId)}
        onGenerate={(videoId) => runMutation('generate', videoId, () => contentApi.generateVideo(videoId))}
        onRetryScene={(videoId, sceneId, intent) => runMutation(
          `scene-${sceneId}`,
          videoId,
          () => contentApi.retryVideoScene(videoId, sceneId, intent),
        )}
        onRetryAssembly={(videoId) => runMutation('assembly', videoId, () => contentApi.retryVideoAssembly(videoId))}
      />
    </div>

    <VideoHistory
      videos={workspace.videos}
      selectedVideoId={selectedVideoId}
      onSelect={(videoId) => { setSelectedVideo(null); setSelectedVideoId(videoId); }}
    />
  </section>;
}

function VideoReadiness({ readiness }: { readiness: ContentVideoReadiness }) {
  const checks = [
    {
      label: 'Gemini للأفكار والمعرفة',
      ready: readiness.geminiApiKeyConfigured,
      detail: readiness.geminiApiKeyConfigured
        ? 'مفتاح الأفكار وفهم المعرفة محفوظ'
        : 'أضف مفتاح Gemini للأفكار والمعرفة',
    },
    {
      label: 'Agent Platform لتوليد الفيديو',
      ready: Boolean(readiness.enterpriseProjectId) && readiness.geminiAgentPlatformApiKeyConfigured,
      detail: !readiness.geminiAgentPlatformApiKeyConfigured
        ? 'أضف مفتاح Agent Platform المستقل'
        : !readiness.enterpriseProjectId
          ? 'أضف Google Cloud Project ID للفيديو'
          : `${readiness.enterpriseProjectId} · مفتاح الفيديو محفوظ`,
    },
    { label: 'قاعدة المعرفة', ready: readiness.knowledgeDocumentCount > 0, detail: `${readiness.knowledgeDocumentCount} مستند معتمد` },
    { label: 'موديل الفيديو', ready: readiness.configured, detail: readiness.configured ? readiness.model : readiness.reason ?? 'الإعداد غير مكتمل' },
  ];
  return <section className={styles.videoReadiness} aria-label="جاهزية توليد الفيديو">
    {checks.map((check) => <div className={styles.videoReadinessItem} key={check.label}>
      <span className={check.ready ? styles.readyIcon : styles.waitIcon}>{check.ready ? <CheckCircle2 size={17} /> : <AlertTriangle size={17} />}</span>
      <div><strong>{check.label}</strong><small>{check.detail}</small></div>
    </div>)}
  </section>;
}

interface VideoPlanFormProps {
  readiness: ContentVideoReadiness;
  canManage: boolean;
  busy: boolean;
  onCreate: (request: CreateContentVideoPlan) => Promise<boolean>;
  onDirtyChange: (dirty: boolean) => void;
}

function VideoPlanForm({ readiness, canManage, busy, onCreate, onDirtyChange }: VideoPlanFormProps) {
  const [brief, setBrief] = useState('');
  const [sceneCount, setSceneCount] = useState(defaultVideoPlan.sceneCount);
  const [durationSeconds, setDurationSeconds] = useState(defaultVideoPlan.durationSeconds);
  const [aspectRatio, setAspectRatio] = useState<ContentVideoAspectRatio>(defaultVideoPlan.aspectRatio);
  const [resolution, setResolution] = useState<'720p' | '1080p'>(defaultVideoPlan.resolution);
  const dirty = Boolean(brief.trim()) || sceneCount !== 4 || durationSeconds !== 6 || aspectRatio !== '9:16' || resolution !== '720p';

  useEffect(() => onDirtyChange(dirty), [dirty, onDirtyChange]);

  const submitPlan = async (event: React.FormEvent) => {
    event.preventDefault();
    const created = await onCreate({
      ...(brief.trim() ? { brief: brief.trim() } : {}),
      sceneCount,
      durationSeconds,
      aspectRatio,
      resolution,
    });
    if (!created) return;
    setBrief('');
    setSceneCount(4);
    setDurationSeconds(6);
    setAspectRatio('9:16');
    setResolution('720p');
    onDirtyChange(false);
  };

  const disabled = busy || !canManage || !readiness.configured || !readiness.geminiAgentPlatformApiKeyConfigured;
  return <aside className={styles.videoPlanPanel} aria-labelledby="content-video-studio-heading">
    <div className={styles.videoSectionHeading}><span>01</span><div><h2 id="content-video-studio-heading">فكرة فيديو جديدة</h2><p>سيقرأ النظام المعرفة المعتمدة، ثم يعرض الفكرة والمشاهد قبل بدء التوليد.</p></div></div>
    <form onSubmit={submitPlan}>
      <fieldset disabled={busy || !canManage} className={styles.videoFieldset}>
        <label className={styles.field}>
          <span>توجيه اختياري</span>
          <textarea rows={4} maxLength={2000} value={brief} onChange={(event) => setBrief(event.target.value)} placeholder="مثال: فيديو يشرح الفرق الذي نصنعه للعميل، واتركه فارغًا ليختار النظام من قاعدة المعرفة." />
          <small>{brief.length} من 2000 حرف</small>
        </label>
        <div className={styles.videoFormGrid}>
          <label className={styles.field}><span>عدد المشاهد</span><select value={sceneCount} onChange={(event) => setSceneCount(Number(event.target.value))}>{[3, 4, 5, 6].map((count) => <option value={count} key={count}>{count} مشاهد</option>)}</select></label>
          <label className={styles.field}><span>مدة المشهد</span><select value={durationSeconds} onChange={(event) => setDurationSeconds(Number(event.target.value))}>{Array.from({ length: 8 }, (_, index) => index + 3).map((seconds) => <option value={seconds} key={seconds}>{seconds} ثوانٍ</option>)}</select></label>
          <label className={styles.field}><span>المقاس</span><select value={aspectRatio} onChange={(event) => setAspectRatio(event.target.value as ContentVideoAspectRatio)}><option value="9:16">رأسي 9:16</option><option value="16:9">أفقي 16:9</option></select></label>
          <label className={styles.field}><span>الدقة</span><select value={resolution} onChange={(event) => setResolution(event.target.value as '720p' | '1080p')}><option value="720p">720p</option><option value="1080p">1080p</option></select></label>
        </div>
      </fieldset>
      <button className={styles.btnPrimary} type="submit" disabled={disabled}>
        {busy ? <LoaderCircle className={styles.spin} size={18} /> : <Sparkles size={18} />} اقترح الفكرة وقسّم المشاهد
      </button>
      {!canManage && <p className={styles.readOnlyNote}>يمكنك مراجعة الفيديوهات فقط. التوليد متاح للمالك والمدير.</p>}
      {canManage && !readiness.configured && <p className={styles.readOnlyNote}>{readiness.reason ?? 'كمّل مفتاح Gemini للأفكار، ومفتاح Agent Platform للفيديو، وGoogle Cloud Project ID، وقاعدة المعرفة أولًا.'}</p>}
    </form>
  </aside>;
}

interface VideoDetailProps {
  video: ContentVideo | null;
  loading: boolean;
  busy?: string;
  canManage: boolean;
  agentPlatformApiKeyConfigured: boolean;
  enterpriseProjectIdConfigured: boolean;
  onGenerate: (videoId: string) => Promise<boolean>;
  onRetryScene: (videoId: string, sceneId: string, intent: ContentVideoSceneRetryIntent) => Promise<boolean>;
  onRetryAssembly: (videoId: string) => Promise<boolean>;
}

function VideoDetail({ video, loading, busy, canManage, agentPlatformApiKeyConfigured, enterpriseProjectIdConfigured, onGenerate, onRetryScene, onRetryAssembly }: VideoDetailProps) {
  const initialSceneId = video?.scenes.find((scene) => scene.status === 'Completed' && scene.videoUrl)?.id;
  const [selectedSceneId, setSelectedSceneId] = useState(initialSceneId);
  const freshGenerationReady = agentPlatformApiKeyConfigured && enterpriseProjectIdConfigured;
  const selectedScene = video?.scenes.find((scene) => scene.id === selectedSceneId && scene.videoUrl)
    ?? video?.scenes.find((scene) => scene.status === 'Completed' && scene.videoUrl);

  if (loading && !video) return <div className={styles.videoDetailLoading} aria-busy="true"><LoaderCircle className={styles.spin} size={24} /><span>بنحمّل تفاصيل الفيديو…</span></div>;
  if (!video) return <div className={styles.videoEmpty}>
    <Film size={38} aria-hidden="true" /><h2>ابدأ من فكرة حقيقية</h2><p>اكتب توجيهًا بسيطًا أو اتركه فارغًا؛ النظام سيقترح فكرة من قاعدة معرفة المشروع ويقسمها إلى مشاهد قابلة للمراجعة.</p>
  </div>;

  const orderedScenes = [...video.scenes].sort((first, second) => first.sceneIndex - second.sceneIndex);
  return <section className={styles.videoDetail} aria-labelledby="selected-video-title">
    <p className={styles.srOnly} role="status" aria-live="polite" aria-atomic="true">{videoProgressAnnouncement(video)}</p>
    <header className={styles.videoDetailHeader}>
      <div><span>02</span><div><p>الفكرة المقترحة</p><h2 id="selected-video-title">{video.ideaTitle || 'جارٍ بناء الفكرة'}</h2></div></div>
      <strong className={`${styles.videoStatus} ${videoStatusClass(video.status)}`}>{videoStatusLabels[video.status]}</strong>
    </header>

    {video.status === 'Planning' ? <VideoPlanning /> : <>
      <div className={styles.videoConcept}>
        <div><span>الافتتاحية</span><strong>{video.hook}</strong></div>
        <div><span>ملخص الفكرة</span><p>{video.summary}</p></div>
        <div><span>الكابشن</span><p>{video.caption}</p></div>
        <small>{video.sceneCount} مشاهد · {video.requestedSceneDurationSeconds} ثوانٍ للمشهد · {video.aspectRatio} · {video.resolution}</small>
      </div>
      {video.knowledgeWasTruncated && <p className={styles.knowledgeBoundedNote}>
        <ShieldCheck size={16} aria-hidden="true" /> استخدم التخطيط سياقًا محدودًا من المعرفة المعتمدة لأن محتوى القاعدة أكبر من حد السياق المخصص للتخطيط.
      </p>}

      {video.error && <div className={styles.videoInlineError} role="alert"><AlertTriangle size={18} /><span>{video.error}</span></div>}

      {video.status === 'AwaitingApproval' && <div className={styles.videoApprovalBar}>
        <div><ShieldCheck size={19} /><span>{freshGenerationReady ? 'راجع الفكرة وترتيب المشاهد. التوليد لن يبدأ قبل موافقتك.' : 'أضف مفتاح Agent Platform وGoogle Cloud Project ID من الإعدادات قبل توليد المشاهد.'}</span></div>
        <button className={styles.btnPrimary} type="button" disabled={Boolean(busy) || !canManage || !freshGenerationReady} onClick={() => void onGenerate(video.id)}>{busy === 'generate' ? <LoaderCircle className={styles.spin} size={18} /> : <Play size={18} />} اعتمد وولّد المشاهد</button>
      </div>}

      <div className={styles.storyboardHeading}><div><span>03</span><div><h3>لوحة المشاهد</h3><p>{video.completedSceneCount} من {video.sceneCount} مشاهد جاهزة</p></div></div></div>
      <div className={styles.sceneList}>
        {orderedScenes.map((scene) => <SceneRow
          key={scene.id}
          scene={scene}
          selected={selectedScene?.id === scene.id}
          busy={busy === `scene-${scene.id}`}
          canManage={canManage}
          agentPlatformApiKeyConfigured={agentPlatformApiKeyConfigured}
          enterpriseProjectIdConfigured={enterpriseProjectIdConfigured}
          onPreview={() => setSelectedSceneId(scene.id)}
          onRetry={(intent) => void onRetryScene(video.id, scene.id, intent)}
        />)}
      </div>

      {(selectedScene?.videoUrl || (video.status === 'Ready' && video.finalVideoUrl)) && <p className={styles.captionAvailabilityNote}>
        <AlertTriangle size={15} aria-hidden="true" /> تفاصيل الصوت المخططة متاحة في المشاهد، لكن الكلام الناتج لا يملك captions موثقة بعد.
      </p>}

      {selectedScene?.videoUrl && <div className={styles.scenePreview}>
        <div><span>معاينة المشهد {selectedScene.sceneIndex + 1}</span><strong>{selectedScene.title}</strong></div>
        <AuthenticatedVideo key={selectedScene.videoUrl} assetUrl={selectedScene.videoUrl} downloadName={`scene-${selectedScene.sceneIndex + 1}.mp4`} aspectRatio={video.aspectRatio} />
      </div>}

      {video.status === 'AssemblyFailed' && <div className={styles.videoApprovalBar}>
        <div><AlertTriangle size={19} /><span>المشاهد مكتملة لكن تركيب الفيديو توقف. أعد التركيب من الملفات المحفوظة.</span></div>
        <button className={styles.btnSecondary} type="button" disabled={Boolean(busy) || !canManage} onClick={() => void onRetryAssembly(video.id)}>{busy === 'assembly' ? <LoaderCircle className={styles.spin} size={18} /> : <RefreshCw size={18} />} أعد تركيب الفيديو</button>
      </div>}

      {video.status === 'Ready' && video.finalVideoUrl && <div className={styles.finalVideo}>
        <div><CheckCircle2 size={21} /><div><span>الفيديو النهائي جاهز</span><strong>{video.ideaTitle}</strong></div></div>
        <AuthenticatedVideo key={video.finalVideoUrl} assetUrl={video.finalVideoUrl} downloadName={`content-video-${video.id}.mp4`} aspectRatio={video.aspectRatio} />
      </div>}
    </>}
  </section>;
}

interface SceneRowProps {
  scene: ContentVideoScene;
  selected: boolean;
  busy: boolean;
  canManage: boolean;
  agentPlatformApiKeyConfigured: boolean;
  enterpriseProjectIdConfigured: boolean;
  onPreview: () => void;
  onRetry: (intent: ContentVideoSceneRetryIntent) => void;
}

function SceneRow({ scene, selected, busy, canManage, agentPlatformApiKeyConfigured, enterpriseProjectIdConfigured, onPreview, onRetry }: SceneRowProps) {
  const retryConfigured = agentPlatformApiKeyConfigured
    && (scene.status === 'RecoveryRequired' || enterpriseProjectIdConfigured);
  return <article className={`${styles.sceneRow} ${selected ? styles.sceneRowSelected : ''}`}>
    <div className={styles.sceneNumber}><span>{String(scene.sceneIndex + 1).padStart(2, '0')}</span><small>{scene.durationSeconds}ث</small></div>
    <div className={styles.sceneCopy}>
      <div className={styles.sceneTitle}><h4>{scene.title}</h4><span className={`${styles.sceneStatus} ${sceneStatusClass(scene.status)}`}>{sceneStatusLabels[scene.status]}</span></div>
      <p>{scene.narrative}</p>
      <details><summary>تفاصيل تنفيذ المشهد</summary><dl><div><dt>الصورة</dt><dd>{scene.visualPrompt}</dd></div><div><dt>الصوت</dt><dd>{scene.audioPrompt}</dd></div><div><dt>الانتقال</dt><dd>{scene.transitionPrompt}</dd></div></dl></details>
      {scene.error && <small className={styles.weekImageError}>{scene.error}</small>}
      {scene.status === 'SubmissionUncertain' && <div className={styles.sceneDuplicateRisk} role="alert">
        <AlertTriangle size={17} aria-hidden="true" />
        <span>لم يؤكد مزود الفيديو قبول الطلب. إعادة المحاولة قد تنشئ فيديو مكررًا وتؤدي إلى احتساب التكلفة مرتين.</span>
      </div>}
      <div className={styles.sceneActions}>
        {scene.status === 'Completed' && scene.videoUrl && <button type="button" className={styles.btnSecondary} onClick={onPreview}><Play size={16} /> {selected ? 'المشهد معروض' : 'عاين المشهد'}</button>}
        {scene.status === 'Failed' && <button type="button" className={styles.btnSecondary} disabled={busy || !canManage || !retryConfigured} onClick={() => onRetry({ mode: 'safe' })}>{busy ? <LoaderCircle className={styles.spin} size={16} /> : <RefreshCw size={16} />} أعد المشهد</button>}
        {scene.status === 'RecoveryRequired' && <button type="button" className={styles.btnSecondary} disabled={busy || !canManage || !retryConfigured} onClick={() => onRetry({ mode: 'safe' })}>{busy ? <LoaderCircle className={styles.spin} size={16} /> : <RefreshCw size={16} />} استكمل المشهد</button>}
        {scene.status === 'SubmissionUncertain' && <button type="button" className={styles.btnSecondary} disabled={busy || !canManage || !retryConfigured} onClick={() => onRetry({ mode: 'confirmed-possible-duplicate' })}>{busy ? <LoaderCircle className={styles.spin} size={16} /> : <RefreshCw size={16} />} أعد رغم احتمال التكرار</button>}
      </div>
    </div>
  </article>;
}

function AuthenticatedVideo({ assetUrl, downloadName, aspectRatio }: { assetUrl: string; downloadName: string; aspectRatio: ContentVideoAspectRatio }) {
  const [videoSource, setVideoSource] = useState<string>();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string>();
  const objectUrlRef = useRef<string | undefined>(undefined);
  const requestRef = useRef<AbortController | undefined>(undefined);

  useEffect(() => () => {
    requestRef.current?.abort();
    if (objectUrlRef.current) URL.revokeObjectURL(objectUrlRef.current);
  }, []);

  const loadPreview = async () => {
    requestRef.current?.abort();
    if (objectUrlRef.current) URL.revokeObjectURL(objectUrlRef.current);
    const controller = new AbortController();
    requestRef.current = controller;
    objectUrlRef.current = undefined;
    setVideoSource(undefined);
    setError(undefined);
    setLoading(true);
    try {
      const videoBlob = await contentApi.downloadAsset(assetUrl, controller.signal);
      if (controller.signal.aborted) return;
      const objectUrl = URL.createObjectURL(videoBlob);
      objectUrlRef.current = objectUrl;
      setVideoSource(objectUrl);
    } catch (requestError) {
      if (!controller.signal.aborted) setError(contentVideoError(requestError));
    } finally {
      if (!controller.signal.aborted) setLoading(false);
    }
  };

  return <div className={`${styles.authenticatedVideo} ${aspectRatio === '9:16' ? styles.authenticatedVideoPortrait : ''}`} style={{ aspectRatio: aspectRatio === '9:16' ? '9 / 16' : '16 / 9' }}>
    {!videoSource && <button type="button" onClick={() => void loadPreview()} disabled={loading}>{loading ? <LoaderCircle className={styles.spin} size={20} /> : <Play size={20} />} {loading ? 'جارٍ تحميل الملف…' : 'حمّل المعاينة'}</button>}
    {error && <span role="alert"><AlertTriangle size={18} />{error}</span>}
    {videoSource && <>
      <video src={videoSource} controls preload="none" playsInline aria-label="معاينة الفيديو بالصوت" />
      <div className={styles.videoDownload}><small>المشهد مولّد بالصوت؛ تحكّم فيه من المشغّل.</small><a href={videoSource} download={downloadName}><Download size={17} /> تنزيل MP4</a></div>
    </>}
  </div>;
}

function VideoHistory({ videos, selectedVideoId, onSelect }: { videos: ContentVideosData['videos']; selectedVideoId?: string; onSelect: (videoId: string) => void }) {
  return <section className={styles.videoHistory} aria-labelledby="video-history-heading">
    <div className={styles.historyHeading}><div><span>04</span><div><h2 id="video-history-heading">سجل الفيديوهات</h2><p>الأفكار السابقة وحالة كل فيديو.</p></div></div><strong>{videos.length} فيديو</strong></div>
    {videos.length === 0 ? <div className={styles.emptyHistory}>أول فكرة فيديو هتظهر هنا.</div> : <div className={styles.videoHistoryList}>
      {videos.map((video) => <button type="button" key={video.id} className={video.id === selectedVideoId ? styles.videoHistoryActive : styles.videoHistoryRow} onClick={() => onSelect(video.id)} aria-current={video.id === selectedVideoId ? 'true' : undefined}>
        <span className={styles.videoHistoryIcon}><Film size={19} /></span>
        <span><strong>{video.ideaTitle || 'فكرة قيد التخطيط'}</strong><small>{formatVideoDate(video.createdAt)} · {video.aspectRatio} · {video.resolution}</small></span>
        <span>{video.completedSceneCount}/{videoSceneTotal(video)}</span>
        <em className={`${styles.videoStatus} ${videoStatusClass(video.status)}`}>{videoStatusLabels[video.status]}</em>
      </button>)}
    </div>}
  </section>;
}

function VideoPlanning() {
  return <div className={styles.videoPlanning} aria-busy="true"><LoaderCircle className={styles.spin} size={25} /><div><strong>بنقرأ قاعدة المعرفة ونبني الفكرة</strong><span>هتظهر الافتتاحية والكابشن والمشاهد هنا أول ما الخطة تكتمل.</span></div></div>;
}

function VideoSkeleton() {
  return <div className={styles.videoSkeleton} aria-busy="true"><span /><span /><div><span /><span /></div></div>;
}

function videoStatusClass(status: ContentVideoStatus) {
  if (status === 'Ready') return styles.videoStatusReady;
  if (status.endsWith('Failed')) return styles.videoStatusFailed;
  if (activeVideoStatuses.includes(status)) return styles.videoStatusWorking;
  return styles.videoStatusPending;
}

function sceneStatusClass(status: ContentVideoSceneStatus) {
  if (status === 'Completed') return styles.videoStatusReady;
  if (status === 'Failed') return styles.videoStatusFailed;
  if (status === 'RecoveryRequired') return styles.videoStatusRecovery;
  if (status === 'SubmissionUncertain') return styles.videoStatusUncertain;
  if (status === 'Generating' || status === 'Submitted' || status === 'Submitting' || status === 'Queued') return styles.videoStatusWorking;
  return styles.videoStatusPending;
}

function videoSceneTotal(video: ContentVideosData['videos'][number]) {
  return video.sceneCount > 0 ? video.sceneCount : video.requestedSceneCount;
}

function videoProgressAnnouncement(video: ContentVideo) {
  const title = video.ideaTitle || 'فكرة الفيديو المحددة';
  return `${title}: ${videoStatusLabels[video.status]}. ${video.completedSceneCount} من ${videoSceneTotal(video)} مشاهد جاهزة.`;
}

function startSerialVideoPolling(poll: (signal: AbortSignal) => Promise<void>) {
  const controller = new AbortController();
  let timer: number | undefined;
  const pollAfterDelay = async () => {
    await poll(controller.signal);
    if (!controller.signal.aborted) timer = window.setTimeout(() => void pollAfterDelay(), videoPollIntervalMs);
  };
  timer = window.setTimeout(() => void pollAfterDelay(), videoPollIntervalMs);
  return () => {
    controller.abort();
    if (timer !== undefined) window.clearTimeout(timer);
  };
}

function formatVideoDate(value: string) {
  return new Intl.DateTimeFormat('ar-EG', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Africa/Cairo' }).format(new Date(value));
}

function contentVideoError(error: unknown) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { error?: string } } }).response;
    if (response?.data?.error) return response.data.error;
  }
  return 'تعذر تنفيذ طلب الفيديو. جرّب مرة ثانية.';
}
