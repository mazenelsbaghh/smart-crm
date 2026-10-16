import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { ToastProvider } from '../../context/toast-context';
import { api } from '../../services/api';
import CustomerDetail from './CustomerDetail';

const customer = {
  id: 'customer-one', projectId: 'project-one', name: 'عميل تجريبي', phoneNumber: '201000000000',
  city: 'الإسكندرية', leadScore: 60, notes: '', tags: ['كورس'], label: 'مهتم', isBlacklisted: false,
};

beforeEach(() => {
  // jsdom has no layout observer; the loader uses this browser boundary only for its shimmer.
  vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} });
  vi.spyOn(api, 'get').mockImplementation(url => {
    if (url === `/api/customers/${customer.id}`) return Promise.resolve({ data: customer });
    if (url === '/api/customers/customer-two') return Promise.resolve({ data: { ...customer, id: 'customer-two', name: 'العميل الثاني' } });
    if (url === `/api/projects/${customer.projectId}/follow-ups`) return Promise.resolve({ data: [] });
    if (url.endsWith('/schedule-availability-preference')) return Promise.resolve({ data: { timeWindow: '12-16', availabilityHorizon: 'AnyTime' } });
    if (url.endsWith('/memory')) return Promise.resolve({ data: { longTermSummary: 'ملخص سابق', factsJson: '[]', triggersJson: '[]', objectionsJson: '[]' } });
    return Promise.reject(new Error(`Unexpected GET: ${url}`));
  });
  vi.spyOn(api, 'put').mockResolvedValue({ data: customer });
});

afterEach(() => vi.unstubAllGlobals());

it.each([false, true])('submits the selected customer task and retains the profile draft (inline=%s)', async isInline => {
  const onClose = vi.fn();
  const onUpdate = vi.fn();
  render(<ToastProvider><CustomerDetail customerId={customer.id} projectId={customer.projectId}
    isInline={isInline} onClose={onClose} onUpdate={onUpdate} /></ToastProvider>);

  fireEvent.change(await screen.findByRole('textbox', { name: 'الاسم الكامل' }), { target: { value: 'اسم معدل' } });
  fireEvent.click(screen.getByRole('tab', { name: 'ملخص الذكاء الاصطناعي' }));
  fireEvent.change(screen.getByRole('textbox', { name: 'ملخص العميل بالذكاء الاصطناعي' }), { target: { value: 'ينتظر موعدًا مسائيًا' } });
  fireEvent.click(screen.getByRole('button', { name: 'حفظ التعديلات' }));
  await waitFor(() => expect(api.put).toHaveBeenCalledWith(`/api/customers/${customer.id}/memory`, {
    longTermSummary: 'ينتظر موعدًا مسائيًا', factsJson: '[]', triggersJson: '[]', objectionsJson: '[]',
  }));
  expect(onClose).not.toHaveBeenCalled();
  expect(onUpdate).not.toHaveBeenCalled();

  fireEvent.click(screen.getByRole('tab', { name: 'المتابعات' }));
  fireEvent.change(screen.getByRole('combobox', { name: 'نوع الإجراء' }), { target: { value: 'AvailabilityAlert' } });
  fireEvent.change(screen.getByRole('combobox', { name: 'فترة الساعة المناسبة' }), { target: { value: '16-20' } });
  fireEvent.change(screen.getByRole('combobox', { name: 'الفترة المطلوبة' }), { target: { value: 'NextMonth' } });
  fireEvent.click(screen.getByRole('button', { name: 'حفظ انتظار الموعد' }));
  await waitFor(() => expect(api.put).toHaveBeenCalledWith(`/api/customers/${customer.id}/schedule-availability-preference`, {
    timeWindow: '16-20', availabilityHorizon: 'NextMonth',
  }));
  expect(onClose).not.toHaveBeenCalled();

  fireEvent.click(screen.getByRole('tab', { name: 'بيانات العميل' }));
  expect(screen.getByRole('textbox', { name: 'الاسم الكامل' })).toHaveValue('اسم معدل');
  fireEvent.click(screen.getByRole('button', { name: 'حفظ التغييرات' }));
  await waitFor(() => expect(api.put).toHaveBeenCalledWith(`/api/customers/${customer.id}`, {
    name: 'اسم معدل', city: customer.city, leadScore: 60, notes: '', tags: ['كورس'], label: 'مهتم', isBlacklisted: false,
  }));
  await waitFor(() => expect(onClose).toHaveBeenCalledOnce());
  expect(onUpdate).toHaveBeenCalledOnce();
  expect(api.put).toHaveBeenCalledTimes(3);
});

it('keeps each profile save and its field labels attached to its own instance', async () => {
  const firstUpdated = vi.fn();
  const secondUpdated = vi.fn();
  render(<ToastProvider>
    <section aria-label="الملف الأول"><CustomerDetail customerId={customer.id} projectId={customer.projectId}
      isInline onClose={vi.fn()} onUpdate={firstUpdated} /></section>
    <section aria-label="الملف الثاني"><CustomerDetail customerId="customer-two" projectId={customer.projectId}
      isInline onClose={vi.fn()} onUpdate={secondUpdated} /></section>
  </ToastProvider>);

  const firstProfile = within(screen.getByRole('region', { name: 'الملف الأول' }));
  const secondProfile = within(screen.getByRole('region', { name: 'الملف الثاني' }));
  const secondName = await secondProfile.findByRole('textbox', { name: 'الاسم الكامل' });
  expect(await firstProfile.findByRole('textbox', { name: 'الاسم الكامل' })).toHaveValue(customer.name);
  fireEvent.change(secondName, { target: { value: 'تعديل العميل الثاني' } });
  fireEvent.click(secondProfile.getByRole('button', { name: 'حفظ التغييرات' }));

  await waitFor(() => expect(secondUpdated).toHaveBeenCalledOnce());
  expect(firstUpdated).not.toHaveBeenCalled();
  expect(api.put).toHaveBeenCalledOnce();
  expect(api.put).toHaveBeenCalledWith('/api/customers/customer-two', expect.objectContaining({ name: 'تعديل العميل الثاني' }));
  expect(firstProfile.getByRole('textbox', { name: 'الاسم الكامل' })).toHaveValue(customer.name);
});
