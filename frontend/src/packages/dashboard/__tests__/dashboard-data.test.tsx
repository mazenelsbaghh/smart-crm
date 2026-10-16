import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { AuthProvider, useAuth } from '../../../context/auth-context';
import { api } from '../../../services/api';
import Dashboard from '../Dashboard';

const firstProject = { id: 'first', name: 'المشروع الأول' };
const secondProject = { id: 'second', name: 'المشروع الثاني' };
const lead = { id: 'lead', name: 'عميل المشروع الأول', phoneNumber: '201000000000', leadScore: 80, pipelineStage: 'Qualified' };
const summary = { customers: 12, chats: 18, totalDeals: 4, wonDeals: 1, stages: ['Qualified'], topLeads: [lead] };
let authorizedProject = firstProject;

function Workspace() {
  const { refreshProjects } = useAuth();
  return <><button onClick={() => void refreshProjects()}>تحديث المشروع</button><Dashboard /></>;
}

beforeEach(() => {
  authorizedProject = firstProject;
  localStorage.clear();
  localStorage.setItem('user', JSON.stringify({ id: 'owner', email: 'owner@example.test', role: 'Owner' }));
  vi.spyOn(api, 'get').mockImplementation(async url => {
    if (url === '/api/projects') return { data: [authorizedProject] };
    if (url === `/api/projects/${authorizedProject.id}`) return { data: authorizedProject };
    if (url.endsWith('/dashboard/summary')) return { data: summary };
    throw new Error(`Unexpected GET: ${url}`);
  });
});

it('refreshes current totals from their source without recalculating unrelated historical snapshots', async () => {
  const post = vi.spyOn(api, 'post').mockRejectedValue(new Error('Unexpected mutation'));
  render(<AuthProvider><Dashboard /></AuthProvider>);
  await screen.findByText(lead.name);
  const initialGet = vi.mocked(api.get).getMockImplementation()!;
  vi.mocked(api.get).mockImplementation((url, config) => url.endsWith('/dashboard/summary')
    ? Promise.resolve({ data: { ...summary, chats: 29 } }) : initialGet(url, config));
  fireEvent.click(screen.getByRole('button', { name: 'تحديث المؤشرات' }));
  expect(await screen.findByText((29).toLocaleString('ar-EG'))).toBeInTheDocument();
  expect(post).not.toHaveBeenCalled();
});

it('does not label previous-stage leads as the newly selected stage when its request fails', async () => {
  render(<AuthProvider><Dashboard /></AuthProvider>);
  await screen.findByText(lead.name);
  vi.mocked(api.get).mockRejectedValue(new Error('offline'));
  fireEvent.change(screen.getByRole('combobox', { name: 'المرحلة' }), { target: { value: 'Qualified' } });
  await screen.findByRole('alert');
  expect(screen.queryByText(lead.name)).not.toBeInTheDocument();
  expect(screen.getByText('بيانات هذه المرحلة غير متاحة؛ أعد المحاولة.')).toBeInTheDocument();
  expect(screen.getByText((18).toLocaleString('ar-EG'))).toBeInTheDocument();
});

it('drops the previous project filter and ignores its delayed response after the project changes', async () => {
  render(<AuthProvider><Workspace /></AuthProvider>);
  await screen.findByText(lead.name);
  const initialGet = vi.mocked(api.get).getMockImplementation()!;
  let finishOldRequest!: (response: { data: typeof summary }) => void;
  vi.mocked(api.get).mockImplementation((url, config) => {
    if (url === '/api/projects/first/dashboard/summary') return new Promise(resolve => { finishOldRequest = resolve; });
    if (url === '/api/projects/second/dashboard/summary') return Promise.resolve({ data: {
      ...summary, customers: 3, chats: 7, stages: ['New'], topLeads: [{ ...lead, name: 'عميل المشروع الثاني', pipelineStage: 'New' }],
    } });
    return initialGet(url, config);
  });
  fireEvent.change(screen.getByRole('combobox', { name: 'المرحلة' }), { target: { value: 'Qualified' } });
  await waitFor(() => expect(finishOldRequest).toBeDefined());
  authorizedProject = secondProject;
  fireEvent.click(screen.getByRole('button', { name: 'تحديث المشروع' }));
  await screen.findByText('عميل المشروع الثاني');
  await act(async () => finishOldRequest({ data: summary }));
  expect(screen.getByRole('combobox', { name: 'المرحلة' })).toHaveValue('الكل');
  expect(screen.queryByText(lead.name)).not.toBeInTheDocument();
  expect(screen.getByText((7).toLocaleString('ar-EG'))).toBeInTheDocument();
});

it('keeps missing counters unavailable instead of inventing zeros', async () => {
  const initialGet = vi.mocked(api.get).getMockImplementation()!;
  vi.mocked(api.get).mockImplementation((url, config) => url.endsWith('/dashboard/summary')
    ? Promise.resolve({ data: { ...summary, chats: undefined, customers: undefined, totalDeals: 0, wonDeals: 0 } })
    : initialGet(url, config));
  render(<AuthProvider><Dashboard /></AuthProvider>);
  await screen.findByText(lead.name);
  expect(screen.getAllByText('غير متاح')).toHaveLength(3);
  expect(screen.queryByText('٠')).not.toBeInTheDocument();
});
