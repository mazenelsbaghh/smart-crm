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
  ChevronDown,
  User,
  Menu,
  X,
} from 'lucide-react';

export default function DashboardLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const { user, activeProject, projects, switchProject, logout, loading } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const [projectDropdownOpen, setProjectDropdownOpen] = useState(false);

  // Redirect if not logged in
  useEffect(() => {
    if (!loading && !user) {
      router.push('/');
    }
  }, [user, loading, router]);

  if (loading || !user) {
    return (
      <div style={styles.loadingContainer}>
        <div style={styles.spinner}></div>
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
    <div style={styles.container}>
      {/* Sidebar - Desktop */}
      <aside className="glass-panel" style={styles.sidebar}>
        <div style={styles.logoContainer}>
          <h2 style={styles.logoText}>Smart Customer</h2>
          <span style={styles.versionBadge}>v6.0</span>
        </div>

        <nav style={styles.nav}>
          {navItems.map((item) => {
            const Icon = item.icon;
            const isActive = pathname === item.path || pathname?.startsWith(item.path + '/');
            return (
              <div
                key={item.path}
                onClick={() => router.push(item.path)}
                style={{
                  ...styles.navItem,
                  ...(isActive ? styles.navItemActive : {}),
                }}
              >
                <Icon size={18} style={isActive ? { color: 'hsl(var(--accent-secondary))' } : {}} />
                <span>{item.name}</span>
              </div>
            );
          })}
        </nav>

        <div style={styles.sidebarFooter}>
          <div onClick={logout} style={styles.logoutBtn}>
            <LogOut size={18} />
            <span>Sign Out</span>
          </div>
        </div>
      </aside>

      {/* Main Content Area */}
      <div style={styles.mainArea}>
        {/* Header */}
        <header className="glass-panel" style={styles.header}>
          {/* Mobile hamburger */}
          <div style={styles.mobileHamburger} onClick={() => setMobileMenuOpen(true)}>
            <Menu size={24} />
          </div>

          {/* Project selector */}
          <div style={styles.projectSelectorContainer}>
            <div 
              style={styles.projectDropdownTrigger}
              onClick={() => setProjectDropdownOpen(!projectDropdownOpen)}
            >
              <span style={styles.projectLabel}>Project:</span>
              <span style={styles.projectName}>{activeProject?.name || 'No Project Selected'}</span>
              <ChevronDown size={16} />
            </div>
            
            {projectDropdownOpen && (
              <div className="glass-panel" style={styles.projectDropdown}>
                {projects.map((proj) => (
                  <div
                    key={proj.id}
                    onClick={() => {
                      switchProject(proj.id);
                      setProjectDropdownOpen(false);
                    }}
                    style={{
                      ...styles.dropdownItem,
                      ...(activeProject?.id === proj.id ? styles.dropdownItemActive : {}),
                    }}
                  >
                    {proj.name}
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* User profile section */}
          <div style={styles.profileSection}>
            <div style={styles.profileInfo}>
              <span style={styles.userName}>{user.fullName}</span>
              <span style={styles.userRole}>{user.role}</span>
            </div>
            <div style={styles.avatar}>
              <User size={20} />
            </div>
          </div>
        </header>

        {/* Content body */}
        <main style={styles.content}>
          {children}
        </main>
      </div>

      {/* Mobile Drawer Navigation Menu */}
      {mobileMenuOpen && (
        <div style={styles.mobileOverlay}>
          <aside className="glass-panel" style={styles.mobileDrawer}>
            <div style={styles.drawerHeader}>
              <h2 style={styles.logoText}>Smart Customer</h2>
              <div onClick={() => setMobileMenuOpen(false)} style={styles.closeBtn}>
                <X size={24} />
              </div>
            </div>

            <nav style={styles.nav}>
              {navItems.map((item) => {
                const Icon = item.icon;
                const isActive = pathname === item.path;
                return (
                  <div
                    key={item.path}
                    onClick={() => {
                      router.push(item.path);
                      setMobileMenuOpen(false);
                    }}
                    style={{
                      ...styles.navItem,
                      ...(isActive ? styles.navItemActive : {}),
                    }}
                  >
                    <Icon size={18} />
                    <span>{item.name}</span>
                  </div>
                );
              })}
            </nav>

            <div style={styles.drawerFooter}>
              <div onClick={logout} style={styles.logoutBtn}>
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

const styles: Record<string, React.CSSProperties> = {
  loadingContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '100vh',
    width: '100vw',
    backgroundColor: 'hsl(var(--bg-primary))',
  },
  spinner: {
    width: '40px',
    height: '40px',
    border: '3px solid rgba(99, 102, 241, 0.1)',
    borderTopColor: 'hsl(var(--accent-primary))',
    borderRadius: '50%',
    animation: 'spin 1s linear infinite',
  },
  container: {
    display: 'flex',
    minHeight: '100vh',
    width: '100vw',
    backgroundColor: 'hsl(var(--bg-primary))',
    overflow: 'hidden',
  },
  sidebar: {
    display: 'flex',
    flexDirection: 'column',
    width: '260px',
    height: '100vh',
    borderRight: '1px solid rgba(255, 255, 255, 0.05)',
    padding: 'var(--space-md)',
    flexShrink: 0,
    zIndex: 20,
    '@media (max-width: 768px)': {
      display: 'none',
    },
  } as React.CSSProperties,
  logoContainer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 'var(--space-xl)',
    paddingBottom: 'var(--space-sm)',
    borderBottom: '1px solid rgba(255, 255, 255, 0.05)',
  },
  logoText: {
    fontSize: '1.2rem',
    fontWeight: 800,
    background: 'linear-gradient(135deg, hsl(var(--text-primary)) 30%, hsl(var(--accent-primary)) 100%)',
    WebkitBackgroundClip: 'text',
    WebkitTextFillColor: 'transparent',
  },
  versionBadge: {
    fontSize: '0.7rem',
    backgroundColor: 'rgba(99, 102, 241, 0.15)',
    color: 'hsl(var(--accent-primary))',
    padding: '2px 6px',
    borderRadius: 'var(--radius-sm)',
    fontWeight: 600,
  },
  nav: {
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-xs)',
    flexGrow: 1,
  },
  navItem: {
    display: 'flex',
    alignItems: 'center',
    gap: 'var(--space-md)',
    padding: 'var(--space-sm) var(--space-md)',
    borderRadius: 'var(--radius-md)',
    color: 'hsl(var(--text-secondary))',
    cursor: 'pointer',
    fontSize: '0.9rem',
    fontWeight: 500,
    transition: 'var(--transition-normal)',
  },
  navItemActive: {
    backgroundColor: 'rgba(99, 102, 241, 0.12)',
    color: 'hsl(var(--text-primary))',
    borderLeft: '3px solid hsl(var(--accent-primary))',
    fontWeight: 600,
  },
  sidebarFooter: {
    marginTop: 'auto',
    paddingTop: 'var(--space-md)',
    borderTop: '1px solid rgba(255, 255, 255, 0.05)',
  },
  logoutBtn: {
    display: 'flex',
    alignItems: 'center',
    gap: 'var(--space-md)',
    padding: 'var(--space-sm) var(--space-md)',
    borderRadius: 'var(--radius-md)',
    color: 'hsl(var(--accent-danger))',
    cursor: 'pointer',
    fontSize: '0.9rem',
    fontWeight: 600,
    transition: 'var(--transition-normal)',
  },
  mainArea: {
    display: 'flex',
    flexDirection: 'column',
    flexGrow: 1,
    height: '100vh',
    overflow: 'hidden',
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    height: '70px',
    padding: '0 var(--space-lg)',
    borderBottom: '1px solid rgba(255, 255, 255, 0.05)',
    zIndex: 10,
  },
  mobileHamburger: {
    display: 'none',
    cursor: 'pointer',
    color: 'hsl(var(--text-primary))',
    '@media (max-width: 768px)': {
      display: 'block',
    },
  } as React.CSSProperties,
  projectSelectorContainer: {
    position: 'relative',
  },
  projectDropdownTrigger: {
    display: 'flex',
    alignItems: 'center',
    gap: 'var(--space-xs)',
    backgroundColor: 'rgba(255, 255, 255, 0.03)',
    border: '1px solid rgba(255, 255, 255, 0.08)',
    padding: 'var(--space-sm) var(--space-md)',
    borderRadius: 'var(--radius-md)',
    cursor: 'pointer',
    fontSize: '0.875rem',
    transition: 'var(--transition-normal)',
  },
  projectLabel: {
    color: 'hsl(var(--text-muted))',
    fontWeight: 500,
  },
  projectName: {
    color: 'hsl(var(--text-primary))',
    fontWeight: 600,
  },
  projectDropdown: {
    position: 'absolute',
    top: 'calc(100% + 8px)',
    left: 0,
    width: '220px',
    borderRadius: 'var(--radius-md)',
    padding: 'var(--space-xs)',
    display: 'flex',
    flexDirection: 'column',
    gap: '2px',
    zIndex: 30,
    boxShadow: '0 10px 25px -5px rgba(0, 0, 0, 0.5)',
  },
  dropdownItem: {
    padding: 'var(--space-sm) var(--space-md)',
    borderRadius: 'var(--radius-sm)',
    color: 'hsl(var(--text-secondary))',
    cursor: 'pointer',
    fontSize: '0.875rem',
    transition: 'var(--transition-fast)',
  },
  dropdownItemActive: {
    backgroundColor: 'rgba(99, 102, 241, 0.15)',
    color: 'hsl(var(--accent-primary))',
    fontWeight: 600,
  },
  profileSection: {
    display: 'flex',
    alignItems: 'center',
    gap: 'var(--space-md)',
  },
  profileInfo: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-end',
    '@media (max-width: 576px)': {
      display: 'none',
    },
  } as React.CSSProperties,
  userName: {
    fontSize: '0.9rem',
    fontWeight: 600,
    color: 'hsl(var(--text-primary))',
  },
  userRole: {
    fontSize: '0.75rem',
    color: 'hsl(var(--text-muted))',
    fontWeight: 500,
  },
  avatar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '36px',
    height: '36px',
    borderRadius: 'var(--radius-full)',
    backgroundColor: 'rgba(99, 102, 241, 0.15)',
    color: 'hsl(var(--accent-primary))',
    border: '1px solid rgba(99, 102, 241, 0.3)',
  },
  content: {
    flexGrow: 1,
    padding: 'var(--space-lg)',
    overflowY: 'auto',
    position: 'relative',
  },
  mobileOverlay: {
    position: 'fixed',
    top: 0,
    left: 0,
    width: '100vw',
    height: '100vh',
    backgroundColor: 'rgba(0,0,0,0.5)',
    backdropFilter: 'blur(4px)',
    zIndex: 50,
  },
  mobileDrawer: {
    display: 'flex',
    flexDirection: 'column',
    width: '280px',
    height: '100vh',
    padding: 'var(--space-md)',
  },
  drawerHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 'var(--space-xl)',
  },
  closeBtn: {
    cursor: 'pointer',
    color: 'hsl(var(--text-primary))',
  },
  drawerFooter: {
    marginTop: 'auto',
    paddingTop: 'var(--space-md)',
    borderTop: '1px solid rgba(255, 255, 255, 0.05)',
  },
};
