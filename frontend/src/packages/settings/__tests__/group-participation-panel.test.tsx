import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '../../../services/api';
import GroupParticipationPanel from '../GroupParticipationPanel';

const report = {
  groupId: 'group-1', groupName: 'مساء', membershipAvailability: 'ready', checkedAtUtc: '2026-10-14T10:00:00Z',
  rows: [
    { customerId: '1', name: 'أحمد', phone: '201011111111', sentAtUtc: '2026-10-14T09:00:00Z', membership: 'not_joined' },
    { customerId: '2', name: 'منى', phone: '201022222222', sentAtUtc: '2026-10-14T09:00:00Z', membership: 'joined' },
    { customerId: '3', name: 'عمر', phone: '201033333333', sentAtUtc: null, membership: 'not_joined' },
  ],
};
const props = { groupId: 'group-1', groupName: 'مساء', timezone: 'Africa/Cairo', onClose: vi.fn() };
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

describe('group participation follow-up', () => {
  it('starts with confirmed invite recipients outside, filters and opens the right conversation', async () => {
    vi.spyOn(api, 'get').mockResolvedValue({ data: report });
    render(<GroupParticipationPanel {...props} />);
    const chat = await screen.findByRole('link', { name: 'فتح محادثة أحمد' });
    expect(chat).toHaveAttribute('href', '/inbox?customerId=1&startConversation=1');
    expect(screen.queryByText('منى')).not.toBeInTheDocument();
    expect(screen.queryByText('عمر')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'خارج الجروب 1' })).toHaveAttribute('aria-pressed', 'true');
    fireEvent.click(screen.getByRole('button', { name: 'اتبعث لهم 2' }));
    expect(screen.getByText('منى')).toBeInTheDocument();
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: '201022' } });
    expect(within(screen.getByRole('table')).queryByText('أحمد')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'تنزيل القائمة (1)' })).toBeEnabled();
  });

  it('keeps unavailable membership out of the outside list and allows a fresh check', async () => {
    vi.spyOn(api, 'get').mockResolvedValueOnce({ data: { ...report, membershipAvailability: 'unavailable',
      checkedAtUtc: null, rows: report.rows.map(row => ({ ...row, membership: 'unknown' })) } })
      .mockResolvedValueOnce({ data: report });
    render(<GroupParticipationPanel {...props} />);
    expect(await screen.findByText(/حالة الدخول غير متاحة/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'خارج الجروب 0' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'الدخول غير مؤكد 2' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'تنزيل القائمة (0)' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'تحديث' }));
    expect(await screen.findByText('أحمد')).toBeInTheDocument();
    expect(screen.queryByText(/حالة الدخول غير متاحة/)).not.toBeInTheDocument();
  });

  it('does not show an old group response after switching groups', async () => {
    let resolveOld!: (value: { data: typeof report }) => void;
    vi.spyOn(api, 'get').mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }))
      .mockResolvedValueOnce({ data: { ...report, groupId: 'group-2', rows: [] } });
    const view = render(<GroupParticipationPanel {...props} />);
    view.rerender(<GroupParticipationPanel {...props} groupId="group-2" />);
    expect(await screen.findByText('لسه مفيش حجوزات في المجموعة دي.')).toBeInTheDocument();
    await act(async () => { resolveOld({ data: report }); });
    expect(screen.queryByText('أحمد')).not.toBeInTheDocument();
  });

  it('exports only the selected follow-up list with phone numbers and names preserved as text', async () => {
    const XLSX = await import('xlsx');
    vi.spyOn(api, 'get').mockResolvedValue({ data: { ...report, rows: report.rows.map(row =>
      row.customerId === '1' ? { ...row, name: '=اسم العميل' } : row) } });
    let exportedBlob: Blob | undefined;
    vi.stubGlobal('URL', { createObjectURL: (blob: Blob) => { exportedBlob = blob; return 'blob:test'; }, revokeObjectURL: vi.fn() });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    render(<GroupParticipationPanel {...props} />);
    fireEvent.click(await screen.findByRole('button', { name: 'تنزيل القائمة (1)' }));
    await waitFor(() => expect(exportedBlob).toBeDefined());
    const bytes = await new Promise<ArrayBuffer>((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result as ArrayBuffer);
      reader.onerror = reject;
      reader.readAsArrayBuffer(exportedBlob!);
    });
    const workbook = XLSX.read(bytes, { type: 'array' });
    const sheet = workbook.Sheets[workbook.SheetNames[0]];
    expect(XLSX.utils.sheet_to_json(sheet)).toHaveLength(1);
    expect(sheet.A2).toMatchObject({ t: 's', v: '=اسم العميل' });
    expect(sheet.A2.f).toBeUndefined();
    expect(sheet.B2).toMatchObject({ t: 's', v: '201011111111' });
  });
});
