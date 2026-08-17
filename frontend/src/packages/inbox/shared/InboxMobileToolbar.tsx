'use client';

import React, { useEffect, useState } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { ChevronDown, LogOut, Menu, X, Zap } from 'lucide-react';
import { useAuth } from '../../../context/auth-context';
import type { Project } from '../../../services/auth';
import { inboxNavigationItems } from './ThinSidebar';
import styles from '../inbox.module.css';

interface InboxMobileToolbarProps {
  onProjectSwitch: () => void;
}

interface MobileProjectSelectorProps {
  activeProjectId: string;
  projects: Project[];
  onChange: (projectId: string) => void;
}

function MobileProjectSelector({ activeProjectId, projects, onChange }: MobileProjectSelectorProps) {
  return (
    <label className={styles.inboxMobileProjectSelector}>
      <span>المشروع</span>
      <div className={styles.inboxMobileSelectWrap}>
        <select
          value={activeProjectId}
          onChange={(event) => onChange(event.target.value)}
          aria-label="تغيير المشروع"
          disabled={projects.length === 0}
        >
          {projects.length === 0 && <option value="">لا يوجد مشروع</option>}
          {projects.map((project) => <option key={project.id} value={project.id}>{project.name}</option>)}
        </select>
        <ChevronDown size={15} aria-hidden="true" />
      </div>
    </label>
  );
}

interface MobileNavigationDrawerProps {
  pathname: string;
  onClose: () => void;
  onNavigate: (path: string) => void;
  onLogout: () => void;
}

function MobileNavigationDrawer({ pathname, onClose, onNavigate, onLogout }: MobileNavigationDrawerProps) {
  return (
    <div className={styles.inboxMobileMenuOverlay} onClick={onClose}>
      <aside className={styles.inboxMobileDrawer} aria-label="قائمة التنقل" onClick={(event) => event.stopPropagation()}>
        <div className={styles.inboxMobileDrawerHeader}>
          <div className={styles.inboxMobileDrawerBrand}>
            <span className={styles.inboxMobileDrawerLogo}><Zap size={18} fill="currentColor" /></span>
            <span>سمارت سيلز</span>
          </div>
          <button type="button" className={styles.inboxMobileDrawerClose} onClick={onClose} aria-label="إغلاق القائمة">
            <X size={20} />
          </button>
        </div>

        <nav className={styles.inboxMobileDrawerNav}>
          {inboxNavigationItems.map((navigationItem) => {
            const Icon = navigationItem.icon;
            const isActive = pathname === navigationItem.path;
            return (
              <button
                key={navigationItem.path}
                type="button"
                className={`${styles.inboxMobileDrawerItem} ${isActive ? styles.inboxMobileDrawerItemActive : ''}`}
                onClick={() => onNavigate(navigationItem.path)}
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

export default function InboxMobileToolbar({ onProjectSwitch }: InboxMobileToolbarProps) {
  const { activeProject, projects, switchProject, logout } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const [menuOpen, setMenuOpen] = useState(false);

  useEffect(() => {
    if (!menuOpen) return;

    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setMenuOpen(false);
    };

    window.addEventListener('keydown', closeOnEscape);
    return () => window.removeEventListener('keydown', closeOnEscape);
  }, [menuOpen]);

  const changeProject = (projectId: string) => {
    if (projectId === activeProject?.id) return;
    onProjectSwitch();
    switchProject(projectId);
  };

  const navigateTo = (path: string) => {
    setMenuOpen(false);
    router.push(path);
  };

  return (
    <>
      <div className={styles.inboxMobileToolbar}>
        <button
          type="button"
          className={styles.inboxMobileMenuButton}
          onClick={() => setMenuOpen(true)}
          aria-label="فتح القائمة"
          aria-expanded={menuOpen}
        >
          <Menu size={22} />
        </button>

        <MobileProjectSelector
          activeProjectId={activeProject?.id ?? ''}
          projects={projects}
          onChange={changeProject}
        />
      </div>

      {menuOpen && <MobileNavigationDrawer
        pathname={pathname}
        onClose={() => setMenuOpen(false)}
        onNavigate={navigateTo}
        onLogout={() => void logout()}
      />}
    </>
  );
}
