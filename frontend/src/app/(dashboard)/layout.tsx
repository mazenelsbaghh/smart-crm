'use client';

import React, { useEffect, useState } from 'react';
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
  X,
} from 'lucide-react';
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

  // Redirect if not logged in
  useEffect(() => {
    if (!loading && !user) {
      router.push('/');
    }
  }, [user, loading, router]);

  // Global Navigation Keyboard Shortcuts (Option + 1..5)
  useEffect(() => {
    const handleGlobalKeyDown = (e: KeyboardEvent) => {
      if (e.altKey && !e.ctrlKey && !e.metaKey) {
        if (e.key === '1') {
          e.preventDefault();
          router.push('/dashboard');
        } else if (e.key === '2') {
          e.preventDefault();
          router.push('/inbox');
        } else if (e.key === '3') {
          e.preventDefault();
          router.push('/inbox/messenger');
        } else if (e.key === '4') {
          e.preventDefault();
          router.push('/inbox/comments');
        } else if (e.key === '5') {
          e.preventDefault();
          router.push('/crm');
        }
      }
    };

    window.addEventListener('keydown', handleGlobalKeyDown);
    return () => window.removeEventListener('keydown', handleGlobalKeyDown);
  }, [router]);

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

  const navItems = [
    { name: 'لوحة التحكم', path: '/dashboard', icon: LayoutDashboard },
    { name: 'صندوق المحادثات', path: '/inbox', icon: Inbox },
    { name: 'العملاء CRM', path: '/crm', icon: Users },
    { name: 'مسار الصفقات', path: '/crm/pipeline', icon: GitBranch },
    { name: 'جدول المتابعات', path: '/management/follow-ups', icon: Calendar },
    { name: 'الحملات التسويقية', path: '/management/campaigns', icon: Megaphone },
    { name: 'أتمتة العمليات', path: '/management/workflows', icon: GitFork },
    { name: 'قاعدة المعرفة', path: '/management/knowledge', icon: BookOpen },
    { name: 'إدارة الموافقات', path: '/management/approvals', icon: ShieldCheck },
    { name: 'التقارير والإحصائيات', path: '/management/reports', icon: BarChart3 },
    { name: 'إعدادات المشروع', path: '/settings', icon: Settings },
  ];



  return (
    <div className={styles.container}>
      {/* Sidebar - Desktop */}
      <ThinSidebar />

      {/* Main Content Area */}
      <div className={styles.mainArea}>
        {/* Header */}
        <Header onMenuClick={() => setMobileMenuOpen(true)} />

        {/* Content body */}
        <main className={styles.content}>
          {children}
        </main>
      </div>

      {/* Mobile Drawer Navigation Menu */}
      {mobileMenuOpen && (
        <div className={styles.mobileOverlay} onClick={() => setMobileMenuOpen(false)}>
          <aside className={`glass-panel ${styles.mobileDrawer}`} onClick={(e) => e.stopPropagation()}>
            <div className={styles.drawerHeader}>
              <h2 className={styles.logoText}>سمارت سيلز</h2>
              <button 
                type="button"
                onClick={() => setMobileMenuOpen(false)} 
                className={styles.closeBtn}
                aria-label="إغلاق القائمة"
              >
                <X size={24} />
              </button>
            </div>

            <nav className={styles.nav}>
              {navItems.map((item) => {
                const Icon = item.icon;
                const isActive = pathname === item.path || pathname?.startsWith(item.path + '/');
                return (
                  <button
                    key={item.path}
                    type="button"
                    onClick={() => {
                      router.push(item.path);
                      setMobileMenuOpen(false);
                    }}
                    className={`${styles.navItem} ${isActive ? styles.navItemActive : ''}`}
                    style={{ background: 'none', border: 'none', width: '100%', textAlign: 'right', display: 'flex', alignItems: 'center' }}
                  >
                    <Icon size={18} style={isActive ? { color: 'hsl(var(--accent-primary))' } : {}} />
                    <span>{item.name}</span>
                  </button>
                );
              })}
            </nav>

            <div className={styles.drawerFooter}>
              <button 
                type="button"
                onClick={logout} 
                className={styles.logoutBtn}
                style={{ background: 'none', border: 'none', width: '100%', display: 'flex', alignItems: 'center' }}
              >
                <LogOut size={18} />
                <span>تسجيل الخروج</span>
              </button>
            </div>
          </aside>
        </div>
      )}
    </div>
  );
}
