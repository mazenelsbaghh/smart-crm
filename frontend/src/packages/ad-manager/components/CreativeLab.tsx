'use client';

import { useState } from 'react';
import { Film, ImageIcon, LoaderCircle, Rocket, Sparkles } from 'lucide-react';
import { adManagerApi } from '../api/ad-manager-api';
import type { AdvertisingOffer, Creative, FacebookPagePost } from '../types';
import styles from '../AdManager.module.css';

export function CreativeLab({ projectId, creatives, onChanged }: { projectId: string; creatives: Creative[]; onChanged: () => Promise<unknown> }) {
  const [posts, setPosts] = useState<FacebookPagePost[]>([]); const [offers, setOffers] = useState<AdvertisingOffer[]>([]);
  const [selected, setSelected] = useState<string[]>([]); const [offerId, setOfferId] = useState('');
  const [name, setName] = useState('اختبار محتوى Facebook'); const [destination, setDestination] = useState('');
  const [busy, setBusy] = useState(false); const [message, setMessage] = useState<string | null>(null);

  const discover = async () => {
    setBusy(true); setMessage(null);
    try {
      const [nextPosts, nextOffers] = await Promise.all([adManagerApi.pagePosts(projectId), adManagerApi.offers(projectId)]);
      const ranked = [...nextPosts].sort((a, b) => new Date(b.createdAtUtc ?? 0).getTime() - new Date(a.createdAtUtc ?? 0).getTime());
      setPosts(ranked); setOffers(nextOffers.filter(x => x.state === 'Eligible')); setOfferId(nextOffers.find(x => x.state === 'Eligible')?.id ?? '');
      setSelected(ranked.slice(0, 3).map(x => x.id)); setMessage(ranked.length ? 'رتّبنا أحدث البوستات أولًا. عدّل الاختيار قبل الإطلاق.' : 'لا توجد بوستات صور أو فيديو متاحة على الصفحة.');
    } catch { setMessage('تعذّر سحب بوستات الصفحة أو العروض الموثقة.'); } finally { setBusy(false); }
  };

  const launch = async () => {
    if (!offerId || selected.length === 0 || !destination.trim() || !name.trim()) { setMessage('اختر عرضًا وبوستًا واحدًا على الأقل واكتب رابط الوجهة.'); return; }
    setBusy(true); setMessage(null);
    try {
      const imported = await adManagerApi.importPosts(projectId, posts.filter(x => selected.includes(x.id)));
      const launch = await adManagerApi.activateLaunch(projectId, { offerId, creativeIds: imported.creativeIds, name: name.trim(), destinationUrl: destination.trim(), objective: 'OUTCOME_SALES', optimizationEvent: 'OFFSITE_CONVERSIONS', customEventType: 'PURCHASE' });
      setMessage(launch.providerState === 'ACTIVATION_QUEUED'
        ? `تم إنشاء ${launch.ads} إعلان Facebook وتقسيم السقف عليها، وبدأ تنفيذ التفعيل بعد موافقة المراجعة.`
        : `تم إنشاء ${launch.ads} إعلان Facebook وتقسيم السقف عليها. ما زالت متوقفة بأمان لحين اكتمال مراجعة AI.`);
      await onChanged();
    } catch { setMessage('لم يكتمل الإطلاق. تأكد من العرض والوجهة والسقف وجاهزية التتبع.'); } finally { setBusy(false); }
  };

  return <div className={styles.creativeLab}>
    <div className={styles.labHeader}><div><Sparkles size={22} /><h2>اختبار عدة إعلانات</h2><p>صور وفيديوهات من بوستات الصفحة. الاختيار المقترح يبدأ بالأحدث ويمكنك تغييره.</p></div><button className={styles.secondaryButton} onClick={() => void discover()} disabled={busy}>{busy ? <LoaderCircle size={17} /> : <Sparkles size={17} />} سحب وترتيب البوستات</button></div>
    {posts.length > 0 && <>
      <div className={styles.postGrid}>{posts.map((post, index) => <label key={post.id} className={`${styles.postCard} ${selected.includes(post.id) ? styles.postSelected : ''}`}>
        <input type="checkbox" checked={selected.includes(post.id)} onChange={() => setSelected(value => value.includes(post.id) ? value.filter(x => x !== post.id) : value.length < 12 ? [...value, post.id] : value)} />
        <span>{post.mediaType === 'Video' ? <Film size={20} /> : <ImageIcon size={20} />}</span><strong>#{index + 1} · {post.mediaType === 'Video' ? 'فيديو' : 'صورة'}</strong><small>{post.message?.slice(0, 90) || 'بوست بدون نص'}</small>
      </label>)}</div>
      <div className={styles.launchForm}>
        <label>العرض<select value={offerId} onChange={e => setOfferId(e.target.value)}><option value="">اختر عرضًا موثقًا</option>{offers.map(x => <option key={x.id} value={x.id}>{x.name}{x.price ? ` · ${x.price} ${x.currency ?? ''}` : ''}</option>)}</select></label>
        <label>اسم الاختبار<input value={name} onChange={e => setName(e.target.value)} /></label>
        <label>رابط الوجهة<input type="url" dir="ltr" placeholder="https://..." value={destination} onChange={e => setDestination(e.target.value)} /></label>
        <button className={styles.primaryButton} onClick={() => void launch()} disabled={busy || selected.length === 0}><Rocket size={17} /> إنشاء {selected.length} إعلان وتقسيم السقف</button>
      </div>
    </>}
    {message && <p className={styles.inlineMessage} role="status">{message}</p>}
    {creatives.length > 0 && <p className={styles.labSummary}>المكتبة الحالية: {creatives.length} محتوى · الفيديو مدعوم في Feed وFacebook Reels، والصور في Feed وStory.</p>}
  </div>;
}
