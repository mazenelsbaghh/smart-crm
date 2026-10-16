import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { ToastProvider } from '../../../context/toast-context';
import { api } from '../../../services/api';
import type { Customer } from '../../../services/crm';
import type { Conversation } from '../../../types/chat';
import ContextSidebar from '../shared/ContextSidebar';

afterEach(() => vi.restoreAllMocks());

it('يواكب الملاحظات المحفوظة من محرر آخر ويحفظ المسودة المحلية حتى يؤكدها المصدر', async () => {
  vi.spyOn(api, 'get').mockResolvedValue({ data: [] });
  const customer: Customer = {
    id: 'customer-1', projectId: 'project-1', name: 'سارة', phoneNumber: '201000000000',
    city: 'القاهرة', leadScore: 80, notes: 'ملاحظة سابقة', tags: [], budget: null, interests: [],
  };
  const conversation: Conversation = {
    id: 'conversation-1', projectId: customer.projectId, channel: 'WhatsApp', status: 'Open',
    customer: { id: customer.id, name: customer.name, phone: customer.phoneNumber, avatarUrl: null },
    lastMessageAt: '2026-10-16T10:00:00Z', unreadCount: 0, assignedAgentId: null, assignedAgentName: null,
  };
  const sidebar = (notes: string) => (
    <ToastProvider>
      <ContextSidebar activeConv={conversation} customer={{ ...customer, notes }}
        onUpdateCustomer={async () => undefined} updating={false} />
    </ToastProvider>
  );
  const view = render(sidebar(customer.notes));
  await screen.findByText('لا توجد مهام حالية للعميل.');
  fireEvent.click(screen.getByText('تعديل بيانات العميل وتقييمه'));

  view.rerender(sidebar('حُفظت من تبويب الملاحظات'));
  const notesField = screen.getByLabelText('ملاحظات العميل');
  expect(notesField).toHaveValue('حُفظت من تبويب الملاحظات');

  fireEvent.change(notesField, { target: { value: 'مسودة محلية' } });
  view.rerender(sidebar('تحديث خارجي أثناء التحرير'));
  expect(notesField).toHaveValue('مسودة محلية');

  view.rerender(sidebar('مسودة محلية'));
  view.rerender(sidebar('تحديث بعد حفظ المسودة'));
  expect(notesField).toHaveValue('تحديث بعد حفظ المسودة');
});
