'use client';

import { useState } from 'react';
import { api } from '@/services/api';
import type { ReviewSchedule } from './processing-types';
import styles from './reply-review.module.css';

export default function ReviewScheduleControls({ initial, url, onSaved }: { initial: ReviewSchedule; url: string; onSaved: () => void }) {
  const [form, setForm] = useState(initial);
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState('');
  async function save(event: React.FormEvent) {
    event.preventDefault(); setSaving(true); setNotice('');
    try {
      await api.put(`${url}/schedule`, form);
      setNotice(form.enabled ? 'تم حفظ الجدولة. سيُلتقط الموعد التالي خلال دقيقة.' : 'تم إيقاف بدء المعالجات الجديدة.');
      onSaved();
    } catch { setNotice('تعذر حفظ الجدولة. راجع الاتصال وحاول مرة أخرى.'); }
    finally { setSaving(false); }
  }
  return <details className={styles.scheduleSettings}>
    <summary>إعدادات الجدولة والمعالجة</summary>
    <form onSubmit={event => void save(event)}>
      <div className={styles.scheduleFields}>
        <label>فحص المحادثات كل (دقيقة)<input type="number" min={5} max={1440} required value={form.intervalMinutes}
          onChange={e => setForm({ ...form, intervalMinutes: Number(e.target.value) })} /></label>
        <label>انتظار هدوء المحادثة (دقيقة)<input type="number" min={1} max={120} required value={form.quietMinutes}
          onChange={e => setForm({ ...form, quietMinutes: Number(e.target.value) })} /></label>
        <label>التحقق بعد المعالجة (دقيقة)<input type="number" min={5} max={1440} required value={form.verifyAfterMinutes}
          onChange={e => setForm({ ...form, verifyAfterMinutes: Number(e.target.value) })} /></label>
        <label>محادثات الدفعة<input type="number" min={1} max={50} required value={form.batchSize}
          onChange={e => setForm({ ...form, batchSize: Number(e.target.value) })} /></label>
      </div>
      <label className={styles.checkbox}><input type="checkbox" checked={form.enabled} onChange={e => setForm({ ...form, enabled: e.target.checked })} />تشغيل المراجعة المجدولة</label>
      <label className={styles.checkbox}><input type="checkbox" checked={form.prepareDrafts} onChange={e => setForm({ ...form, prepareDrafts: e.target.checked })} />تجهيز مسودات تصحيح تلقائيًا للموظف</label>
      <p className={styles.note}>التصحيحات مسودات محفوظة؛ إرسالها يتم من المحادثة بعد مراجعتك. إيقاف الجدولة يمنع بدء مهام جديدة، وقد تُستكمل المراجعة الجارية.</p>
      <button type="submit" className={styles.primary} disabled={saving}>{saving ? 'جاري الحفظ…' : 'حفظ الجدولة'}</button>
      {notice && <p role="status" className={styles.note}>{notice}</p>}
    </form>
  </details>;
}
