'use client';

import Link from 'next/link';
import { Calendar, ChevronDown } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { useAuth } from '../../context/auth-context';
import PageHeader from '../../components/ui/PageHeader';
import EmptyState from '../../components/ui/EmptyState';
import GroupAppointmentsManager from './GroupAppointmentsManager';
import styles from './settings.module.css';

export default function GroupAppointmentsPage() {
  const { activeProject, user, loading } = useAuth();
  const router = useRouter();
  const searchParams = useSearchParams();
  const courseType = searchParams.get('course') === 'intensive' ? 'intensive' : 'regular';
  if (loading) return <p role="status">جاري تحميل المشروع والمواعيد…</p>;
  if (!activeProject) return <EmptyState title="اختر مشروعًا لعرض المواعيد" />;
  if (user?.role !== 'Owner' && user?.role !== 'Admin') return <EmptyState title="إدارة المجموعات متاحة للمالك أو المدير" />;
  return <div className={`${styles.container} ${styles.groupPage}`}>
    <GroupAppointmentsManager key={`${activeProject.id}:${courseType}`} courseType={courseType}
      heading={<PageHeader title="المواعيد والمجموعات" icon={<Calendar size={23} aria-hidden="true" />} />}
      courseNavigation={<details name="group-toolbar-menu" className={styles.groupCoursePicker} onKeyDown={event => {
        if (event.key !== 'Escape') return;
        event.currentTarget.open = false;
        event.currentTarget.querySelector('summary')?.focus();
      }}>
        <summary aria-label="اختيار نوع الكورس">{courseType === 'regular' ? 'الكورس العادي' : 'الكورس المكثف'}<ChevronDown size={16} aria-hidden="true" /></summary>
        <nav aria-label="نوع الكورس">
          <Link href="/management/groups" aria-current={courseType === 'regular' ? 'page' : undefined} data-dashboard-navigation>الكورس العادي</Link>
          <Link href="/management/groups?course=intensive" aria-current={courseType === 'intensive' ? 'page' : undefined} data-dashboard-navigation>الكورس المكثف</Link>
        </nav>
      </details>}
      timezone={activeProject.settings?.timezone || 'Africa/Cairo'} backLabel="طلبات المواعيد"
      onBack={() => router.push('/management/schedule-demand')} />
  </div>;
}
