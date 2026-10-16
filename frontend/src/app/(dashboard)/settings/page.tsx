import { redirect } from 'next/navigation';
import Settings from '../../../packages/settings/Settings';

export default async function SettingsPage({ searchParams }: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const query = await searchParams;
  const tab = Array.isArray(query.tab) ? query.tab[0] : query.tab;
  const view = Array.isArray(query.view) ? query.view[0] : query.view;
  if (tab === 'addons' && view === 'manage-groups') redirect('/management/groups');
  if (tab === 'addons' && view === 'manage-intensive-groups') redirect('/management/groups?course=intensive');

  return <Settings />;
}
