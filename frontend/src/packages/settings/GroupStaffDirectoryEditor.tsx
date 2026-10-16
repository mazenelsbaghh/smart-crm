'use client';

import styles from './settings.module.css';

export interface StaffContact { id: string; name: string; phone: string }

interface Props {
  title: string;
  hint: string;
  contacts: StaffContact[];
  disabled: boolean;
  onChange: (contacts: StaffContact[]) => void;
}

export default function GroupStaffDirectoryEditor({ title, hint, contacts, disabled, onChange }: Props) {
  const update = (id: string, field: 'name' | 'phone', value: string) =>
    onChange(contacts.map(contact => contact.id === id ? { ...contact, [field]: value } : contact));
  return (
    <fieldset disabled={disabled} style={{ border: 0, padding: 0, minWidth: 0 }}>
      <legend style={{ fontWeight: 600, marginBottom: 8 }}>{title}</legend>
      <p className={styles.sectionHint}>{hint}</p>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 12, marginTop: 12 }}>
        {contacts.map((contact, index) => (
          <div key={contact.id} style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'end', gap: 8 }}>
            <label style={{ flex: '1 1 160px' }}>
              <span className={styles.label}>الاسم</span>
              <input aria-label={`${title}: الاسم ${index + 1}`} className={styles.input} value={contact.name}
                maxLength={120} onChange={event => update(contact.id, 'name', event.target.value)} />
            </label>
            <label style={{ flex: '1 1 160px' }}>
              <span className={styles.label}>رقم واتساب</span>
              <input aria-label={`${title}: رقم واتساب ${index + 1}`} className={styles.input} type="tel" dir="ltr"
                value={contact.phone} maxLength={64} placeholder="01xxxxxxxxx"
                onChange={event => update(contact.id, 'phone', event.target.value)} />
            </label>
            <button type="button" className={`${styles.btn} ${styles.btnSecondary}`}
              aria-label={`حذف ${contact.name || `الاسم ${index + 1}`} من ${title}`}
              onClick={() => onChange(contacts.filter(row => row.id !== contact.id))}>حذف</button>
          </div>
        ))}
        <button type="button" className={`${styles.btn} ${styles.btnSecondary}`} style={{ alignSelf: 'start' }}
          onClick={() => onChange([...contacts, { id: crypto.randomUUID(), name: '', phone: '' }])}>
          إضافة اسم إلى {title}
        </button>
      </div>
    </fieldset>
  );
}
