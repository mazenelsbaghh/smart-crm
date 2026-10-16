import {
  BarChart3,
  CalendarSearch,
  CalendarDays,
  BookOpen,
  GitFork,
  Home,
  Images,
  ListTodo,
  Megaphone,
  MessageCircle,
  MessageSquare,
  MessageSquareMore,
  Settings,
  ShieldCheck,
  TrendingUp,
  Users,
  type LucideIcon,
} from 'lucide-react';

export interface NavigationItem {
  name: string;
  path: string;
  icon: LucideIcon;
  group: 'daily' | 'growth' | 'admin';
  adminOnly?: boolean;
  parent?: string;
  keywords?: readonly string[];
  shortcut?: string;
}

export const navigationItems: NavigationItem[] = [
  { name: 'نظرة عامة', path: '/dashboard', icon: Home, shortcut: '1', group: 'daily' },
  { name: 'المحادثات', path: '/inbox', icon: MessageSquare, shortcut: '2', group: 'daily', keywords: ['واتساب', 'الرسائل', 'الدردشة'] },
  { name: 'ماسنجر', path: '/inbox/messenger', icon: MessageCircle, shortcut: '3', group: 'daily', parent: '/inbox' },
  { name: 'التعليقات', path: '/inbox/comments', icon: MessageSquareMore, shortcut: '4', group: 'daily', parent: '/inbox' },
  { name: 'العملاء', path: '/crm', icon: Users, shortcut: '5', group: 'daily' },
  { name: 'المواعيد والمجموعات', path: '/management/groups', icon: CalendarDays, group: 'daily', adminOnly: true },
  { name: 'المتابعات', path: '/management/follow-ups', icon: ListTodo, group: 'daily' },
  { name: 'حملات واتساب', path: '/management/campaigns', icon: Megaphone, group: 'growth' },
  { name: 'استوديو المحتوى', path: '/management/content', icon: Images, shortcut: '7', group: 'growth' },
  { name: 'مدير الإعلانات', path: '/management/ad-manager', icon: BarChart3, shortcut: '6', group: 'growth' },
  { name: 'أتمتة العمليات', path: '/management/workflows', icon: GitFork, group: 'admin' },
  { name: 'قاعدة المعرفة', path: '/management/knowledge', icon: BookOpen, group: 'admin' },
  { name: 'الموافقات', path: '/management/approvals', icon: ShieldCheck, group: 'admin' },
  { name: 'تقارير المبيعات', path: '/management/reports', icon: TrendingUp, shortcut: '8', group: 'admin', keywords: ['مدير المبيعات', 'التحليل'] },
  { name: 'مراجعة الردود', path: '/management/reply-review', icon: ShieldCheck, group: 'admin' },
  { name: 'تحليل الأيام والتحويل', path: '/management/reports/funnel', icon: BarChart3, group: 'admin', parent: '/management/reports' },
  { name: 'طلبات المواعيد', path: '/management/schedule-demand', icon: CalendarSearch, shortcut: '9', group: 'daily' },
  { name: 'إعدادات المشروع', path: '/settings', icon: Settings, group: 'admin', adminOnly: true },
];

export const navigationGroups = [
  { key: 'daily', label: 'العمل اليومي' },
  { key: 'growth', label: 'النمو والتسويق' },
  { key: 'admin', label: 'الإدارة' },
] as const;

export const navigationItemsForRole = (role?: string) => navigationItems.filter(destination => (
  !destination.adminOnly || role === 'Owner' || role === 'Admin'
));

export function activeNavigationPath(pathname: string, destinations: NavigationItem[]) {
  return destinations.filter(destination => pathname === destination.path || pathname.startsWith(`${destination.path}/`))
    .sort((first, second) => second.path.length - first.path.length)[0]?.path;
}
