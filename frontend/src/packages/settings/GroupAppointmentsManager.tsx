'use client';

import React, { useEffect, useState, useCallback, useRef } from 'react';
import { api } from '../../services/api';
import { useAuth } from '../../context/auth-context';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import { 
  Calendar, 
  Plus, 
  Trash2, 
  Edit3, 
  Users, 
  ArrowRight,
  Clock,
  Download,
  FileDown,
  Search,
  ShieldAlert,
  Upload,
  UserCheck
} from 'lucide-react';
import styles from './settings.module.css';

interface Booking {
  id: string;
  customerName: string;
  customerPhone: string;
  customerId: string;
  createdAt: string;
  isAttended: boolean;
  isPaid: boolean;
}

interface ManualBookingResponse {
  message: string;
  booking: Booking;
  group: {
    id: string;
    name: string;
    capacity: number;
    bookedCount: number;
    slotsLeft: number;
    isFull: boolean;
  };
}

interface ManualBookingForm {
  customerName: string;
  customerPhone: string;
  notes: string;
  isPaid: boolean;
  isAttended: boolean;
}

type ManualBookingFieldErrors = Partial<Record<'customerName' | 'customerPhone', string>>;

interface ManualBookingPayload {
  customerName: string;
  customerPhone: string;
  notes?: string;
  isPaid: boolean;
  isAttended: boolean;
}

const EMPTY_MANUAL_BOOKING: ManualBookingForm = {
  customerName: '',
  customerPhone: '',
  notes: '',
  isPaid: false,
  isAttended: false,
};

const normalizeManualBookingPhone = (rawPhone: string) => {
  const latinDigits = rawPhone
    .replace(/[٠-٩]/g, digit => String(digit.charCodeAt(0) - 1632))
    .replace(/[۰-۹]/g, digit => String(digit.charCodeAt(0) - 1776));

  if (/[^\d\s()+-]/.test(latinDigits)) return null;

  let digits = latinDigits.replace(/[\s()-]/g, '');
  if (digits.startsWith('+')) digits = digits.slice(1);
  else if (digits.startsWith('00')) digits = digits.slice(2);
  if (/^01\d{9}$/.test(digits)) digits = `2${digits}`;
  else if (/^1\d{9}$/.test(digits)) digits = `20${digits}`;

  return /^[1-9]\d{6,14}$/.test(digits) ? digits : null;
};

const validateManualBooking = (booking: ManualBookingForm) => {
  const customerName = booking.customerName.trim();
  const customerPhone = normalizeManualBookingPhone(booking.customerPhone);
  const fieldErrors: ManualBookingFieldErrors = {};

  if (!customerName) fieldErrors.customerName = 'اكتب اسم المشترك.';
  else if (customerName.length > 120) fieldErrors.customerName = 'الاسم يجب ألا يزيد عن 120 حرفًا.';
  if (!booking.customerPhone.trim()) fieldErrors.customerPhone = 'اكتب رقم الهاتف.';
  else if (booking.customerPhone.length > 64 || !customerPhone) {
    fieldErrors.customerPhone = 'اكتب رقمًا صحيحًا من 7 إلى 15 رقمًا، محليًا أو دوليًا.';
  }

  const payload: ManualBookingPayload | null = Object.keys(fieldErrors).length > 0 || !customerPhone
    ? null
    : { customerName, customerPhone, notes: booking.notes.trim() || undefined, isPaid: booking.isPaid, isAttended: booking.isAttended };
  return { fieldErrors, payload };
};

const mergeManualBookingIntoGroup = (group: GroupAppointment, response: ManualBookingResponse): GroupAppointment => ({
  ...group,
  name: response.group.name || group.name,
  capacity: response.group.capacity,
  bookedCount: response.group.bookedCount,
  bookings: [response.booking, ...group.bookings.filter(booking => booking.id !== response.booking.id)],
});

const manualBookingErrorMessage = (error: unknown) => {
  const response = (error as { response?: { status?: number; data?: { error?: unknown; message?: unknown; code?: unknown } } })?.response;
  const isExpectedClientError = response?.status !== undefined && response.status >= 400 && response.status < 500;
  const errorMessage = typeof response?.data?.error === 'string' ? response.data.error.trim() : '';
  const responseMessage = typeof response?.data?.message === 'string' ? response.data.message.trim() : '';
  const serverMessage = errorMessage || responseMessage;
  if (isExpectedClientError && serverMessage) return serverMessage;

  const code = typeof response?.data?.code === 'string' ? response.data.code : '';
  const codeMessages: Record<string, string> = {
    PHONE_INVALID: 'رقم الهاتف غير صالح. اكتب رقمًا دوليًا من 7 إلى 15 رقمًا.',
    MANUAL_BOOKING_INVALID: 'راجع الاسم ورقم الهاتف وحدود الملاحظة ثم حاول مرة أخرى.',
    GROUP_NOT_FOUND: 'المجموعة لم تعد موجودة. حدّث الصفحة واختر مجموعة أخرى.',
    GROUP_INACTIVE: 'لا يمكن إضافة مشترك إلى مجموعة غير نشطة.',
    GROUP_FULL: 'المجموعة ممتلئة. زوّد السعة أو اختر مجموعة أخرى.',
    BOOKING_ALREADY_EXISTS: 'رقم الهاتف مسجل بالفعل في إحدى المجموعات.',
  };
  if (isExpectedClientError && codeMessages[code]) return codeMessages[code];
  if (response?.status === 403) return 'ليس لديك صلاحية لإضافة مشترك يدويًا.';

  return 'تعذر إضافة المشترك الآن. احتفظنا بالبيانات لتجربة الحفظ مرة أخرى.';
};

interface InstructorsResponse {
  instructors: string[];
}

interface PaidBlacklistImportResult {
  matchedCount: number;
  newCount: number;
  removedBookingsCount: number;
  cancelledFollowUpsCount: number;
  blacklistGroupName: string;
  matchedPhones: string[];
  newPhones: string[];
}

interface GroupAppointment {
  id: string;
  name: string;
  dateTime: string;
  freeSessionDateTime?: string | null;
  courseSecondDateTime?: string | null;
  capacity: number;
  isActive: boolean;
  days: string;
  bookedCount: number;
  bookings: Booking[];
  mode: string;
  instructorName?: string;
}

interface GroupAppointmentsManagerProps {
  onBack: () => void;
  timezone: string;
}

const zonedParts = (date: Date, timezone: string) => Object.fromEntries(
  new Intl.DateTimeFormat('en-CA', {
    timeZone: timezone, year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
  }).formatToParts(date).filter(part => part.type !== 'literal').map(part => [part.type, part.value]),
);

const timezoneOffsetAt = (timestamp: number, timezone: string) => {
  const parts = zonedParts(new Date(timestamp), timezone);
  const representedAsUtc = Date.UTC(Number(parts.year), Number(parts.month) - 1, Number(parts.day), Number(parts.hour), Number(parts.minute), Number(parts.second));
  return representedAsUtc - Math.floor(timestamp / 1000) * 1000;
};

const projectLocalToUtc = (value: string, timezone: string) => {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value);
  if (!match) throw new Error('INVALID_LOCAL_DATE');
  const [, year, month, day, hour, minute] = match;
  const wallClockUtc = Date.UTC(Number(year), Number(month) - 1, Number(day), Number(hour), Number(minute));
  const offsetSamples = new Set([
    timezoneOffsetAt(wallClockUtc - 12 * 60 * 60 * 1000, timezone),
    timezoneOffsetAt(wallClockUtc, timezone),
    timezoneOffsetAt(wallClockUtc + 12 * 60 * 60 * 1000, timezone),
  ]);
  const matchingInstants = [...offsetSamples]
    .map((offset) => wallClockUtc - offset)
    .filter((utc) => {
      const resolved = zonedParts(new Date(utc), timezone);
      return `${resolved.year}-${resolved.month}-${resolved.day}T${resolved.hour}:${resolved.minute}` === value;
    });
  if (matchingInstants.length === 0) throw new Error('INVALID_LOCAL_DATE');
  if (new Set(matchingInstants).size > 1) throw new Error('AMBIGUOUS_LOCAL_DATE');
  return new Date(matchingInstants[0]).toISOString();
};

const projectDateTimeInput = (isoString: string, timezone: string) => {
  const parts = zonedParts(new Date(isoString), timezone);
  return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}`;
};

const validTimezone = (timezone: string) => {
  try { new Intl.DateTimeFormat('en', { timeZone: timezone }).format(); return timezone; }
  catch { return 'Africa/Cairo'; }
};

export default function GroupAppointmentsManager({ onBack, timezone }: GroupAppointmentsManagerProps) {
  const { activeProject } = useAuth();
  const projectTimezone = validTimezone(timezone);
  const [groups, setGroups] = useState<GroupAppointment[]>([]);
  const [loading, setLoading] = useState(true);
  const [actionLoading, setActionLoading] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  
  // Modal states
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingGroupId, setEditingGroupId] = useState<string | null>(null);
  const [selectedGroup, setSelectedGroup] = useState<GroupAppointment | null>(null);
  const [pendingDeleteBookingId, setPendingDeleteBookingId] = useState<string | null>(null);
  const [deletingBookingId, setDeletingBookingId] = useState<string | null>(null);
  
  // Confirmation state for deleting group
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);
  const [groupToDelete, setGroupToDelete] = useState<string | null>(null);
  
  // Form states
  const [mode, setMode] = useState<string>('offline');
  const [dateTime, setDateTime] = useState('');
  const [freeSessionDateTime, setFreeSessionDateTime] = useState('');
  const [courseSecondDateTime, setCourseSecondDateTime] = useState('');
  const [capacity, setCapacity] = useState(5);
  const [isActive, setIsActive] = useState(true);
  const [selectedDays, setSelectedDays] = useState<number[]>([]);
  const [instructors, setInstructors] = useState<string[]>([]);
  const [instructorsText, setInstructorsText] = useState('');
  const [selectedInstructor, setSelectedInstructor] = useState('');
  const [savingInstructors, setSavingInstructors] = useState(false);
  const [paidImportFileName, setPaidImportFileName] = useState('');
  const [paidImportPhones, setPaidImportPhones] = useState<string[]>([]);
  const [paidImporting, setPaidImporting] = useState(false);
  const [paidImportConfirmOpen, setPaidImportConfirmOpen] = useState(false);
  const [paidImportResult, setPaidImportResult] = useState<PaidBlacklistImportResult | null>(null);
  
  const [searchQuery, setSearchQuery] = useState('');
  const [manualBookingOpen, setManualBookingOpen] = useState(false);
  const [manualBooking, setManualBooking] = useState<ManualBookingForm>(EMPTY_MANUAL_BOOKING);
  const [manualBookingErrors, setManualBookingErrors] = useState<ManualBookingFieldErrors>({});
  const [manualBookingSubmitError, setManualBookingSubmitError] = useState('');
  const [manualBookingSubmitting, setManualBookingSubmitting] = useState(false);
  const manualBookingToggleRef = useRef<HTMLButtonElement>(null);
  const manualBookingNameRef = useRef<HTMLInputElement>(null);
  const subscribersHeadingRef = useRef<HTMLHeadingElement>(null);
  const editorRef = useRef<HTMLDivElement>(null);
  const editorCloseRef = useRef<HTMLButtonElement>(null);

  const DAY_NAMES = ['الأحد', 'الاثنين', 'الثلاثاء', 'الأربعاء', 'الخميس', 'الجمعة', 'السبت'];
  const DAY_NAMES_SHORT = ['أحد', 'اثنين', 'ثلاثاء', 'أربعاء', 'خميس', 'جمعة', 'سبت'];

  const closeEditor = useCallback(() => setIsModalOpen(false), []);

  useEffect(() => {
    if (!isModalOpen) return;
    const previouslyFocused = document.activeElement as HTMLElement | null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    const focusTimer = window.setTimeout(() => editorCloseRef.current?.focus(), 0);
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { event.preventDefault(); closeEditor(); return; }
      if (event.key !== 'Tab') return;
      const focusable = Array.from(editorRef.current?.querySelectorAll<HTMLElement>(
        'button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [href], [tabindex]:not([tabindex="-1"])',
      ) ?? []);
      if (!focusable.length) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      window.clearTimeout(focusTimer);
      document.removeEventListener('keydown', handleKeyDown);
      document.body.style.overflow = previousOverflow;
      previouslyFocused?.focus();
    };
  }, [closeEditor, isModalOpen]);

  const fetchGroups = useCallback(async () => {
    if (!activeProject) return;
    try {
      setLoading(true);
      const response = await api.get<GroupAppointment[]>('/api/group-appointments');
      setGroups(response.data);
      setSelectedGroup(current => current
        ? response.data.find(group => group.id === current.id) ?? null
        : null);
    } catch (e) {
      console.error(e);
      setMessage({ type: 'error', text: 'فشل تحميل مجموعات المواعيد.' });
    } finally {
      setLoading(false);
    }
  }, [activeProject]);

  const fetchInstructors = useCallback(async () => {
    if (!activeProject) return;
    try {
      const response = await api.get<InstructorsResponse>('/api/group-appointments/instructors');
      const nextInstructors = response.data.instructors || [];
      setInstructors(nextInstructors);
      setInstructorsText(nextInstructors.join('\n'));
    } catch (e) {
      console.error(e);
      setMessage({ type: 'error', text: 'فشل تحميل أسماء الإنستراكتورز.' });
    }
  }, [activeProject]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    fetchGroups();
    void fetchInstructors();
  }, [fetchGroups, fetchInstructors]);

  const handleSaveGroup = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!dateTime || !freeSessionDateTime || !courseSecondDateTime || capacity <= 0) return;
    if (selectedDays.length !== 2) {
      setMessage({ type: 'error', text: 'اختار يومين بالضبط لمواعيد الكورس الأسبوعية.' });
      return;
    }
    if (!selectedInstructor.trim()) {
      setMessage({ type: 'error', text: 'اختار اسم الإنستراكتور المسؤول عن المجموعة.' });
      return;
    }

    try {
      setActionLoading(true);
      setMessage(null);

      const utcDate = projectLocalToUtc(dateTime, projectTimezone);
      const freeSessionUtcDate = projectLocalToUtc(freeSessionDateTime, projectTimezone);
      const courseSecondUtcDate = projectLocalToUtc(courseSecondDateTime, projectTimezone);

      const payload = {
        dateTime: utcDate,
        freeSessionDateTime: freeSessionUtcDate,
        courseSecondDateTime: courseSecondUtcDate,
        capacity,
        isActive,
        days: selectedDays.join(','),
        mode,
        instructorName: selectedInstructor.trim()
      };

      if (editingGroupId) {
        await api.put(`/api/group-appointments/${editingGroupId}`, payload);
        setMessage({ type: 'success', text: 'تم تحديث المجموعة بنجاح.' });
      } else {
        await api.post('/api/group-appointments', payload);
        setMessage({ type: 'success', text: 'تمت إضافة المجموعة بنجاح.' });
      }

      setIsModalOpen(false);
      setDateTime('');
      setFreeSessionDateTime('');
      setCourseSecondDateTime('');
      setCapacity(5);
      setIsActive(true);
      setSelectedDays([]);
      setMode('offline');
      setSelectedInstructor('');
      setEditingGroupId(null);
      void fetchGroups();
    } catch (e) {
      console.error(e);
      setMessage({ type: 'error', text: e instanceof Error && e.message === 'INVALID_LOCAL_DATE'
        ? `هذا الوقت غير موجود في المنطقة الزمنية ${projectTimezone} بسبب تغيير التوقيت. اختر وقتًا آخر.`
        : e instanceof Error && e.message === 'AMBIGUOUS_LOCAL_DATE'
          ? `هذا الوقت يتكرر مرتين في المنطقة الزمنية ${projectTimezone} بسبب تغيير التوقيت. اختر وقتًا أوضح قبله أو بعده.`
          : 'حدث خطأ أثناء حفظ المجموعة.' });
    } finally {
      setActionLoading(false);
    }
  };

  const handleStartEdit = (group: GroupAppointment) => {
    if (manualBookingSubmitting) return;
    setEditingGroupId(group.id);
    setMode(group.mode || 'offline');
    setDateTime(projectDateTimeInput(group.dateTime, projectTimezone));
    setFreeSessionDateTime(group.freeSessionDateTime ? projectDateTimeInput(group.freeSessionDateTime, projectTimezone) : '');
    setCourseSecondDateTime(group.courseSecondDateTime ? projectDateTimeInput(group.courseSecondDateTime, projectTimezone) : '');
    setCapacity(group.capacity);
    setIsActive(group.isActive);
    setSelectedDays(group.days ? group.days.split(',').filter(Boolean).map(Number) : []);
    setSelectedInstructor(group.instructorName || '');
    setIsModalOpen(true);
  };

  const handleBack = () => {
    if (manualBookingSubmitting) return;
    onBack();
  };

  const openNewGroupEditor = () => {
    if (manualBookingSubmitting) return;
    setEditingGroupId(null);
    setMode('offline');
    setDateTime('');
    setFreeSessionDateTime('');
    setCourseSecondDateTime('');
    setCapacity(5);
    setIsActive(true);
    setIsModalOpen(true);
    setSelectedDays([]);
    setSelectedInstructor(instructors[0] || '');
  };

  const handleSaveInstructors = async () => {
    const nextInstructors = instructorsText
      .split(/\n|,|;/)
      .map(item => item.trim())
      .filter(Boolean);

    try {
      setSavingInstructors(true);
      const response = await api.put<InstructorsResponse>('/api/group-appointments/instructors', {
        instructors: nextInstructors
      });
      const savedInstructors = response.data.instructors || [];
      setInstructors(savedInstructors);
      setInstructorsText(savedInstructors.join('\n'));
      if (selectedInstructor && !savedInstructors.includes(selectedInstructor)) {
        setSelectedInstructor('');
      }
      setMessage({ type: 'success', text: 'تم حفظ أسماء الإنستراكتورز.' });
    } catch (e) {
      console.error(e);
      setMessage({ type: 'error', text: 'فشل حفظ أسماء الإنستراكتورز.' });
    } finally {
      setSavingInstructors(false);
    }
  };

  const handleDownloadPaidTemplate = async () => {
    const XLSX = await import('xlsx');
    const worksheet = XLSX.utils.aoa_to_sheet([
      ['رقم الهاتف']
    ]);
    const workbook = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(workbook, worksheet, 'أرقام الطلاب المدفوعة');
    const excelBuffer = XLSX.write(workbook, { bookType: 'xlsx', type: 'array' });
    const blob = new Blob([excelBuffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.setAttribute('href', url);
    link.setAttribute('download', 'paid_students_blacklist_template.xlsx');
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  };

  const handlePaidImportFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    if (manualBookingSubmitting) {
      e.target.value = '';
      return;
    }
    const file = e.target.files?.[0];
    if (!file) return;

    try {
      setPaidImportResult(null);
      setPaidImportFileName(file.name);
      const binaryContent = await readFileAsBinaryString(file);
      const XLSX = await import('xlsx');
      const workbook = XLSX.read(binaryContent, { type: 'binary' });
      const worksheet = workbook.Sheets[workbook.SheetNames[0]];
      const rows = XLSX.utils.sheet_to_json<Record<string, unknown>>(worksheet);
      const phones = rows
        .map(row => {
          const phone = row['رقم الهاتف'] || row.Phone || row.phone || row['الرقم'] || Object.values(row)[0] || '';
          return String(phone).trim();
        })
        .filter(Boolean);

      if (phones.length === 0) {
        setPaidImportPhones([]);
        setMessage({ type: 'error', text: 'لم يتم العثور على أرقام هواتف داخل ملف Excel.' });
        return;
      }

      setPaidImportPhones(phones);
      setMessage({ type: 'success', text: `تم قراءة ${phones.length} رقم من الملف. اضغط تأكيد لتنفيذ الحظر.` });
    } catch (e) {
      console.error(e);
      setPaidImportPhones([]);
      setPaidImportFileName('');
      setMessage({ type: 'error', text: 'فشل قراءة ملف Excel. تأكد من صيغة الملف.' });
    } finally {
      e.target.value = '';
    }
  };

  const openPaidImportConfirmation = () => {
    if (manualBookingSubmitting || paidImporting || paidImportPhones.length === 0) return;
    setPaidImportConfirmOpen(true);
  };

  const handleConfirmPaidImport = async () => {
    if (manualBookingSubmitting || paidImporting || !activeProject || paidImportPhones.length === 0) return;

    try {
      setPaidImportConfirmOpen(false);
      setPaidImporting(true);
      setMessage(null);
      const response = await api.post<PaidBlacklistImportResult>(
        `/api/projects/${activeProject.id}/import-blacklist`,
        paidImportPhones
      );
      setPaidImportResult(response.data);
      setPaidImportPhones([]);
      setPaidImportFileName('');
      setSelectedGroup(null);
      setMessage({
        type: 'success',
        text: `تم الحظر في مجموعة ${response.data.blacklistGroupName} وحذف ${response.data.removedBookingsCount} حجز وإلغاء ${response.data.cancelledFollowUpsCount} متابعة.`
      });
      void fetchGroups();
    } catch (e) {
      console.error(e);
      setMessage({ type: 'error', text: 'فشل تنفيذ استيراد المحظورين للدفع.' });
    } finally {
      setPaidImporting(false);
    }
  };

  const triggerDeleteGroup = (id: string) => {
    if (manualBookingSubmitting) return;
    setGroupToDelete(id);
    setConfirmDeleteOpen(true);
  };

  const handleConfirmDeleteGroup = async () => {
    if (!groupToDelete) return;
    const id = groupToDelete;
    setConfirmDeleteOpen(false);
    setGroupToDelete(null);

    try {
      setActionLoading(true);
      await api.delete(`/api/group-appointments/${id}`);
      setMessage({ type: 'success', text: 'تم حذف المجموعة بنجاح.' });
      if (selectedGroup?.id === id) {
        setSelectedGroup(null);
      }
      void fetchGroups();
    } catch (e) {
      console.error(e);
      setMessage({ type: 'error', text: 'فشل حذف المجموعة.' });
    } finally {
      setActionLoading(false);
    }
  };

  const resetManualBookingEditor = () => {
    setManualBooking(EMPTY_MANUAL_BOOKING);
    setManualBookingErrors({});
    setManualBookingSubmitError('');
  };

  const handleSelectGroup = (group: GroupAppointment) => {
    if (manualBookingSubmitting) return;
    setSelectedGroup(group);
    setSearchQuery('');
    setManualBookingOpen(false);
    resetManualBookingEditor();
  };

  const closeSelectedGroup = () => {
    setSelectedGroup(null);
    setSearchQuery('');
    setManualBookingOpen(false);
    resetManualBookingEditor();
  };

  const openManualBookingEditor = () => {
    if (!selectedGroup || !selectedGroup.isActive || selectedGroup.bookedCount >= selectedGroup.capacity) return;
    setManualBookingOpen(true);
    setManualBookingErrors({});
    setManualBookingSubmitError('');
    setMessage(null);
    window.setTimeout(() => manualBookingNameRef.current?.focus(), 0);
  };

  const closeManualBookingEditor = () => {
    if (manualBookingSubmitting) return;
    setManualBookingOpen(false);
    resetManualBookingEditor();
    window.setTimeout(() => manualBookingToggleRef.current?.focus(), 0);
  };

  const applyManualBookingCreation = (groupId: string, groupName: string, response: ManualBookingResponse) => {
    const mergeCreatedBookingIntoGroup = (group: GroupAppointment) => mergeManualBookingIntoGroup(group, response);
    setGroups(previous => previous.map(group => group.id === groupId ? mergeCreatedBookingIntoGroup(group) : group));
    setSelectedGroup(previous => previous?.id === groupId ? mergeCreatedBookingIntoGroup(previous) : previous);
    setSearchQuery('');
    setManualBookingOpen(false);
    resetManualBookingEditor();
    setMessage({ type: 'success', text: `تمت إضافة ${response.booking.customerName} إلى مجموعة ${groupName} بنجاح.` });
    window.setTimeout(() => {
      const focusTarget = response.group.isFull ? subscribersHeadingRef.current : manualBookingToggleRef.current;
      focusTarget?.focus();
    }, 0);
  };

  const handleManualBookingSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!selectedGroup || manualBookingSubmitting) return;
    if (!selectedGroup.isActive || selectedGroup.bookedCount >= selectedGroup.capacity) {
      setManualBookingSubmitError(selectedGroup.isActive
        ? 'المجموعة ممتلئة. زوّد السعة أو اختر مجموعة أخرى.'
        : 'لا يمكن إضافة مشترك إلى مجموعة غير نشطة.');
      return;
    }

    const validation = validateManualBooking(manualBooking);
    if (!validation.payload) {
      setManualBookingErrors(validation.fieldErrors);
      setManualBookingSubmitError('');
      const firstInvalidField = validation.fieldErrors.customerName ? manualBookingNameRef.current : document.getElementById('manual-booking-phone');
      firstInvalidField?.focus();
      return;
    }

    try {
      setManualBookingSubmitting(true);
      setManualBookingErrors({});
      setManualBookingSubmitError('');
      setMessage(null);

      const groupId = selectedGroup.id;
      const groupName = selectedGroup.name || (selectedGroup.mode === 'online' ? 'أونلاين' : 'في السنتر');
      const response = await api.post<ManualBookingResponse>(
        `/api/group-appointments/${groupId}/bookings/manual`,
        validation.payload,
      );
      applyManualBookingCreation(groupId, groupName, response.data);
    } catch (error) {
      console.error(error);
      setManualBookingSubmitError(manualBookingErrorMessage(error));
    } finally {
      setManualBookingSubmitting(false);
    }
  };

  const handleManualBookingKeyDown = (event: React.KeyboardEvent<HTMLFormElement>) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      closeManualBookingEditor();
      return;
    }
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      event.currentTarget.requestSubmit();
    }
  };

  const handleDeleteBooking = async (booking: Booking) => {
    if (manualBookingSubmitting) return;
    if (pendingDeleteBookingId !== booking.id) {
      setPendingDeleteBookingId(booking.id);
      return;
    }

    try {
      setDeletingBookingId(booking.id);
      setMessage(null);
      await api.delete(`/api/group-appointments/bookings/${booking.id}`);
      setMessage({ type: 'success', text: `تم حذف ${booking.customerName || 'المشترك'} من المجموعة.` });

      setSelectedGroup(prev => {
        if (!prev) return prev;
        return {
          ...prev,
          bookedCount: Math.max(0, prev.bookedCount - 1),
          bookings: prev.bookings.filter(item => item.id !== booking.id)
        };
      });

      setGroups(prev => prev.map(group => {
        if (!group.bookings.some(item => item.id === booking.id)) return group;
        return {
          ...group,
          bookedCount: Math.max(0, group.bookedCount - 1),
          bookings: group.bookings.filter(item => item.id !== booking.id)
        };
      }));
      setPendingDeleteBookingId(null);
      void fetchGroups();
    } catch (e) {
      console.error(e);
      setMessage({ type: 'error', text: 'فشل حذف المشترك من المجموعة.' });
    } finally {
      setDeletingBookingId(null);
    }
  };

  const handleToggleBookingStatus = async (bookingId: string, updates: { isAttended?: boolean; isPaid?: boolean }) => {
    if (manualBookingSubmitting) return;
    try {
      await api.patch(`/api/group-appointments/bookings/${bookingId}`, updates);
      
      // Update local state
      setSelectedGroup(prev => {
        if (!prev) return prev;
        return {
          ...prev,
          bookings: prev.bookings.map(b => b.id === bookingId ? { ...b, ...updates } : b)
        };
      });

      setGroups(prev => prev.map(group => {
        if (!group.bookings.some(b => b.id === bookingId)) return group;
        return {
          ...group,
          bookings: group.bookings.map(b => b.id === bookingId ? { ...b, ...updates } : b)
        };
      }));
    } catch (e) {
      console.error(e);
      setMessage({ type: 'error', text: 'فشل تحديث حالة الحجز.' });
    }
  };

  const handleToggleGroup = async (group: GroupAppointment) => {
    if (manualBookingSubmitting) return;
    try {
      setActionLoading(true);
      await api.patch(`/api/group-appointments/${group.id}/toggle`);
      void fetchGroups();
    } catch (e) {
      console.error(e);
      setMessage({ type: 'error', text: 'فشل تغيير حالة المجموعة.' });
    } finally {
      setActionLoading(false);
    }
  };

  const handleExportExcel = async (group: GroupAppointment) => {
    if (!group || group.bookings.length === 0) return;
    const XLSX = await import('xlsx');
    
    const sortedBookings = [...group.bookings].sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
    const data = sortedBookings.map(b => ({
      'اسم الطالب': b.customerName,
      'رقم الهاتف': `+${b.customerPhone}`,
      'تاريخ الحجز': new Date(b.createdAt).toLocaleString('ar-EG', { timeZone: projectTimezone }),
      'حالة الحضور': b.isAttended ? 'حضر' : 'لم يحضر',
      'حالة الدفع': b.isPaid ? 'دفع' : 'لم يدفع'
    }));
    
    const worksheet = XLSX.utils.json_to_sheet(data);
    const workbook = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(workbook, worksheet, 'المشتركين');
    
    const excelBuffer = XLSX.write(workbook, { bookType: 'xlsx', type: 'array' });
    const blob = new Blob([excelBuffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.setAttribute('href', url);
    
    const timeStr = new Intl.DateTimeFormat('en-GB', { timeZone: projectTimezone, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })
      .format(new Date(group.dateTime)).replace(':', '_');
    const fileName = `bookings_${group.mode}_${timeStr}.xlsx`;
    
    link.setAttribute('download', fileName);
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  };

  const handleExportInactiveGroupsExcel = async () => {
    const inactiveGroups = groups.filter(group => !group.isActive && group.bookings.length > 0);
    const data = inactiveGroups.flatMap(group => {
      const groupMode = group.mode === 'online' ? 'أونلاين (Online)' : 'في السنتر (Offline)';
      return group.bookings.map(booking => ({
        'اسم الطالب': booking.customerName,
        'رقم الهاتف': `+${booking.customerPhone}`,
        'اسم المجموعة': group.name || groupMode,
        'نوع المجموعة': groupMode,
        'إنستراكتور الكورس': group.instructorName || '',
        'ميعاد السيشن المجانية': group.freeSessionDateTime ? new Date(group.freeSessionDateTime).toLocaleString('ar-EG', { timeZone: projectTimezone }) : '',
        'ميعاد السيشن الأولى للكورس': new Date(group.dateTime).toLocaleString('ar-EG', { timeZone: projectTimezone }),
        'ميعاد السيشن الثانية للكورس': group.courseSecondDateTime ? new Date(group.courseSecondDateTime).toLocaleString('ar-EG', { timeZone: projectTimezone }) : '',
        'أيام الكورس': formatDays(group.days),
        'تاريخ الحجز': new Date(booking.createdAt).toLocaleString('ar-EG', { timeZone: projectTimezone }),
        'حالة الحضور': booking.isAttended ? 'حضر' : 'لم يحضر',
        'حالة الدفع': booking.isPaid ? 'دفع' : 'لم يدفع'
      }));
    });

    if (data.length === 0) {
      setMessage({ type: 'error', text: 'لا توجد حجوزات داخل مجموعات غير نشطة للتصدير.' });
      return;
    }

    const XLSX = await import('xlsx');
    const worksheet = XLSX.utils.json_to_sheet(data);
    const workbook = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(workbook, worksheet, 'المجموعات غير النشطة');
    const excelBuffer = XLSX.write(workbook, { bookType: 'xlsx', type: 'array' });
    const blob = new Blob([excelBuffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.setAttribute('href', url);
    link.setAttribute('download', 'inactive_group_bookings.xlsx');
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  };

  const toggleDay = (dayIndex: number) => {
    setSelectedDays(prev => {
      if (prev.includes(dayIndex)) {
        return prev.filter(d => d !== dayIndex);
      }
      if (prev.length >= 2) {
        return [...prev.slice(1), dayIndex].sort();
      }
      return [...prev, dayIndex].sort();
    });
  };

  const formatTime = (isoString: string) => {
    const dateObj = new Date(isoString);
    const dateStr = dateObj.toLocaleDateString('ar-EG', { month: 'long', day: 'numeric', timeZone: projectTimezone });
    const timeStr = dateObj.toLocaleTimeString('ar-EG', { hour: '2-digit', minute: '2-digit', timeZone: projectTimezone });
    return `${dateStr} الساعة ${timeStr}`;
  };

  const readFileAsBinaryString = (file: File) => {
    return new Promise<string>((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = (event) => resolve(String(event.target?.result || ''));
      reader.onerror = () => reject(reader.error);
      reader.readAsBinaryString(file);
    });
  };

  const formatDays = (days: string) => {
    if (!days) return '';
    return days.split(',').filter(Boolean).map(d => DAY_NAMES_SHORT[parseInt(d)] || '').join(' · ');
  };

  const sortedGroups = [...groups].sort((a, b) => {
    const getRank = (g: GroupAppointment) => {
      if (g.bookedCount >= g.capacity) return 2;
      if (!g.isActive) return 3;
      return 1;
    };
    const rankA = getRank(a);
    const rankB = getRank(b);
    if (rankA !== rankB) {
      return rankA - rankB;
    }
    return new Date(a.dateTime).getTime() - new Date(b.dateTime).getTime();
  });

  const filteredBookings = selectedGroup
    ? [...selectedGroup.bookings]
        .filter(b => {
          const query = searchQuery.trim().toLowerCase();
          if (!query) return true;
          return (
            (b.customerName || '').toLowerCase().includes(query) ||
            (b.customerPhone || '').includes(query)
          );
        })
        .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
    : [];

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-md)', width: '100%' }}>
      {/* Top Header Controls */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 'var(--space-md)' }}>
        <button 
          type="button"
          onClick={handleBack}
          disabled={manualBookingSubmitting}
          className={`${styles.btn} ${styles.btnSecondary}`}
          style={{ padding: '6px 12px', fontSize: '0.85rem', display: 'flex', alignItems: 'center', gap: '4px' }}
        >
          <ArrowRight size={14} />
          العودة للإضافات
        </button>

        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
          <button
            type="button"
            onClick={handleExportInactiveGroupsExcel}
            className={`${styles.btn} ${styles.btnSecondary}`}
            style={{ padding: '8px 16px', fontSize: '0.85rem' }}
          >
            <Download size={16} />
            تصدير حجوزات المجموعات غير النشطة
          </button>

          <button
            type="button"
            onClick={openNewGroupEditor}
            disabled={manualBookingSubmitting}
            className={`${styles.btn} ${styles.btnPrimary}`}
            style={{ padding: '8px 16px', fontSize: '0.85rem' }}
          >
            <Plus size={16} />
            إضافة مجموعة جديدة
          </button>
        </div>
      </div>

      {message && (
        <div role={message.type === 'error' ? 'alert' : 'status'} aria-live="polite" className="glass-panel" style={{
          padding: 'var(--space-md)', 
          border: `1px solid ${message.type === 'success' ? 'rgba(16, 185, 129, 0.2)' : 'rgba(239, 68, 68, 0.2)'}`,
          backgroundColor: message.type === 'success' ? 'rgba(16, 185, 129, 0.04)' : 'rgba(239, 68, 68, 0.04)',
          borderRadius: 'var(--radius-md)',
          fontSize: '0.85rem',
          fontWeight: 600
        }}>
          {message.text}
        </div>
      )}

      <div className="glass-panel" style={{ padding: 'var(--space-lg)', display: 'flex', flexDirection: 'column', gap: 'var(--space-sm)' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 'var(--space-md)', flexWrap: 'wrap' }}>
          <div>
            <h3 style={{ fontSize: '1rem', fontWeight: 600, color: 'hsl(var(--text-primary))' }}>الإنستراكتورز العاملين</h3>
            <p style={{ fontSize: '0.78rem', color: 'hsl(var(--text-secondary))', marginTop: '4px' }}>
              اكتب كل اسم في سطر منفصل. نفس الأسماء ستظهر كاختيارات عند إنشاء أو تعديل المجموعة.
            </p>
          </div>
          <button
            type="button"
            onClick={handleSaveInstructors}
            className={`${styles.btn} ${styles.btnPrimary}`}
            disabled={savingInstructors}
            style={{ padding: '6px 14px', fontSize: '0.8rem' }}
          >
            {savingInstructors ? 'جاري الحفظ...' : 'حفظ الأسماء'}
          </button>
        </div>
        <label className={styles.label} htmlFor="group-instructors">أسماء المدرّبين، اسم واحد في كل سطر</label>
        <textarea
          id="group-instructors"
          value={instructorsText}
          onChange={(e) => setInstructorsText(e.target.value)}
          className={styles.input}
          rows={3}
          placeholder={'مثال:\nأحمد علي\nمنى حسن'}
          style={{ resize: 'vertical', minHeight: '86px', lineHeight: 1.7 }}
        />
      </div>

      <p role="note" className={styles.inlineMessage}>كل المواعيد معروضة وتُحفظ حسب المنطقة الزمنية: <b dir="ltr">{projectTimezone}</b>.</p>

      <div className="glass-panel" style={{ padding: 'var(--space-lg)', display: 'flex', flexDirection: 'column', gap: 'var(--space-sm)' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 'var(--space-md)', flexWrap: 'wrap' }}>
          <div>
            <h3 style={{ fontSize: '1rem', fontWeight: 600, color: 'hsl(var(--text-primary))', display: 'flex', alignItems: 'center', gap: '8px' }}>
              <ShieldAlert size={18} style={{ color: 'hsl(var(--accent-warning))' }} />
              استيراد المحظورين للدفع
            </h3>
            <p style={{ fontSize: '0.78rem', color: 'hsl(var(--text-secondary))', marginTop: '4px', lineHeight: 1.6 }}>
              ارفع ملف Excel بأرقام الطلاب الذين اشتركوا بالفعل. عند التأكيد سيتم حذفهم من كل المجموعات النشطة والمعطلة، وإلغاء متابعاتهم، ووضعهم في مجموعة المحظورين للدفع.
            </p>
          </div>
          <button
            type="button"
            onClick={handleDownloadPaidTemplate}
            className={`${styles.btn} ${styles.btnSecondary}`}
            style={{ padding: '6px 14px', fontSize: '0.8rem' }}
          >
            <FileDown size={14} />
            تحميل قالب
          </button>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-sm)', flexWrap: 'wrap' }}>
          <label
            className={`${styles.btn} ${styles.btnSecondary}`}
            style={{
              padding: '8px 14px',
              fontSize: '0.82rem',
              cursor: paidImporting || manualBookingSubmitting ? 'not-allowed' : 'pointer',
              opacity: paidImporting || manualBookingSubmitting ? 0.55 : 1,
            }}
          >
            <Upload size={14} />
            اختيار ملف Excel
            <input
              type="file"
              accept=".xlsx,.xls"
              onChange={handlePaidImportFileChange}
              disabled={paidImporting || manualBookingSubmitting}
              style={{ display: 'none' }}
            />
          </label>
          <button
            type="button"
            onClick={openPaidImportConfirmation}
            className={`${styles.btn} ${styles.btnPrimary}`}
            disabled={paidImporting || manualBookingSubmitting || paidImportPhones.length === 0}
            style={{ padding: '8px 16px', fontSize: '0.82rem', opacity: paidImporting || manualBookingSubmitting || paidImportPhones.length === 0 ? 0.55 : 1 }}
          >
            {paidImporting ? 'جاري التنفيذ...' : 'مراجعة وتنفيذ الحظر'}
          </button>
          <span style={{ fontSize: '0.78rem', color: 'hsl(var(--text-secondary))' }}>
            {paidImportFileName ? `${paidImportFileName} - ${paidImportPhones.length} رقم جاهز` : 'لم يتم اختيار ملف بعد'}
          </span>
        </div>

        {paidImportResult && (
          <div style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))',
            gap: 'var(--space-sm)',
            marginTop: 'var(--space-xs)'
          }}>
            {[
              ['تم حظرهم من العملاء الحاليين', paidImportResult.matchedCount],
              ['أرقام جديدة تم حظرها', paidImportResult.newCount],
              ['حجوزات تم حذفها', paidImportResult.removedBookingsCount],
              ['متابعات تم إلغاؤها', paidImportResult.cancelledFollowUpsCount]
            ].map(([label, count]) => (
              <div
                key={label}
                style={{
                  padding: '10px 12px',
                  background: 'var(--surface-muted)',
                  border: '1px solid var(--border-subtle)',
                  borderRadius: 'var(--radius-md)'
                }}
              >
                <div style={{ fontSize: '0.72rem', color: 'hsl(var(--text-secondary))', fontWeight: 600 }}>{label}</div>
                <div style={{ fontSize: '1.2rem', color: 'hsl(var(--text-primary))', fontWeight: 800 }}>{count}</div>
              </div>
            ))}
          </div>
        )}
      </div>

      {!loading && groups.length > 0 && (
        <div style={{ 
          display: 'grid', 
          gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', 
          gap: 'var(--space-md)',
          marginBottom: 'var(--space-xs)' 
        }}>
          {/* Card 1: Total Booked Students */}
          <div className="glass-panel" style={{ 
            padding: 'var(--space-lg)', 
            display: 'flex', 
            alignItems: 'center', 
            gap: 'var(--space-md)',
            background: 'var(--surface-muted)',
            border: '1px solid var(--accent-soft-strong)',
            borderRadius: 'var(--radius-lg)'
          }}>
            <div style={{ 
              display: 'flex', 
              alignItems: 'center', 
              justifyContent: 'center', 
              width: '48px', 
              height: '48px', 
              borderRadius: 'var(--radius-md)', 
              background: 'hsla(var(--accent-primary-hsl), 0.15)',
              color: 'hsl(var(--accent-primary))'
            }}>
              <Users size={24} aria-hidden="true" />
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
              <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-secondary))', fontWeight: 500 }}>إجمالي الطلاب المحجوزين</span>
              <span style={{ fontSize: '1.6rem', fontWeight: 800, color: 'hsl(var(--text-primary))', lineHeight: 1.2 }}>
                {groups.reduce((sum, g) => sum + (g.bookedCount || 0), 0)}
              </span>
            </div>
          </div>

          {/* Card 2: Active Groups */}
          <div className="glass-panel" style={{ 
            padding: 'var(--space-lg)', 
            display: 'flex', 
            alignItems: 'center', 
            gap: 'var(--space-md)',
            background: 'var(--surface-muted)',
            border: '1px solid rgba(34, 197, 94, 0.15)',
            borderRadius: 'var(--radius-lg)'
          }}>
            <div style={{ 
              display: 'flex', 
              alignItems: 'center', 
              justifyContent: 'center', 
              width: '48px', 
              height: '48px', 
              borderRadius: 'var(--radius-md)', 
              background: 'rgba(34, 197, 94, 0.12)',
              color: 'rgb(34, 197, 94)'
            }}>
              <Calendar size={24} aria-hidden="true" />
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
              <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-secondary))', fontWeight: 500 }}>المجموعات النشطة</span>
              <span style={{ fontSize: '1.6rem', fontWeight: 800, color: 'hsl(var(--text-primary))', lineHeight: 1.2 }}>
                {groups.filter(g => g.isActive).length} <span style={{ fontSize: '0.8rem', fontWeight: 500, color: 'hsl(var(--text-muted))' }}>/ {groups.length}</span>
              </span>
            </div>
          </div>

          {/* Card 2.5: Active Students in Active Groups */}
          <div className="glass-panel" style={{ 
            padding: 'var(--space-lg)', 
            display: 'flex', 
            alignItems: 'center', 
            gap: 'var(--space-md)',
            background: 'var(--surface-muted)',
            border: '1px solid rgba(249, 115, 22, 0.15)',
            borderRadius: 'var(--radius-lg)'
          }}>
            <div style={{ 
              display: 'flex', 
              alignItems: 'center', 
              justifyContent: 'center', 
              width: '48px', 
              height: '48px', 
              borderRadius: 'var(--radius-md)', 
              background: 'rgba(249, 115, 22, 0.12)',
              color: 'rgb(249, 115, 22)'
            }}>
              <UserCheck size={24} aria-hidden="true" />
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
              <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-secondary))', fontWeight: 500 }}>الطلاب النشطة في المجموعات النشطة</span>
              <span style={{ fontSize: '1.6rem', fontWeight: 800, color: 'hsl(var(--text-primary))', lineHeight: 1.2 }}>
                {groups.filter(g => g.isActive).reduce((sum, g) => sum + (g.bookedCount || 0), 0)}
              </span>
            </div>
          </div>

          {/* Card 3: Booking Fill Rate */}
          <div className="glass-panel" style={{ 
            padding: 'var(--space-lg)', 
            display: 'flex', 
            alignItems: 'center', 
            gap: 'var(--space-md)',
            background: 'var(--surface-muted)',
            border: '1px solid rgba(168, 85, 247, 0.15)',
            borderRadius: 'var(--radius-lg)'
          }}>
            <div style={{ 
              display: 'flex', 
              alignItems: 'center', 
              justifyContent: 'center', 
              width: '48px', 
              height: '48px', 
              borderRadius: 'var(--radius-md)', 
              background: 'rgba(168, 85, 247, 0.12)',
              color: 'rgb(168, 85, 247)'
            }}>
              <Clock size={24} aria-hidden="true" />
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
              <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-secondary))', fontWeight: 500 }}>نسبة إشغال المجموعات</span>
              <span style={{ fontSize: '1.6rem', fontWeight: 800, color: 'hsl(var(--text-primary))', lineHeight: 1.2 }}>
                {(() => {
                  const totalCap = groups.reduce((sum, g) => sum + (g.capacity || 0), 0);
                  const totalBooked = groups.reduce((sum, g) => sum + (g.bookedCount || 0), 0);
                  return totalCap > 0 ? `${Math.round((totalBooked / totalCap) * 100)}%` : '0%';
                })()}
              </span>
            </div>
          </div>
        </div>
      )}

      {loading ? (
        <div role="status" aria-live="polite" style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 'var(--space-sm)', padding: '4rem 0' }}>
          <div className={styles.spinner} aria-hidden="true"></div>
          <span>جاري تحميل مجموعات المواعيد…</span>
        </div>
      ) : groups.length === 0 ? (
        <div className="glass-panel" style={{ 
          display: 'flex', 
          flexDirection: 'column', 
          alignItems: 'center', 
          justifyContent: 'center', 
          padding: '4rem var(--space-md)', 
          textAlign: 'center',
          gap: 'var(--space-sm)'
        }}>
          <Calendar size={48} style={{ color: 'hsl(var(--text-muted))' }} />
          <h3 style={{ fontSize: '1.1rem', fontWeight: 600 }}>لا توجد مجموعات بعد</h3>
          <p style={{ fontSize: '0.85rem', color: 'hsl(var(--text-secondary))', maxWidth: '280px' }}>
            قم بإنشاء مجموعتك الأولى وتحديد السعة المطلوبة للبدء في استقبال الحجوزات.
          </p>
        </div>
      ) : (
        <div style={{ display: 'grid', gridTemplateColumns: '1fr', gap: 'var(--space-lg)' }}>
          {/* List of Groups */}
          <div className="glass-panel" style={{ padding: 'var(--space-lg)' }}>
            <h3 style={{ fontSize: '1rem', fontWeight: 600, marginBottom: 'var(--space-md)', color: 'hsl(var(--text-primary))' }}>المجموعات الحالية</h3>
            
            <div style={{ overflowX: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'right' }}>
                <caption className={styles.tableCaption}>المجموعات الحالية ومواعيدها وسعتها وحالتها</caption>
                <thead>
                  <tr style={{ borderBottom: '1px solid var(--border-subtle)' }}>
                    <th scope="col" style={{ padding: '12px 8px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>نوع المجموعة</th>
                    <th scope="col" style={{ padding: '12px 8px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>المواعيد والمدرّب</th>
                    <th scope="col" style={{ padding: '12px 8px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>الحجوزات / السعة</th>
                    <th scope="col" style={{ padding: '12px 8px', fontSize: '0.8rem', color: 'var(--text-soft)', textAlign: 'center' }}>الحالة</th>
                    <th scope="col" style={{ padding: '12px 8px', fontSize: '0.8rem', color: 'var(--text-soft)', textAlign: 'center' }}>الإجراءات</th>
                  </tr>
                </thead>
                <tbody>
                  {sortedGroups.map((group) => {
                    const percent = Math.min(100, Math.round((group.bookedCount / group.capacity) * 100));
                    const isFull = group.bookedCount >= group.capacity;

                    return (
                      <tr key={group.id} style={{ borderBottom: '1px solid var(--border-subtle)', verticalAlign: 'middle' }}>
                        <td style={{ padding: '16px 8px' }}>
                          <span style={{ fontWeight: 600, color: 'var(--text-strong)' }}>
                            {group.mode === 'online' ? 'أونلاين (Online)' : 'في السنتر (Offline)'}
                          </span>
                          {!group.isActive && (
                            <span style={{ 
                              marginRight: '8px',
                              padding: '2px 6px',
                              fontSize: '0.7rem',
                              backgroundColor: 'rgba(239, 68, 68, 0.15)',
                              color: 'hsl(var(--accent-danger))',
                              borderRadius: '4px'
                            }}>غير نشطة</span>
                          )}
                        </td>
                        <td style={{ padding: '16px 8px', fontSize: '0.85rem' }}>
                          <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '4px', color: 'hsl(var(--text-primary))', fontWeight: 600 }}>
                              <Clock size={12} />
                              {formatTime(group.dateTime)}
                            </div>
                            {group.days && (
                              <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-secondary))' }}>
                                أيام الكورس: {formatDays(group.days)}
                              </span>
                            )}
                            {group.courseSecondDateTime && (
                              <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-secondary))' }}>
                                السيشن الثانية: {formatTime(group.courseSecondDateTime)}
                              </span>
                            )}
                            {group.freeSessionDateTime && (
                              <span style={{ fontSize: '0.75rem', color: 'hsl(var(--text-secondary))' }}>
                                الجلسة المجانية: {formatTime(group.freeSessionDateTime)}
                              </span>
                            )}
                            {group.instructorName && (
                              <span style={{ fontSize: '0.75rem', color: 'hsl(var(--accent-primary))', fontWeight: 700 }}>
                                إنستراكتور الكورس: {group.instructorName}
                              </span>
                            )}
                          </div>
                        </td>
                        <td style={{ padding: '16px 8px' }}>
                          <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', width: '120px' }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.75rem', fontWeight: 600 }}>
                              <span style={{ color: isFull ? 'hsl(var(--accent-danger))' : 'hsl(var(--text-secondary))' }}>
                                {isFull ? 'مكتملة!' : `${group.bookedCount} / ${group.capacity}`}
                              </span>
                              <span>{percent}%</span>
                            </div>
                            <div role="progressbar" aria-label={`إشغال ${group.name}`} aria-valuemin={0} aria-valuemax={group.capacity} aria-valuenow={group.bookedCount} aria-valuetext={`${group.bookedCount} من ${group.capacity}`} style={{ height: '6px', background: 'hsl(var(--bg-tertiary))', borderRadius: '3px', overflow: 'hidden' }}>
                              <div style={{ 
                                width: `${percent}%`, 
                                height: '100%', 
                                background: isFull ? 'hsl(var(--accent-danger))' : 'hsl(var(--accent-success))',
                                borderRadius: '3px'
                              }}></div>
                            </div>
                          </div>
                        </td>
                        <td style={{ padding: '16px 8px', textAlign: 'center' }}>
                          <button
                            type="button"
                            onClick={() => handleToggleGroup(group)}
                            disabled={actionLoading || manualBookingSubmitting}
                            style={{
                              padding: '4px 12px',
                              fontSize: '0.75rem',
                              border: 'none',
                              borderRadius: '12px',
                              cursor: actionLoading || manualBookingSubmitting ? 'not-allowed' : 'pointer',
                              fontWeight: 600,
                              background: group.isActive ? 'rgba(34, 197, 94, 0.15)' : 'rgba(239, 68, 68, 0.15)',
                              color: group.isActive ? 'hsl(var(--accent-success))' : 'hsl(var(--accent-danger))',
                            }}
                          >
                            {group.isActive ? 'نشطة ✓' : 'معطلة ✗'}
                          </button>
                        </td>
                        <td style={{ padding: '16px 8px', textAlign: 'center' }}>
                          <div style={{ display: 'flex', gap: '6px', justifyContent: 'center' }}>
                            <button
                              type="button"
                              onClick={() => handleSelectGroup(group)}
                              disabled={manualBookingSubmitting}
                              aria-expanded={selectedGroup?.id === group.id}
                              aria-controls="group-subscribers-panel"
                              className={`${styles.btn} ${styles.btnSecondary}`}
                              style={{ padding: '4px 8px', fontSize: '0.75rem', backgroundColor: 'var(--accent-soft)', color: 'var(--accent)' }}
                            >
                              <Users size={12} />
                              المشتركين ({group.bookedCount})
                            </button>
                            <button
                              type="button"
                              aria-label={`تعديل مجموعة ${group.name}`}
                              onClick={() => handleStartEdit(group)}
                              disabled={manualBookingSubmitting}
                              className={`${styles.btn} ${styles.btnSecondary}`}
                              style={{ padding: '4px 8px', fontSize: '0.75rem' }}
                            >
                              <Edit3 size={12} />
                            </button>
                             <button
                               type="button"
                               aria-label={`حذف مجموعة ${group.name}`}
                               onClick={() => triggerDeleteGroup(group.id)}
                               className={`${styles.btn} ${styles.btnDanger}`}
                               style={{ padding: '4px 8px', fontSize: '0.75rem' }}
                               disabled={actionLoading || manualBookingSubmitting}
                             >
                               <Trash2 size={12} />
                             </button>
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>

          {/* Booked Customers List (Conditional Panel) */}
          {selectedGroup && (
            <div id="group-subscribers-panel" className="glass-panel" style={{ padding: 'var(--space-lg)' }}>
              <div className={styles.subscribersHeader}>
                <div className={styles.subscribersHeading}>
                  <h3 ref={subscribersHeadingRef} tabIndex={-1} className={styles.subscribersTitle}>
                    المشتركون في مجموعة: <span style={{ color: 'hsl(var(--accent-primary))' }}>{selectedGroup.name || (selectedGroup.mode === 'online' ? 'أونلاين' : 'في السنتر')}</span>
                  </h3>
                  <p id="manual-booking-availability" className={styles.subscribersMeta}>
                    {!selectedGroup.isActive
                      ? 'المجموعة غير نشطة، فعّلها أولًا لإضافة مشترك.'
                      : selectedGroup.bookedCount >= selectedGroup.capacity
                        ? `المجموعة ممتلئة (${selectedGroup.bookedCount} من ${selectedGroup.capacity}).`
                        : `${selectedGroup.bookedCount} من ${selectedGroup.capacity} مشترك، متاح ${selectedGroup.capacity - selectedGroup.bookedCount}.`}
                  </p>
                </div>
                <div className={styles.subscribersActions}>
                  <button
                    ref={manualBookingToggleRef}
                    type="button"
                    onClick={manualBookingOpen ? closeManualBookingEditor : openManualBookingEditor}
                    disabled={!selectedGroup.isActive || selectedGroup.bookedCount >= selectedGroup.capacity || manualBookingSubmitting}
                    aria-expanded={manualBookingOpen}
                    aria-controls="manual-booking-editor"
                    aria-describedby="manual-booking-availability"
                    className={`${styles.btn} ${manualBookingOpen ? styles.btnSecondary : styles.btnPrimary}`}
                  >
                    <Plus size={15} aria-hidden="true" />
                    {manualBookingOpen ? 'إلغاء الإضافة' : 'إضافة مشترك يدويًا'}
                  </button>
                  <button
                    type="button"
                    onClick={() => handleExportExcel(selectedGroup)}
                    disabled={selectedGroup.bookings.length === 0}
                    className={`${styles.btn} ${styles.btnSecondary}`}
                  >
                    <Download size={12} />
                    تصدير المشتركين (Excel)
                  </button>
                  <button
                    type="button"
                    onClick={closeSelectedGroup}
                    disabled={manualBookingSubmitting}
                    className={`${styles.btn} ${styles.btnSecondary}`}
                  >
                    إغلاق القائمة
                  </button>
                </div>
              </div>

              {manualBookingOpen && (
                <section id="manual-booking-editor" className={styles.manualBookingEditor} aria-labelledby="manual-booking-title">
                  <div>
                    <h4 id="manual-booking-title" className={styles.manualBookingTitle}>بيانات المشترك</h4>
                    <p className={styles.sectionHint}>سيُضاف الحجز مباشرة إلى هذه المجموعة، ولن يُنقل رقم مسجل في مجموعة أخرى تلقائيًا.</p>
                  </div>

                  <form
                    className={styles.manualBookingForm}
                    onSubmit={handleManualBookingSubmit}
                    onKeyDown={handleManualBookingKeyDown}
                    noValidate
                    autoComplete="off"
                    aria-busy={manualBookingSubmitting}
                  >
                    <div className={styles.manualBookingGrid}>
                      <div className={styles.formGroup}>
                        <label className={styles.label} htmlFor="manual-booking-name">اسم المشترك</label>
                        <input
                          ref={manualBookingNameRef}
                          id="manual-booking-name"
                          name="manualBookingCustomerName"
                          type="text"
                          value={manualBooking.customerName}
                          onChange={(event) => {
                            setManualBooking(previous => ({ ...previous, customerName: event.target.value }));
                            setManualBookingErrors(previous => ({ ...previous, customerName: undefined }));
                          }}
                          className={styles.input}
                          autoComplete="off"
                          maxLength={120}
                          required
                          aria-invalid={Boolean(manualBookingErrors.customerName)}
                          aria-describedby={manualBookingErrors.customerName ? 'manual-booking-name-error' : undefined}
                          disabled={manualBookingSubmitting}
                        />
                        {manualBookingErrors.customerName && (
                          <span id="manual-booking-name-error" className={styles.fieldError}>{manualBookingErrors.customerName}</span>
                        )}
                      </div>

                      <div className={styles.formGroup}>
                        <label className={styles.label} htmlFor="manual-booking-phone">رقم الهاتف أو واتساب</label>
                        <input
                          id="manual-booking-phone"
                          name="manualBookingCustomerPhone"
                          type="tel"
                          dir="ltr"
                          value={manualBooking.customerPhone}
                          onChange={(event) => {
                            setManualBooking(previous => ({ ...previous, customerPhone: event.target.value }));
                            setManualBookingErrors(previous => ({ ...previous, customerPhone: undefined }));
                          }}
                          className={`${styles.input} ${styles.phoneInput}`}
                          inputMode="tel"
                          autoComplete="off"
                          maxLength={64}
                          placeholder="01012345678 أو +201012345678"
                          required
                          aria-invalid={Boolean(manualBookingErrors.customerPhone)}
                          aria-describedby={manualBookingErrors.customerPhone ? 'manual-booking-phone-error' : 'manual-booking-phone-hint'}
                          disabled={manualBookingSubmitting}
                        />
                        {manualBookingErrors.customerPhone ? (
                          <span id="manual-booking-phone-error" className={styles.fieldError}>{manualBookingErrors.customerPhone}</span>
                        ) : (
                          <span id="manual-booking-phone-hint" className={styles.fieldHint}>يُقبل الرقم المحلي أو الدولي، وسنحفظه بصيغة موحدة.</span>
                        )}
                      </div>

                      <div className={`${styles.formGroup} ${styles.manualBookingNotes}`}>
                        <label className={styles.label} htmlFor="manual-booking-notes">ملاحظة داخلية <span className={styles.optionalLabel}>(اختياري)</span></label>
                        <textarea
                          id="manual-booking-notes"
                          name="manualBookingNotes"
                          value={manualBooking.notes}
                          onChange={(event) => setManualBooking(previous => ({ ...previous, notes: event.target.value }))}
                          className={styles.input}
                          rows={3}
                          maxLength={2000}
                          placeholder="مثال: تم تأكيد الحجز هاتفيًا"
                          aria-describedby="manual-booking-notes-count"
                          disabled={manualBookingSubmitting}
                        />
                        <span id="manual-booking-notes-count" className={styles.fieldHint}>{manualBooking.notes.length} من 2000 حرف</span>
                      </div>
                    </div>

                    <fieldset className={styles.manualBookingStatuses}>
                      <legend className={styles.label}>حالة المشترك عند الإضافة</legend>
                      <label className={styles.checkboxGroup}>
                        <input
                          type="checkbox"
                          name="manualBookingIsPaid"
                          checked={manualBooking.isPaid}
                          onChange={(event) => setManualBooking(previous => ({ ...previous, isPaid: event.target.checked }))}
                          className={styles.checkbox}
                          disabled={manualBookingSubmitting}
                        />
                        <span>تم الدفع</span>
                      </label>
                      <label className={styles.checkboxGroup}>
                        <input
                          type="checkbox"
                          name="manualBookingIsAttended"
                          checked={manualBooking.isAttended}
                          onChange={(event) => setManualBooking(previous => ({ ...previous, isAttended: event.target.checked }))}
                          className={styles.checkbox}
                          disabled={manualBookingSubmitting}
                        />
                        <span>تم الحضور</span>
                      </label>
                      <p className={styles.statusesHint}>تحديد «تم الدفع» يلغي المتابعات المعلقة لهذا العميل.</p>
                    </fieldset>

                    {manualBookingSubmitError && (
                      <p role="alert" className={styles.inlineError}>{manualBookingSubmitError}</p>
                    )}

                    <div className={styles.manualBookingFormActions}>
                      <button
                        type="button"
                        onClick={closeManualBookingEditor}
                        className={`${styles.btn} ${styles.btnSecondary}`}
                        disabled={manualBookingSubmitting}
                      >
                        إلغاء
                      </button>
                      <button
                        type="submit"
                        className={`${styles.btn} ${styles.btnPrimary}`}
                        disabled={manualBookingSubmitting || !selectedGroup.isActive || selectedGroup.bookedCount >= selectedGroup.capacity}
                      >
                        {manualBookingSubmitting ? 'جاري إضافة المشترك...' : 'إضافة إلى المجموعة'}
                        {!manualBookingSubmitting && <kbd className={styles.shortcutHint} aria-hidden="true">Ctrl/⌘ + Enter</kbd>}
                      </button>
                    </div>
                  </form>
                </section>
              )}

              <div style={{ position: 'relative', marginBottom: 'var(--space-md)' }}>
                <input
                  aria-label="البحث في مشتركي المجموعة بالاسم أو رقم الهاتف"
                  type="text"
                  placeholder={selectedGroup.bookings.length === 0 ? "لا يوجد مشتركون للبحث" : "البحث باسم الطالب أو رقم الهاتف..."}
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  disabled={selectedGroup.bookings.length === 0}
                  className={styles.input}
                  style={{ width: '100%', paddingLeft: '2.5rem', paddingRight: '1rem' }}
                />
                <Search 
                  size={16} 
                  style={{ 
                    position: 'absolute', 
                    left: '12px', 
                    top: '50%', 
                    transform: 'translateY(-50%)', 
                    color: 'hsl(var(--text-muted))', 
                    pointerEvents: 'none' 
                  }} 
                />
              </div>

              {selectedGroup.bookings.length === 0 ? (
                <p style={{ fontSize: '0.85rem', color: 'hsl(var(--text-secondary))', textAlign: 'center', padding: '2rem 0' }}>
                  لا يوجد مشتركون مسجلون في هذه المجموعة بعد. استخدم زر «إضافة مشترك يدويًا» للبدء.
                </p>
              ) : filteredBookings.length === 0 ? (
                <p style={{ fontSize: '0.85rem', color: 'hsl(var(--accent-danger))', textAlign: 'center', padding: '2rem 0', fontWeight: 600 }}>
                  لم يتم العثور على نتائج تطابق البحث &quot;{searchQuery}&quot;
                </p>
              ) : (
                <div style={{ overflowX: 'auto' }}>
                  <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'right' }}>
                    <caption className={styles.tableCaption}>مشتركو المجموعة وحالة الحضور والدفع</caption>
                    <thead>
                      <tr style={{ borderBottom: '1px solid var(--border-subtle)' }}>
                        <th scope="col" style={{ padding: '10px 6px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>اسم العميل</th>
                        <th scope="col" style={{ padding: '10px 6px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>رقم الواتساب</th>
                        <th scope="col" style={{ padding: '10px 6px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>تاريخ الحجز</th>
                        <th scope="col" style={{ padding: '10px 6px', fontSize: '0.8rem', color: 'var(--text-soft)', textAlign: 'center' }}>حضور</th>
                        <th scope="col" style={{ padding: '10px 6px', fontSize: '0.8rem', color: 'var(--text-soft)', textAlign: 'center' }}>دفع</th>
                        <th scope="col" style={{ padding: '10px 6px', fontSize: '0.8rem', color: 'var(--text-soft)', textAlign: 'center' }}>الإجراءات</th>
                      </tr>
                    </thead>
                    <tbody>
                      {filteredBookings.map((booking) => (
                        <tr key={booking.id} style={{ borderBottom: '1px solid var(--border-subtle)' }}>
                          <td style={{ padding: '12px 6px', fontWeight: 600 }}>{booking.customerName}</td>
                          <td style={{ padding: '12px 6px', fontSize: '0.85rem' }}>+{booking.customerPhone}</td>
                          <td style={{ padding: '12px 6px', fontSize: '0.85rem', color: 'hsl(var(--text-secondary))' }}>
                            {new Date(booking.createdAt).toLocaleDateString('ar-EG', { year: 'numeric', month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit', timeZone: projectTimezone })}
                          </td>
                          <td style={{ padding: '12px 6px', textAlign: 'center' }}>
                            <input 
                              type="checkbox" 
                              aria-label={`تحديد حضور ${booking.customerName}`}
                              checked={booking.isAttended || false} 
                              onChange={(e) => handleToggleBookingStatus(booking.id, { isAttended: e.target.checked })} 
                              disabled={manualBookingSubmitting}
                              style={{ width: '16px', height: '16px', cursor: manualBookingSubmitting ? 'not-allowed' : 'pointer' }}
                            />
                          </td>
                          <td style={{ padding: '12px 6px', textAlign: 'center' }}>
                            <input 
                              type="checkbox" 
                              aria-label={`تحديد دفع ${booking.customerName}`}
                              checked={booking.isPaid || false} 
                              onChange={(e) => handleToggleBookingStatus(booking.id, { isPaid: e.target.checked })} 
                              disabled={manualBookingSubmitting}
                              style={{ width: '16px', height: '16px', cursor: manualBookingSubmitting ? 'not-allowed' : 'pointer' }}
                            />
                          </td>
                          <td style={{ padding: '12px 6px', textAlign: 'center' }}>
                            <div style={{ display: 'flex', justifyContent: 'center', gap: '6px', flexWrap: 'wrap' }}>
                              <a 
                                href={`/inbox?customerId=${booking.customerId}`}
                                className={`${styles.btn} ${styles.btnSecondary}`}
                                style={{ padding: '4px 10px', fontSize: '0.75rem', backgroundColor: 'var(--accent-soft)' }}
                              >
                                فتح المحادثة
                              </a>
                              <button
                                type="button"
                                onClick={() => handleDeleteBooking(booking)}
                                disabled={manualBookingSubmitting || deletingBookingId === booking.id}
                                className={`${styles.btn} ${styles.btnDanger}`}
                                style={{ padding: '4px 10px', fontSize: '0.75rem' }}
                                title={pendingDeleteBookingId === booking.id ? 'اضغط للتأكيد النهائي' : 'حذف المشترك من المجموعة'}
                              >
                                <Trash2 size={12} />
                                {deletingBookingId === booking.id
                                  ? 'جاري الحذف...'
                                  : pendingDeleteBookingId === booking.id
                                    ? 'تأكيد الحذف'
                                    : 'حذف'}
                              </button>
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {/* Add/Edit Modal */}
      {isModalOpen && (
        <div className={styles.overlay} onMouseDown={(event) => { if (event.target === event.currentTarget) closeEditor(); }}>
          <div ref={editorRef} className={styles.modal} role="dialog" aria-modal="true" aria-labelledby="group-editor-title" aria-describedby="group-editor-timezone" style={{ maxWidth: '560px', maxHeight: '92dvh', overflowY: 'auto' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderBottom: '1px solid var(--border-subtle)', paddingBottom: 'var(--space-sm)' }}>
              <h3 id="group-editor-title" style={{ fontSize: '1.1rem', fontWeight: 600 }}>
                {editingGroupId ? 'تعديل مجموعة مواعيد' : 'إنشاء مجموعة جديدة'}
              </h3>
              <button 
                ref={editorCloseRef}
                type="button"
                aria-label="إغلاق نموذج المجموعة"
                onClick={closeEditor}
                className={styles.closeBtn}
                style={{ fontSize: '1.5rem' }}
              >
                &times;
              </button>
            </div>

            <p id="group-editor-timezone" className={styles.sectionHint}>أدخل كل المواعيد بتوقيت <b dir="ltr">{projectTimezone}</b>.</p>

            <form onSubmit={handleSaveGroup} className={styles.form}>
              <div className={styles.formGroup}>
                <label className={styles.label} htmlFor="group-editor-mode">نوع المجموعة</label>
                <select 
                  id="group-editor-mode"
                  value={mode} 
                  onChange={(e) => setMode(e.target.value)} 
                  className={styles.select}
                  required
                >
                  <option value="offline">في السنتر (Offline)</option>
                  <option value="online">أونلاين (Online)</option>
                </select>
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label} htmlFor="group-editor-instructor">مدرّب المجموعة</label>
                <select
                  id="group-editor-instructor"
                  value={selectedInstructor}
                  onChange={(e) => setSelectedInstructor(e.target.value)}
                  className={styles.select}
                  required
                >
                  <option value="">اختر الإنستراكتور</option>
                  {instructors.map((instructor) => (
                    <option key={instructor} value={instructor}>{instructor}</option>
                  ))}
                </select>
                {instructors.length === 0 && (
                  <span style={{ fontSize: '0.72rem', color: 'hsl(var(--accent-danger))' }}>
                    أضف أسماء الإنستراكتورز من الخانة الموجودة خارج النموذج ثم احفظها.
                  </span>
                )}
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label} htmlFor="group-editor-free-session">موعد الجلسة المجانية</label>
                <input
                  id="group-editor-free-session"
                  type="datetime-local"
                  value={freeSessionDateTime}
                  onChange={(e) => setFreeSessionDateTime(e.target.value)}
                  className={styles.input}
                  required
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label} htmlFor="group-editor-first-session">موعد الجلسة الأولى للكورس</label>
                <input 
                  id="group-editor-first-session"
                  type="datetime-local" 
                  value={dateTime} 
                  onChange={(e) => setDateTime(e.target.value)} 
                  className={styles.input} 
                  required 
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label} htmlFor="group-editor-second-session">موعد الجلسة الثانية للكورس</label>
                <input
                  id="group-editor-second-session"
                  type="datetime-local"
                  value={courseSecondDateTime}
                  onChange={(e) => setCourseSecondDateTime(e.target.value)}
                  className={styles.input}
                  required
                />
              </div>

              <div className={styles.formGroup}>
                <span id="group-editor-days-label" className={styles.label}>أيام الكورس الأسبوعية (اختر يومين)</span>
                <div role="group" aria-labelledby="group-editor-days-label" style={{ display: 'flex', flexWrap: 'wrap', gap: '8px', marginTop: '4px' }}>
                  {DAY_NAMES.map((dayName, idx) => (
                    <button
                      key={idx}
                      type="button"
                      aria-pressed={selectedDays.includes(idx)}
                      onClick={() => toggleDay(idx)}
                      style={{
                        minHeight: '44px',
                        padding: '6px 14px',
                        fontSize: '0.8rem',
                        borderRadius: '16px',
                        border: selectedDays.includes(idx) ? '2px solid hsl(var(--accent-primary))' : '1px solid var(--border-subtle)',
                        background: selectedDays.includes(idx) ? 'hsla(var(--accent-primary), 0.15)' : 'transparent',
                        color: selectedDays.includes(idx) ? 'hsl(var(--accent-primary))' : 'hsl(var(--text-secondary))',
                        cursor: 'pointer',
                        fontWeight: selectedDays.includes(idx) ? 700 : 400,
                        transition: 'background-color 0.2s, border-color 0.2s, color 0.2s'
                      }}
                    >
                      {dayName}
                    </button>
                  ))}
                </div>
                <span style={{ fontSize: '0.72rem', color: selectedDays.length === 2 ? 'hsl(var(--text-secondary))' : 'hsl(var(--accent-danger))' }}>
                  المختار حالياً: {selectedDays.length} / 2
                </span>
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label} htmlFor="group-editor-capacity">السعة (عدد المشتركين الأقصى)</label>
                <input 
                  id="group-editor-capacity"
                  type="number" 
                  min={1}
                  value={capacity} 
                  onChange={(e) => setCapacity(Number(e.target.value))} 
                  className={styles.input} 
                  required 
                />
              </div>

              <div className={styles.formGroup} style={{ marginTop: 'var(--space-xs)' }}>
                <label className={styles.checkboxGroup}>
                  <input 
                    type="checkbox" 
                    checked={isActive}
                    onChange={(e) => setIsActive(e.target.checked)}
                    className={styles.checkbox} 
                  />
                  <span className={styles.label} style={{ userSelect: 'none' }}>مجموعة نشطة ومتاحة للحجز</span>
                </label>
              </div>

              <div style={{ display: 'flex', gap: 'var(--space-md)', justifyContent: 'flex-end', marginTop: 'var(--space-sm)' }}>
                <button 
                  type="button" 
                  onClick={closeEditor}
                  className={`${styles.btn} ${styles.btnSecondary}`}
                  disabled={actionLoading}
                >
                  إلغاء
                </button>
                <button 
                  type="submit" 
                  className={`${styles.btn} ${styles.btnPrimary}`}
                  disabled={actionLoading}
                >
                  {actionLoading ? 'جاري الحفظ...' : 'حفظ'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      <ConfirmDialog 
        isOpen={confirmDeleteOpen}
        title="تأكيد حذف المجموعة"
        message="هل أنت متأكد من حذف هذه المجموعة؟ سيتم حذف جميع الحجوزات والبيانات المرتبطة بها نهائياً."
        confirmLabel="حذف"
        cancelLabel="إلغاء"
        onConfirm={handleConfirmDeleteGroup}
        onCancel={() => { setConfirmDeleteOpen(false); setGroupToDelete(null); }}
      />
      <ConfirmDialog
        isOpen={paidImportConfirmOpen}
        title="تأكيد استيراد قائمة المدفوعين"
        message={`سيتم فحص ${paidImportPhones.length} رقم، ثم حذف أي حجوزات مطابقة من كل المجموعات وإلغاء متابعاتها وإضافتها لقائمة الحظر. هذا الإجراء لا يمكن التراجع عنه تلقائيًا.`}
        confirmLabel="تنفيذ الحظر والحذف"
        cancelLabel="رجوع للمراجعة"
        onConfirm={() => void handleConfirmPaidImport()}
        onCancel={() => setPaidImportConfirmOpen(false)}
      />
    </div>
  );
}
