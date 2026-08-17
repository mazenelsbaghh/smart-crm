'use client';

import React, { useEffect, useState, useCallback } from 'react';
import { api } from '../../services/api';
import { useAuth } from '../../context/auth-context';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import * as XLSX from 'xlsx';
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
}

export default function GroupAppointmentsManager({ onBack }: GroupAppointmentsManagerProps) {
  const { activeProject } = useAuth();
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
  const [paidImportResult, setPaidImportResult] = useState<PaidBlacklistImportResult | null>(null);
  
  const [searchQuery, setSearchQuery] = useState('');

  const DAY_NAMES = ['الأحد', 'الاثنين', 'الثلاثاء', 'الأربعاء', 'الخميس', 'الجمعة', 'السبت'];
  const DAY_NAMES_SHORT = ['أحد', 'اثنين', 'ثلاثاء', 'أربعاء', 'خميس', 'جمعة', 'سبت'];

  const fetchGroups = useCallback(async () => {
    if (!activeProject) return;
    try {
      setLoading(true);
      const response = await api.get<GroupAppointment[]>('/api/group-appointments');
      setGroups(response.data);
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

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setSearchQuery('');
  }, [selectedGroup?.id]);

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

      // Parse full local date-time value
      const dateObj = new Date(dateTime);
      const utcDate = dateObj.toISOString();
      const freeSessionUtcDate = new Date(freeSessionDateTime).toISOString();
      const courseSecondUtcDate = new Date(courseSecondDateTime).toISOString();

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
      setMessage({ type: 'error', text: 'حدث خطأ أثناء حفظ المجموعة.' });
    } finally {
      setActionLoading(false);
    }
  };

  const handleStartEdit = (group: GroupAppointment) => {
    setEditingGroupId(group.id);
    setMode(group.mode || 'offline');
    setDateTime(formatDateTimeLocal(group.dateTime));
    setFreeSessionDateTime(group.freeSessionDateTime ? formatDateTimeLocal(group.freeSessionDateTime) : '');
    setCourseSecondDateTime(group.courseSecondDateTime ? formatDateTimeLocal(group.courseSecondDateTime) : '');
    setCapacity(group.capacity);
    setIsActive(group.isActive);
    setSelectedDays(group.days ? group.days.split(',').filter(Boolean).map(Number) : []);
    setSelectedInstructor(group.instructorName || '');
    setIsModalOpen(true);
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

  const handleDownloadPaidTemplate = () => {
    const worksheet = XLSX.utils.aoa_to_sheet([
      ['رقم الهاتف'],
      ['01068690092'],
      ['20122334455']
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
    const file = e.target.files?.[0];
    if (!file) return;

    try {
      setPaidImportResult(null);
      setPaidImportFileName(file.name);
      const binaryContent = await readFileAsBinaryString(file);
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

  const handleConfirmPaidImport = async () => {
    if (!activeProject || paidImportPhones.length === 0) return;

    try {
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

  const handleDeleteBooking = async (booking: Booking) => {
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

  const handleExportExcel = (group: GroupAppointment) => {
    if (!group || group.bookings.length === 0) return;
    
    const sortedBookings = [...group.bookings].sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
    const data = sortedBookings.map(b => ({
      'اسم الطالب': b.customerName,
      'رقم الهاتف': `+${b.customerPhone}`,
      'تاريخ الحجز': new Date(b.createdAt).toLocaleString('ar-EG'),
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
    
    const formattedDate = new Date(group.dateTime);
    const timeStr = `${formattedDate.getHours()}_${formattedDate.getMinutes()}`;
    const fileName = `bookings_${group.mode}_${timeStr}.xlsx`;
    
    link.setAttribute('download', fileName);
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  };

  const handleExportInactiveGroupsExcel = () => {
    const inactiveGroups = groups.filter(group => !group.isActive && group.bookings.length > 0);
    const data = inactiveGroups.flatMap(group => {
      const groupMode = group.mode === 'online' ? 'أونلاين (Online)' : 'في السنتر (Offline)';
      return group.bookings.map(booking => ({
        'اسم الطالب': booking.customerName,
        'رقم الهاتف': `+${booking.customerPhone}`,
        'اسم المجموعة': group.name || groupMode,
        'نوع المجموعة': groupMode,
        'إنستراكتور الكورس': group.instructorName || '',
        'ميعاد السيشن المجانية': group.freeSessionDateTime ? new Date(group.freeSessionDateTime).toLocaleString('ar-EG') : '',
        'ميعاد السيشن الأولى للكورس': new Date(group.dateTime).toLocaleString('ar-EG'),
        'ميعاد السيشن الثانية للكورس': group.courseSecondDateTime ? new Date(group.courseSecondDateTime).toLocaleString('ar-EG') : '',
        'أيام الكورس': formatDays(group.days),
        'تاريخ الحجز': new Date(booking.createdAt).toLocaleString('ar-EG'),
        'حالة الحضور': booking.isAttended ? 'حضر' : 'لم يحضر',
        'حالة الدفع': booking.isPaid ? 'دفع' : 'لم يدفع'
      }));
    });

    if (data.length === 0) {
      setMessage({ type: 'error', text: 'لا توجد حجوزات داخل مجموعات غير نشطة للتصدير.' });
      return;
    }

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
    const dateStr = dateObj.toLocaleDateString('ar-EG', { month: 'long', day: 'numeric' });
    const timeStr = dateObj.toLocaleTimeString('ar-EG', { hour: '2-digit', minute: '2-digit' });
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

  const formatDateTimeLocal = (isoString: string) => {
    const localDate = new Date(isoString);
    const year = localDate.getFullYear();
    const month = (localDate.getMonth() + 1).toString().padStart(2, '0');
    const date = localDate.getDate().toString().padStart(2, '0');
    const hours = localDate.getHours().toString().padStart(2, '0');
    const mins = localDate.getMinutes().toString().padStart(2, '0');
    return `${year}-${month}-${date}T${hours}:${mins}`;
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
          onClick={onBack}
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
            onClick={() => {
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
            }}
            className={`${styles.btn} ${styles.btnPrimary}`}
            style={{ padding: '8px 16px', fontSize: '0.85rem' }}
          >
            <Plus size={16} />
            إضافة مجموعة جديدة
          </button>
        </div>
      </div>

      {message && (
        <div className="glass-panel" style={{ 
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
        <textarea
          value={instructorsText}
          onChange={(e) => setInstructorsText(e.target.value)}
          className={styles.input}
          rows={3}
          placeholder={'مثال:\nأحمد علي\nمنى حسن'}
          style={{ resize: 'vertical', minHeight: '86px', lineHeight: 1.7 }}
        />
      </div>

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
            style={{ padding: '8px 14px', fontSize: '0.82rem', cursor: paidImporting ? 'not-allowed' : 'pointer' }}
          >
            <Upload size={14} />
            اختيار ملف Excel
            <input
              type="file"
              accept=".xlsx,.xls"
              onChange={handlePaidImportFileChange}
              disabled={paidImporting}
              style={{ display: 'none' }}
            />
          </label>
          <button
            type="button"
            onClick={handleConfirmPaidImport}
            className={`${styles.btn} ${styles.btnPrimary}`}
            disabled={paidImporting || paidImportPhones.length === 0}
            style={{ padding: '8px 16px', fontSize: '0.82rem', opacity: paidImportPhones.length === 0 ? 0.55 : 1 }}
          >
            {paidImporting ? 'جاري التنفيذ...' : 'تأكيد الحظر'}
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
            background: 'linear-gradient(135deg, var(--accent-soft) 0%, rgba(5, 7, 12, 0.4) 100%)',
            border: '1px solid var(--accent-soft-strong)',
            borderRadius: 'var(--radius-lg)',
            boxShadow: 'var(--shadow-neon)',
            position: 'relative',
            overflow: 'hidden'
          }}>
            <div style={{
              position: 'absolute',
              top: '-20px',
              right: '-20px',
              width: '60px',
              height: '60px',
              background: 'hsl(var(--accent-primary))',
              filter: 'blur(30px)',
              opacity: 0.15,
              pointerEvents: 'none'
            }}></div>
            
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
              <Users size={24} />
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
            background: 'linear-gradient(135deg, rgba(34, 197, 94, 0.08) 0%, rgba(5, 7, 12, 0.4) 100%)',
            border: '1px solid rgba(34, 197, 94, 0.15)',
            borderRadius: 'var(--radius-lg)',
            boxShadow: '0 8px 32px 0 rgba(34, 197, 94, 0.03)',
            position: 'relative',
            overflow: 'hidden'
          }}>
            <div style={{
              position: 'absolute',
              top: '-20px',
              right: '-20px',
              width: '60px',
              height: '60px',
              background: 'rgb(34, 197, 94)',
              filter: 'blur(30px)',
              opacity: 0.12,
              pointerEvents: 'none'
            }}></div>

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
              <Calendar size={24} />
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
            background: 'linear-gradient(135deg, rgba(249, 115, 22, 0.08) 0%, rgba(5, 7, 12, 0.4) 100%)',
            border: '1px solid rgba(249, 115, 22, 0.15)',
            borderRadius: 'var(--radius-lg)',
            boxShadow: '0 8px 32px 0 rgba(249, 115, 22, 0.03)',
            position: 'relative',
            overflow: 'hidden'
          }}>
            <div style={{
              position: 'absolute',
              top: '-20px',
              right: '-20px',
              width: '60px',
              height: '60px',
              background: 'rgb(249, 115, 22)',
              filter: 'blur(30px)',
              opacity: 0.12,
              pointerEvents: 'none'
            }}></div>

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
              <UserCheck size={24} />
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
            background: 'linear-gradient(135deg, rgba(168, 85, 247, 0.08) 0%, rgba(5, 7, 12, 0.4) 100%)',
            border: '1px solid rgba(168, 85, 247, 0.15)',
            borderRadius: 'var(--radius-lg)',
            boxShadow: '0 8px 32px 0 rgba(168, 85, 247, 0.03)',
            position: 'relative',
            overflow: 'hidden'
          }}>
            <div style={{
              position: 'absolute',
              top: '-20px',
              right: '-20px',
              width: '60px',
              height: '60px',
              background: 'rgb(168, 85, 247)',
              filter: 'blur(30px)',
              opacity: 0.12,
              pointerEvents: 'none'
            }}></div>

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
              <Clock size={24} />
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
        <div style={{ display: 'flex', justifyContent: 'center', padding: '4rem 0' }}>
          <div className={styles.spinner}></div>
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
                <thead>
                  <tr style={{ borderBottom: '1px solid var(--border-subtle)' }}>
                    <th style={{ padding: '12px 8px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>نوع المجموعة</th>
                    <th style={{ padding: '12px 8px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>المواعيد والإنستراكتور</th>
                    <th style={{ padding: '12px 8px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>الحجوزات / السعة</th>
                    <th style={{ padding: '12px 8px', fontSize: '0.8rem', color: 'var(--text-soft)', textAlign: 'center' }}>الحالة</th>
                    <th style={{ padding: '12px 8px', fontSize: '0.8rem', color: 'var(--text-soft)', textAlign: 'center' }}>الإجراءات</th>
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
                                السيشن المجانية: {formatTime(group.freeSessionDateTime)} مع دكتور مصطفى
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
                            <div style={{ height: '6px', background: 'hsl(var(--bg-tertiary))', borderRadius: '3px', overflow: 'hidden' }}>
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
                            onClick={() => handleToggleGroup(group)}
                            disabled={actionLoading}
                            style={{
                              padding: '4px 12px',
                              fontSize: '0.75rem',
                              border: 'none',
                              borderRadius: '12px',
                              cursor: 'pointer',
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
                              onClick={() => setSelectedGroup(group)}
                              className={`${styles.btn} ${styles.btnSecondary}`}
                              style={{ padding: '4px 8px', fontSize: '0.75rem', backgroundColor: 'var(--accent-soft)', color: 'var(--accent)' }}
                            >
                              <Users size={12} />
                              المشتركين ({group.bookedCount})
                            </button>
                            <button
                              onClick={() => handleStartEdit(group)}
                              className={`${styles.btn} ${styles.btnSecondary}`}
                              style={{ padding: '4px 8px', fontSize: '0.75rem' }}
                            >
                              <Edit3 size={12} />
                            </button>
                             <button
                               type="button"
                               onClick={() => triggerDeleteGroup(group.id)}
                               className={`${styles.btn} ${styles.btnDanger}`}
                               style={{ padding: '4px 8px', fontSize: '0.75rem' }}
                               disabled={actionLoading}
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
            <div className="glass-panel" style={{ padding: 'var(--space-lg)' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 'var(--space-md)' }}>
                <h3 style={{ fontSize: '1rem', fontWeight: 600, color: 'hsl(var(--text-primary))' }}>
                  المشتركون في مَجموعة: <span style={{ color: 'hsl(var(--accent-primary))' }}>{selectedGroup.mode === 'online' ? 'أونلاين (Online)' : 'في السنتر (Offline)'}</span>
                </h3>
                <div style={{ display: 'flex', gap: '8px' }}>
                  <button
                    onClick={() => handleExportExcel(selectedGroup)}
                    className={`${styles.btn} ${styles.btnSecondary}`}
                    style={{ padding: '4px 10px', fontSize: '0.75rem', display: 'flex', alignItems: 'center', gap: '4px', backgroundColor: 'rgba(34, 197, 94, 0.08)', color: 'rgb(34, 197, 94)', borderColor: 'rgba(34, 197, 94, 0.2)' }}
                  >
                    <Download size={12} />
                    تصدير المشتركين (Excel)
                  </button>
                  <button
                    onClick={() => setSelectedGroup(null)}
                    className={`${styles.btn} ${styles.btnSecondary}`}
                    style={{ padding: '4px 10px', fontSize: '0.75rem' }}
                  >
                    إغلاق القائمة
                  </button>
                </div>
              </div>

              <div style={{ position: 'relative', marginBottom: 'var(--space-md)' }}>
                <input
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
                  لا يوجد مشتركون مسجلون في هذه المجموعة بعد.
                </p>
              ) : filteredBookings.length === 0 ? (
                <p style={{ fontSize: '0.85rem', color: 'hsl(var(--accent-danger))', textAlign: 'center', padding: '2rem 0', fontWeight: 600 }}>
                  لم يتم العثور على نتائج تطابق البحث &quot;{searchQuery}&quot;
                </p>
              ) : (
                <div style={{ overflowX: 'auto' }}>
                  <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'right' }}>
                    <thead>
                      <tr style={{ borderBottom: '1px solid var(--border-subtle)' }}>
                        <th style={{ padding: '10px 6px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>اسم العميل</th>
                        <th style={{ padding: '10px 6px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>رقم الواتساب</th>
                        <th style={{ padding: '10px 6px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>تاريخ الحجز</th>
                        <th style={{ padding: '10px 6px', fontSize: '0.8rem', color: 'var(--text-soft)', textAlign: 'center' }}>حضور</th>
                        <th style={{ padding: '10px 6px', fontSize: '0.8rem', color: 'var(--text-soft)', textAlign: 'center' }}>دفع</th>
                        <th style={{ padding: '10px 6px', fontSize: '0.8rem', color: 'var(--text-soft)', textAlign: 'center' }}>الإجراءات</th>
                      </tr>
                    </thead>
                    <tbody>
                      {filteredBookings.map((booking) => (
                        <tr key={booking.id} style={{ borderBottom: '1px solid var(--border-subtle)' }}>
                          <td style={{ padding: '12px 6px', fontWeight: 600 }}>{booking.customerName}</td>
                          <td style={{ padding: '12px 6px', fontSize: '0.85rem' }}>+{booking.customerPhone}</td>
                          <td style={{ padding: '12px 6px', fontSize: '0.85rem', color: 'hsl(var(--text-secondary))' }}>
                            {new Date(booking.createdAt).toLocaleDateString('ar-EG', { year: 'numeric', month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' })}
                          </td>
                          <td style={{ padding: '12px 6px', textAlign: 'center' }}>
                            <input 
                              type="checkbox" 
                              checked={booking.isAttended || false} 
                              onChange={(e) => handleToggleBookingStatus(booking.id, { isAttended: e.target.checked })} 
                              style={{ width: '16px', height: '16px', cursor: 'pointer' }}
                            />
                          </td>
                          <td style={{ padding: '12px 6px', textAlign: 'center' }}>
                            <input 
                              type="checkbox" 
                              checked={booking.isPaid || false} 
                              onChange={(e) => handleToggleBookingStatus(booking.id, { isPaid: e.target.checked })} 
                              style={{ width: '16px', height: '16px', cursor: 'pointer' }}
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
                                disabled={deletingBookingId === booking.id}
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
        <div style={{
          position: 'fixed',
          top: 0,
          left: 0,
          right: 0,
          bottom: 0,
          backgroundColor: 'rgba(5, 7, 12, 0.8)',
          backdropFilter: 'blur(8px)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          zIndex: 9999,
          padding: 'var(--space-md)'
        }}>
          <div className="glass-panel" style={{ width: '100%', maxWidth: '560px', maxHeight: '92vh', overflowY: 'auto', padding: 'var(--space-xl)', display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderBottom: '1px solid var(--border-subtle)', paddingBottom: 'var(--space-sm)' }}>
              <h3 style={{ fontSize: '1.1rem', fontWeight: 600 }}>
                {editingGroupId ? 'تعديل مجموعة مواعيد' : 'إنشاء مجموعة جديدة'}
              </h3>
              <button 
                type="button"
                onClick={() => setIsModalOpen(false)} 
                style={{ background: 'none', border: 'none', color: 'hsl(var(--text-muted))', fontSize: '1.5rem', cursor: 'pointer' }}
              >
                &times;
              </button>
            </div>

            <form onSubmit={handleSaveGroup} className={styles.form}>
              <div className={styles.formGroup}>
                <label className={styles.label}>نوع المجموعة</label>
                <select 
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
                <label className={styles.label}>إنستراكتور المجموعة</label>
                <select
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
                <label className={styles.label}>ميعاد السيشن المجانية (مع دكتور مصطفى)</label>
                <input
                  type="datetime-local"
                  value={freeSessionDateTime}
                  onChange={(e) => setFreeSessionDateTime(e.target.value)}
                  className={styles.input}
                  required
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>ميعاد السيشن الأولى للكورس</label>
                <input 
                  type="datetime-local" 
                  value={dateTime} 
                  onChange={(e) => setDateTime(e.target.value)} 
                  className={styles.input} 
                  required 
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>ميعاد السيشن الثانية للكورس</label>
                <input
                  type="datetime-local"
                  value={courseSecondDateTime}
                  onChange={(e) => setCourseSecondDateTime(e.target.value)}
                  className={styles.input}
                  required
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>أيام الكورس الأسبوعية (اختر يومين)</label>
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px', marginTop: '4px' }}>
                  {DAY_NAMES.map((dayName, idx) => (
                    <button
                      key={idx}
                      type="button"
                      onClick={() => toggleDay(idx)}
                      style={{
                        padding: '6px 14px',
                        fontSize: '0.8rem',
                        borderRadius: '16px',
                        border: selectedDays.includes(idx) ? '2px solid hsl(var(--accent-primary))' : '1px solid var(--border-subtle)',
                        background: selectedDays.includes(idx) ? 'hsla(var(--accent-primary), 0.15)' : 'transparent',
                        color: selectedDays.includes(idx) ? 'hsl(var(--accent-primary))' : 'hsl(var(--text-secondary))',
                        cursor: 'pointer',
                        fontWeight: selectedDays.includes(idx) ? 700 : 400,
                        transition: 'all 0.2s'
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
                <label className={styles.label}>السعة (عدد المشتركين الأقصى)</label>
                <input 
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
                  onClick={() => setIsModalOpen(false)} 
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
    </div>
  );
}
