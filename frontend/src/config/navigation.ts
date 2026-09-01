import {
  BarChart3,
  CalendarSearch,
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
  { name: 'المحتوى', path: '/management/content', icon: Images, compact: true, shortcut: '7' },
  { name: 'مدير الإعلانات', path: '/management/ad-manager', icon: BarChart3, compact: true, shortcut: '6' },
  { name: 'أتمتة العمليات', path: '/management/workflows', icon: GitFork },
  { name: 'قاعدة المعرفة', path: '/management/knowledge', icon: BookOpen, compact: true },
  { name: 'إدارة الموافقات', path: '/management/approvals', icon: ShieldCheck },
  { name: 'مدير المبيعات', path: '/management/reports', icon: TrendingUp, compact: true, shortcut: '8' },
  { name: 'طلبات المواعيد', path: '/management/schedule-demand', icon: CalendarSearch, compact: true, shortcut: '9' },
  { name: 'إعدادات المشروع', path: '/settings', icon: Settings, compact: true },
];

export const navigationItemsForRole = (role?: string) => navigationItems.filter((item) => (
  item.path !== '/settings' || role === 'Owner' || role === 'Admin'
));
