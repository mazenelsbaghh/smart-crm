'use client';

import React, { useEffect, useRef, useState } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { LogOut, Menu, X, Zap } from 'lucide-react';
import { useAuth } from '../../../context/auth-context';
import { navigationItemsForRole, type NavigationItem } from '../../../config/navigation';
import styles from '../inbox.module.css';

function MobileProjectContext({ projectName }: { projectName?: string }) {
  return (
    <div className={styles.inboxMobileProjectContext} aria-label="المشروع الحالي">
      <span>المشروع</span>
      <strong className={styles.inboxMobileProjectName}>{projectName || 'مساحة العمل غير متاحة'}</strong>
    </div>
  );
}

interface MobileNavigationDrawerProps {
  pathname: string;
  onClose: () => void;
  onNavigate: (path: string) => void;
  onLogout: () => void;
  navigationItems: NavigationItem[];
}

function MobileNavigationDrawer({ pathname, onClose, onNavigate, onLogout, navigationItems }: MobileNavigationDrawerProps) {
  const drawerRef = useRef<HTMLElement>(null);

  const keepFocusInside = (event: React.KeyboardEvent<HTMLElement>) => {
    if (event.key !== 'Tab') return;
    const focusable = Array.from(
      drawerRef.current?.querySelectorAll<HTMLElement>('button:not(:disabled), a[href], select:not(:disabled)') ?? [],
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

  return (
    <div className={styles.inboxMobileMenuOverlay} onClick={onClose}>
      <aside
        ref={drawerRef}
        className={styles.inboxMobileDrawer}
        role="dialog"
        aria-modal="true"
        aria-label="قائمة التنقل"
        onClick={(event) => event.stopPropagation()}
        onKeyDown={keepFocusInside}
      >
        <div className={styles.inboxMobileDrawerHeader}>
          <div className={styles.inboxMobileDrawerBrand}>
            <span className={styles.inboxMobileDrawerLogo}><Zap size={18} fill="currentColor" /></span>
            <span>سمارت كاستمر</span>
          </div>
          <button type="button" className={styles.inboxMobileDrawerClose} onClick={onClose} aria-label="إغلاق القائمة" autoFocus>
            <X size={20} />
          </button>
        </div>

        <nav className={styles.inboxMobileDrawerNav}>
          {navigationItems.map((navigationItem) => {
            const Icon = navigationItem.icon;
            const isActive = pathname === navigationItem.path;
            return (
              <button
                key={navigationItem.path}
                type="button"
                className={`${styles.inboxMobileDrawerItem} ${isActive ? styles.inboxMobileDrawerItemActive : ''}`}
                onClick={() => onNavigate(navigationItem.path)}
                aria-current={isActive ? 'page' : undefined}
              >
                <Icon size={19} />
                <span>{navigationItem.name}</span>
              </button>
            );
          })}
        </nav>

        <button type="button" className={styles.inboxMobileLogout} onClick={onLogout}>
          <LogOut size={19} />
          <span>تسجيل الخروج</span>
        </button>
      </aside>
    </div>
  );
}

export default function InboxMobileToolbar() {
  const { activeProject, logout, user } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const [menuOpen, setMenuOpen] = useState(false);
  const menuButtonRef = useRef<HTMLButtonElement>(null);
  const wasMenuOpen = useRef(false);

  useEffect(() => {
    if (!menuOpen) return;

    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setMenuOpen(false);
    };

    window.addEventListener('keydown', closeOnEscape);
    return () => window.removeEventListener('keydown', closeOnEscape);
  }, [menuOpen]);

  useEffect(() => {
    if (wasMenuOpen.current && !menuOpen) menuButtonRef.current?.focus();
    wasMenuOpen.current = menuOpen;
  }, [menuOpen]);

  const navigateTo = (path: string) => {
    setMenuOpen(false);
    router.push(path);
  };

  return (
    <>
      <div className={styles.inboxMobileToolbar}>
        <button
          type="button"
          ref={menuButtonRef}
          className={styles.inboxMobileMenuButton}
          onClick={() => setMenuOpen(true)}
          aria-label="فتح القائمة"
          aria-expanded={menuOpen}
        >
          <Menu size={22} />
        </button>

        <MobileProjectContext projectName={activeProject?.name} />
      </div>

      {menuOpen && <MobileNavigationDrawer
        pathname={pathname}
        navigationItems={navigationItemsForRole(user?.role).filter((item) => item.compact)}
        onClose={() => setMenuOpen(false)}
        onNavigate={navigateTo}
        onLogout={() => void logout()}
      />}
    </>
  );
}
