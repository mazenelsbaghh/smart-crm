'use client';

import React from 'react';
import { useAuth } from '../../context/auth-context';
import { useRouter, usePathname } from 'next/navigation';
import {
  LayoutDashboard,
  Inbox,
  Users,
  GitBranch,
  Calendar,
  Megaphone,
  BarChart3,
  GitFork,
  BookOpen,
  ShieldCheck,
  Settings,
  LogOut,
  User,
  MessageCircle,
  MessageSquareMore,
} from 'lucide-react';
import styles from './layout.module.css';

export default function Sidebar() {
  const { user, logout } = useAuth();
  const router = useRouter();
  const pathname = usePathname();

  const menuGroups = [
    {
      title: 'لوحات وقوائم',
      items: [
        { name: 'لوحة التحكم', path: '/dashboard', icon: LayoutDashboard, shortcut: '⌥1' },
        { name: 'صندوق المحادثات', path: '/inbox', icon: Inbox, shortcut: '⌥2' },
        { name: 'صندوق الماسنجر', path: '/inbox/messenger', icon: MessageCircle, shortcut: '⌥3' },
        { name: 'صندوق التعليقات', path: '/inbox/comments', icon: MessageSquareMore, shortcut: '⌥4' },
        { name: 'العملاء CRM', path: '/crm', icon: Users, shortcut: '⌥5' },
        { name: 'مسار الصفقات', path: '/crm/pipeline', icon: GitBranch },
      ]
    },
    {
      title: 'أدوات الإدارة والتشغيل',
      items: [
        { name: 'جدول المتابعات', path: '/management/follow-ups', icon: Calendar },
        { name: 'الحملات التسويقية', path: '/management/campaigns', icon: Megaphone },
        { name: 'أتمتة العمليات', path: '/management/workflows', icon: GitFork },
        { name: 'قاعدة المعرفة', path: '/management/knowledge', icon: BookOpen },
        { name: 'إدارة الموافقات', path: '/management/approvals', icon: ShieldCheck },
        { name: 'التقارير والإحصائيات', path: '/management/reports', icon: BarChart3 },
      ]
    },
    {
      title: 'النظام والإعدادات',
      items: [
        { name: 'إعدادات المشروع', path: '/settings', icon: Settings },
      ]
    }
  ];

  return (
    <aside className={styles.sidebar}>
      <div className={styles.logoContainer}>
        <h2 className={styles.logoText}>سمارت كاستمر</h2>
        <span className={styles.versionBadge}>v6.0</span>
      </div>

      <div className={styles.profileCard}>
        <div className={styles.avatar}>
          <User size={30} />
        </div>
        <h4 className={styles.userName}>{user?.fullName || 'مستخدم النظام'}</h4>
        <div className={styles.statusWrapper}>
          <span className={styles.statusDot}></span>
          <span className={styles.statusText}>نشط</span>
        </div>
      </div>

      <nav className={styles.nav}>
        {menuGroups.map((group) => (
          <div key={group.title} className={styles.navGroup}>
            <span className={styles.navGroupTitle}>{group.title}</span>
            <div className={styles.navGroupItems}>
              {group.items.map((item) => {
                const Icon = item.icon;
                const isActive = pathname === item.path || pathname?.startsWith(item.path + '/');
                return (
                  <button
                    key={item.path}
                    type="button"
                    onClick={() => router.push(item.path)}
                    className={`${styles.navItem} ${isActive ? styles.navItemActive : ''}`}
                    style={{ background: 'none', border: 'none', width: '100%', textAlign: 'right', display: 'flex', alignItems: 'center' }}
                  >
                    <Icon size={16} style={isActive ? { color: 'hsl(var(--accent-primary))' } : {}} />
                    <span>{item.name}</span>
                    {item.shortcut && (
                      <kbd style={{ marginRight: 'auto', fontSize: '0.65rem' }}>{item.shortcut}</kbd>
                    )}
                  </button>
                );
              })}
            </div>
          </div>
        ))}
      </nav>

      <div className={styles.sidebarFooter}>
        <button 
          type="button"
          onClick={logout} 
          className={styles.logoutBtn}
          style={{ background: 'none', border: 'none', width: '100%', display: 'flex', alignItems: 'center' }}
        >
          <LogOut size={16} />
          <span>تسجيل الخروج</span>
        </button>
        <p className={styles.copyrightText}>سمارت كاستمر &copy; ٢٠٢٦</p>
      </div>
    </aside>
  );
}
