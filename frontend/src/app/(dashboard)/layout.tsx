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
import Sidebar from '../../components/layout/Sidebar';
import Header from '../../components/layout/Header';
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

  if (loading || !user) {
    return (
      <div className={styles.loadingContainer}>
        <div className={styles.spinner}></div>
        <p style={{ marginTop: 'var(--space-md)', color: 'hsl(var(--text-secondary))' }}>
          Verifying security credentials...
        </p>
      </div>
    );
  }

  const navItems = [
    { name: 'Dashboard', path: '/dashboard', icon: LayoutDashboard },
    { name: 'Realtime Inbox', path: '/inbox', icon: Inbox },
    { name: 'Customers CRM', path: '/crm', icon: Users },
    { name: 'Pipeline', path: '/crm/pipeline', icon: GitBranch },
    { name: 'Follow-ups', path: '/management/follow-ups', icon: Calendar },
    { name: 'Campaigns', path: '/management/campaigns', icon: Megaphone },
    { name: 'Workflows', path: '/management/workflows', icon: GitFork },
    { name: 'Knowledge Base', path: '/management/knowledge', icon: BookOpen },
    { name: 'Approvals', path: '/management/approvals', icon: ShieldCheck },
    { name: 'Reports', path: '/management/reports', icon: BarChart3 },
    { name: 'Settings', path: '/settings', icon: Settings },
  ];

  return (
    <div className={styles.container}>
      {/* Sidebar - Desktop */}
      <Sidebar />

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
        <div className={styles.mobileOverlay}>
          <aside className={`glass-panel ${styles.mobileDrawer}`}>
            <div className={styles.drawerHeader}>
              <h2 className={styles.logoText}>Smart Customer</h2>
              <div onClick={() => setMobileMenuOpen(false)} className={styles.closeBtn}>
                <X size={24} />
              </div>
            </div>

            <nav className={styles.nav}>
              {navItems.map((item) => {
                const Icon = item.icon;
                const isActive = pathname === item.path || pathname?.startsWith(item.path + '/');
                return (
                  <div
                    key={item.path}
                    onClick={() => {
                      router.push(item.path);
                      setMobileMenuOpen(false);
                    }}
                    className={`${styles.navItem} ${isActive ? styles.navItemActive : ''}`}
                  >
                    <Icon size={18} style={isActive ? { color: 'hsl(var(--accent-secondary))' } : {}} />
                    <span>{item.name}</span>
                  </div>
                );
              })}
            </nav>

            <div className={styles.drawerFooter}>
              <div onClick={logout} className={styles.logoutBtn}>
                <LogOut size={18} />
                <span>Sign Out</span>
              </div>
            </div>
          </aside>
        </div>
      )}
    </div>
  );
}
