'use client';

import { Menu, Moon, Sun } from 'lucide-react';
import { usePathname } from 'next/navigation';
import { useTheme } from '../../context/theme-context';
import { useAuth } from '../../context/auth-context';
import { activeNavigationPath, navigationItemsForRole } from '../../config/navigation';
import ProjectSwitcher from './ProjectSwitcher';
import WorkspaceSearch from './WorkspaceSearch';
import styles from './layout.module.css';

export default function Header({ onMenuClick, isMenuOpen = false }: { onMenuClick: () => void; isMenuOpen?: boolean }) {
  const { user } = useAuth();
  const pathname = usePathname();
  const { isLight, toggleTheme } = useTheme();
  const destinations = navigationItemsForRole(user?.role);
  const currentPath = activeNavigationPath(pathname, destinations);
  const currentDestination = destinations.find(destination => destination.path === currentPath);
  return <header className={styles.header}>
    <button type="button" className={styles.mobileHamburger} onClick={onMenuClick}
      aria-label="فتح قائمة التنقل" aria-expanded={isMenuOpen} aria-haspopup="dialog"><Menu size={22} aria-hidden="true" /></button>
    <nav className={styles.breadcrumb} aria-label="موقعك في مساحة العمل"><span>مساحة العمل</span><span aria-hidden="true">/</span><strong>{currentDestination?.name || 'الرئيسية'}</strong></nav>
    <WorkspaceSearch />
    <div className={styles.headerToolbar}>
      <ProjectSwitcher />
      <button type="button" className={styles.toolbarIconBtn} onClick={toggleTheme}
        aria-label={isLight ? 'تفعيل الوضع الداكن' : 'تفعيل الوضع الفاتح'} aria-pressed={!isLight}>
        {isLight ? <Moon size={18} aria-hidden="true" /> : <Sun size={18} aria-hidden="true" />}
      </button>
    </div>
  </header>;
}
