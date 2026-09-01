export function automationDateTime(utcDate: string, projectTimezone: string | null) {
  const timezone = projectTimezone || 'UTC';
  try {
    const formatted = new Intl.DateTimeFormat('ar-EG', {
      dateStyle: 'medium',
      timeStyle: 'short',
      timeZone: timezone,
    }).format(new Date(utcDate));
    return `${formatted} · ${timezone}`;
  } catch {
    const formatted = new Intl.DateTimeFormat('ar-EG', {
      dateStyle: 'medium',
      timeStyle: 'short',
      timeZone: 'UTC',
    }).format(new Date(utcDate));
    return `${formatted} · UTC`;
  }
}
