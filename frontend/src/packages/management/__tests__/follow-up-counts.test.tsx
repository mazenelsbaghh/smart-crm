import { fireEvent, render, screen, within } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../../context/auth-context';
import { api } from '../../../services/api';
import FollowUps from '../FollowUps';

vi.mock('next/navigation', () => ({ useSearchParams: () => new URLSearchParams(window.location.search) }));

const project = { id: 'follow-up-project', name: 'المشروع' };
const counts = { totalCount: 28, pendingCount: 1, completedCount: 2, bypassedCount: 3,
  cancelledCount: 4, processingCount: 5, deliveryUnknownCount: 6, missedCount: 7 };

beforeEach(() => {
  window.history.replaceState({}, '', '/management/follow-ups');
  localStorage.clear();
  localStorage.setItem('user', JSON.stringify({ id: 'owner', email: 'owner@example.test', role: 'Owner' }));
  vi.spyOn(api, 'get').mockImplementation((url, config) => {
    if (url === '/api/projects') return Promise.resolve({ data: [project] });
    if (url === `/api/projects/${project.id}`) return Promise.resolve({ data: project });
    if (url.endsWith('/follow-ups/page')) {
      const status = (config?.params as { status: string }).status;
      return Promise.resolve({ data: { ...counts, filteredCount: 1, items: [{
        id: 'follow-up', customerId: 'customer', customerName: `عميل ${status}`, customerPhoneNumber: '201000000000',
        dueDate: '2026-10-16T10:00:00Z', status, notes: 'رسالة المتابعة', type: 'Nurturing',
      }] } });
    }
    return Promise.reject(new Error(`Unexpected GET: ${url}`));
  });
});

it.each([
  ['Cancelled', 'ملغية', 4],
  ['Processing', 'قيد التنفيذ', 5],
  ['DeliveryUnknown', 'الإرسال غير محسوم', 6],
] as const)('exposes %s with its source count and filters to its actual records', async (status, label, count) => {
  render(<AuthProvider><FollowUps /></AuthProvider>);
  await screen.findByText('عميل Pending');
  const filters = screen.getByRole('group', { name: 'تصفية المتابعات حسب الحالة' });
  const filter = within(filters).getByRole('button', { name: new RegExp(label) });
  expect(filter).toHaveTextContent(count.toLocaleString('ar-EG'));
  fireEvent.click(filter);

  const record = await screen.findByText(`عميل ${status}`);
  expect(within(record.closest('tr')!).getByText(label)).toBeVisible();
  expect(screen.queryByText('عميل Pending')).not.toBeInTheDocument();
  expect(within(filters).getByRole('button', { name: /تم تجاوزها/ })).toHaveTextContent('٣');
});

it('does not show zero status counts when the first read fails', async () => {
  vi.mocked(api.get).mockImplementation((url) => {
    if (url === '/api/projects') return Promise.resolve({ data: [project] });
    if (url === `/api/projects/${project.id}`) return Promise.resolve({ data: project });
    return Promise.reject(new Error('Unavailable'));
  });
  render(<AuthProvider><FollowUps /></AuthProvider>);
  await screen.findByText('فشل تحميل مواعيد المتابعات. لم يتم عرض قائمة فارغة بديلًا عنها.');
  expect(within(screen.getByRole('group', { name: 'تصفية المتابعات حسب الحالة' })).queryByText('٠')).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'ضبط المتابعات تلقائياً بالـ AI' })).toBeDisabled();
});
