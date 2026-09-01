'use client';

import React from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { User, Zap } from 'lucide-react';
import { navigationItemsForRole } from '../../../config/navigation';
import { useAuth } from '../../../context/auth-context';
import styles from '../inbox.module.css';

export default function ThinSidebar() {
  const pathname = usePathname();
  const { user } = useAuth();
  const inboxNavigationItems = navigationItemsForRole(user?.role).filter((item) => item.compact);

  return (
    <aside className={styles.thinSidebar} aria-label="تنقل المحادثات">
      <Link
        href="/dashboard"
        className={styles.sidebarLogoContainer}
        aria-label="العودة إلى لوحة التحكم"
        data-dashboard-navigation
      >
        <div className={styles.sidebarLogoBox}>
          <Zap size={22} fill="currentColor" aria-hidden="true" />
        </div>
      </Link>

      {/* Navigation Menu */}
      <nav className={styles.sidebarNav} aria-label="الأقسام الرئيسية">
        {inboxNavigationItems.map((item) => {
          const Icon = item.icon;
          const isActive = pathname === item.path || pathname.startsWith(`${item.path}/`);
          return (
            <Link
              key={item.path}
              href={item.path}
              className={`${styles.sidebarNavItem} ${isActive ? styles.sidebarNavItemActive : ''}`}
              aria-label={item.name}
              aria-current={isActive ? 'page' : undefined}
              data-dashboard-navigation
            >
              <Icon size={20} strokeWidth={1.5} />
              <span className={styles.sidebarNavLabel}>{item.name}</span>
            </Link>
          );
        })}
      </nav>

      {/* User profile avatar / Footer */}
      <div className={styles.sidebarFooter} aria-hidden="true">
        <div className={styles.sidebarAvatar}>
          <User size={18} aria-hidden="true" />
        </div>
      </div>
    </aside>
  );
}
