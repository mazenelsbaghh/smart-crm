'use client';

import React from 'react';
import { useRouter, usePathname } from 'next/navigation';
import { User, Zap } from 'lucide-react';
import { compactNavigationItems } from '../../../config/navigation';
import styles from '../inbox.module.css';

export const inboxNavigationItems = compactNavigationItems;

export default function ThinSidebar() {
  const router = useRouter();
  const pathname = usePathname();

  return (
    <div className={styles.thinSidebar}>
      {/* Premium Logo (Lime Green Zap Icon wrapper) */}
      <div className={styles.sidebarLogoContainer} onClick={() => router.push('/dashboard')}>
        <div className={styles.sidebarLogoBox}>
          <Zap size={22} fill="currentColor" />
        </div>
      </div>

      {/* Navigation Menu */}
      <nav className={styles.sidebarNav}>
        {inboxNavigationItems.map((item) => {
          const Icon = item.icon;
          const isActive = pathname === item.path;
          return (
            <button
              key={item.path}
              type="button"
              className={`${styles.sidebarNavItem} ${isActive ? styles.sidebarNavItemActive : ''}`}
              onClick={() => router.push(item.path)}
              aria-label={item.name}
            >
              <Icon size={20} strokeWidth={1.5} />
              <span className={styles.sidebarNavLabel}>{item.name}</span>
            </button>
          );
        })}
      </nav>

      {/* User profile avatar / Footer */}
      <div className={styles.sidebarFooter}>
        <div className={styles.sidebarAvatar}>
          <User size={18} />
        </div>
      </div>
    </div>
  );
}
