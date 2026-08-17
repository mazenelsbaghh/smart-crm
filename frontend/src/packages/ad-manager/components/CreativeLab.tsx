'use client';

import { useState } from 'react';
import { Film, ImageIcon, LoaderCircle, Rocket, Sparkles } from 'lucide-react';
import { adManagerApi } from '../api/ad-manager-api';
import type { Creative, FacebookPagePost } from '../types';
import styles from '../AdManager.module.css';

export function CreativeLab({ projectId, creatives, onChanged }: { projectId: string; creatives: Creative[]; onChanged: () => Promise<unknown> }) {
  const [posts, setPosts] = useState<FacebookPagePost[]>([]);
  const [selected, setSelected] = useState<string[]>([]);
  const [busy, setBusy] = useState(false); const [message, setMessage] = useState<string | null>(null);

  const discover = async () => {
    setBusy(true); setMessage(null);
    try {
      const nextPosts = await adManagerApi.pagePosts(projectId);
      const ranked = [...nextPosts].sort((a, b) => new Date(b.createdAtUtc ?? 0).getTime() - new Date(a.createdAtUtc ?? 0).getTime());
      setPosts(ranked);
      setSelected(ranked.slice(0, 3).map(x => x.id)); setMessage(ranked.length ? 'رتّبنا أحدث البوستات أولًا. عدّل الاختيار قبل الإطلاق.' : 'لا توجد بوستات صور أو فيديو متاحة على الصفحة.');
    } catch { setMessage('تعذّر سحب بوستات الصفحة أو العروض الموثقة.'); } finally { setBusy(false); }
  };

  const launch = async () => {
    setBusy(true); setMessage(null);
    try {
      if (selected.length > 0) await adManagerApi.importPosts(projectId, posts.filter(x => selected.includes(x.id)));
      const launch = await adManagerApi.startWhatsAppTest(projectId);
      setMessage(launch.createdAds > 0
        ? `تم إنشاء ${launch.createdAds} إعلان اختبار لواتساب. ${launch.state === 'ACTIVATION_QUEUED' ? 'تمت الموافقة على التفعيل.' : 'ما زال متوقفًا لحين مراجعة التفعيل.'}`
        : launch.reason);
      await onChanged();
    } catch { setMessage('لم يكتمل اختبار WhatsApp. تأكد من الحملة النشطة والصفحة وسقف الصرف.'); } finally { setBusy(false); }
  };

  return <div className={styles.creativeLab}>
    <div className={styles.labHeader}><div><Sparkles size={22} /><h2>اختبار Creatives لواتساب</h2><p>يستخدم بوستات وفيديوهات الصفحة فقط، ويضيفها إلى نفس إعداد حملة WhatsApp بدون Pixel.</p></div><button className={styles.secondaryButton} onClick={() => void discover()} disabled={busy}>{busy ? <LoaderCircle size={17} /> : <Sparkles size={17} />} سحب وترتيب البوستات</button></div>
    {posts.length > 0 && <>
      <div className={styles.postGrid}>{posts.map((post, index) => <label key={post.id} className={`${styles.postCard} ${selected.includes(post.id) ? styles.postSelected : ''}`}>
        <input type="checkbox" checked={selected.includes(post.id)} onChange={() => setSelected(value => value.includes(post.id) ? value.filter(x => x !== post.id) : value.length < 12 ? [...value, post.id] : value)} />
        <span>{post.mediaType === 'Video' ? <Film size={20} /> : <ImageIcon size={20} />}</span><strong>#{index + 1} · {post.mediaType === 'Video' ? 'فيديو' : 'صورة'}</strong><small>{post.message?.slice(0, 90) || 'بوست بدون نص'}</small>
      </label>)}</div>
      <div className={styles.launchForm}><button className={styles.primaryButton} onClick={() => void launch()} disabled={busy || selected.length === 0}><Rocket size={17} /> إنشاء اختبار WhatsApp من {selected.length} محتوى</button></div>
    </>}
    {posts.length === 0 && <button className={styles.primaryButton} onClick={() => void launch()} disabled={busy}><Rocket size={17} /> فحص المحتوى المحفوظ وبدء اختبار WhatsApp</button>}
    {message && <p className={styles.inlineMessage} role="status">{message}</p>}
    {creatives.length > 0 && <p className={styles.labSummary}>المكتبة الحالية: {creatives.length} محتوى · الفيديو مدعوم في Feed وFacebook Reels، والصور في Feed وStory.</p>}
  </div>;
}
