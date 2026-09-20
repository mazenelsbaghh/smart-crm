import type { FollowUpDispatchOptions, FollowUpPlanAction } from './types';

export function followUpScheduleError(options: FollowUpDispatchOptions, available: number, action: FollowUpPlanAction) {
  if (!Number.isInteger(options.count) || options.count < 1 || options.count > Math.min(available, 10_000))
    return `اختر عددًا بين 1 و${Math.min(available, 10_000)}.`;
  if (!Number.isInteger(options.minIntervalSeconds) || !Number.isInteger(options.maxIntervalSeconds)
    || options.minIntervalSeconds < 1 || options.maxIntervalSeconds > 3600
    || options.maxIntervalSeconds < options.minIntervalSeconds)
    return 'الفاصل بين 1 و3600 ثانية، والحد الأقصى لا يقل عن الحد الأدنى.';
  if (action === 'SendNow') return '';
  if (!Number.isInteger(options.scheduleDays) || options.scheduleDays < 1 || options.scheduleDays > Math.min(options.count, 365))
    return 'عدد الأيام بين 1 و365، ولا يزيد عن عدد الرسائل.';
  if ((Math.ceil(options.count / options.scheduleDays) - 1) * options.maxIntervalSeconds >= 23 * 3600)
    return 'الدفعة اليومية طويلة. زوّد عدد الأيام أو قلّل العدد أو الفاصل.';
  return '';
}

export function followUpDayCounts(count: number, days: number) {
  return Array.from({ length: days }, (_, index) => Math.floor(count / days) + (index < count % days ? 1 : 0));
}

export function followUpDuration(seconds: number) {
  if (seconds === 0) return 'فور بدء الإرسال';
  const units = [
    [Math.floor(seconds / 86_400), 'يوم'],
    [Math.floor(seconds % 86_400 / 3600), 'ساعة'],
    [Math.floor(seconds % 3600 / 60), 'دقيقة'],
    [seconds % 60, 'ثانية'],
  ] as const;
  return units.filter(([count]) => count > 0).slice(0, 3)
    .map(([count, unit]) => `${count.toLocaleString('ar-EG')} ${unit}`).join(' و');
}
