'use client';

import React from 'react';
import { useRouter, usePathname } from 'next/navigation';
import {
  Home,
  MessageSquare,
  MessageCircle,
  MessageSquareMore,
  ListTodo,
  Megaphone,
  BarChart3,
  Settings,
  User,
  Users
} from 'lucide-react';
import styles from '../inbox.module.css';

const InstagramIcon = (props: React.SVGProps<SVGSVGElement>) => (
  <svg
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.5"
    strokeLinecap="round"
    strokeLinejoin="round"
    style={{ width: '20px', height: '20px' }}
    {...props}
  >
    <rect x="2" y="2" width="20" height="20" rx="5" ry="5" />
    <path d="M16 11.37A4 4 0 1 1 12.63 8 4 4 0 0 1 16 11.37z" />
    <line x1="17.5" y1="6.5" x2="17.51" y2="6.5" />
  </svg>
);

export default function ThinSidebar() {
  const router = useRouter();
  const pathname = usePathname();

  const navItems: { name: string; path: string; icon: React.ElementType }[] = [
    { name: 'الرئيسية', path: '/dashboard', icon: Home },
    { name: 'واتساب', path: '/inbox', icon: MessageSquare },
    { name: 'ماسنجر', path: '/inbox/messenger', icon: MessageCircle },
    { name: 'إنستغرام', path: '/inbox/instagram', icon: InstagramIcon },
    { name: 'التعليقات', path: '/inbox/comments', icon: MessageSquareMore },
    { name: 'العملاء CRM', path: '/crm', icon: Users },
    { name: 'المهام', path: '/management/follow-ups', icon: ListTodo },
    { name: 'الحملات', path: '/management/campaigns', icon: Megaphone },
    { name: 'التحليلات', path: '/management/reports', icon: BarChart3 },
    { name: 'الإعدادات', path: '/settings', icon: Settings },
  ];

  return (
    <div className={styles.thinSidebar}>
      {/* Premium Logo (Lime Green Star/Flower Accent) */}
      <div className={styles.sidebarLogoContainer} onClick={() => router.push('/dashboard')}>
        <svg viewBox="0 0 100 100" className={styles.sidebarLogo}>
          {/* Flower/Star logo from screenshot */}
          <path
            fill="var(--accent)"
            d="M50 0 L60 30 L90 20 L70 50 L90 80 L60 70 L50 100 L40 70 L10 80 L30 50 L10 20 L40 30 Z"
          />
        </svg>
      </div>

      {/* Navigation Menu */}
      <nav className={styles.sidebarNav}>
        {navItems.map((item) => {
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
