import type { ReportWindow } from './reports-api';

const dayMilliseconds = 86_400_000;

export const reportTimezone = (configuredTimezone: string | undefined): string => {
  const timezone = configuredTimezone?.trim() || 'Africa/Cairo';
  try {
    new Intl.DateTimeFormat('en', { timeZone: timezone });
    return timezone;
  } catch (error) {
    if (error instanceof RangeError) return 'Africa/Cairo';
    throw error;
  }
};

export const reportInputDate = (date: Date, timezone: string): string => {
  const parts = new Intl.DateTimeFormat('en', { timeZone: timezone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(date);
  return ['year', 'month', 'day'].map((part) => parts.find(({ type }) => type === part)!.value).join('-');
};

const calendarDate = (date: string): number | null => {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) return null;
  const timestamp = Date.parse(`${date}T00:00:00Z`);
  return Number.isFinite(timestamp) && new Date(timestamp).toISOString().startsWith(date) ? timestamp : null;
};

const startOfProjectDay = (date: number, timezone: string): number => {
  const target = new Date(date).toISOString().slice(0, 10);
  let before = date - dayMilliseconds;
  let after = date + dayMilliseconds;
  // Find the first instant of the date, including days whose midnight is skipped or repeated by DST.
  while (before < after) {
    const middle = Math.floor((before + after) / 2);
    if (reportInputDate(new Date(middle), timezone) < target) before = middle + 1;
    else after = middle;
  }
  return before;
};

export const reportDateWindow = (fromDate: string, toDate: string, timezone: string): ReportWindow | null => {
  const fromDay = calendarDate(fromDate);
  const toDay = calendarDate(toDate);
  if (fromDay === null || toDay === null || fromDay > toDay) return null;
  const from = startOfProjectDay(fromDay, timezone);
  const to = Math.min(startOfProjectDay(toDay + dayMilliseconds, timezone), Date.now());
  if (from >= to) return null;
  return { fromUtc: new Date(from).toISOString(), toUtc: new Date(to).toISOString() };
};
