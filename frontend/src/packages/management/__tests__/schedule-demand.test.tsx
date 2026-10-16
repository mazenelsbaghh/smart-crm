import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as XLSX from 'xlsx';
import { AuthProvider, useAuth } from '../../../context/auth-context';
import { api } from '../../../services/api';
import ScheduleDemandPage from '../schedule-demand/ScheduleDemandPage';

vi.mock('xlsx', async (importOriginal) => ({
  ...await importOriginal<typeof import('xlsx')>(),
  writeFileXLSX: vi.fn(),
}));

const project = { id: 'project-1', name: 'المشروع', settings: { timezone: 'Africa/Cairo' } };
const overview = {
  totalPeople: 5,
  distinctSchedules: 2,
  groups: [{ label: 'السبت مساءً', peopleCount: 2 }, { label: 'الأحد صباحًا', peopleCount: 1 }, { label: 'بيسألوا بس', peopleCount: 2 }],
  rows: [
    { customerId: 'ahmed', customerName: 'أحمد', phoneNumber: '01001234567', channel: 'WhatsApp', requestedScheduleLabel: 'السبت مساءً', requestedScheduleText: 'بعد الساعة ٦، لو سمحت', lastMessageAtUtc: '2026-09-06T18:00:00Z' },
    { customerId: 'mona', customerName: 'منى', phoneNumber: '01112345678', channel: 'WhatsApp', requestedScheduleLabel: 'السبت مساءً', requestedScheduleText: 'يوم السبت', lastMessageAtUtc: '2026-09-06T17:00:00Z' },
    { customerId: 'sara', customerName: 'سارة', phoneNumber: '+201201234567', channel: 'WhatsApp', requestedScheduleLabel: 'الأحد صباحًا', requestedScheduleText: '=1+1', lastMessageAtUtc: '2026-09-06T16:00:00Z' },
    { customerId: 'omar', customerName: 'عمر', phoneNumber: '01001234568', channel: 'WhatsApp', requestedScheduleLabel: 'بيسألوا بس', requestedScheduleText: '', requestKind: 'InquiryOnly', attendanceMode: 'Offline', lastMessageAtUtc: '2026-09-06T15:00:00Z' },
    { customerId: 'nour', customerName: 'نور', phoneNumber: '01001234569', channel: 'WhatsApp', requestedScheduleLabel: 'بيسألوا بس', requestedScheduleText: '', requestKind: 'InquiryOnly', attendanceMode: 'Unknown', lastMessageAtUtc: '2026-09-06T14:00:00Z' },
  ],
  openAppointments: [],
};

function ProjectReload() {
  const { refreshProjects } = useAuth();
  return <button onClick={() => void refreshProjects()}>تحديث مساحة العمل</button>;
}

describe('Schedule demand student export', () => {
  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem('user', JSON.stringify({ id: 'owner-1', email: 'owner@example.test', role: 'Owner' }));
    vi.mocked(XLSX.writeFileXLSX).mockReset();
    vi.spyOn(api, 'get').mockImplementation((url) => {
      if (url === '/api/projects') return Promise.resolve({ data: [{ id: project.id }] });
      if (url === `/api/projects/${project.id}`) return Promise.resolve({ data: project });
      if (url.endsWith('/schedule-demand')) return Promise.resolve({ data: overview });
      return Promise.reject(new Error(`Unexpected GET: ${url}`));
    });
  });

  it('exports only selected students across filters, preserving Arabic and phone numbers without sending messages', async () => {
    const sendRequest = vi.spyOn(api, 'post');
    render(<AuthProvider><ScheduleDemandPage /></AuthProvider>);
    await screen.findByText('أحمد');
    fireEvent.click(screen.getByText('تصفية', { exact: true }));
    expect(screen.queryByRole('button', { name: 'تنزيل المحددين (Excel)' })).not.toBeInTheDocument();

    fireEvent.change(screen.getByRole('combobox', { name: 'الموعد المطلوب' }), { target: { value: 'السبت مساءً' } });
    fireEvent.click(screen.getByRole('button', { name: 'تحديد كل الظاهر' }));
    fireEvent.click(screen.getByRole('checkbox', { name: /منى/ }));
    fireEvent.change(screen.getByRole('combobox', { name: 'الموعد المطلوب' }), { target: { value: 'الأحد صباحًا' } });
    fireEvent.click(screen.getByRole('checkbox', { name: /سارة/ }));
    fireEvent.click(screen.getByRole('tab', { name: 'المواعيد المفتوحة' }));
    expect(screen.getByText('لا توجد مجموعات مفتوحة بها أماكن حاليًا؛ الإرسال متوقف لحماية العملاء.')).toBeVisible();
    fireEvent.click(screen.getByRole('tab', { name: 'طلبات العملاء' }));
    expect(screen.getByRole('checkbox', { name: /سارة/ })).toBeChecked();
    expect(screen.getByRole('button', { name: 'إرسال المواعيد المفتوحة' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'تنزيل المحددين (Excel)' }));

    expect(await screen.findByRole('status')).toHaveTextContent('٢ طالب');
    const [workbook, filename] = vi.mocked(XLSX.writeFileXLSX).mock.calls[0];
    const downloaded = XLSX.read(XLSX.write(workbook, { type: 'array', bookType: 'xlsx' }));
    const worksheet = downloaded.Sheets[downloaded.SheetNames[0]];
    const students = XLSX.utils.sheet_to_json(worksheet);
    expect(filename).toMatch(/^schedule-demand-students-.*\.xlsx$/);
    expect(students).toEqual([
      expect.objectContaining({ 'اسم الطالب': 'أحمد', 'رقم الهاتف': '01001234567', 'الموعد المطلوب': 'السبت مساءً', 'تفاصيل طلب الموعد': 'بعد الساعة ٦، لو سمحت' }),
      expect.objectContaining({ 'اسم الطالب': 'سارة', 'رقم الهاتف': '+201201234567', 'الموعد المطلوب': 'الأحد صباحًا', 'تفاصيل طلب الموعد': '=1+1' }),
    ]);
    expect(worksheet.G3.f).toBeUndefined();
    expect(students[0]).toHaveProperty('آخر رسالة (Africa/Cairo)', new Date('2026-09-06T18:00:00Z').toLocaleString('ar-EG', { timeZone: 'Africa/Cairo' }));
    expect(sendRequest).not.toHaveBeenCalled();
  });

  it('combines inquiry and attendance filters and includes the mode in the downloaded sheet', async () => {
    render(<AuthProvider><ScheduleDemandPage /></AuthProvider>);
    await screen.findByText('عمر');
    fireEvent.click(screen.getByText('تصفية', { exact: true }));
    fireEvent.change(screen.getByRole('combobox', { name: 'الموعد المطلوب' }), { target: { value: 'inquiry' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'نوع الحضور' }), { target: { value: 'Unknown' } });
    expect(screen.getByRole('checkbox', { name: /نور.*غير محدد/ })).not.toBeChecked();
    expect(screen.queryByRole('checkbox', { name: /عمر/ })).not.toBeInTheDocument();
    fireEvent.change(screen.getByRole('combobox', { name: 'نوع الحضور' }), { target: { value: 'Offline' } });
    expect(screen.getAllByRole('checkbox')).toHaveLength(1);
    fireEvent.click(screen.getByRole('button', { name: 'تحديد كل الظاهر' }));
    fireEvent.click(screen.getByRole('button', { name: 'تنزيل المحددين (Excel)' }));

    await screen.findByRole('status');
    const [workbook] = vi.mocked(XLSX.writeFileXLSX).mock.calls[0];
    const students = XLSX.utils.sheet_to_json(workbook.Sheets[workbook.SheetNames[0]]);
    expect(students).toEqual([expect.objectContaining({ 'اسم الطالب': 'عمر', 'نوع الطلب': 'بيسألوا بس', 'نوع الحضور': 'أوفلاين (في السنتر)', 'تفاصيل طلب الموعد': '' })]);
  });

  it('keeps the selection and allows retry when the download fails', async () => {
    vi.mocked(XLSX.writeFileXLSX).mockImplementationOnce(() => { throw new Error('Download unavailable'); });
    render(<AuthProvider><ScheduleDemandPage /></AuthProvider>);
    fireEvent.click(await screen.findByRole('checkbox', { name: /أحمد/ }));
    fireEvent.click(screen.getByRole('button', { name: 'تنزيل المحددين (Excel)' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('تعذّر تنزيل الشيت');
    expect(screen.getByRole('checkbox', { name: /أحمد/ })).toBeChecked();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'تنزيل المحددين (Excel)' }));
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('١ طالب'));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('does not report zero demand or no open appointments when the initial request fails, and recovers on retry', async () => {
    let unavailable = true;
    vi.mocked(api.get).mockImplementation((url) => {
      if (url === '/api/projects') return Promise.resolve({ data: [project] });
      if (url === `/api/projects/${project.id}`) return Promise.resolve({ data: project });
      if (url.endsWith('/schedule-demand')) return unavailable
        ? Promise.reject(new Error('Unavailable')) : Promise.resolve({ data: overview });
      return Promise.reject(new Error(`Unexpected GET: ${url}`));
    });
    render(<AuthProvider><ScheduleDemandPage /></AuthProvider>);
    expect(await screen.findByRole('alert')).toHaveTextContent('تعذّر تحميل طلبات المواعيد');
    expect(screen.queryByRole('region', { name: 'ملخص طلبات المواعيد' })).not.toBeInTheDocument();
    expect(screen.queryByText(/لا يوجد عملاء مطابقون|لا توجد مجموعات مفتوحة/)).not.toBeInTheDocument();

    unavailable = false;
    fireEvent.click(screen.getByRole('button', { name: 'تحديث' }));
    await screen.findByText('أحمد');
    fireEvent.click(screen.getByRole('tab', { name: 'المواعيد المفتوحة' }));
    expect(within(screen.getByRole('region', { name: 'ملخص طلبات المواعيد' })).getByText('٥')).toBeVisible();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('clears project selections and ignores an older project refresh that completes after the new project loads', async () => {
    const secondProject = { ...project, id: 'project-2' };
    const secondOverview = { ...overview, totalPeople: 1, rows: [{ ...overview.rows[0], customerId: 'other', customerName: 'عميل المشروع الثاني' }] };
    let authorizedProject = project;
    let deferRefresh = false;
    let resolveRefresh!: (response: { data: typeof overview }) => void;
    const pendingRefresh = new Promise<{ data: typeof overview }>(resolve => { resolveRefresh = resolve; });
    vi.mocked(api.get).mockImplementation((url) => {
      if (url === '/api/projects') return Promise.resolve({ data: [authorizedProject] });
      if (url === `/api/projects/${authorizedProject.id}`) return Promise.resolve({ data: authorizedProject });
      if (url === `/api/projects/${project.id}/reports/sales-intelligence/schedule-demand`)
        return deferRefresh ? pendingRefresh : Promise.resolve({ data: overview });
      if (url === `/api/projects/${secondProject.id}/reports/sales-intelligence/schedule-demand`)
        return Promise.resolve({ data: secondOverview });
      return Promise.reject(new Error(`Unexpected GET: ${url}`));
    });
    render(<AuthProvider><ProjectReload /><ScheduleDemandPage /></AuthProvider>);
    fireEvent.click(await screen.findByRole('checkbox', { name: /أحمد/ }));
    deferRefresh = true;
    fireEvent.click(screen.getByRole('button', { name: 'تحديث' }));
    authorizedProject = secondProject;
    fireEvent.click(screen.getByRole('button', { name: 'تحديث مساحة العمل' }));
    await screen.findByText('عميل المشروع الثاني');
    expect(screen.queryByRole('region', { name: 'إجراءات العملاء المحددين' })).not.toBeInTheDocument();

    await act(async () => { resolveRefresh({ data: overview }); await pendingRefresh; });
    expect(screen.getByText('عميل المشروع الثاني')).toBeVisible();
    expect(screen.queryByText('أحمد')).not.toBeInTheDocument();
  });
});
