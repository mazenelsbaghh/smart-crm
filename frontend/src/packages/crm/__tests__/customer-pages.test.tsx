import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../../context/auth-context';
import { ToastProvider } from '../../../context/toast-context';
import { api } from '../../../services/api';
import CustomerList from '../CustomerList';
import Dashboard from '../../dashboard/Dashboard';

const project = { id: 'performance-project', name: 'Project' };
const customer = { id: 'customer', projectId: project.id, name: 'First page', phoneNumber: '201012345678',
  city: 'Cairo', leadScore: 90, tags: [], budget: null, pipelineStage: 'New' };

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('user', JSON.stringify({ id: 'owner', email: 'owner@example.test', role: 'Owner' }));
  vi.spyOn(api, 'get').mockImplementation((url, config) => {
    if (url === '/api/projects') return Promise.resolve({ data: [project] });
    if (url === `/api/projects/${project.id}`) return Promise.resolve({ data: project });
    if (url.endsWith('/customers/page')) {
      const { page, pageSize, search } = config!.params as { page: number; pageSize: number; search: string };
      return Promise.resolve({ data: {
        items: [{ ...customer, name: search ? 'Search result' : page === 2 ? 'Second page' : 'First page' }],
        page, pageSize, totalFiltered: search ? 1 : 40000, totalCustomers: 40000,
        labels: [{ name: 'بدون تصنيف', count: 40000 }], stages: [{ name: 'New', count: 40000 }], cities: ['Cairo'],
      } });
    }
    if (url.endsWith('/dashboard/summary')) return Promise.resolve({ data: {
      customers: 40000, chats: 46000, totalDeals: 1000, wonDeals: 250, topLeads: [customer], stages: ['New'],
    } });
    return Promise.reject(new Error(`Unexpected GET: ${url}`));
  });
});

it('loads subsequent customer pages and searches while keeping project-wide counts and search focus', async () => {
  render(<AuthProvider><ToastProvider><CustomerList /></ToastProvider></AuthProvider>);
  await screen.findByText('First page');

  fireEvent.click(screen.getByRole('button', { name: 'الصفحة التالية' }));
  await screen.findByText('Second page');
  expect(screen.queryByText('First page')).not.toBeInTheDocument();
  const search = screen.getByRole('searchbox', { name: 'بحث في العملاء بالاسم أو رقم الهاتف' });
  search.focus();
  fireEvent.change(search, { target: { value: 'Customer 12' } });

  await screen.findByText('Search result');
  expect(search).toHaveFocus();
  expect(screen.getByRole('button', { name: 'الصفحة التالية' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'الصفحة 1' })).toHaveAttribute('aria-current', 'page');
  expect(screen.getAllByText(/٤٠٬٠٠٠/).length).toBeGreaterThan(0);
});

it('shows dashboard totals from the complete project without downloading the customer or conversation lists', async () => {
  render(<AuthProvider><Dashboard /></AuthProvider>);
  await screen.findByText('First page');

  expect(screen.getByText((46000).toLocaleString('ar-EG'))).toBeInTheDocument();
  expect(screen.getByText((40000).toLocaleString('ar-EG'))).toBeInTheDocument();
  expect(screen.getByText(`${(25).toLocaleString('ar-EG')}٪`)).toBeInTheDocument();
  await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
});
