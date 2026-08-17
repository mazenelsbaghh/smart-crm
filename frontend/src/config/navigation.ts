import {
  BarChart3,
  BookOpen,
  GitFork,
  Home,
  ListTodo,
  Megaphone,
  MessageCircle,
  MessageSquare,
  MessageSquareMore,
  Settings,
  ShieldCheck,
  Users,
  type LucideIcon,
} from 'lucide-react';

export interface NavigationItem {
  name: string;
  path: string;
  icon: LucideIcon;
  compact?: boolean;
  shortcut?: string;
}

export const navigationItems: NavigationItem[] = [
  { name: 'الرئيسية', path: '/dashboard', icon: Home, compact: true, shortcut: '1' },
  { name: 'واتساب', path: '/inbox', icon: MessageSquare, compact: true, shortcut: '2' },
  { name: 'ماسنجر', path: '/inbox/messenger', icon: MessageCircle, compact: true, shortcut: '3' },
  { name: 'التعليقات', path: '/inbox/comments', icon: MessageSquareMore, compact: true, shortcut: '4' },
  { name: 'العملاء CRM', path: '/crm', icon: Users, compact: true, shortcut: '5' },
  { name: 'المهام', path: '/management/follow-ups', icon: ListTodo, compact: true },
  { name: 'حملات واتساب', path: '/management/campaigns', icon: Megaphone, compact: true },
  { name: 'مدير الإعلانات', path: '/management/ad-manager', icon: BarChart3, compact: true, shortcut: '6' },
  { name: 'أتمتة العمليات', path: '/management/workflows', icon: GitFork },
  { name: 'قاعدة المعرفة', path: '/management/knowledge', icon: BookOpen, compact: true },
  { name: 'إدارة الموافقات', path: '/management/approvals', icon: ShieldCheck },
  { name: 'التقارير والإحصائيات', path: '/management/reports', icon: BarChart3, compact: true },
  { name: 'إعدادات المشروع', path: '/settings', icon: Settings, compact: true },
];

export const compactNavigationItems = navigationItems.filter((item) => item.compact);

export const shortcutNavigationItems = navigationItems.filter((item) => item.shortcut);
