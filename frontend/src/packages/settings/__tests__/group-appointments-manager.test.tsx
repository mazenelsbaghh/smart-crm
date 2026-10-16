import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider, useAuth } from '../../../context/auth-context';
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

function ProjectReload() {
  const { refreshProjects } = useAuth();
  return <button onClick={() => void refreshProjects()}>تحديث المشروع</button>;
}

describe('GroupAppointmentsManager manual booking', () => {
  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem('user', JSON.stringify({ id: 'owner-1', email: 'owner@example.test', role: 'Owner' }));
    vi.spyOn(api, 'get').mockImplementation((url) => {
      if (url === '/api/projects') return Promise.resolve({ data: [{ id: project.id }] }) as never;
      if (url === `/api/projects/${project.id}`) return Promise.resolve({ data: project }) as never;
      if (url === '/api/group-appointments') return Promise.resolve({ data: [group, secondGroup] }) as never;
      if (url === '/api/group-appointments/instructors') return Promise.resolve({ data: { instructors: ['أحمد'], instructorContacts: [{ id: 'teacher-1', name: 'أحمد', phone: '201112345678' }], supervisors: [{ id: 'staff-1', name: 'سارة', phone: '201515089806' }] } }) as never;
      return Promise.reject(new Error(`Unexpected URL: ${String(url)}`)) as never;
    });
  });

  it('keeps the latest booking count, payment and attendance when an older group refresh arrives last', async () => {
    const latestGroup = { ...group, bookedCount: 2, bookings: [
      { ...existingBooking, isPaid: true, isAttended: true },
      { ...existingBooking, id: 'booking-2', customerId: 'customer-2', customerName: 'مشترك آخر' },
    ] };
    let groupReads = 0;
    let resolveOlder!: (response: { data: typeof group[] }) => void;
    const olderRefresh = new Promise<{ data: typeof group[] }>(resolve => { resolveOlder = resolve; });
    vi.mocked(api.get).mockImplementation((url) => {
      if (url === '/api/projects') return Promise.resolve({ data: [project] });
      if (url === `/api/projects/${project.id}`) return Promise.resolve({ data: { ...project } });
      if (url === '/api/group-appointments') {
        groupReads += 1;
        if (groupReads === 2) return olderRefresh;
        return Promise.resolve({ data: [groupReads === 1 ? group : latestGroup] });
      }
      if (url === '/api/group-appointments/instructors') return Promise.resolve({ data: { instructors: [] } });
      return Promise.reject(new Error(`Unexpected GET: ${url}`));
    });
    render(<AuthProvider><ProjectReload /><GroupAppointmentsManager onBack={vi.fn()} timezone="Africa/Cairo" /></AuthProvider>);
    fireEvent.click(await screen.findByRole('button', { name: 'المشتركين (1)' }));
    fireEvent.click(screen.getByRole('button', { name: 'تحديث المشروع' }));
    await screen.findByText('جاري تحميل مجموعات المواعيد…');
    fireEvent.click(screen.getByRole('button', { name: 'تحديث المشروع' }));
    await screen.findByRole('button', { name: 'المشتركين (2)' });

    await act(async () => { resolveOlder({ data: [{ ...group, bookedCount: 0, bookings: [] }] }); await olderRefresh; });
    expect(screen.getByText('المجموعة ممتلئة (2 من 2).')).toBeVisible();
    expect(screen.getByRole('checkbox', { name: 'تحديد دفع سارة أحمد' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'تحديد حضور سارة أحمد' })).toBeChecked();
    expect(screen.queryByRole('button', { name: 'المشتركين (0)' })).not.toBeInTheDocument();
  });

  it('يفتح المكثف بقائمة مستقلة ويحفظ المجموعة الجديدة بنوع المكثف', async () => {
    const user = userEvent.setup();
    const post = vi.spyOn(api, 'post').mockResolvedValue({ data: {} } as never);
    vi.spyOn(api, 'get').mockImplementation((url, config) => {
      if (url === '/api/projects') return Promise.resolve({ data: [{ id: project.id }] }) as never;
      if (url === `/api/projects/${project.id}`) return Promise.resolve({ data: project }) as never;
      if (url === '/api/group-appointments') return Promise.resolve({ data: (config?.params as { courseType?: string } | undefined)?.courseType === 'intensive' ? [] : [group] }) as never;
      if (url === '/api/group-appointments/instructors') return Promise.resolve({ data: { instructors: ['أحمد'], instructorContacts: [{ id: 'teacher-1', name: 'أحمد', phone: '201112345678' }], supervisors: [{ id: 'staff-1', name: 'سارة', phone: '201515089806' }] } }) as never;
      return Promise.reject(new Error(`Unexpected URL: ${String(url)}`)) as never;
    });
    render(<AuthProvider><GroupAppointmentsManager courseType="intensive" onBack={vi.fn()} timezone="Africa/Cairo" /></AuthProvider>);
    expect(await screen.findByRole('heading', { name: 'مواعيد الكورس المكثف' })).toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: 'إضافة مجموعة جديدة' }));
    expect(screen.queryByRole('button', { name: 'المشتركين (1)' })).not.toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('المسؤولة عن المجموعة'), '201515089806');
    fireEvent.change(screen.getByLabelText('موعد الجلسة المجانية'), { target: { value: '2026-10-01T18:00' } });
    expect(screen.queryByLabelText('موعد الجلسة الأولى للكورس')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('موعد الجلسة الثانية للكورس')).not.toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'أيام الكورس الأسبوعية (اختر يومين)' })).not.toBeInTheDocument();
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'حفظ' }));
    expect(await screen.findByText('تمت إضافة المجموعة بنجاح.')).toBeInTheDocument();
    expect(post).toHaveBeenCalledWith('/api/group-appointments', expect.objectContaining({ courseType: 'intensive', dateTime: '2026-10-01T15:00:00.000Z', freeSessionDateTime: '2026-10-01T15:00:00.000Z', instructorName: 'أحمد', supervisorPhone: '201515089806' }));
  });

  it('يحفظ أسماء وأرقام فريق العمل ويحتفظ بالتعديلات عند فشل الحفظ', async () => {
    const user = userEvent.setup();
    const put = vi.spyOn(api, 'put').mockRejectedValueOnce({ response: { data: { error: 'رقم غير صالح' } } })
      .mockResolvedValueOnce({ data: { instructors: ['أحمد'], instructorContacts: [{ id: 'teacher-1', name: 'أحمد', phone: '201112345678' }], supervisors: [{ id: 'staff-1', name: 'منى', phone: '201515089806' }] } });
    renderManager();
    await user.click(await screen.findByLabelText('إدارة المجموعات'));
    await user.click(screen.getByRole('button', { name: 'فريق العمل' }));
    const name = await screen.findByLabelText('مسؤولات المتابعة: الاسم 1');
    await user.clear(name); await user.type(name, 'منى');
    await user.click(screen.getByRole('button', { name: 'العودة للمجموعات' }));
    expect(screen.getByRole('button', { name: 'المشتركين (1)' })).toBeVisible();
    await user.click(screen.getByLabelText('إدارة المجموعات'));
    await user.click(screen.getByRole('button', { name: /فريق العمل/ }));
    expect(name).toHaveValue('منى');
    await user.click(screen.getByRole('button', { name: 'حفظ فريق العمل' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('رقم غير صالح');
    expect(name).toHaveValue('منى');
    await user.click(screen.getByRole('button', { name: 'حفظ فريق العمل' }));
    expect(await screen.findByText('تم حفظ أسماء وأرقام فريق العمل.')).toBeInTheDocument();
    expect(put).toHaveBeenLastCalledWith('/api/group-appointments/instructors', expect.objectContaining({ supervisors: [{ id: 'staff-1', name: 'منى', phone: '201515089806' }] }));
    await user.click(screen.getByRole('button', { name: 'إضافة مجموعة جديدة' }));
    expect(within(screen.getByLabelText('المسؤولة عن المجموعة')).getByRole('option', { name: 'منى (+201515089806)' })).toBeInTheDocument();
  });

  it('يبقي الاستيراد وطلبات المواعيد متاحة من قائمة الإدارة ويعود إلى المجموعات', async () => {
    const user = userEvent.setup();
    const onBack = vi.fn();
    render(<AuthProvider><GroupAppointmentsManager onBack={onBack} backLabel="طلبات المواعيد" timezone="Africa/Cairo" /></AuthProvider>);
    expect(await screen.findByRole('button', { name: 'المشتركين (1)' })).toBeVisible();
    const management = screen.getByLabelText('إدارة المجموعات');
    await user.click(management);
    await user.click(screen.getByRole('button', { name: 'الاستيراد والتصدير' }));
    expect(screen.getByRole('region', { name: 'الاستيراد والتصدير' })).toBeVisible();
    expect(screen.getByText('اختيار ملف Excel')).toBeVisible();
    expect(management).toHaveFocus();
    await user.click(screen.getByRole('button', { name: 'العودة للمجموعات' }));
    expect(screen.getByRole('button', { name: 'المشتركين (1)' })).toBeVisible();
    await user.click(management);
    await user.click(screen.getByRole('button', { name: 'طلبات المواعيد' }));
    expect(onBack).toHaveBeenCalledOnce();
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
    await user.click(screen.getByLabelText('إدارة المجموعات'));
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

  it('يفصل المجموعات المكتملة غير النشطة عن المكتملة المتاحة للتشغيل في العدّ والتصفية', async () => {
    const user = userEvent.setup();
    const fullGroup = { ...group, id: 'full', name: 'مجموعة مكتملة نشطة', bookedCount: 2 };
    const inactiveFullGroup = { ...fullGroup, id: 'inactive-full', name: 'مجموعة مكتملة متوقفة', isActive: false };
    vi.spyOn(api, 'get').mockImplementation((url) => {
      if (url === '/api/projects') return Promise.resolve({ data: [{ id: project.id }] }) as never;
      if (url === `/api/projects/${project.id}`) return Promise.resolve({ data: project }) as never;
      if (url === '/api/group-appointments') return Promise.resolve({ data: [group, secondGroup, fullGroup, inactiveFullGroup] }) as never;
      if (url === '/api/group-appointments/instructors') return Promise.resolve({ data: { instructors: ['أحمد'] } }) as never;
      return Promise.reject(new Error(`Unexpected URL: ${String(url)}`)) as never;
    });
    renderManager();
    const filters = await screen.findByRole('group', { name: 'حالة المجموعات' });
    expect(within(filters).getByRole('button', { name: /الكل/ })).toHaveTextContent('٤');
    expect(within(filters).getByRole('button', { name: /متاحة/ })).toHaveTextContent('٢');
    expect(within(filters).getByRole('button', { name: /مكتملة/ })).toHaveTextContent('١');
    expect(within(filters).getByRole('button', { name: /غير نشطة/ })).toHaveTextContent('١');
    await user.click(within(filters).getByRole('button', { name: /مكتملة/ }));
    expect(screen.getByText(fullGroup.name, { exact: true })).toBeVisible();
    expect(screen.queryByText(inactiveFullGroup.name, { exact: true })).not.toBeInTheDocument();
    await user.click(within(filters).getByRole('button', { name: /غير نشطة/ }));
    expect(screen.getByText(inactiveFullGroup.name, { exact: true })).toBeVisible();
    expect(screen.queryByText(fullGroup.name, { exact: true })).not.toBeInTheDocument();
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
      if (url === '/api/group-appointments/instructors') return Promise.resolve({ data: { instructors: ['أحمد'], instructorContacts: [{ id: 'teacher-1', name: 'أحمد', phone: '201112345678' }], supervisors: [{ id: 'staff-1', name: 'سارة', phone: '201515089806' }] } }) as never;
      return Promise.reject(new Error(`Unexpected URL: ${String(url)}`)) as never;
    });
    const patch = vi.spyOn(api, 'patch').mockResolvedValue({ data: {} } as never);

    renderManager();
    const subscribersButton = await screen.findByRole('button', { name: 'المشتركين (1)' });
    await user.click(subscribersButton);
    await user.click(screen.getByRole('button', { name: 'إضافة مشترك يدويًا' }));

    const groupRow = subscribersButton.closest('tr');
    expect(groupRow).not.toBeNull();
    await user.click(within(groupRow as HTMLTableRowElement).getByText('إجراءات أخرى'));
    await user.click(within(groupRow as HTMLTableRowElement).getByRole('button', { name: 'إيقاف الحجز' }));

    await waitFor(() => expect(patch).toHaveBeenCalledWith(`/api/group-appointments/${group.id}/toggle`));
    expect(await screen.findByText('المجموعة غير نشطة، فعّلها أولًا لإضافة مشترك.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'إلغاء الإضافة' })).toBeDisabled();
    expect(screen.getByRole('button', { name: /إضافة إلى المجموعة/ })).toBeDisabled();
  });
});
