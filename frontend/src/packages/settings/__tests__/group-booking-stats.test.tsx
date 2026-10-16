import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { api } from '../../../services/api';
import GroupBookingStats from '../GroupBookingStats';

const originalAdapter = api.defaults.adapter;
const initialProps = { projectId: 'project-a', courseType: 'regular' as const, refreshKey: 0 };
const snapshot = (bookedToday: number, totalBooked: number, courseType = 'regular') => ({
  bookedToday, totalBooked, courseType, timezone: 'Africa/Cairo',
  dayStartUtc: '2026-10-15T21:00:00Z', dayEndUtc: '2026-10-16T21:00:00Z', asOfUtc: '2026-10-16T12:00:00Z',
});

interface PendingRequest {
  config: InternalAxiosRequestConfig;
  complete: (data: ReturnType<typeof snapshot>) => void;
  fail: () => void;
}

function holdRequests() {
  const requests: PendingRequest[] = [];
  api.defaults.adapter = config => new Promise<AxiosResponse>((resolve, reject) => {
    if (config.url !== '/api/group-appointments/booking-stats') throw new Error(`Unexpected request: ${config.url}`);
    requests.push({ config,
      complete: data => resolve({ config, data, status: 200, statusText: 'OK', headers: {} }),
      fail: () => reject(new AxiosError('Unavailable', 'ERR_BAD_RESPONSE', config, undefined, {
        config, data: {}, status: 503, statusText: 'Service Unavailable', headers: {},
      })),
    });
  });
  return requests;
}

function expectCounts(today: string, total: string, courseLabel = 'العادي') {
  expect(within(screen.getByRole('group', { name: `حجوزات ${courseLabel} اليوم` })).getByText(today)).toBeVisible();
  expect(within(screen.getByRole('group', { name: `إجمالي حجوزات ${courseLabel}` })).getByText(total)).toBeVisible();
}

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('accessToken', 'owner-access-token');
  localStorage.setItem('user', JSON.stringify({ id: 'owner', email: 'owner@example.test', role: 'Owner' }));
  localStorage.setItem('activeProject', JSON.stringify({ id: initialProps.projectId }));
});

afterEach(() => {
  api.defaults.adapter = originalAdapter;
  localStorage.clear();
});

describe('group booking statistics', () => {
  it.each([
    { today: 4, total: 120, shownToday: '٤', shownTotal: '١٢٠' },
    { today: 0, total: 0, shownToday: '٠', shownTotal: '٠' },
  ])('waits for server counts before displaying $today today and $total total', async ({ today, total, shownToday, shownTotal }) => {
    const requests = holdRequests();
    render(<GroupBookingStats {...initialProps} />);
    await waitFor(() => expect(requests).toHaveLength(1));

    expect(screen.getByRole('status')).toHaveTextContent('جاري تحميل');
    expect(screen.queryByText('٠')).not.toBeInTheDocument();
    expect(requests[0].config.params).toEqual({ courseType: 'regular' });
    expect(requests[0].config.headers.get('Authorization')).toBe('Bearer owner-access-token');
    await act(async () => requests[0].complete(snapshot(today, total)));

    expectCounts(shownToday, shownTotal);
    expect(screen.getByText('يشمل الحجز الجديد والنقل بين المجموعات')).toBeVisible();
    expect(screen.getByRole('status')).toHaveTextContent('اليوم بتوقيت القاهرة');
  });

  it('shows unavailable counts after a failed first load and recovers on retry', async () => {
    const requests = holdRequests();
    render(<GroupBookingStats {...initialProps} />);
    await waitFor(() => expect(requests).toHaveLength(1));
    await act(async () => requests[0].fail());

    expect(screen.getByRole('alert')).toHaveTextContent('تعذر تحميل إحصائيات الحجوزات');
    expect(screen.getAllByText('غير متاح')).toHaveLength(2);
    expect(screen.queryByText('٠')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'إعادة المحاولة' }));
    await waitFor(() => expect(requests).toHaveLength(2));
    await act(async () => requests[1].complete(snapshot(2, 17)));

    expectCounts('٢', '١٧');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('keeps and labels the last successful counts when a refresh fails', async () => {
    const requests = holdRequests();
    const view = render(<GroupBookingStats {...initialProps} />);
    await waitFor(() => expect(requests).toHaveLength(1));
    await act(async () => requests[0].complete(snapshot(4, 120)));
    view.rerender(<GroupBookingStats {...initialProps} refreshKey={1} />);
    await waitFor(() => expect(requests).toHaveLength(2));

    expectCounts('٤', '١٢٠');
    expect(screen.getByRole('status')).toHaveTextContent('جاري التحديث');
    await act(async () => requests[1].fail());

    expectCounts('٤', '١٢٠');
    expect(screen.getByRole('alert')).toHaveTextContent('المعروض آخر بيانات متاحة');
    expect(screen.getByRole('alert').querySelector('time')).toHaveAttribute('datetime', snapshot(4, 120).asOfUtc);
    expect(screen.getByRole('button', { name: 'إعادة المحاولة' })).toBeEnabled();
  });

  it.each([
    { scope: 'project', props: { projectId: 'project-b', courseType: 'regular' as const, refreshKey: 1 } },
    { scope: 'course', props: { projectId: 'project-a', courseType: 'intensive' as const, refreshKey: 1 } },
    { scope: 'refresh', props: { projectId: 'project-a', courseType: 'regular' as const, refreshKey: 2 } },
  ])('discards an older in-flight response after the $scope changes', async ({ scope, props }) => {
    const requests = holdRequests();
    const view = render(<GroupBookingStats {...initialProps} />);
    await waitFor(() => expect(requests).toHaveLength(1));
    await act(async () => requests[0].complete(snapshot(11, 77)));
    view.rerender(<GroupBookingStats {...initialProps} refreshKey={1} />);
    await waitFor(() => expect(requests).toHaveLength(2));

    localStorage.setItem('activeProject', JSON.stringify({ id: props.projectId }));
    view.rerender(<GroupBookingStats {...props} />);
    if (scope !== 'refresh') {
      expect(screen.queryByText('٧٧')).not.toBeInTheDocument();
      expect(screen.queryByText('١١')).not.toBeInTheDocument();
    }
    await waitFor(() => expect(requests).toHaveLength(3));
    expect(requests[1].config.signal?.aborted).toBe(true);
    expect(requests[2].config.params).toEqual({ courseType: props.courseType });
    expect(requests[2].config.headers.get('X-Project-Id')).toBe(props.projectId);
    await act(async () => requests[2].complete(snapshot(3, 25, props.courseType)));
    await act(async () => requests[1].complete(snapshot(99, 999)));

    expectCounts('٣', '٢٥', props.courseType === 'regular' ? 'العادي' : 'المكثف');
    expect(screen.queryByText('٩٩٩')).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
