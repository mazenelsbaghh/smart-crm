'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { ArrowUpLeft, Search } from 'lucide-react';
import { useAuth } from '../../context/auth-context';
import { navigationItemsForRole, navigationGroups } from '../../config/navigation';
import Dialog from '../shared/Dialog';
import Button from '../ui/Button';
import styles from './workspace-search.module.css';

function SearchResults({ onClose }: { onClose: () => void }) {
  const { user } = useAuth();
  const [query, setQuery] = useState('');
  const [activeIndex, setActiveIndex] = useState(0);
  const destinations = useMemo(() => {
    const normalizedQuery = query.trim().toLocaleLowerCase('ar');
    return navigationItemsForRole(user?.role).filter(destination => !normalizedQuery
      || [destination.name, ...(destination.keywords ?? [])].some(label => label.toLocaleLowerCase('ar').includes(normalizedQuery)));
  }, [query, user?.role]);
  return <Dialog label="البحث في مساحة العمل" title="البحث في مساحة العمل" closeLabel="إغلاق البحث" onClose={onClose}
    footer={<><p className={styles.keyboardHint}>↑ ↓ للتنقل <span>Enter لفتح الصفحة</span><span>Esc للإغلاق</span></p><Button variant="secondary" onClick={onClose}>إغلاق</Button></>}>
    <div className={styles.searchField}>
      <Search size={20} aria-hidden="true" />
      <input autoFocus type="search" role="combobox" aria-label="البحث في صفحات لوحة التحكم" placeholder="اكتب اسم الصفحة أو القسم…"
        aria-expanded="true" aria-controls="workspace-search-results" aria-autocomplete="list"
        aria-activedescendant={destinations[activeIndex] ? `workspace-result-${activeIndex}` : undefined}
        value={query} onChange={event => { setQuery(event.target.value); setActiveIndex(0); }}
        onKeyDown={event => {
          if (event.nativeEvent.isComposing) return;
          if (event.key === 'Escape') { event.preventDefault(); onClose(); }
          if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') event.preventDefault();
          if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key) && destinations.length) {
            event.preventDefault();
            const nextIndex = event.key === 'Home' ? 0 : event.key === 'End' ? destinations.length - 1
              : (activeIndex + (event.key === 'ArrowDown' ? 1 : -1) + destinations.length) % destinations.length;
            setActiveIndex(nextIndex);
            document.getElementById(`workspace-result-${nextIndex}`)?.scrollIntoView({ block: 'nearest' });
          }
          if (event.key === 'Enter' && destinations[activeIndex]) {
            event.preventDefault(); document.getElementById(`workspace-result-${activeIndex}`)?.click();
          }
        }} />
    </div>
    <p className={styles.resultContext} role="status">{query ? `${destinations.length.toLocaleString('ar-EG')} نتيجة للبحث` : 'انتقل مباشرة إلى أي قسم في مساحة العمل'}</p>
    <div id="workspace-search-results" role="listbox" aria-label="نتائج البحث" className={styles.results}>
      {destinations.map((destination, index) => {
        const Icon = destination.icon;
        return <Link key={destination.path} id={`workspace-result-${index}`} href={destination.path} role="option"
          tabIndex={-1} aria-selected={activeIndex === index} onPointerMove={() => setActiveIndex(index)}
          onClick={onClose} data-dashboard-navigation>
          <Icon size={19} aria-hidden="true" /><span>{destination.name}<small>{navigationGroups.find(group => group.key === destination.group)?.label}</small></span>
          <ArrowUpLeft size={16} aria-hidden="true" />
        </Link>;
      })}
      {!destinations.length && <p className={styles.empty}>لا توجد صفحة بهذا الاسم. جرّب «العملاء» أو «المواعيد».</p>}
    </div>
  </Dialog>;
}

export default function WorkspaceSearch() {
  const [open, setOpen] = useState(false);
  useEffect(() => {
    const openFromKeyboard = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault(); setOpen(true);
      }
    };
    window.addEventListener('keydown', openFromKeyboard);
    return () => window.removeEventListener('keydown', openFromKeyboard);
  }, []);
  return <>
    <button type="button" className={styles.trigger} aria-label="البحث في مساحة العمل" aria-haspopup="dialog" onClick={() => setOpen(true)}>
      <Search size={18} aria-hidden="true" /><span>ابحث في مساحة العمل…</span><kbd aria-hidden="true">⌘ K</kbd>
    </button>
    {open && <SearchResults onClose={() => setOpen(false)} />}
  </>;
}
