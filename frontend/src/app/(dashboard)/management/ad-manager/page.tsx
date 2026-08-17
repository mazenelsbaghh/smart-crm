'use client';

import { useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { AlertTriangle, CheckCircle2, CircleDollarSign, Gauge, Megaphone, MousePointerClick, PauseCircle, RefreshCw, ShieldAlert, Sparkles, Target } from 'lucide-react';
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

export default function AdManagerPage() {
  const { activeProject } = useAuth();
  const projectId = activeProject?.id;
  const router = useRouter();
  const searchParams = useSearchParams();
  const data = useAdManager(projectId);
  const [tab, setTab] = useState<(typeof tabs)[number][0]>('overview');
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const facebookConnected = searchParams.get('facebook') === 'connected';
  const activeTab = facebookConnected ? 'settings' : tab;

  const finishFacebookConnection = async () => {
    await data.refresh();
    router.replace('/management/ad-manager');
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
          <article><span>النتائج</span><strong>{data.overview?.purchases ?? 0}</strong><small>{data.overview?.leads ?? 0} Lead مؤهل</small></article>
          <article><span>الإعلانات</span><strong>{data.overview?.activeAds ?? 0}</strong><small>من {data.overview?.totalAds ?? 0} إعلان</small></article>
        </div>
        <div className={styles.allocation}>
          <div><h2>توزيع السقف اليومي</h2><p>المبلغ المتاح بعد حجز هامش الأمان: {money(data.overview?.usableCap ?? 0)}</p></div>
          <div className={styles.allocationBar} aria-label="70% فائز، 15% محتوى، 10% جمهور، 5% إعادة استهداف"><span style={{ width: '70%' }}>70% فائز</span><span style={{ width: '15%' }}>15%</span><span style={{ width: '10%' }}>10%</span><span style={{ width: '5%' }}>5%</span></div>
        </div>
      </>}

      {activeTab === 'campaigns' && <>{projectId && <ExistingCampaignImport projectId={projectId} onImported={data.refresh} />}<DataTable empty="لا توجد حملات مدارة بعد." headers={['الإعلان', 'الحالة', 'الميزانية', 'المصدر', 'المنصة']} rows={data.campaigns.map(x => [x.name, `${x.status} / ${x.effectiveStatus}`, money(x.dailyBudget), x.managementSource === 'ImportedFromMeta' ? 'حملة موجودة' : 'أنشأها النظام', x.publisherPlatform === 'facebook' ? 'Facebook فقط' : x.publisherPlatform])} /></>}
      {activeTab === 'creatives' && <><CreativeLab projectId={projectId ?? ''} creatives={data.creatives} onChanged={data.refresh} /><DataTable empty="اربط الصفحة أو أضف صورًا وفيديوهات للمشروع." headers={['المصدر', 'النوع', 'الأهلية', 'التقييم', 'الإرهاق']} rows={data.creatives.map(x => [x.sourceType, x.mediaType, x.eligibility, `${x.recommendationScore}%`, x.fatigueState])} /></>}
      {activeTab === 'conversions' && <DataTable empty="لم تصل تحويلات مؤكدة بعد." headers={['الحدث', 'الوقت', 'القيمة', 'الحالة', 'الإسناد']} rows={data.conversions.map(x => [x.eventType, new Date(x.occurredAtUtc).toLocaleString('ar-EG'), x.currentValue ? `${money(x.currentValue)} ${x.currency ?? ''}` : '—', x.state, x.attributionMethod])} />}
      {activeTab === 'decisions' && <DataTable empty="لا توجد قرارات AI حتى الآن." headers={['القرار', 'الهدف', 'المخاطر', 'الحالة', 'الوقت']} rows={data.decisions.map(x => [x.actionType, x.targetType, x.riskClass, x.state, new Date(x.createdAt).toLocaleString('ar-EG')])} />}
      {activeTab === 'settings' && <div className={styles.settings}>
        {projectId && <SettingsPanel projectId={projectId} loadResources={facebookConnected} onSaved={finishFacebookConnection} />}
        <div><Megaphone size={20} /><h2>Facebook فقط في الإصدار الأول</h2><p>حتى لو كانت الوجهة WhatsApp أو Messenger، الـplacement يظل Facebook ولن يتم تشغيل Instagram أو أي منصة أخرى.</p></div>
        <div><Sparkles size={20} /><h2>المحتوى المسموح</h2><p>بوستات الصفحة وصور وفيديوهات المشروع، مع نصوص وCTA وقص ومقاسات وThumbnail؛ بدون توليد صورة أو فيديو من الصفر.</p></div>
        <div><MousePointerClick size={20} /><h2>التحويل الحقيقي</h2><p>الدفع والاشتراك والحضور أولوية، ثم الحجز والـTrial والـLead المؤهل، حسب جودة البيانات.</p></div>
        <div className={styles.controlRow}>
          <button className={styles.primaryButton} disabled={busy || !data.overview?.readiness.ready || data.overview?.autopilot} onClick={() => projectId && void run(() => adManagerApi.enable(projectId), 'تم تشغيل Autopilot داخل السقف المفوّض.')}>تشغيل Autopilot</button>
          <button className={styles.secondaryButton} disabled={busy || !data.overview?.autopilot} onClick={() => projectId && void run(() => adManagerApi.disable(projectId), 'توقفت القرارات الجديدة وبقيت الإعلانات على آخر حالة آمنة.')}>إيقاف عادي</button>
        </div>
      </div>}
    </section>
  );
}

function DataTable({ headers, rows, empty }: { headers: string[]; rows: (string | number)[][]; empty: string }) {
  if (!rows.length) return <div className={styles.empty}><Megaphone size={28} /><h2>{empty}</h2><p>ستظهر البيانات هنا بعد اكتمال الربط والتشغيل.</p></div>;
  return <div className={styles.tableWrap}><table><thead><tr>{headers.map(h => <th key={h}>{h}</th>)}</tr></thead><tbody>{rows.map((row, i) => <tr key={i}>{row.map((cell, j) => <td key={j}>{cell}</td>)}</tr>)}</tbody></table></div>;
}
