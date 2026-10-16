'use client';

import React, { useEffect, useState, useCallback, useRef } from 'react';
import { api } from '../../services/api';
import { SignalRService } from '../../services/signalr';
import { useAuth } from '../../context/auth-context';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import { useDialogViewport } from '../../components/shared/useDialogViewport';
import { lockBodyScroll } from '../../components/shared/modal-accessibility';
import FilterTabs from '../../components/ui/FilterTabs';
import StatusMessage from '../../components/ui/StatusMessage';
import { useUnsavedNavigationGuard } from '../../hooks/use-unsaved-navigation-guard';
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
  UserCheck,
  SlidersHorizontal,
  ListFilter,
  CirclePause,
  Settings2,
  ChevronDown,
  X
} from 'lucide-react';
import styles from './settings.module.css';
import GroupStaffDirectoryEditor, { StaffContact } from './GroupStaffDirectoryEditor';
import GroupParticipationPanel from './GroupParticipationPanel';
import GroupBookingStats from './GroupBookingStats';

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
  instructorContacts?: StaffContact[];
  supervisors?: StaffContact[];
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
  supervisorPhone?: string;
  staffSyncError?: string | null;
  staffSyncedAtUtc?: string | null;
}

type GroupSummary = Omit<GroupAppointment, 'bookings'>;

type GroupStatus = 'available' | 'full' | 'inactive';

const getGroupStatus = (group: GroupAppointment): GroupStatus => {
  if (!group.isActive) return 'inactive';
  return group.bookedCount >= group.capacity ? 'full' : 'available';
};

const groupStatusLabels: Record<GroupStatus, string> = {
  available: 'متاحة للحجز', full: 'مكتملة', inactive: 'غير نشطة',
};

interface GroupAppointmentsManagerProps {
  courseType?: 'regular' | 'intensive';
  onBack: () => void;
  backLabel?: string;
  timezone: string;
  onDirtyChange?: (dirty: boolean) => void;
  heading?: React.ReactNode;
  courseNavigation?: React.ReactNode;
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

export default function GroupAppointmentsManager(props: GroupAppointmentsManagerProps) {
  const { activeProject } = useAuth();
  return activeProject ? <GroupAppointmentsWorkspace key={`${activeProject.id}:${props.courseType ?? 'regular'}`} {...props} /> : null;
}

function GroupAppointmentsWorkspace({ onBack, timezone, courseType = 'regular', backLabel = 'العودة للإضافات', onDirtyChange, heading, courseNavigation }: GroupAppointmentsManagerProps) {
  const { activeProject } = useAuth();
  const projectTimezone = validTimezone(timezone);
  const [groups, setGroups] = useState<GroupAppointment[]>([]);
  const [instructorFilter, setInstructorFilter] = useState('');
  const [groupStatusFilter, setGroupStatusFilter] = useState<'all' | GroupStatus>('all');
  const [workspaceSection, setWorkspaceSection] = useState<'groups' | 'staff' | 'data'>('groups');
  const managementMenuRef = useRef<HTMLDetailsElement>(null);
  const [loading, setLoading] = useState(true);
  const [groupsLoadFailed, setGroupsLoadFailed] = useState(false);
  const groupsLoadController = useRef<AbortController | null>(null);
  const bookingsLoadController = useRef<AbortController | null>(null);
  const staffLoadController = useRef<AbortController | null>(null);
  const exportController = useRef<AbortController | null>(null);
  const selectedGroupIdRef = useRef<string | null>(null);
  const [bookingsLoading, setBookingsLoading] = useState(false);
  const [bookingsLoadFailed, setBookingsLoadFailed] = useState(false);
  const [statsRefreshKey, setStatsRefreshKey] = useState(0);
  const [exporting, setExporting] = useState(false);
  const [actionLoading, setActionLoading] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  // Modal states
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingGroupId, setEditingGroupId] = useState<string | null>(null);
  const [editorError, setEditorError] = useState('');
  const [selectedGroup, setSelectedGroup] = useState<GroupAppointment | null>(null);
  const [participationGroup, setParticipationGroup] = useState<{ id: string; name: string; projectId: string } | null>(null);
  const participationTrigger = useRef<HTMLButtonElement | null>(null);
  const [pendingDeleteBookingId, setPendingDeleteBookingId] = useState<string | null>(null);
  const [deletingBookingId, setDeletingBookingId] = useState<string | null>(null);

  // Confirmation state for deleting group
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);
  const [groupToDelete, setGroupToDelete] = useState<string | null>(null);

  // Form states
  const [mode, setMode] = useState<string>('offline');
  const [freeSessionDateTime, setFreeSessionDateTime] = useState('');
  const [capacity, setCapacity] = useState(5);
  const [isActive, setIsActive] = useState(true);
  const [instructors, setInstructors] = useState<string[]>([]);
  const [instructorContacts, setInstructorContacts] = useState<StaffContact[]>([]);
  const [supervisors, setSupervisors] = useState<StaffContact[]>([]);
  const [draftInstructors, setDraftInstructors] = useState<StaffContact[]>([]);
  const [draftSupervisors, setDraftSupervisors] = useState<StaffContact[]>([]);
  const [selectedInstructor, setSelectedInstructor] = useState('');
  const [supervisorPhone, setSupervisorPhone] = useState('');
  const [savingInstructors, setSavingInstructors] = useState(false);
  const [staffLoaded, setStaffLoaded] = useState(false);
  const staffDirty = staffLoaded && (JSON.stringify(draftInstructors) !== JSON.stringify(instructorContacts)
    || JSON.stringify(draftSupervisors) !== JSON.stringify(supervisors));
  const staffNavigationGuard = useUnsavedNavigationGuard(staffDirty);
  useEffect(() => onDirtyChange?.(staffDirty), [onDirtyChange, staffDirty]);
  useEffect(() => () => onDirtyChange?.(false), [onDirtyChange]);
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
  const subscribersTrigger = useRef<HTMLButtonElement | null>(null);
  const editorRef = useRef<HTMLDivElement>(null);
  const editorOverlayRef = useRef<HTMLDivElement>(null);
  const editorCloseRef = useRef<HTMLButtonElement>(null);
  useDialogViewport(editorOverlayRef, isModalOpen);

  const closeEditor = useCallback(() => setIsModalOpen(false), []);

  useEffect(() => {
    if (!isModalOpen) return;
    const previouslyFocused = document.activeElement as HTMLElement | null;
    const releaseScrollLock = lockBodyScroll();
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
      releaseScrollLock();
      previouslyFocused?.focus();
    };
  }, [closeEditor, isModalOpen]);

  const fetchBookings = useCallback(async (groupId: string) => {
    bookingsLoadController.current?.abort();
    const controller = new AbortController();
    bookingsLoadController.current = controller;
    setBookingsLoading(true);
    setBookingsLoadFailed(false);
    try {
      const response = await api.get<Booking[]>(`/api/group-appointments/${groupId}/bookings`, { signal: controller.signal });
      if (controller.signal.aborted) return;
      setSelectedGroup(current => current?.id === groupId ? { ...current, bookings: response.data } : current);
    } catch (error) {
      if (controller.signal.aborted) return;
      console.error(error);
      setBookingsLoadFailed(true);
    } finally {
      if (!controller.signal.aborted) setBookingsLoading(false);
    }
  }, []);

  const fetchGroups = useCallback(async () => {
    if (!activeProject) return;
    if (groupsLoadController.current) setStatsRefreshKey(previous => previous + 1);
    groupsLoadController.current?.abort();
    const controller = new AbortController();
    groupsLoadController.current = controller;
    try {
      setLoading(true);
      setGroupsLoadFailed(false);
      const response = await api.get<GroupSummary[]>('/api/group-appointments', { params: { courseType, includeBookings: false }, signal: controller.signal });
      if (controller.signal.aborted) return;
      const summaries = response.data.map(group => ({ ...group, bookings: [] }));
      setGroups(summaries);
      const selected = summaries.find(group => group.id === selectedGroupIdRef.current);
      setSelectedGroup(current => current && selected ? { ...selected, bookings: current.bookings } : null);
      if (selected) void fetchBookings(selected.id);
      else {
        selectedGroupIdRef.current = null;
        bookingsLoadController.current?.abort();
      }
    } catch (e) {
      if (controller.signal.aborted) return;
      console.error(e);
      setGroupsLoadFailed(true);
    } finally {
      if (!controller.signal.aborted) setLoading(false);
    }
  }, [activeProject, courseType, fetchBookings]);

  useEffect(() => {
    if (!activeProject) return;
    const token = localStorage.getItem('accessToken');
    if (!token) return;

    const signalR = new SignalRService(activeProject.id, token);
    let refreshTimer: ReturnType<typeof setTimeout> | undefined;
    signalR.registerOnGroupBookingUpdate(() => {
      clearTimeout(refreshTimer);
      refreshTimer = setTimeout(() => void fetchGroups(), 150);
    });
    void signalR.start().catch(error => {
      console.error('Group booking live updates failed to start:', error);
    });
    return () => {
      clearTimeout(refreshTimer);
      void signalR.stop();
    };
  }, [activeProject, fetchGroups]);

  const fetchInstructors = useCallback(async () => {
    if (!activeProject) return;
    staffLoadController.current?.abort();
    const controller = new AbortController();
    staffLoadController.current = controller;
    setStaffLoaded(false);
    try {
      const response = await api.get<InstructorsResponse>('/api/group-appointments/instructors', { signal: controller.signal });
      if (controller.signal.aborted) return;
      const nextInstructors = response.data.instructors || [];
      setInstructors(nextInstructors);
      const contacts = response.data.instructorContacts ?? nextInstructors.map(name => ({ id: `legacy:${name}`, name, phone: '' }));
      setInstructorContacts(contacts);
      setDraftInstructors(contacts);
      setSupervisors(response.data.supervisors ?? []);
      setDraftSupervisors(response.data.supervisors ?? []);
      setStaffLoaded(true);
    } catch (e) {
      if (controller.signal.aborted) return;
      console.error(e);
      setMessage({ type: 'error', text: 'فشل تحميل أسماء الإنستراكتورز.' });
    }
  }, [activeProject, setDraftInstructors, setDraftSupervisors]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    fetchGroups();
    void fetchInstructors();
    return () => {
      groupsLoadController.current?.abort();
      bookingsLoadController.current?.abort();
      staffLoadController.current?.abort();
    };
  }, [fetchGroups, fetchInstructors]);

  useEffect(() => () => exportController.current?.abort(), []);

  const handleSaveGroup = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!freeSessionDateTime || capacity <= 0) return;
    if (supervisorPhone.trim() && !normalizeManualBookingPhone(supervisorPhone)) {
      setEditorError('اكتب رقم واتساب صحيحًا للمسؤولة.');
      return;
    }
    if (!selectedInstructor.trim()) {
      setEditorError('اختر الدكتور المسؤول عن المجموعة.');
      return;
    }

    try {
      setActionLoading(true);
      setEditorError('');
      setMessage(null);

      const freeSessionUtcDate = projectLocalToUtc(freeSessionDateTime, projectTimezone);

      const payload = {
        courseType,
        dateTime: freeSessionUtcDate,
        freeSessionDateTime: freeSessionUtcDate,
        capacity,
        isActive,
        mode,
        supervisorPhone: supervisorPhone.trim() ? normalizeManualBookingPhone(supervisorPhone) : '',
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
      setFreeSessionDateTime('');
      setCapacity(5);
      setIsActive(true);
      setMode('offline');
      setSelectedInstructor('');
      setEditingGroupId(null);
      void fetchGroups();
    } catch (e) {
      console.error(e);
      const errorText = e instanceof Error && e.message === 'INVALID_LOCAL_DATE'
        ? `هذا الوقت غير موجود في المنطقة الزمنية ${projectTimezone} بسبب تغيير التوقيت. اختر وقتًا آخر.`
        : e instanceof Error && e.message === 'AMBIGUOUS_LOCAL_DATE'
          ? `هذا الوقت يتكرر مرتين في المنطقة الزمنية ${projectTimezone} بسبب تغيير التوقيت. اختر وقتًا أوضح قبله أو بعده.`
          : 'تعذر حفظ المجموعة. حاول مرة أخرى.';
      setEditorError(errorText);
      setMessage({ type: 'error', text: errorText });
    } finally {
      setActionLoading(false);
    }
  };

  const handleStartEdit = (group: GroupAppointment) => {
    if (manualBookingSubmitting) return;
    setEditorError('');
    setEditingGroupId(group.id);
    setMode(group.mode || 'offline');
    setFreeSessionDateTime(projectDateTimeInput(group.freeSessionDateTime || group.dateTime, projectTimezone));
    setCapacity(group.capacity);
    setIsActive(group.isActive);
    setSelectedInstructor(group.instructorName || '');
    setSupervisorPhone(group.supervisorPhone || '');
    setIsModalOpen(true);
  };

  const openWorkspaceSection = (section: 'groups' | 'staff' | 'data') => {
    if (manualBookingSubmitting) return;
    setWorkspaceSection(section);
    if (managementMenuRef.current) {
      managementMenuRef.current.open = false;
      managementMenuRef.current.querySelector('summary')?.focus();
    }
  };

  const handleBack = () => {
    if (manualBookingSubmitting) return;
    if (staffDirty) {
      openWorkspaceSection('staff');
      setMessage({ type: 'error', text: 'احفظ تعديلات فريق العمل قبل مغادرة المجموعات.' });
      return;
    }
    onBack();
  };

  const openNewGroupEditor = () => {
    if (manualBookingSubmitting) return;
    setEditorError('');
    setEditingGroupId(null);
    setMode('offline');
    setFreeSessionDateTime('');
    setCapacity(5);
    setIsActive(true);
    setIsModalOpen(true);
    setSelectedInstructor(instructors[0] || '');
    setSupervisorPhone('');
  };

  const handleSaveInstructors = async () => {
    try {
      setSavingInstructors(true);
      const response = await api.put<InstructorsResponse>('/api/group-appointments/instructors', {
        instructorContacts: draftInstructors, supervisors: draftSupervisors
      });
      const savedInstructors = response.data.instructors || [];
      const contacts = response.data.instructorContacts ?? [];
      const savedSupervisors = response.data.supervisors ?? [];
      const selectedId = instructorContacts.find(contact => contact.name === selectedInstructor)?.id;
      const supervisorId = supervisors.find(contact => contact.phone === supervisorPhone)?.id;
      setSelectedInstructor(contacts.find(contact => contact.id === selectedId)?.name ?? '');
      setSupervisorPhone(savedSupervisors.find(contact => contact.id === supervisorId)?.phone ?? supervisorPhone);
      setInstructors(savedInstructors);
      setInstructorContacts(contacts);
      setDraftInstructors(contacts);
      setSupervisors(savedSupervisors);
      setDraftSupervisors(savedSupervisors);
      await fetchGroups();
      setMessage({ type: 'success', text: 'تم حفظ أسماء وأرقام فريق العمل.' });
    } catch (error) {
      const response = (error as { response?: { data?: { error?: string } } }).response;
      setMessage({ type: 'error', text: response?.data?.error || 'فشل حفظ فريق العمل. بياناتك ما زالت موجودة، حاول مرة أخرى.' });
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
      selectedGroupIdRef.current = null;
      bookingsLoadController.current?.abort();
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
        selectedGroupIdRef.current = null;
        bookingsLoadController.current?.abort();
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
    selectedGroupIdRef.current = group.id;
    setSelectedGroup(group);
    void fetchBookings(group.id);
    setSearchQuery('');
    setManualBookingOpen(false);
    resetManualBookingEditor();
    window.requestAnimationFrame(() => subscribersHeadingRef.current?.focus());
  };

  const closeSelectedGroup = () => {
    selectedGroupIdRef.current = null;
    bookingsLoadController.current?.abort();
    setSelectedGroup(null);
    setSearchQuery('');
    setManualBookingOpen(false);
    resetManualBookingEditor();
    window.requestAnimationFrame(() => subscribersTrigger.current?.focus());
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
    groupsLoadController.current?.abort();
    bookingsLoadController.current?.abort();
    setLoading(false);
    setBookingsLoading(false);
    setStatsRefreshKey(previous => previous + 1);
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
    const groupId = selectedGroup?.id;

    try {
      bookingsLoadController.current?.abort();
      setBookingsLoading(false);
      setDeletingBookingId(booking.id);
      setMessage(null);
      await api.delete(`/api/group-appointments/bookings/${booking.id}`);
      setMessage({ type: 'success', text: `تم حذف ${booking.customerName || 'المشترك'} من المجموعة.` });

      setSelectedGroup(prev => {
        if (!prev || prev.id !== groupId) return prev;
        return {
          ...prev,
          bookedCount: Math.max(0, prev.bookedCount - 1),
          bookings: prev.bookings.filter(item => item.id !== booking.id)
        };
      });

      setGroups(prev => prev.map(group => {
        if (group.id !== groupId) return group;
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
      bookingsLoadController.current?.abort();
      setBookingsLoading(false);
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
    if (group.bookedCount === 0 || exporting) return;
    const controller = new AbortController();
    exportController.current = controller;
    setExporting(true);
    try {
    const response = await api.get<Booking[]>(`/api/group-appointments/${group.id}/bookings`, { signal: controller.signal });
    if (controller.signal.aborted) return;
    const XLSX = await import('xlsx');
    if (controller.signal.aborted) return;

    const sortedBookings = [...response.data].sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
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
      .format(new Date(group.freeSessionDateTime || group.dateTime)).replace(':', '_');
    const fileName = `bookings_${group.mode}_${timeStr}.xlsx`;

    link.setAttribute('download', fileName);
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
    } catch (error) {
      if (!controller.signal.aborted) {
        console.error(error);
        setMessage({ type: 'error', text: 'تعذر تحميل الحجوزات للتصدير. حاول مرة أخرى.' });
      }
    } finally {
      if (!controller.signal.aborted) setExporting(false);
    }
  };

  const handleExportInactiveGroupsExcel = async () => {
    if (exporting) return;
    const controller = new AbortController();
    exportController.current = controller;
    setExporting(true);
    try {
    const response = await api.get<GroupAppointment[]>('/api/group-appointments', {
      params: { courseType, includeBookings: true }, signal: controller.signal,
    });
    if (controller.signal.aborted) return;
    const inactiveGroups = response.data.filter(group => !group.isActive && group.bookings.length > 0);
    const data = inactiveGroups.flatMap(group => {
      const groupMode = group.mode === 'online' ? 'أونلاين (Online)' : 'في السنتر (Offline)';
      return group.bookings.map(booking => ({
        'اسم الطالب': booking.customerName,
        'رقم الهاتف': `+${booking.customerPhone}`,
        'اسم المجموعة': group.name || groupMode,
        'نوع المجموعة': groupMode,
        'إنستراكتور الكورس': group.instructorName || '',
        'ميعاد السيشن المجانية': group.freeSessionDateTime ? new Date(group.freeSessionDateTime).toLocaleString('ar-EG', { timeZone: projectTimezone }) : '',
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
    if (controller.signal.aborted) return;
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
    } catch (error) {
      if (!controller.signal.aborted) {
        console.error(error);
        setMessage({ type: 'error', text: 'تعذر تحميل حجوزات المجموعات غير النشطة للتصدير.' });
      }
    } finally {
      if (!controller.signal.aborted) setExporting(false);
    }
  };

  const formatTime = (isoString: string) => {
    const dateObj = new Date(isoString);
    const dateStr = dateObj.toLocaleDateString('ar-EG', { weekday: 'long', month: 'long', day: 'numeric', timeZone: projectTimezone });
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

  const groupInstructors = [...new Set(groups.map(group => group.instructorName?.trim()).filter((name): name is string => Boolean(name)))].sort((a, b) => a.localeCompare(b, 'ar'));
  const instructorGroups = groups.filter(group => !instructorFilter || group.instructorName?.trim() === instructorFilter);
  const statusFilters = [
    { key: 'all' as const, label: 'الكل', tone: 'info' as const, icon: <ListFilter size={16} aria-hidden="true" />, count: instructorGroups.length },
    { key: 'available' as const, label: 'متاحة', tone: 'success' as const, icon: <UserCheck size={16} aria-hidden="true" />, count: instructorGroups.filter(group => getGroupStatus(group) === 'available').length },
    { key: 'full' as const, label: 'مكتملة', tone: 'warning' as const, icon: <Users size={16} aria-hidden="true" />, count: instructorGroups.filter(group => getGroupStatus(group) === 'full').length },
    { key: 'inactive' as const, label: 'غير نشطة', icon: <CirclePause size={16} aria-hidden="true" />, count: instructorGroups.filter(group => getGroupStatus(group) === 'inactive').length },
  ];
  const filteredGroups = instructorGroups.filter(group => groupStatusFilter === 'all' || getGroupStatus(group) === groupStatusFilter);
  const sortedGroups = [...filteredGroups].sort((a, b) => {
    const getRank = (group: GroupAppointment) => ({ available: 1, full: 2, inactive: 3 })[getGroupStatus(group)];
    const rankA = getRank(a);
    const rankB = getRank(b);
    if (rankA !== rankB) {
      return rankA - rankB;
    }
    return new Date(a.freeSessionDateTime || a.dateTime).getTime() - new Date(b.freeSessionDateTime || b.dateTime).getTime();
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
      <div className={styles.groupHeadingRow}>
        <div className={styles.groupHeadingContent}>
          {heading ?? <>
            <h2 className={styles.groupWorkspaceTitle}>{courseType === 'intensive' ? 'مواعيد الكورس المكثف' : 'مواعيد الكورس العادي'}</h2>
            <p className={styles.groupTimezone}>المواعيد بتوقيت <bdi>{projectTimezone}</bdi></p>
          </>}
        </div>
        <details ref={managementMenuRef} name="group-toolbar-menu" className={styles.groupManagementMenu} onKeyDown={event => {
          if (event.key !== 'Escape') return;
          event.currentTarget.open = false;
          event.currentTarget.querySelector('summary')?.focus();
          event.stopPropagation();
        }}>
          <summary aria-label="إدارة المجموعات"><Settings2 size={18} aria-hidden="true" />إدارة<ChevronDown size={15} aria-hidden="true" /></summary>
          <nav aria-label="مهام إدارة المجموعات">
            <button type="button" disabled={manualBookingSubmitting} onClick={() => openWorkspaceSection('groups')} aria-current={workspaceSection === 'groups' ? 'page' : undefined}>
              <Calendar size={18} aria-hidden="true" />المجموعات والحجوزات
            </button>
            <button type="button" disabled={manualBookingSubmitting} onClick={() => openWorkspaceSection('staff')} aria-current={workspaceSection === 'staff' ? 'page' : undefined}>
              <Users size={18} aria-hidden="true" />فريق العمل{staffDirty && <span className={styles.groupUnsavedMark}>غير محفوظ</span>}
            </button>
            <button type="button" disabled={manualBookingSubmitting} onClick={() => openWorkspaceSection('data')} aria-current={workspaceSection === 'data' ? 'page' : undefined}>
              <FileDown size={18} aria-hidden="true" />الاستيراد والتصدير
            </button>
            <button type="button" onClick={handleBack} disabled={manualBookingSubmitting}>
              <ArrowRight size={18} aria-hidden="true" />{backLabel}
            </button>
          </nav>
        </details>
      </div>
      <div className={styles.groupPrimaryToolbar}>
        {workspaceSection === 'groups' ? courseNavigation : <button type="button" disabled={manualBookingSubmitting}
          className={`${styles.btn} ${styles.btnSecondary}`} onClick={() => openWorkspaceSection('groups')}>
          <ArrowRight size={16} aria-hidden="true" />العودة للمجموعات
        </button>}
        <button type="button" onClick={openNewGroupEditor} disabled={manualBookingSubmitting} aria-label="إضافة مجموعة جديدة"
          className={`${styles.btn} ${styles.btnPrimary}`}><Plus size={17} aria-hidden="true" />مجموعة جديدة</button>
      </div>

      {staffDirty && workspaceSection !== 'staff' && <StatusMessage tone="warning">لديك تعديلات غير محفوظة في فريق العمل. ارجع إلى قسم فريق العمل لحفظها.</StatusMessage>}
      {message && <StatusMessage tone={message.type === 'error' ? 'danger' : 'success'}>{message.text}</StatusMessage>}

      <div id="groups-workspace-panel-groups" role="region" aria-label="المجموعات والحجوزات" hidden={workspaceSection !== 'groups'} className={styles.groupWorkspacePanel}>
      {activeProject && <GroupBookingStats projectId={activeProject.id} courseType={courseType} refreshKey={statsRefreshKey} />}
      {groupsLoadFailed && <StatusMessage tone="danger">
        <span>{groups.length ? 'تعذر تحديث المجموعات. البيانات المعروضة من آخر تحميل ناجح.' : 'فشل تحميل مجموعات المواعيد.'}</span>
        <button type="button" className={`${styles.btn} ${styles.btnSecondary}`} onClick={() => void fetchGroups()}>إعادة تحميل المجموعات</button>
      </StatusMessage>}
      {loading && groups.length > 0 && <p role="status" className={styles.sectionHint}>جاري تحميل مجموعات المواعيد…</p>}
      {loading && groups.length === 0 ? (
        <div role="status" aria-live="polite" style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 'var(--space-sm)', padding: '4rem 0' }}>
          <div className={styles.spinner} aria-hidden="true"></div>
          <span>جاري تحميل مجموعات المواعيد…</span>
        </div>
      ) : groupsLoadFailed && groups.length === 0 ? null : groups.length === 0 ? (
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
        <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 'var(--space-lg)' }}>
          {/* Booked Customers List (Conditional Panel) */}
          {participationGroup && participationGroup.projectId === activeProject?.id && (
            <GroupParticipationPanel key={`${participationGroup.projectId}:${participationGroup.id}`}
              groupId={participationGroup.id} groupName={participationGroup.name} timezone={timezone}
              onClose={() => { setParticipationGroup(null); participationTrigger.current?.focus(); }} />
          )}
          {selectedGroup && (
            <div id="group-subscribers-panel" className="glass-panel" style={{ padding: 'var(--space-lg)', minWidth: 0 }}>
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
                    disabled={!selectedGroup.isActive || selectedGroup.bookedCount >= selectedGroup.capacity || manualBookingSubmitting || bookingsLoading || bookingsLoadFailed}
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
                    disabled={selectedGroup.bookedCount === 0 || exporting}
                    className={`${styles.btn} ${styles.btnSecondary}`}
                  >
                    <Download size={12} />
                    {exporting ? 'جاري تجهيز التصدير…' : 'تصدير المشتركين (Excel)'}
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

              {bookingsLoading && <p role="status" className={styles.sectionHint}>جاري تحميل المشتركين…</p>}
              {bookingsLoadFailed && <StatusMessage tone="danger">
                <span>{selectedGroup.bookings.length ? 'تعذر تحديث المشتركين. البيانات المعروضة من آخر تحميل ناجح.' : 'تعذر تحميل المشتركين.'}</span>
                <button type="button" className={`${styles.btn} ${styles.btnSecondary}`} onClick={() => void fetchBookings(selectedGroup.id)}>إعادة تحميل المشتركين</button>
              </StatusMessage>}
              {(bookingsLoading || bookingsLoadFailed) && selectedGroup.bookings.length === 0 ? null : selectedGroup.bookings.length === 0 ? (
                <p style={{ fontSize: '0.85rem', color: 'hsl(var(--text-secondary))', textAlign: 'center', padding: '2rem 0' }}>
                  لا يوجد مشتركون مسجلون في هذه المجموعة بعد. استخدم زر «إضافة مشترك يدويًا» للبدء.
                </p>
              ) : filteredBookings.length === 0 ? (
                <p style={{ fontSize: '0.85rem', color: 'hsl(var(--accent-danger))', textAlign: 'center', padding: '2rem 0', fontWeight: 600 }}>
                  لم يتم العثور على نتائج تطابق البحث &quot;{searchQuery}&quot;
                </p>
              ) : (
                <div className={styles.groupSubscribersTableRegion} role="region" aria-label="سجل مشتركي المجموعة" tabIndex={0}>
                  <table className={styles.groupSubscribersTable}>
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
                          <td data-label="اسم العميل" style={{ fontWeight: 600 }}>{booking.customerName}</td>
                          <td data-label="رقم الواتساب"><bdi dir="ltr">+{booking.customerPhone}</bdi></td>
                          <td data-label="تاريخ الحجز" style={{ color: 'hsl(var(--text-secondary))' }}>
                            {new Date(booking.createdAt).toLocaleDateString('ar-EG', { year: 'numeric', month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit', timeZone: projectTimezone })}
                          </td>
                          <td>
                            <label className={styles.bookingStatusControl}>
                            <input
                              type="checkbox"
                              aria-label={`تحديد حضور ${booking.customerName}`}
                              checked={booking.isAttended || false}
                              onChange={(e) => handleToggleBookingStatus(booking.id, { isAttended: e.target.checked })}
                              disabled={manualBookingSubmitting}
                              style={{ width: '16px', height: '16px', cursor: manualBookingSubmitting ? 'not-allowed' : 'pointer' }}
                            />
                            <span>الحضور</span>
                            </label>
                          </td>
                          <td>
                            <label className={styles.bookingStatusControl}>
                            <input
                              type="checkbox"
                              aria-label={`تحديد دفع ${booking.customerName}`}
                              checked={booking.isPaid || false}
                              onChange={(e) => handleToggleBookingStatus(booking.id, { isPaid: e.target.checked })}
                              disabled={manualBookingSubmitting}
                              style={{ width: '16px', height: '16px', cursor: manualBookingSubmitting ? 'not-allowed' : 'pointer' }}
                            />
                            <span>الدفع</span>
                            </label>
                          </td>
                          <td>
                            <div style={{ display: 'flex', justifyContent: 'center', gap: '6px', flexWrap: 'wrap' }}>
                              <a
                                href={`/inbox?customerId=${booking.customerId}&startConversation=1`}
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

          {/* List of Groups */}
          <div className={styles.groupRecords}>
            <h3 className={styles.tableCaption}>المجموعات الحالية</h3>

            <div className={styles.groupStatusFilters}>
              <FilterTabs compactOnMobile label="حالة المجموعات" options={statusFilters} selected={groupStatusFilter} onChange={setGroupStatusFilter} />
            </div>
            <div className={styles.groupListTools}>
            <p aria-live="polite" aria-atomic="true" className={styles.groupResultCount}>
              {filteredGroups.length} مجموعة <span className={styles.groupContextDivider}>·</span> <bdi>{projectTimezone}</bdi>
            </p>

              <details className={styles.groupFilterDisclosure}>
                <summary><SlidersHorizontal size={16} aria-hidden="true" />المدرّب
                  {instructorFilter && <span className={styles.filterActiveCount}>{instructorFilter}</span>}
                </summary>
            <div className={styles.groupFilters}>
              <div style={{ flex: '1 1 220px', minWidth: 0 }}>
                <label className={styles.label} htmlFor="group-instructor-filter">الإنستراكتور</label>
                <select id="group-instructor-filter" className={styles.select} value={instructorFilter} onChange={event => setInstructorFilter(event.target.value)}>
                  <option value="">كل الإنستراكتورز</option>
                  {groupInstructors.map(instructor => <option key={instructor} value={instructor}>{instructor}</option>)}
                </select>
              </div>
              <button type="button" className={`${styles.btn} ${styles.btnSecondary}`} disabled={!instructorFilter} onClick={() => setInstructorFilter('')}>
                كل المدربين
              </button>
            </div>
              </details>
            </div>

            <div style={{ overflowX: 'auto' }}>
              <table className={styles.groupTable}>
                <caption className={styles.tableCaption}>المجموعات الحالية ومواعيدها وسعتها وحالتها</caption>
                <thead>
                  <tr style={{ borderBottom: '1px solid var(--border-subtle)' }}>
                    <th scope="col" style={{ padding: '12px 8px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>المجموعة</th>
                    <th scope="col" style={{ padding: '12px 8px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>المواعيد والمدرّب</th>
                    <th scope="col" style={{ padding: '12px 8px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>الحجوزات / السعة</th>
                    <th scope="col" style={{ padding: '12px 8px', fontSize: '0.8rem', color: 'var(--text-soft)', textAlign: 'center' }}>الحالة</th>
                    <th scope="col" style={{ padding: '12px 8px', fontSize: '0.8rem', color: 'var(--text-soft)', textAlign: 'center' }}>الإجراءات</th>
                  </tr>
                </thead>
                <tbody>
                  {sortedGroups.length === 0 && (
                    <tr><td colSpan={5} style={{ padding: 'var(--space-xl)', textAlign: 'center', color: 'hsl(var(--text-secondary))' }}>لا توجد مجموعات مطابقة. جرّب تغيير الإنستراكتور أو الحالة، أو امسح الفلاتر.</td></tr>
                  )}
                  {sortedGroups.map((group) => {
                    const percent = Math.min(100, Math.round((group.bookedCount / group.capacity) * 100));
                    const status = getGroupStatus(group);
                    const slotsLeft = Math.max(0, group.capacity - group.bookedCount);

                    return (
                      <tr key={group.id} data-selected={selectedGroup?.id === group.id || participationGroup?.id === group.id}>
                        <td data-label="المجموعة" className={styles.groupIdentity}>
                          <strong>{group.name || (group.mode === 'online' ? 'مجموعة أونلاين' : 'مجموعة في السنتر')}</strong>
                          <span>{group.mode === 'online' ? 'أونلاين' : 'في السنتر'}</span>
                        </td>
                        <td data-label="الموعد والمدرّب" className={styles.groupSchedule}>
                          <div className={styles.groupScheduleDetails}>
                            <span className={styles.groupDateLabel}>الحصة المجانية</span>
                            <strong className={styles.groupDate}><Clock size={14} aria-hidden="true" />{formatTime(group.freeSessionDateTime || group.dateTime)}</strong>
                            {group.supervisorPhone && <div>المسؤولة: <bdi>{group.supervisorPhone}</bdi>
                              {group.staffSyncError ? <span role="status"> · تعذرت إضافة فريق العمل، راجع صلاحيات وخصوصية واتساب</span>
                                : !group.staffSyncedAtUtc ? <span> · إضافة فريق العمل قيد الانتظار</span> : <span> · تمت إضافة فريق العمل</span>}
                            </div>}
                            {group.instructorName && (
                              <span className={styles.groupInstructor}>
                                المدرّب: {group.instructorName}
                              </span>
                            )}
                          </div>
                        </td>
                        <td data-label="الحجوزات والسعة">
                          <div className={styles.groupAvailability} data-status={status}>
                            <div className={styles.groupCapacityNumbers}>
                              <span><strong>{group.bookedCount}</strong> / {group.capacity} مشترك</span>
                              {status === 'available' && <span>{slotsLeft} مقعد متاح</span>}
                            </div>
                            <div className={styles.groupCapacityTrack} role="progressbar" aria-label={`إشغال ${group.name}`}
                              aria-valuemin={0} aria-valuemax={group.capacity} aria-valuenow={Math.min(group.bookedCount, group.capacity)}
                              aria-valuetext={`${group.bookedCount} من ${group.capacity}`}>
                              <span style={{ width: `${percent}%` }} />
                            </div>
                          </div>
                        </td>
                        <td data-label="الحالة">
                          <span className={styles.groupStatusBadge} data-status={status}>{groupStatusLabels[status]}</span>
                        </td>
                        <td data-label="الإجراءات" className={styles.groupActionsCell}>
                          <div className={styles.groupRowActions}>
                            <button
                              type="button"
                              onClick={event => { subscribersTrigger.current = event.currentTarget; handleSelectGroup(group); }}
                              disabled={manualBookingSubmitting}
                              aria-expanded={selectedGroup?.id === group.id}
                              aria-controls="group-subscribers-panel"
                              className={`${styles.btn} ${styles.btnPrimary} ${styles.groupPrimaryAction}`}
                            >
                              <Users size={12} />
                              المشتركين ({group.bookedCount})
                            </button>
                            <button type="button" className={`${styles.btn} ${styles.btnSecondary}`}
                              style={{ padding: '4px 8px', fontSize: '0.75rem' }}
                              disabled={manualBookingSubmitting || !activeProject}
                              aria-expanded={participationGroup?.id === group.id && participationGroup?.projectId === activeProject?.id}
                              aria-controls="group-participation-panel"
                              onClick={event => {
                                participationTrigger.current = event.currentTarget;
                                if (activeProject) setParticipationGroup(current => current?.id === group.id && current.projectId === activeProject.id
                                  ? null : { id: group.id, name: group.name, projectId: activeProject.id });
                              }}>
                              <UserCheck size={12} /> متابعة دخول الجروب
                            </button>
                            <details className={styles.groupMoreActions}>
                              <summary><SlidersHorizontal size={16} aria-hidden="true" />إجراءات أخرى</summary>
                              <div>
                            <button
                              type="button"
                              aria-label={`تعديل مجموعة ${group.name}`}
                              onClick={() => handleStartEdit(group)}
                              disabled={manualBookingSubmitting}
                              className={`${styles.btn} ${styles.btnSecondary}`}
                              style={{ padding: '4px 8px', fontSize: '0.75rem' }}
                            >
                              <Edit3 size={14} /> تعديل
                            </button>
                             <button
                               type="button"
                               aria-label={`حذف مجموعة ${group.name}`}
                               onClick={() => triggerDeleteGroup(group.id)}
                               className={`${styles.btn} ${styles.btnDanger}`}
                               style={{ padding: '4px 8px', fontSize: '0.75rem' }}
                               disabled={actionLoading || manualBookingSubmitting}
                             >
                               <Trash2 size={14} /> حذف
                             </button>
                                <button type="button" onClick={() => handleToggleGroup(group)} disabled={actionLoading || manualBookingSubmitting}
                                  className={`${styles.btn} ${styles.btnSecondary}`}>{group.isActive ? 'إيقاف الحجز' : 'تفعيل الحجز'}</button>
                              </div>
                            </details>
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>


        </div>
      )}

      </div>

      <div id="groups-workspace-panel-staff" role="region" aria-label="إدارة فريق العمل" hidden={workspaceSection !== 'staff'} className={styles.groupWorkspacePanel}>
      <section className={styles.staffDirectory} aria-label="فريق المجموعات">
        <h3 className={styles.sectionTitle}>فريق المجموعات</h3>
        <GroupStaffDirectoryEditor title="الإنستراكتورز" hint="احفظ الاسم ورقم واتساب لإضافة دكتور كل مجموعة كعضو. يمكنك استكمال أرقام الأسماء القديمة لاحقًا."
          contacts={draftInstructors} onChange={setDraftInstructors} disabled={savingInstructors || !staffLoaded} />
        <GroupStaffDirectoryEditor title="مسؤولات المتابعة" hint="احفظ الاسم والرقم، ثم اختر المسؤولة لكل مجموعة. تُضاف كعضوة ويصلها كل حجز جديد على واتساب."
          contacts={draftSupervisors} onChange={setDraftSupervisors} disabled={savingInstructors || !staffLoaded} />
        <button type="button" onClick={handleSaveInstructors} className={`${styles.btn} ${styles.btnPrimary}`}
          disabled={savingInstructors || !staffLoaded} style={{ alignSelf: 'start' }}>
          {!staffLoaded ? 'تحميل فريق العمل...' : savingInstructors ? 'جاري الحفظ...' : 'حفظ فريق العمل'}
        </button>
      </section>
      </div>
      <div id="groups-workspace-panel-data" role="region" aria-label="الاستيراد والتصدير" hidden={workspaceSection !== 'data'} className={styles.groupWorkspacePanel}>
        <section className={styles.groupExport} aria-label="تصدير الحجوزات">
          <div><h3 className={styles.sectionTitle}>تصدير الحجوزات</h3><p className={styles.sectionHint}>نزّل حجوزات المجموعات غير النشطة في ملف Excel.</p></div>
          <button type="button" onClick={handleExportInactiveGroupsExcel} disabled={exporting} className={`${styles.btn} ${styles.btnSecondary}`}>
            <Download size={16} />{exporting ? 'جاري تجهيز التصدير…' : 'تصدير حجوزات المجموعات غير النشطة'}
          </button>
        </section>
      <section className={styles.importPanel} aria-label="استيراد المحظورين للدفع">
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
      </section>
      </div>

      {/* Add/Edit Modal */}
      {isModalOpen && (
        <div ref={editorOverlayRef} className={`${styles.overlay} ${styles.groupEditorOverlay}`} onMouseDown={(event) => { if (event.target === event.currentTarget) closeEditor(); }}>
          <div ref={editorRef} className={`${styles.modal} ${styles.groupEditor}`} role="dialog" aria-modal="true" aria-labelledby="group-editor-title" aria-describedby="group-editor-timezone">
            <div className={styles.groupEditorHeader}>
              <div>
                <h3 id="group-editor-title" className={styles.modalTitle}>
                  {editingGroupId ? 'تعديل المجموعة' : 'مجموعة جديدة'}
                </h3>
                <p id="group-editor-timezone" className={styles.groupEditorTimezone}>
                  <Clock size={15} aria-hidden="true" />
                  {projectTimezone === 'Africa/Cairo' ? 'المواعيد بتوقيت القاهرة' : <>المواعيد بتوقيت <b dir="ltr">{projectTimezone}</b></>}
                </p>
              </div>
              <button
                ref={editorCloseRef}
                type="button"
                aria-label="إغلاق نموذج المجموعة"
                onClick={closeEditor}
                className={styles.closeBtn}
              >
                <X size={20} aria-hidden="true" />
              </button>
            </div>

            <form onSubmit={handleSaveGroup} className={styles.groupEditorForm}>
              <div className={styles.groupEditorBody}>
                <div className={styles.groupEditorGrid}>
                  <div className={styles.formGroup}>
                    <label className={styles.label} htmlFor="group-editor-mode">نوع المجموعة</label>
                    <select
                      id="group-editor-mode"
                      value={mode}
                      onChange={(e) => setMode(e.target.value)}
                      className={styles.select}
                      required
                    >
                      <option value="offline">في السنتر</option>
                      <option value="online">أونلاين</option>
                    </select>
                  </div>

                  <div className={styles.formGroup}>
                    <label className={styles.label} htmlFor="group-editor-instructor">دكتور المجموعة</label>
                    <select
                      id="group-editor-instructor"
                      value={selectedInstructor}
                      onChange={(e) => setSelectedInstructor(e.target.value)}
                      className={styles.select}
                      required
                    >
                      <option value="">اختر الدكتور</option>
                      {selectedInstructor && !instructors.includes(selectedInstructor) && <option value={selectedInstructor}>{selectedInstructor} (محفوظ سابقًا)</option>}
                      {instructors.map((instructor) => (
                        <option key={instructor} value={instructor}>{instructor}</option>
                      ))}
                    </select>
                    {instructors.length === 0 && (
                      <span style={{ fontSize: '0.72rem', color: 'hsl(var(--accent-danger))' }}>
                        أضف دكتورًا من قائمة فريق العمل أولًا.
                      </span>
                    )}
                  </div>
                </div>

                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="group-supervisor-phone">المسؤولة عن المجموعة</label>
                  <select id="group-supervisor-phone" value={supervisorPhone} onChange={event => setSupervisorPhone(event.target.value)}
                    className={styles.select} aria-describedby="group-supervisor-help">
                    <option value="">بدون مسؤولة</option>
                    {supervisorPhone && !supervisors.some(contact => contact.phone === supervisorPhone) && (
                      <option value={supervisorPhone}>الرقم المحفوظ سابقًا: +{supervisorPhone}</option>
                    )}
                    {supervisors.map(contact => <option key={contact.id} value={contact.phone}>{contact.name} (+{contact.phone})</option>)}
                  </select>
                  <small id="group-supervisor-help">اختياري. تُضاف للجروب ويصلها كل حجز جديد على الخاص.</small>
                </div>

                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="group-editor-free-session">موعد الجلسة المجانية</label>
                  <input
                    id="group-editor-free-session"
                    type="datetime-local"
                    value={freeSessionDateTime}
                    onChange={(e) => setFreeSessionDateTime(e.target.value)}
                    className={styles.input}
                    aria-describedby="group-editor-schedule-help"
                    required
                  />
                  <small id="group-editor-schedule-help">الدكتور بيحدد باقي مواعيد الكورس في الجلسة المجانية.</small>
                </div>

                <div className={styles.formGroup}>
                  <label className={styles.label} htmlFor="group-editor-capacity">الحد الأقصى للمشتركين</label>
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

                <div className={styles.formGroup}>
                  <label className={`${styles.checkboxGroup} ${styles.groupEditorAvailability}`}>
                    <input
                      type="checkbox"
                      checked={isActive}
                      onChange={(e) => setIsActive(e.target.checked)}
                      className={styles.checkbox}
                    />
                    <span>متاحة للحجز</span>
                  </label>
                </div>
              </div>
              <div className={styles.groupEditorFooter}>
                {editorError && <p className={styles.groupEditorError} role="alert">{editorError}</p>}
                <div className={styles.groupEditorActions}>
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
                    {actionLoading ? 'جاري الحفظ...' : editingGroupId ? 'حفظ التعديلات' : 'إنشاء المجموعة'}
                  </button>
                </div>
              </div>
            </form>
          </div>
        </div>
      )}

      <ConfirmDialog
        isOpen={staffNavigationGuard.navigationBlocked}
        title="تعديلات فريق العمل غير محفوظة"
        message="مغادرة الصفحة ستفقد تعديلات الأسماء والأرقام التي لم تحفظها."
        confirmLabel="مغادرة دون حفظ" cancelLabel="العودة للتعديلات"
        onConfirm={staffNavigationGuard.confirmNavigation} onCancel={staffNavigationGuard.cancelNavigation}
      />
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
