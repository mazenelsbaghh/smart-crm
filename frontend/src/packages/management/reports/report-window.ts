import type { ReportWindow } from './reports-api';

export const reportDateWindow = (fromDate: string, toDate: string): ReportWindow | null => {
  if (!fromDate || !toDate || fromDate > toDate) return null;
  const from = new Date(`${fromDate}T00:00:00`);
  const dayAfterTo = new Date(`${toDate}T00:00:00`);
  dayAfterTo.setDate(dayAfterTo.getDate() + 1);
  const to = new Date(Math.min(dayAfterTo.getTime(), Date.now()));
  if (from >= to) return null;
  return { fromUtc: from.toISOString(), toUtc: to.toISOString() };
};
