'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { ChevronDown, LogOut, MessagesSquare } from 'lucide-react';
import { useAuth } from '../../context/auth-context';
import { activeNavigationPath, navigationGroups, navigationItemsForRole, type NavigationItem } from '../../config/navigation';
import styles from './app-shell.module.css';

export function Brand() {
  return <Link href="/dashboard" className={styles.brand} data-dashboard-navigation>
    <span className={styles.brandMark}><MessagesSquare size={23} aria-hidden="true" /></span>
    <span>سمارت كاستمر<small>علاقات أوضح، متابعة أسهل</small></span>
  </Link>;
}

function NavigationLink({ destination, activePath, onNavigate }: {
  destination: NavigationItem; activePath?: string; onNavigate?: () => void;
}) {
  const Icon = destination.icon;
  return <Link href={destination.path} onClick={onNavigate} className={styles.navigationLink}
    aria-keyshortcuts={destination.shortcut ? `Alt+${destination.shortcut}` : undefined}
    aria-current={activePath === destination.path ? 'page' : undefined} data-dashboard-navigation>
    <span className={styles.navigationIcon}><Icon size={19} strokeWidth={1.8} aria-hidden="true" /></span>
    <span>{destination.name}</span>
    {destination.shortcut && <kbd aria-hidden="true">{destination.shortcut}</kbd>}
  </Link>;
}

export default function AppNavigation({ onNavigate }: { onNavigate?: () => void }) {
  const { user, logout } = useAuth();
  const pathname = usePathname();
  const destinations = navigationItemsForRole(user?.role).filter(destination => !destination.parent);
  const activePath = activeNavigationPath(pathname, destinations);
  const activeGroup = destinations.find(destination => destination.path === activePath)?.group ?? 'daily';
  const overview = destinations.find(destination => destination.path === '/dashboard')!;
  const roleLabel = user?.role === 'Owner' ? 'مالك مساحة العمل' : user?.role === 'Admin' ? 'مدير مساحة العمل' : 'عضو الفريق';
  return <>
    <nav className={styles.navigation} aria-label="التنقل الرئيسي">
      <p className={styles.workspaceCaption}>مساحة العمل</p>
      <NavigationLink destination={overview} activePath={activePath} onNavigate={onNavigate} />
      {navigationGroups.map(group => {
        const links = destinations.filter(destination => destination.group === group.key && destination.path !== overview.path);
        if (!links.length) return null;
        return <details key={`${pathname}:${group.key}`} className={styles.navigationGroup} open={activeGroup === group.key}>
          <summary><span>{group.label}</span><span className={styles.groupCount} aria-hidden="true">{links.length}</span><ChevronDown size={15} aria-hidden="true" /></summary>
          <div className={styles.groupLinks}>{links.map(destination => <NavigationLink key={destination.path}
            destination={destination} activePath={activePath} onNavigate={onNavigate} />)}</div>
        </details>;
      })}
    </nav>
    <div className={styles.navigationFooter}>
      <span className={styles.userAvatar} aria-hidden="true">{(user?.fullName || 'ح')[0]}</span>
      <span className={styles.userIdentity}>{user?.fullName || 'حسابي'}<small>{roleLabel}</small></span>
      <button type="button" onClick={() => void logout()} aria-label="تسجيل الخروج"><LogOut size={18} aria-hidden="true" /></button>
    </div>
  </>;
}
