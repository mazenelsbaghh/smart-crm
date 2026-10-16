import { fireEvent, render, screen, within } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../../context/auth-context';
import { ToastProvider } from '../../../context/toast-context';
import { api } from '../../../services/api';
import Campaigns from '../Campaigns';
import KnowledgeBase from '../KnowledgeBase';
import Workflows from '../Workflows';

vi.mock('next/navigation', () => ({ useSearchParams: () => new URLSearchParams(window.location.search) }));

const project = { id: 'list-count-project', name: 'المشروع' };
beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('user', JSON.stringify({ id: 'owner', email: 'owner@example.test', role: 'Owner' }));
});

it('keeps accepted sends visible while explaining unavailable campaign receipt statistics', async () => {
  window.history.replaceState({}, '', '/management/campaigns');
  vi.spyOn(api, 'get').mockImplementation((url) => {
    if (url === '/api/projects') return Promise.resolve({ data: [project] });
    if (url === `/api/projects/${project.id}`) return Promise.resolve({ data: project });
    if (url.endsWith('/segments')) return Promise.resolve({ data: [] });
    if (url === `/api/projects/${project.id}/campaigns`) return Promise.resolve({ data: [{
      id: 'campaign', name: 'رسائل مقبولة للإرسال', segmentId: 'segment', messageTemplateA: 'مرحبًا',
      status: 4, sentCount: 27, deliveredCount: null, readCount: null, responseCount: null,
    }] });
    return Promise.reject(new Error(`Unexpected GET: ${url}`));
  });
  render(<AuthProvider><ToastProvider><Campaigns /></ToastProvider></AuthProvider>);

  const row = await screen.findByRole('row', { name: /رسائل مقبولة للإرسال/ });
  expect(within(row).getByText('٢٧')).toBeVisible();
  expect(within(row).getAllByText('غير متاح')).toHaveLength(3);
  expect(within(row).queryByText('٠')).not.toBeInTheDocument();
  expect(within(row).getByText(/مرسل يعني قبول مزود الرسائل للإرسال/)).toBeVisible();
});

it.each([
  [3, 'متوقفة مؤقتاً'],
  [5, 'ملغاة'],
  [6, 'قيد التحضير'],
] as const)('shows and filters the source campaign lifecycle status %s', async (status, label) => {
  window.history.replaceState({}, '', '/management/campaigns');
  const campaign = { segmentId: 'segment', messageTemplateA: 'مرحبًا',
    sentCount: 0, deliveredCount: null, readCount: null, responseCount: null };
  vi.spyOn(api, 'get').mockImplementation((url) => {
    if (url === '/api/projects') return Promise.resolve({ data: [project] });
    if (url === `/api/projects/${project.id}`) return Promise.resolve({ data: project });
    if (url.endsWith('/segments')) return Promise.resolve({ data: [] });
    if (url === `/api/projects/${project.id}/campaigns`) return Promise.resolve({ data: [
      { ...campaign, id: 'target', name: 'الحملة المطلوبة', status },
      { ...campaign, id: 'completed', name: 'حملة مكتملة أخرى', status: 4 },
    ] });
    return Promise.reject(new Error(`Unexpected GET: ${url}`));
  });
  render(<AuthProvider><ToastProvider><Campaigns /></ToastProvider></AuthProvider>);
  const row = await screen.findByRole('row', { name: /الحملة المطلوبة/ });
  expect(within(row).getByText(label)).toBeVisible();
  const filters = screen.getByRole('group', { name: 'حالة الحملات' });
  const filter = within(filters).getByRole('button', { name: new RegExp(label) });
  expect(filter).toHaveTextContent('١');
  fireEvent.click(filter);
  expect(screen.getByText('الحملة المطلوبة')).toBeVisible();
  expect(screen.queryByText('حملة مكتملة أخرى')).not.toBeInTheDocument();
});

it.each([
  { name: 'campaigns', Component: Campaigns, filter: 'حالة الحملات', all: 'الكل', empty: 'لا توجد حملات' },
  { name: 'knowledge', Component: KnowledgeBase, filter: 'حالة مصادر المعرفة', all: 'كل المصادر', empty: 'لا توجد مستندات' },
  { name: 'workflows', Component: Workflows, filter: 'حالة قواعد الأتمتة', all: 'كل القواعد', empty: 'لا توجد قواعد أتمتة' },
])('distinguishes unavailable $name counts from a confirmed empty list', async ({ name, Component, filter, all, empty }) => {
  window.history.replaceState({}, '', `/management/${name}`);
  let unavailable = true;
  vi.spyOn(api, 'get').mockImplementation((url) => {
    if (url === '/api/projects') return Promise.resolve({ data: [project] });
    if (url === `/api/projects/${project.id}`) return Promise.resolve({ data: project });
    if (url.endsWith('/segments')) return Promise.resolve({ data: [] });
    if (url === `/api/projects/${project.id}/${name}`) return unavailable
      ? Promise.reject(new Error('Unavailable')) : Promise.resolve({ data: [] });
    return Promise.reject(new Error(`Unexpected GET: ${url}`));
  });
  render(<AuthProvider><ToastProvider><Component /></ToastProvider></AuthProvider>);
  const retry = await screen.findByRole('button', { name: 'إعادة المحاولة' });
  const filters = screen.getByRole('group', { name: filter });
  expect(within(filters).queryByText('٠')).not.toBeInTheDocument();

  unavailable = false;
  fireEvent.click(retry);
  await screen.findByRole('heading', { name: empty });
  expect(within(filters).getByRole('button', { name: new RegExp(`^${all}`) })).toHaveTextContent('٠');
});
