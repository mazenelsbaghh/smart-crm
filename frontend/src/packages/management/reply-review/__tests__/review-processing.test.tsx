import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/services/api';
import ReviewProcessingPanel from '../ReviewProcessingPanel';
import { processingFixture } from './fixtures';

afterEach(() => vi.restoreAllMocks());
const panel = (projectId = 'project-1', canManage = true) => <ReviewProcessingPanel projectId={projectId} timezone="Africa/Cairo" canManage={canManage} refreshSignal={0} />;

describe('Scheduled reply processing', () => {
  it('saves the cadence and draft setting and schedules work without sending customer messages', async () => {
    vi.spyOn(api, 'get').mockResolvedValue({ data: processingFixture });
    const put = vi.spyOn(api, 'put').mockResolvedValue({ data: processingFixture.schedule });
    const post = vi.spyOn(api, 'post').mockResolvedValue({ data: {} });
    render(panel());
    fireEvent.click(await screen.findByText('إعدادات الجدولة والمعالجة'));
    fireEvent.change(screen.getByLabelText('فحص المحادثات كل (دقيقة)'), { target: { value: '15' } });
    fireEvent.click(screen.getByRole('button', { name: 'حفظ الجدولة' }));
    expect(await screen.findByText('تم حفظ الجدولة. سيُلتقط الموعد التالي خلال دقيقة.')).toBeInTheDocument();
    expect(put).toHaveBeenCalledWith('/api/projects/project-1/reports/daily-review/processing/schedule', expect.objectContaining({ intervalMinutes: 15, prepareDrafts: true }));
    fireEvent.click(screen.getByRole('button', { name: 'جدولة فحص الآن' }));
    expect(await screen.findByText('تمت جدولة الدفعة التالية خلال دقيقة.')).toBeInTheDocument();
    expect(post.mock.calls.map(call => call[0])).toEqual(['/api/projects/project-1/reports/daily-review/processing/scan']);
  });

  it('shows attempt evidence and blocks a stale saved correction', async () => {
    vi.spyOn(api, 'get').mockImplementation(async url => ({ data: String(url).endsWith('/processing') ? processingFixture : {
      draftContent: 'مسودة قديمة قبل رسالة العميل الأخيرة', draftOutdated: true, draftGeneratedAtUtc: '2026-09-07T15:00:00Z',
      runs: [{ id: 'run-1', startedAtUtc: '2026-09-07T15:00:00Z', finishedAtUtc: '2026-09-07T15:01:00Z', outcome: 'NeedsHuman',
        attempt: 2, qualityScore: 25, summary: 'لم يُرصد رد فعلي يعالج المشكلة.', error: null }],
    } }));
    render(panel());
    fireEvent.click(await screen.findByRole('button', { name: 'المسودة وسجل المعالجة' }));
    expect(await screen.findByText('لم يُرصد رد فعلي يعالج المشكلة.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'نسخ المسودة بعد التحقق' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: /^إرسال/ })).not.toBeInTheDocument();
  });

  it('does not show manager actions to readers or accept an old project response', async () => {
    let finishOld!: (value: { data: typeof processingFixture }) => void;
    vi.spyOn(api, 'get').mockImplementation(async url => String(url).includes('project-1')
      ? new Promise(resolve => { finishOld = resolve; })
      : { data: { ...processingFixture, cases: [], total: 0 } });
    const { rerender } = render(panel('project-1', false));
    rerender(panel('project-2', false));
    await screen.findByText('لا توجد مهام في هذا الفلتر. الفحص المجدول يضيف المحادثات التي تحتاج مراجعة.');
    await act(async () => finishOld({ data: processingFixture }));
    expect(screen.queryByText('عميلة اختبار — سهيلة')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'جدولة فحص الآن' })).not.toBeInTheDocument();
    expect(screen.queryByText('إعدادات الجدولة والمعالجة')).not.toBeInTheDocument();
  });
});
