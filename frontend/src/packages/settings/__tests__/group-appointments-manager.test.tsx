import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../../context/auth-context';
import { api } from '../../../services/api';
import GroupAppointmentsManager from '../GroupAppointmentsManager';

const project = { id: 'project-1', name: 'المشروع', settings: { aiAutoReplyEnabled: true } };

const existingBooking = {
  id: 'booking-1',
  customerId: 'customer-1',
  customerName: 'سارة أحمد',
  customerPhone: '201000000000',
  createdAt: '2026-08-25T10:00:00Z',
  isPaid: false,
  isAttended: false,
};

const group = {
  id: 'group-1',
  name: 'أونلاين المساء',
  dateTime: '2026-09-01T17:00:00Z',
  freeSessionDateTime: '2026-08-30T17:00:00Z',
  courseSecondDateTime: '2026-09-03T17:00:00Z',
  capacity: 2,
  isActive: true,
  days: '1,3',
  bookedCount: 1,
  bookings: [existingBooking],
  mode: 'online',
  instructorName: 'أحمد',
};

const secondGroup = {
  ...group,
  id: 'group-2',
  name: 'في السنتر الصباح',
  dateTime: '2026-09-02T08:00:00Z',
  bookedCount: 0,
  bookings: [],
  mode: 'offline',
};

const renderManager = (onBack: () => void = vi.fn()) => render(
  <AuthProvider>
    <GroupAppointmentsManager onBack={onBack} timezone="Africa/Cairo" />
  </AuthProvider>,
);

describe('GroupAppointmentsManager manual booking', () => {
  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem('user', JSON.stringify({ id: 'owner-1', email: 'owner@example.test', role: 'Owner' }));
    vi.spyOn(api, 'get').mockImplementation((url) => {
      if (url === '/api/projects') return Promise.resolve({ data: [{ id: project.id }] }) as never;
      if (url === `/api/projects/${project.id}`) return Promise.resolve({ data: project }) as never;
      if (url === '/api/group-appointments') return Promise.resolve({ data: [group, secondGroup] }) as never;
      if (url === '/api/group-appointments/instructors') return Promise.resolve({ data: { instructors: ['أحمد'] } }) as never;
      return Promise.reject(new Error(`Unexpected URL: ${String(url)}`)) as never;
    });
  });

  it('يثبّت سياق المجموعة أثناء الحفظ ثم يحدّث القائمة والسعة ويستعيد التركيز', async () => {
    const user = userEvent.setup();
    const onBack = vi.fn();
    const createdBooking = {
      id: 'booking-2',
      customerId: 'customer-2',
      customerName: 'محمود علي',
      customerPhone: '201012345678',
      createdAt: '2026-08-26T11:00:00Z',
      isPaid: true,
      isAttended: true,
    };
    const successfulResponse = {
      data: {
        message: 'تمت الإضافة',
        booking: createdBooking,
        group: { id: group.id, name: group.name, capacity: 2, bookedCount: 2, slotsLeft: 0, isFull: true },
      },
    };
    let resolvePost!: (value: typeof successfulResponse) => void;
    const pendingPost = new Promise<typeof successfulResponse>((resolve) => { resolvePost = resolve; });
    const post = vi.spyOn(api, 'post').mockReturnValue(pendingPost as never);

    renderManager(onBack);
    await user.click(await screen.findByRole('button', { name: 'المشتركين (1)' }));
    await user.click(screen.getByRole('button', { name: 'إضافة مشترك يدويًا' }));

    await user.type(screen.getByLabelText('اسم المشترك'), ' محمود علي ');
    await user.type(screen.getByLabelText('رقم الهاتف أو واتساب'), '010 1234 5678');
    await user.type(screen.getByLabelText(/ملاحظة داخلية/), 'تم تأكيد الحجز هاتفيًا');
    await user.click(screen.getByLabelText('تم الدفع'));
    await user.click(screen.getByLabelText('تم الحضور'));
    await user.click(screen.getByRole('button', { name: /إضافة إلى المجموعة/ }));

    await waitFor(() => expect(post).toHaveBeenCalledWith(
      `/api/group-appointments/${group.id}/bookings/manual`,
      {
        customerName: 'محمود علي',
        customerPhone: '201012345678',
        notes: 'تم تأكيد الحجز هاتفيًا',
        isPaid: true,
        isAttended: true,
      },
    ));
    const secondGroupButton = screen.getByRole('button', { name: 'المشتركين (0)' });
    expect(secondGroupButton).toBeDisabled();
    const backButton = screen.getByRole('button', { name: 'العودة للإضافات' });
    const addGroupButton = screen.getByRole('button', { name: 'إضافة مجموعة جديدة' });
    expect(backButton).toBeDisabled();
    expect(addGroupButton).toBeDisabled();
    expect(screen.getByLabelText('اختيار ملف Excel')).toBeDisabled();
    await user.click(secondGroupButton);
    await user.click(backButton);
    await user.click(addGroupButton);
    expect(onBack).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /المشتركون في مجموعة: أونلاين المساء/ })).toBeInTheDocument();

    await act(async () => {
      resolvePost(successfulResponse);
      await pendingPost;
    });

    expect(await screen.findByRole('status')).toHaveTextContent('تمت إضافة محمود علي إلى مجموعة أونلاين المساء بنجاح');
    expect(screen.getByRole('button', { name: 'المشتركين (2)' })).toBeInTheDocument();
    expect(screen.getByText('المجموعة ممتلئة (2 من 2).')).toBeInTheDocument();
    const subscribersHeading = screen.getByRole('heading', { name: /المشتركون في مجموعة: أونلاين المساء/ });
    await waitFor(() => expect(subscribersHeading).toHaveFocus());

    const subscribersTable = screen.getByRole('table', { name: 'مشتركو المجموعة وحالة الحضور والدفع' });
    expect(within(subscribersTable).getByText('محمود علي')).toBeInTheDocument();
    expect(within(subscribersTable).getByLabelText('تحديد دفع محمود علي')).toBeChecked();
    expect(within(subscribersTable).getByLabelText('تحديد حضور محمود علي')).toBeChecked();
  });

  it('يرفض رقمًا يبدأ بصفر بعد التطبيع ولا يرسل الطلب', async () => {
    const user = userEvent.setup();
    const post = vi.spyOn(api, 'post');

    renderManager();
    await user.click(await screen.findByRole('button', { name: 'المشتركين (1)' }));
    await user.click(screen.getByRole('button', { name: 'إضافة مشترك يدويًا' }));

    const nameInput = screen.getByLabelText('اسم المشترك');
    const phoneInput = screen.getByLabelText('رقم الهاتف أو واتساب');
    await user.type(nameInput, 'ليلى حسن');
    await user.type(phoneInput, '01234567');
    await user.click(screen.getByRole('button', { name: /إضافة إلى المجموعة/ }));

    expect(await screen.findByText(/اكتب رقمًا صحيحًا من 7 إلى 15 رقمًا/)).toBeInTheDocument();
    expect(post).not.toHaveBeenCalled();
    expect(phoneInput).toHaveFocus();
  });

  it('يعرض تعارض الحجز ويحافظ على البيانات لتصحيحها', async () => {
    const user = userEvent.setup();
    vi.spyOn(api, 'post').mockRejectedValue({
      response: {
        status: 409,
        data: {
          code: 'BOOKING_ALREADY_EXISTS',
          message: 'رقم الهاتف مسجل بالفعل في مجموعة أونلاين الصباح.',
        },
      },
    });

    renderManager();
    await user.click(await screen.findByRole('button', { name: 'المشتركين (1)' }));
    await user.click(screen.getByRole('button', { name: 'إضافة مشترك يدويًا' }));

    const nameInput = screen.getByLabelText('اسم المشترك');
    const phoneInput = screen.getByLabelText('رقم الهاتف أو واتساب');
    const notesInput = screen.getByLabelText(/ملاحظة داخلية/);
    await user.type(nameInput, 'ليلى حسن');
    await user.type(phoneInput, '+44 7700 900123');
    await user.type(notesInput, 'تحتاج متابعة');
    await user.click(screen.getByLabelText('تم الدفع'));
    await user.click(screen.getByRole('button', { name: /إضافة إلى المجموعة/ }));

    expect(await screen.findByRole('alert')).toHaveTextContent('رقم الهاتف مسجل بالفعل في مجموعة أونلاين الصباح');
    expect(nameInput).toHaveValue('ليلى حسن');
    expect(phoneInput).toHaveValue('+44 7700 900123');
    expect(notesInput).toHaveValue('تحتاج متابعة');
    expect(screen.getByLabelText('تم الدفع')).toBeChecked();
  });

  it('يعيد مزامنة المجموعة المفتوحة بعد تعطيلها ويمنع الحفظ اليدوي', async () => {
    const user = userEvent.setup();
    const inactiveGroup = { ...group, isActive: false };
    let groupFetchCount = 0;
    vi.spyOn(api, 'get').mockImplementation((url) => {
      if (url === '/api/projects') return Promise.resolve({ data: [{ id: project.id }] }) as never;
      if (url === `/api/projects/${project.id}`) return Promise.resolve({ data: project }) as never;
      if (url === '/api/group-appointments') {
        groupFetchCount += 1;
        return Promise.resolve({ data: groupFetchCount === 1 ? [group, secondGroup] : [inactiveGroup, secondGroup] }) as never;
      }
      if (url === '/api/group-appointments/instructors') return Promise.resolve({ data: { instructors: ['أحمد'] } }) as never;
      return Promise.reject(new Error(`Unexpected URL: ${String(url)}`)) as never;
    });
    const patch = vi.spyOn(api, 'patch').mockResolvedValue({ data: {} } as never);

    renderManager();
    const subscribersButton = await screen.findByRole('button', { name: 'المشتركين (1)' });
    await user.click(subscribersButton);
    await user.click(screen.getByRole('button', { name: 'إضافة مشترك يدويًا' }));

    const groupRow = subscribersButton.closest('tr');
    expect(groupRow).not.toBeNull();
    await user.click(within(groupRow as HTMLTableRowElement).getByRole('button', { name: 'نشطة ✓' }));

    await waitFor(() => expect(patch).toHaveBeenCalledWith(`/api/group-appointments/${group.id}/toggle`));
    expect(await screen.findByText('المجموعة غير نشطة، فعّلها أولًا لإضافة مشترك.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'إلغاء الإضافة' })).toBeDisabled();
    expect(screen.getByRole('button', { name: /إضافة إلى المجموعة/ })).toBeDisabled();
  });
});
