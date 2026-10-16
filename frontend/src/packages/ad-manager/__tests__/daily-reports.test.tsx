import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { adManagerApi } from '../api/ad-manager-api';
import { DailyReportsView } from '../components/DailyReportsView';
import type { DailyAdvertisingReport } from '../types';

const report = (date: string, name: string): DailyAdvertisingReport => ({
  date, timezone: 'Africa/Cairo', currency: 'EGP', startUtc: `${date}T00:00:00Z`, endUtc: `${date}T23:59:00Z`,
  totals: { entrants: 1, qualified: 1, bookings: 0, spend: 50 },
  rows: [{ id: name, name, entrants: 1, qualified: 1, bookings: 0, spend: 50 }],
  unattributed: { entrants: 0, qualified: 0, bookings: 0 },
});

describe('daily advertising report selection', () => {
  it('distinguishes a reported zero from unavailable ad spend in a partial report', () => {
    const partialReport = report('2026-10-16', 'صرف موثق');
    partialReport.spendAvailable = false;
    partialReport.rows = [
      { ...partialReport.rows[0], spend: 0, spendAvailable: true, costPerQualified: 0 },
      { ...partialReport.rows[0], id: 'missing', name: 'صرف ناقص', spend: 0, spendAvailable: false },
    ];
    render(<DailyReportsView projectId="project-a" initial={partialReport} />);

    expect(within(screen.getByRole('row', { name: /صرف ناقص/ })).getByText('غير متاح')).toBeVisible();
    expect(within(screen.getByRole('row', { name: /صرف موثق/ })).queryByText('غير متاح')).not.toBeInTheDocument();
    expect(within(screen.getByRole('row', { name: /صرف موثق/ })).getAllByText(/٠.*ج/)).toHaveLength(2);
  });

  it.each(['success', 'failure'])('keeps the latest selected day when an older request completes with %s', async outcome => {
    let resolveOld!: (value: DailyAdvertisingReport) => void;
    let rejectOld!: (reason: Error) => void;
    const oldRequest = new Promise<DailyAdvertisingReport>((resolve, reject) => { resolveOld = resolve; rejectOld = reject; });
    vi.spyOn(adManagerApi, 'dailyReport').mockImplementation(async (_project, date) =>
      date === '2026-10-14' ? oldRequest : report('2026-10-15', 'الإعلان الأحدث'));
    render(<DailyReportsView projectId="project-a" initial={report('2026-10-16', 'التقرير الأول')} />);
    const dateInput = screen.getByLabelText(/اليوم المختار/);
    fireEvent.change(dateInput, { target: { value: '2026-10-14' } });
    fireEvent.change(dateInput, { target: { value: '2026-10-15' } });
    expect(await screen.findByText('الإعلان الأحدث')).toBeVisible();

    await act(async () => {
      if (outcome === 'success') resolveOld(report('2026-10-14', 'الإعلان المتأخر'));
      else rejectOld(new Error('old request failed'));
      await oldRequest.catch(() => undefined);
    });

    expect(dateInput).toHaveValue('2026-10-15');
    expect(screen.getByText('الإعلان الأحدث')).toBeVisible();
    expect(screen.queryByText('الإعلان المتأخر')).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
