'use client';

import { useState, type ReactNode } from 'react';
import { useSearchParams } from 'next/navigation';
import { Activity, AlertTriangle, Bot, CheckCircle2, CircleDollarSign, Clock3, DatabaseZap, Gauge, Megaphone, MessageCircleMore, MousePointerClick, PauseCircle, RefreshCw, ShieldAlert, Sparkles, Target } from 'lucide-react';
import { useAuth } from '../../../../context/auth-context';
import { adManagerApi } from '../../../../packages/ad-manager/api/ad-manager-api';
import { useAdManager } from '../../../../packages/ad-manager/hooks/use-ad-manager';
import { SettingsPanel } from '../../../../packages/ad-manager/components/SettingsPanel';
import { CreativeLab } from '../../../../packages/ad-manager/components/CreativeLab';
import { ExistingCampaignImport } from '../../../../packages/ad-manager/components/ExistingCampaignImport';
import styles from '../../../../packages/ad-manager/AdManager.module.css';

const tabs = [
  ['overview', 'نظرة عامة'], ['campaigns', 'الحملات'], ['creatives', 'المحتوى'],
  ['conversions', 'التحويلات'], ['decisions', 'قرارات AI'], ['settings', 'الإعدادات'],
] as const;

const money = (value: number) => new Intl.NumberFormat('ar-EG', { maximumFractionDigits: 2 }).format(value);
const dateTime = (value?: string) => value ? new Date(value).toLocaleString('ar-EG', { dateStyle: 'short', timeStyle: 'short' }) : 'لم يُسجّل بعد';

const jobLabels: Record<string, { label: string; schedule: string }> = {
  spend: { label: 'مراقبة الصرف والسقف', schedule: 'كل 5 دقائق' },
  sync: { label: 'مزامنة حالة الحملة مع Meta', schedule: 'كل 10 دقائق' },
  insights: { label: 'سحب الأداء من Meta', schedule: 'مرتين كل ساعة' },
  tracking: { label: 'فحص التتبع', schedule: 'كل 15 دقيقة' },
  decision: { label: 'قرار AI ومراجعة الأمان', schedule: 'كل ساعة' },
  fatigue: { label: 'فحص إرهاق المحتوى', schedule: 'كل 6 ساعات' },
  impact: { label: 'مراجعة أثر القرارات', schedule: 'كل ساعتين' },
  'conversion-delivery': { label: 'إرسال التحويلات', schedule: 'كل دقيقة' },
  rebalance: { label: 'إعادة توزيع الميزانية', schedule: 'يوميًا 04:00' },
  tests: { label: 'اقتراح اختبارات جديدة', schedule: 'كل يومين' },
  strategy: { label: 'مراجعة الاستراتيجية', schedule: 'كل يوم اثنين' },
};

export default function AdManagerPage() {
  const { activeProject } = useAuth();
  const projectId = activeProject?.id;
  const searchParams = useSearchParams();
  const data = useAdManager(projectId);
  const [tab, setTab] = useState<(typeof tabs)[number][0]>('overview');
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [connectionFinished, setConnectionFinished] = useState(false);
  const facebookConnected = searchParams.get('facebook') === 'connected';
  const activeTab = facebookConnected && !connectionFinished ? 'settings' : tab;

  const finishFacebookConnection = async (message: string) => {
    setConnectionFinished(true);
    setNotice(message);
    await data.refresh();
  };

  const run = async (action: () => Promise<unknown>, success: string) => {
    setBusy(true); setNotice(null);
    try { await action(); setNotice(success); await data.refresh(); }
    catch { setNotice('لم يتم تنفيذ الإجراء. راجع شروط الجاهزية والصلاحيات.'); }
    finally { setBusy(false); }
  };

  if (data.loading) return <div className={styles.loading} aria-busy="true"><span /><span /><span /></div>;

  return (
    <section className={styles.workspace} dir="rtl">
      <header className={styles.header}>
        <div>
          <p className={styles.eyebrow}>FACEBOOK • PROJECT AUTOPILOT</p>
          <h1>مدير الإعلانات</h1>
          <p>إنشاء واختبار وتحسين إعلانات Facebook من محتوى المشروع ونتائجه التجارية.</p>
        </div>
        <div className={styles.headerActions}>
          <button className={styles.secondaryButton} onClick={() => void data.refresh()} disabled={busy}><RefreshCw size={17} /> تحديث</button>
          {data.overview?.emergencyStop ? (
            <button className={styles.secondaryButton} onClick={() => projectId && void run(() => adManagerApi.resume(projectId), 'تم فحص الاستعادة. فعّل Autopilot بعد مراجعة الحالة.')} disabled={busy}>استعادة آمنة</button>
          ) : (
            <button className={styles.dangerButton} onClick={() => projectId && window.confirm('سيتم إيقاف كل إعلانات النظام فورًا بدون حذفها. هل أنت متأكد؟') && void run(() => adManagerApi.emergencyStop(projectId, 'Manual dashboard emergency stop'), 'تم إيقاف إعلانات النظام بأمان.')} disabled={busy}><ShieldAlert size={17} /> إيقاف طارئ</button>
          )}
        </div>
      </header>

      {notice && <div className={styles.notice} role="status" aria-live="polite">{notice}</div>}
      {data.error && <div className={styles.error} role="alert">{data.error}</div>}

      <div className={styles.healthBar}>
        <span className={data.overview?.readiness.ready ? styles.good : styles.warning}>
          {data.overview?.readiness.ready ? <CheckCircle2 size={17} /> : <AlertTriangle size={17} />}
          {data.overview?.readiness.ready ? 'جاهز للتشغيل' : 'الإعداد غير مكتمل'}
        </span>
        <span><Gauge size={16} /> Autopilot: {data.overview?.autopilot ? 'يعمل' : 'متوقف'}</span>
        <span><Sparkles size={16} /> AI: {data.overview?.aiModel ?? '—'} {data.overview?.usesProjectApiKey ? '· مفتاح المشروع' : '· مفتاح النظام'}</span>
        <span>آخر تحديث: {data.overview?.asOfUtc ? new Date(data.overview.asOfUtc).toLocaleTimeString('ar-EG') : '—'}</span>
      </div>

      <nav className={styles.tabs} aria-label="أقسام مدير الإعلانات">
        {tabs.map(([key, label]) => <button key={key} className={activeTab === key ? styles.activeTab : ''} aria-current={activeTab === key ? 'page' : undefined} onClick={() => setTab(key)}>{label}</button>)}
      </nav>

      {activeTab === 'overview' && <>
        {!data.overview?.readiness.ready && <div className={styles.readiness}>
          <div><Target size={22} /><h2>جهّز المشروع قبل الصرف</h2><p>لن يتم إنشاء أو تعديل أي ميزانية قبل اكتمال العناصر التالية.</p></div>
          <ol>{data.overview?.readiness.items.map((item) => <li key={item.key} className={item.ready ? styles.complete : ''}><span>{item.ready ? <CheckCircle2 size={18} /> : item.key === 'budget' ? <CircleDollarSign size={18} /> : <PauseCircle size={18} />}</span><div><strong>{item.label}</strong>{item.reason && <small>{item.reason}</small>}</div></li>)}</ol>
          <button className={styles.primaryButton} onClick={() => setTab('settings')}>إكمال الإعداد</button>
        </div>}
        <div className={styles.metrics}>
          <article><span>الصرف</span><strong>{money(data.overview?.spend ?? 0)}</strong><small>من سقف {money(data.overview?.dailyCap ?? 0)} يوميًا</small></article>
          <article><span>الإيراد المؤكد</span><strong>{money(data.overview?.revenue ?? 0)}</strong><small>ROAS {data.overview?.roas ?? 0}×</small></article>
          <article><span>النتائج</span><strong>{data.overview?.bookings ?? 0}</strong><small>{data.overview?.purchases ?? 0} شراء، {data.overview?.leads ?? 0} Lead مؤهل</small></article>
          <article><span>الإعلانات</span><strong>{data.overview?.activeAds ?? 0}</strong><small>من {data.overview?.totalAds ?? 0} إعلان</small></article>
        </div>
        <OperationalReport
          overview={data.overview}
          busy={busy}
          onSync={() => projectId && void run(() => adManagerApi.syncNow(projectId), 'بدأ سحب حالة الحملة والأداء. ستظهر أحدث الأرقام خلال لحظات.')}
        />
        <div className={styles.allocation}>
          <div><h2>توزيع السقف اليومي</h2><p>المبلغ المتاح بعد حجز هامش الأمان: {money(data.overview?.usableCap ?? 0)}</p></div>
          <div className={styles.allocationBar} aria-label="70% فائز، 15% محتوى، 10% جمهور، 5% إعادة استهداف"><span style={{ width: '70%' }}>70% فائز</span><span style={{ width: '15%' }}>15%</span><span style={{ width: '10%' }}>10%</span><span style={{ width: '5%' }}>5%</span></div>
        </div>
      </>}

      {activeTab === 'campaigns' && <>{projectId && <ExistingCampaignImport projectId={projectId} dailyCap={data.overview?.dailyCap ?? 0} refreshToken={data.overview?.asOfUtc ?? ''} onImported={data.refresh} />}<DataTable empty="لا توجد حملات مدارة بعد." headers={['الإعلان', 'الحالة', 'الميزانية', 'المصدر', 'المنصة']} rows={data.campaigns.map(x => [x.name, `${x.status} / ${x.effectiveStatus}`, money(x.dailyBudget), x.managementSource === 'ImportedFromMeta' ? 'حملة موجودة' : 'أنشأها النظام', x.publisherPlatform === 'facebook' ? 'Facebook فقط' : x.publisherPlatform])} /></>}
      {activeTab === 'creatives' && <><CreativeLab projectId={projectId ?? ''} creatives={data.creatives} onChanged={data.refresh} /><DataTable empty="اربط الصفحة أو أضف صورًا وفيديوهات للمشروع." headers={['المصدر', 'النوع', 'الأهلية', 'التقييم', 'الإرهاق']} rows={data.creatives.map(x => [x.sourceType, x.mediaType, x.eligibility, `${x.recommendationScore}%`, x.fatigueState])} /></>}
      {activeTab === 'conversions' && <DataTable empty="لم تصل تحويلات مؤكدة بعد." headers={['الحدث', 'الوقت', 'القيمة', 'الحالة', 'الإسناد']} rows={data.conversions.map(x => [x.eventType, new Date(x.occurredAtUtc).toLocaleString('ar-EG'), x.currentValue ? `${money(x.currentValue)} ${x.currency ?? ''}` : '—', x.state, x.attributionMethod])} />}
      {activeTab === 'decisions' && <DataTable empty="لا توجد قرارات AI حتى الآن." headers={['القرار', 'الهدف', 'المخاطر', 'الحالة', 'الوقت']} rows={data.decisions.map(x => [x.actionType, x.targetType, x.riskClass, x.state, new Date(x.createdAt).toLocaleString('ar-EG')])} />}
      {activeTab === 'settings' && <div className={styles.settings}>
        {projectId && <SettingsPanel projectId={projectId} dailyCap={data.overview?.dailyCap ?? 0} onSaved={finishFacebookConnection} />}
        <div><Megaphone size={20} /><h2>Facebook فقط في الإصدار الأول</h2><p>حتى لو كانت الوجهة WhatsApp أو Messenger، الـplacement يظل Facebook ولن يتم تشغيل Instagram أو أي منصة أخرى.</p></div>
        <div><Sparkles size={20} /><h2>المحتوى المسموح</h2><p>بوستات الصفحة وصور وفيديوهات المشروع، مع نصوص وCTA وقص ومقاسات وThumbnail؛ بدون توليد صورة أو فيديو من الصفر.</p></div>
        <div><MousePointerClick size={20} /><h2>التحويل الحقيقي</h2><p>الدفع والاشتراك والحضور أولوية، ثم الحجز والـTrial والـLead المؤهل، حسب جودة البيانات.</p></div>
        <div className={styles.controlRow}>
          <button className={styles.primaryButton} disabled={busy || !data.overview?.readiness.ready || data.overview?.autopilot} onClick={() => projectId && void run(() => adManagerApi.enable(projectId), 'تم تشغيل Autopilot داخل السقف المفوّض.')}>{data.overview?.autopilot ? 'Autopilot يعمل الآن' : 'تشغيل Autopilot'}</button>
          <button className={styles.secondaryButton} disabled={busy || !data.overview?.autopilot} onClick={() => projectId && void run(() => adManagerApi.disable(projectId), 'توقفت القرارات الجديدة وبقيت الإعلانات على آخر حالة آمنة.')}>إيقاف عادي</button>
        </div>
      </div>}
    </section>
  );
}

function OperationalReport({ overview, busy, onSync }: { overview: import('../../../../packages/ad-manager/types').AdvertisingOverview | null; busy: boolean; onSync: () => void }) {
  const operations = overview?.operations;
  if (!operations) return null;
  const campaign = operations.campaign;
  const latestDecision = operations.ai.latestDecision;
  return <section className={styles.operationalReport} aria-label="تقرير تشغيل مدير الإعلانات">
    <div className={styles.reportHeading}>
      <div>
        <p className={styles.sectionEyebrow}>LIVE OPERATIONS</p>
        <h2>حالة التشغيل الآن</h2>
        <p>ملخص مباشر للربط، السحب، التتبع، قرارات AI، والحملة التي يديرها النظام.</p>
      </div>
      <button className={styles.secondaryButton} onClick={onSync} disabled={busy || !overview?.readiness.ready}><DatabaseZap size={17} /> سحب الآن</button>
    </div>

    <div className={styles.statusRail}>
      <StatusItem icon={<Activity size={18} />} title="ربط Meta" good={operations.connection?.connected ?? false} value={operations.connection?.connected ? 'متصل وجاهز' : 'يحتاج مراجعة'} detail={`آخر مزامنة: ${dateTime(operations.connection?.lastSyncAtUtc)}`} />
      <StatusItem icon={<Target size={18} />} title="الحملة المدارة" good={campaign?.effectiveStatus === 'ACTIVE'} value={campaign ? campaign.effectiveStatus || 'تحت المراجعة' : 'لا توجد حملة'} detail={campaign ? `${money(campaign.dailyBudget)} جنيه يوميًا · آخر قراءة ${dateTime(campaign.lastSyncedAtUtc)}` : 'اختر حملة من تبويب الحملات'} />
      <StatusItem icon={<CheckCircle2 size={18} />} title="تتبع النتائج" good={operations.tracking.healthy} value={operations.tracking.healthy ? 'سليم' : 'متوقف للحماية'} detail={operations.tracking.mode === 'CRM_WHATSAPP' ? 'رسائل ونتائج WhatsApp من CRM، بدون Pixel' : 'CRM وDataset متصلان'} />
      <StatusItem icon={<Bot size={18} />} title="AI الإعلاني" good={Boolean(latestDecision)} value={latestDecision ? latestDecision.actionType : 'ينتظر أول دورة'} detail={latestDecision ? `آخر قرار: ${dateTime(latestDecision.createdAt)}` : `الموديل: ${operations.ai.model}`} />
    </div>

    <div className={styles.reportGrid}>
      <div className={styles.reportBlock}>
        <div className={styles.reportBlockTitle}><DatabaseZap size={18} /><h3>آخر سحب من Meta</h3></div>
        <div className={styles.performanceLine}><strong>{money(overview?.spend ?? 0)} جنيه</strong><span>صرف اليوم</span></div>
        <dl className={styles.compactFacts}>
          <div><dt>آخر سحب</dt><dd>{dateTime(operations.performance.lastPulledAtUtc)}</dd></div>
          <div><dt>الظهور اليوم</dt><dd>{money(operations.performance.impressions)}</dd></div>
          <div><dt>الضغطات اليوم</dt><dd>{money(operations.performance.clicks)}</dd></div>
          <div><dt>سجل الأداء</dt><dd>{operations.performance.snapshots} قراءة عبر {operations.performance.daysLoaded} أيام</dd></div>
        </dl>
      </div>
      <div className={styles.reportBlock}>
        <div className={styles.reportBlockTitle}><MessageCircleMore size={18} /><h3>AI وWhatsApp</h3></div>
        <dl className={styles.compactFacts}>
          <div><dt>الموديل</dt><dd>{operations.ai.model}</dd></div>
          <div><dt>مفتاح AI</dt><dd>{operations.ai.usesProjectApiKey ? 'مفتاح المشروع' : 'مفتاح النظام'}</dd></div>
          <div><dt>التحويلات المؤكدة</dt><dd>{overview?.bookings ?? 0} حجز، {overview?.purchases ?? 0} شراء، {overview?.leads ?? 0} Lead مؤهل</dd></div>
          <div><dt>حالة القرارات</dt><dd>{latestDecision ? `${latestDecision.actionType} · ${latestDecision.state}` : 'لا توجد بيانات كافية بعد'}</dd></div>
        </dl>
      </div>
    </div>

    {operations.connection?.lastErrorSummary && <div className={styles.operationWarning} role="status"><AlertTriangle size={17} /><span>آخر ملاحظة من Meta: {operations.connection.lastErrorSummary}</span></div>}
    {operations.lastFailure && <div className={styles.operationWarning} role="status"><AlertTriangle size={17} /><span>آخر محاولة غير مكتملة: {jobLabels[operations.lastFailure.jobName]?.label ?? operations.lastFailure.jobName}، وستُعاد تلقائيًا في موعدها التالي.</span></div>}
    {operations.tracking.openIncidents.length > 0 && <div className={styles.operationWarning} role="alert"><AlertTriangle size={17} /><span>{operations.tracking.openIncidents[0].summary}</span></div>}

    <div className={styles.schedulerHeader}><div><Clock3 size={18} /><h3>مهام النظام المجدولة</h3></div><span>يتم التنفيذ تلقائيًا، ولا يحتاج منك تشغيل يدوي.</span></div>
    <div className={styles.schedulerList}>
      {Object.entries(jobLabels).map(([key, job]) => {
        const run = operations.jobs.find(item => item.jobName === key);
        const ok = run?.state === 'Completed';
        return <div className={styles.schedulerRow} key={key}>
          <span className={ok ? styles.runGood : styles.runWait}>{ok ? <CheckCircle2 size={16} /> : <Clock3 size={16} />}</span>
          <strong>{job.label}</strong>
          <small>{job.schedule}</small>
          <time>{run ? `${ok ? 'آخر تشغيل' : 'الحالة'}: ${dateTime(run.completedAtUtc ?? run.startedAtUtc)}` : 'لم يبدأ بعد'}</time>
        </div>;
      })}
    </div>
  </section>;
}

function StatusItem({ icon, title, good, value, detail }: { icon: ReactNode; title: string; good: boolean; value: string; detail: string }) {
  return <div className={styles.statusItem}>
    <span className={good ? styles.runGood : styles.runWait}>{icon}</span>
    <div><small>{title}</small><strong>{value}</strong><p>{detail}</p></div>
  </div>;
}

function DataTable({ headers, rows, empty }: { headers: string[]; rows: (string | number)[][]; empty: string }) {
  if (!rows.length) return <div className={styles.empty}><Megaphone size={28} /><h2>{empty}</h2><p>ستظهر البيانات هنا بعد اكتمال الربط والتشغيل.</p></div>;
  return <div className={styles.tableWrap}><table><thead><tr>{headers.map(h => <th key={h}>{h}</th>)}</tr></thead><tbody>{rows.map((row, i) => <tr key={i}>{row.map((cell, j) => <td key={j}>{cell}</td>)}</tr>)}</tbody></table></div>;
}
