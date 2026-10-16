import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { reportDateWindow, reportInputDate, reportTimezone } from '../reports/report-window';

describe('project calendar report windows', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime('2026-12-01T12:00:00Z');
  });
  afterEach(() => vi.useRealTimers());

  it('uses the same Cairo fallback as the API for absent or invalid project timezones', () => {
    for (const configured of [undefined, '', 'invalid/timezone']) {
      expect(reportTimezone(configured)).toBe('Africa/Cairo');
    }
    expect(reportTimezone('America/Los_Angeles')).toBe('America/Los_Angeles');
  });

  it.each([
    ['2026-08-28', 'Africa/Cairo', '2026-08-27T21:00:00.000Z', '2026-08-28T21:00:00.000Z'],
    ['2026-01-15', 'Africa/Cairo', '2026-01-14T22:00:00.000Z', '2026-01-15T22:00:00.000Z'],
    ['2026-04-24', 'Africa/Cairo', '2026-04-23T22:00:00.000Z', '2026-04-24T21:00:00.000Z'],
    ['2026-10-29', 'Africa/Cairo', '2026-10-28T21:00:00.000Z', '2026-10-29T22:00:00.000Z'],
    ['2026-01-15', 'America/Los_Angeles', '2026-01-15T08:00:00.000Z', '2026-01-16T08:00:00.000Z'],
    ['2026-01-15', 'Asia/Kolkata', '2026-01-14T18:30:00.000Z', '2026-01-15T18:30:00.000Z'],
  ])('uses the complete %s day in %s including timezone transitions', (day, timezone, fromUtc, toUtc) => {
    expect(reportDateWindow(day, day, timezone)).toEqual({ fromUtc, toUtc });
  });

  it('caps the current project day at now and rejects future or invalid days', () => {
    vi.setSystemTime('2026-08-28T22:30:00Z');
    expect(reportInputDate(new Date(), 'Africa/Cairo')).toBe('2026-08-29');
    expect(reportDateWindow('2026-08-29', '2026-08-29', 'Africa/Cairo')).toEqual({
      fromUtc: '2026-08-28T21:00:00.000Z', toUtc: '2026-08-28T22:30:00.000Z',
    });
    for (const day of ['2026-08-30', '2026-02-30', 'invalid', '']) {
      expect(reportDateWindow(day, day, 'Africa/Cairo')).toBeNull();
    }
    expect(reportDateWindow('2026-08-28', '2026-08-27', 'Africa/Cairo')).toBeNull();
  });
});
