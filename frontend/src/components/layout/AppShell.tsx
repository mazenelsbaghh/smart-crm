'use client';

import { useEffect, useRef, useState, type ReactNode } from 'react';
import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { Ellipsis, Home, MessageSquare, Users, X } from 'lucide-react';
import { useAuth } from '../../context/auth-context';
import { navigationItemsForRole } from '../../config/navigation';
import Dialog from '../shared/Dialog';
import Button from '../ui/Button';
import AppNavigation, { Brand } from './AppNavigation';
import Header from './Header';
import styles from './app-shell.module.css';

function MobileNavigation({ onMore, menuOpen }: { onMore: () => void; menuOpen: boolean }) {
  const pathname = usePathname();
  const destinations = [
    { path: '/dashboard', label: 'الرئيسية', icon: Home },
    { path: '/inbox', label: 'المحادثات', icon: MessageSquare },
    { path: '/crm', label: 'العملاء', icon: Users },
  ];
  return <nav className={styles.mobileNavigation} aria-label="التنقل السريع">
    {destinations.map(destination => <Link key={destination.path} href={destination.path} data-dashboard-navigation
      aria-current={pathname === destination.path || pathname.startsWith(`${destination.path}/`) ? 'page' : undefined}>
      <destination.icon size={21} aria-hidden="true" /><span>{destination.label}</span>
    </Link>)}
    <button type="button" onClick={onMore} aria-haspopup="dialog" aria-expanded={menuOpen}
      data-active={menuOpen || !destinations.some(destination => pathname === destination.path || pathname.startsWith(`${destination.path}/`))}><Ellipsis size={22} aria-hidden="true" /><span>المزيد</span></button>
  </nav>;
}

export default function AppShell({ children, workspace = false, mobileConversation = false }: {
  children: ReactNode; workspace?: boolean; mobileConversation?: boolean;
}) {
  const { user, loading } = useAuth();
  const router = useRouter();
  const [menuOpen, setMenuOpen] = useState(false);
  const shellRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const viewport = window.visualViewport;
    const resizeWorkspace = () => {
      if (viewport?.scale === 1) shellRef.current?.style.setProperty('--app-height', `${viewport.height}px`);
    };
    resizeWorkspace();
    viewport?.addEventListener('resize', resizeWorkspace);
    return () => viewport?.removeEventListener('resize', resizeWorkspace);
  }, []);
  useEffect(() => { if (!loading && !user) router.replace('/'); }, [loading, user, router]);
  useEffect(() => {
    const navigateByShortcut = (event: KeyboardEvent) => {
      if (!event.altKey || event.ctrlKey || event.metaKey || (event.target as HTMLElement)?.closest('input, textarea, select, [contenteditable="true"]')) return;
      const destination = navigationItemsForRole(user?.role).find(link => link.shortcut === event.key || `Digit${link.shortcut}` === event.code);
      if (!destination) return;
      event.preventDefault();
      document.querySelector<HTMLAnchorElement>(`a[data-dashboard-navigation][href="${destination.path}"]`)?.click();
    };
    window.addEventListener('keydown', navigateByShortcut);
    return () => window.removeEventListener('keydown', navigateByShortcut);
  }, [user?.role]);
  if (loading || !user) return <div className={styles.loading} role="status">جاري تحميل مساحة العمل…<span className={styles.loadingLine} /></div>;
  return <div ref={shellRef} className={`${styles.shell} ${mobileConversation ? styles.mobileConversation : ''}`}>
    <a href="#workspace-main-content" className={styles.skipLink}>تخطي إلى المحتوى الرئيسي</a>
    <aside className={styles.sidebar}><Brand /><AppNavigation /></aside>
    <div className={styles.mainArea}>
      <div className={styles.globalHeader}><Header onMenuClick={() => setMenuOpen(true)} isMenuOpen={menuOpen} /></div>
      <main id="workspace-main-content" tabIndex={-1} className={`${styles.content} ${workspace ? styles.workspaceContent : ''}`}>{children}</main>
      <MobileNavigation onMore={() => setMenuOpen(true)} menuOpen={menuOpen} />
      <div hidden>{navigationItemsForRole(user?.role).filter(destination => destination.parent && destination.shortcut).map(destination =>
        <Link key={destination.path} href={destination.path} data-dashboard-navigation>{destination.name}</Link>)}</div>
    </div>
    {menuOpen && <Dialog label="قائمة التنقل" placement="drawer" onClose={() => setMenuOpen(false)}>
      <div className={styles.drawerHeader}><Brand /><Button variant="ghost" onClick={() => setMenuOpen(false)} aria-label="إغلاق القائمة"><X size={21} /></Button></div>
      <AppNavigation onNavigate={() => setMenuOpen(false)} />
    </Dialog>}
  </div>;
}
