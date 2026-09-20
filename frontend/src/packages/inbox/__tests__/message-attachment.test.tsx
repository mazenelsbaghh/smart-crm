import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { api } from '../../../services/api';
import MessageAttachment from '../shared/MessageAttachment';

beforeEach(() => {
  vi.stubGlobal('URL', class extends URL {
    static createObjectURL() { return 'blob:attachment'; }
    static revokeObjectURL() {}
  });
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
});
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

it.each([['Document', 'المستند'], ['Video', 'الفيديو']] as const)('ينزل مرفق %s المحفوظ عبر التطبيق', async (mediaType, label) => {
  vi.spyOn(api, 'get').mockResolvedValue({ data: new Blob(['attachment']), headers: { 'content-disposition': 'attachment; filename="saved-file.pdf"' } });
  render(<MessageAttachment assetId="saved-asset" mediaType={mediaType} />);
  await userEvent.click(screen.getByRole('button', { name: `تنزيل ${label}` }));
  const link = await screen.findByRole('link', { name: `تنزيل ${label} مرة أخرى` });
  expect(link).toHaveAttribute('href', 'blob:attachment');
  expect(link).toHaveAttribute('download', 'saved-file.pdf');
});

it('يتيح إعادة المحاولة عند فشل تنزيل المرفق', async () => {
  vi.spyOn(api, 'get').mockRejectedValueOnce(new Error('offline'))
    .mockResolvedValueOnce({ data: new Blob(['file']), headers: {} });
  render(<MessageAttachment assetId="saved-asset" mediaType="Document" />);
  await userEvent.click(screen.getByRole('button', { name: 'تنزيل المستند' }));
  await userEvent.click(await screen.findByRole('button', { name: 'إعادة المحاولة' }));
  expect(await screen.findByRole('link', { name: 'تنزيل المستند مرة أخرى' })).toHaveAttribute('download', 'attachment');
});

it('يوضح أن الملف لم يحفظ بدل إظهار رسالة نصية مضللة', () => {
  render(<MessageAttachment assetId={null} mediaType="Video" />);
  expect(screen.getByRole('status')).toHaveTextContent('تعذر حفظ الفيديو');
  expect(screen.queryByRole('link')).not.toBeInTheDocument();
});
