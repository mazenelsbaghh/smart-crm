import { createRef, useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { Conversation } from '../../../types/chat';
import ConversationList from '../shared/ConversationList';

const conversation: Conversation = {
  id: 'conversation-1',
  projectId: 'project-1',
  customer: {
    id: 'customer-1',
    name: 'سارة أحمد',
    phone: '201000000000',
    avatarUrl: null,
    label: 'عميل حالي',
  },
  status: 'Pending',
  channel: 'WhatsApp',
  whatsAppAccountId: 'account-sales',
  whatsAppAccountName: 'رقم المبيعات',
  lastMessageAt: '2026-08-25T10:00:00Z',
  unreadCount: 3,
  assignedAgentId: null,
  assignedAgentName: null,
};

const statusLabels = {
  All: 'الكل',
  Open: 'مفتوحة',
  Pending: 'قيد المتابعة',
  Resolved: 'تم حلها',
  Closed: 'مغلقة',
};

describe('ConversationList', () => {
  it('يعرض بيانات المصدر ويتيح اختيار حالة المحادثات بلا ملخصات مختلقة', () => {
    function ConversationListScenario() {
      const [filterStatus, setFilterStatus] = useState('All');
      return <ConversationList
        conversations={[conversation]}
        activeConv={null}
        setActiveConv={vi.fn()}
        searchQuery=""
        setSearchQuery={vi.fn()}
        filterStatus={filterStatus}
        setFilterStatus={setFilterStatus}
        channel="WhatsApp"
        searchInputRef={createRef<HTMLInputElement>()}
        statusLabels={statusLabels}
      />;
    }
    render(<ConversationListScenario />);

    expect(screen.getByText('تصنيف CRM: عميل حالي')).toBeInTheDocument();
    expect(screen.getByText('رقم المبيعات')).toBeInTheDocument();
    expect(screen.getByLabelText('3 رسائل غير مقروءة')).toBeInTheDocument();
    const statusFilter = screen.getByRole('combobox', { name: 'حالة المحادثات' });
    expect(statusFilter).toHaveValue('All');
    fireEvent.change(statusFilter, { target: { value: 'Pending' } });
    expect(statusFilter).toHaveValue('Pending');
    expect(screen.queryByText('مؤهل للشراء')).not.toBeInTheDocument();
    expect(screen.queryByText('لا يتوفر ملخص للرسالة في هذا المصدر')).not.toBeInTheDocument();
  });

  it('يتيح تحميل الصفحة التالية عندما يعلن المصدر وجودها', () => {
    const onLoadMore = vi.fn();
    render(
      <ConversationList
        conversations={[conversation]}
        activeConv={null}
        setActiveConv={vi.fn()}
        searchQuery=""
        setSearchQuery={vi.fn()}
        filterStatus="All"
        setFilterStatus={vi.fn()}
        channel="WhatsApp"
        searchInputRef={createRef<HTMLInputElement>()}
        statusLabels={statusLabels}
        hasMore
        onLoadMore={onLoadMore}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'تحميل محادثات أقدم' }));
    expect(onLoadMore).toHaveBeenCalledOnce();
  });

  it('does not label an unknown unread count as zero or as unread messages', () => {
    render(<ConversationList conversations={[{ ...conversation, unreadCount: null }]}
      activeConv={null} setActiveConv={vi.fn()} searchQuery="" setSearchQuery={vi.fn()}
      filterStatus="All" setFilterStatus={vi.fn()} channel="WhatsApp"
      searchInputRef={createRef<HTMLInputElement>()} statusLabels={statusLabels} />);

    expect(screen.getByText('سارة أحمد')).toBeInTheDocument();
    expect(screen.queryByText(/غير مقروءة/)).not.toBeInTheDocument();
    expect(screen.queryByText('٠')).not.toBeInTheDocument();
  });
});
