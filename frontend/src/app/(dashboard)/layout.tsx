'use client';

import React, { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { useAuth } from '../../context/auth-context';
import { useRouter, usePathname } from 'next/navigation';
import { LogOut, X } from 'lucide-react';
import { navigationItemsForRole } from '../../config/navigation';
import ThinSidebar from '../../packages/inbox/shared/ThinSidebar';
import Header from '../../components/layout/Header';
import PhantomLoader from '../../components/shared/PhantomLoader';
import styles from '../../components/layout/layout.module.css';

export default function DashboardLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const { user, loading, logout } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const drawerRef = useRef<HTMLElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const appShellRef = useRef<HTMLDivElement>(null);

  // Redirect if not logged in
  useEffect(() => {
    if (!loading && !user) {
      router.push('/');
    }
  }, [user, loading, router]);

  useEffect(() => {
    const navigateByKeyboardShortcut = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      if (target?.matches('input, textarea, select, [contenteditable="true"]')) return;
      if (e.altKey && !e.ctrlKey && !e.metaKey) {
        const destination = navigationItemsForRole(user?.role).find((item) => item.shortcut === e.key);
        if (destination) {
          e.preventDefault();
          const navigationLink = document.querySelector<HTMLAnchorElement>(`a[data-dashboard-navigation][href="${destination.path}"]`);
          if (navigationLink) navigationLink.click();
          else router.push(destination.path);
        }
      }
    };

    window.addEventListener('keydown', navigateByKeyboardShortcut);
    return () => window.removeEventListener('keydown', navigateByKeyboardShortcut);
  }, [router, user?.role]);

  useEffect(() => {
    if (!mobileMenuOpen) return;

    const previouslyFocused = document.activeElement as HTMLElement | null;
    const appShell = appShellRef.current;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    appShell?.setAttribute('inert', '');
    closeButtonRef.current?.focus();

    const trapDrawerKeyboard = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        setMobileMenuOpen(false);
        return;
      }

      if (event.key !== 'Tab') return;
      const focusable = Array.from(
        drawerRef.current?.querySelectorAll<HTMLElement>('button:not(:disabled), a[href], [tabindex]:not([tabindex="-1"])') ?? [],
      );
      if (focusable.length === 0) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener('keydown', trapDrawerKeyboard);
    return () => {
      document.removeEventListener('keydown', trapDrawerKeyboard);
      document.body.style.overflow = previousOverflow;
      appShell?.removeAttribute('inert');
      previouslyFocused?.focus();
    };
  }, [mobileMenuOpen]);

  if (loading || !user) {
    return (
      <div className={styles.loadingContainer}>
        <PhantomLoader loading label="تحميل مساحة العمل">
          <div className={styles.authLoadingCard}>
            <div className={styles.authLoadingAvatar} />
            <div className={styles.authLoadingContent}>
              <div className={styles.authLoadingTitle}>تأمين اتصالك بلوحة التحكم</div>
              <div className={styles.authLoadingLine}>مراجعة بيانات الجلسة والمشروع النشط</div>
              <div className={styles.authLoadingMeta}>تجهيز أدوات المحادثات والعملاء</div>
            </div>
          </div>
        </PhantomLoader>
      </div>
    );
  }

  const navItems = navigationItemsForRole(user.role);

  return (
    <div className={styles.container}>
      <a className={styles.skipLink} href="#dashboard-main-content">
        تخطي إلى المحتوى الرئيسي
      </a>
      <div ref={appShellRef} className={styles.appShell} aria-hidden={mobileMenuOpen || undefined}>
        <ThinSidebar />

        <div className={styles.mainArea}>
          <Header onMenuClick={() => setMobileMenuOpen(true)} isMenuOpen={mobileMenuOpen} />

          <main className={styles.content} id="dashboard-main-content" tabIndex={-1}>
            {children}
          </main>
        </div>
      </div>

      {/* Mobile Drawer Navigation Menu */}
      {mobileMenuOpen && (
        <div className={styles.mobileOverlay} onMouseDown={(event) => { if (event.target === event.currentTarget) setMobileMenuOpen(false); }}>
          <aside
            ref={drawerRef}
            id="mobile-navigation-drawer"
            className={styles.mobileDrawer}
            role="dialog"
            aria-modal="true"
            aria-labelledby="mobile-navigation-title"
          >
            <div className={styles.drawerHeader}>
              <h2 id="mobile-navigation-title" className={styles.logoText}>Smart Customer Core</h2>
              <button 
                ref={closeButtonRef}
                type="button"
                onClick={() => setMobileMenuOpen(false)} 
                className={styles.closeBtn}
                aria-label="إغلاق القائمة"
              >
                <X size={24} />
              </button>
            </div>

            <nav className={styles.nav} aria-label="التنقل الرئيسي">
              {navItems.map((item) => {
                const Icon = item.icon;
                const isActive = pathname === item.path || pathname?.startsWith(item.path + '/');
                return (
                  <Link
                    key={item.path}
                    href={item.path}
                    onClick={() => {
                      setMobileMenuOpen(false);
                    }}
                    className={`${styles.navItem} ${isActive ? styles.navItemActive : ''}`}
                    aria-current={isActive ? 'page' : undefined}
                    style={{ background: 'none', border: 'none', width: '100%', textAlign: 'right' }}
                    data-dashboard-navigation
                  >
                    <Icon size={18} aria-hidden="true" />
                    <span>{item.name}</span>
                  </Link>
                );
              })}
            </nav>

            <div className={styles.drawerFooter}>
              <button 
                type="button"
                onClick={() => void logout()}
                className={styles.logoutBtn}
                style={{ background: 'none', border: 'none', width: '100%' }}
              >
                <LogOut size={18} aria-hidden="true" />
                <span>تسجيل الخروج</span>
              </button>
            </div>
          </aside>
        </div>
      )}
    </div>
  );
}
