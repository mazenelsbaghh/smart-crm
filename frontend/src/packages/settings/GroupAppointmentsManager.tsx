'use client';

import React, { useEffect, useState, useCallback } from 'react';
import { api } from '../../services/api';
import { useAuth } from '../../context/auth-context';
import { 
  Calendar, 
  Plus, 
  Trash2, 
  Edit3, 
  Users, 
  Link, 
  Copy, 
  Check, 
  ArrowRight,
  Clock
} from 'lucide-react';
import styles from './settings.module.css';

interface Booking {
  id: string;
  customerName: string;
  customerPhone: string;
  customerId: string;
  createdAt: string;
}

interface GroupAppointment {
  id: string;
  name: string;
  dateTime: string;
  capacity: number;
  isActive: boolean;
  days: string;
  bookedCount: number;
  bookings: Booking[];
  mode: string;
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
  
  // Form states
  const [mode, setMode] = useState<string>('offline');
  const [dateTime, setDateTime] = useState('');
  const [capacity, setCapacity] = useState(5);
  const [isActive, setIsActive] = useState(true);
  const [selectedDays, setSelectedDays] = useState<number[]>([]);
  
  const [copiedId, setCopiedId] = useState<string | null>(null);

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

  useEffect(() => {
    fetchGroups();
  }, [fetchGroups]);

  const handleSaveGroup = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!dateTime || capacity <= 0) return;

    try {
      setActionLoading(true);
      setMessage(null);

      // Convert time-only value to today's date with the specified time
      const [hours, minutes] = dateTime.split(':').map(Number);
      const dateObj = new Date();
      dateObj.setHours(hours, minutes, 0, 0);
      const utcDate = dateObj.toISOString();

      const payload = {
        dateTime: utcDate,
        capacity,
        isActive,
        days: selectedDays.join(','),
        mode
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
      setCapacity(5);
      setIsActive(true);
      setSelectedDays([]);
      setMode('offline');
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
    
    // Format UTC time to time-only (HH:mm)
    const localDate = new Date(group.dateTime);
    const hours = localDate.getHours().toString().padStart(2, '0');
    const mins = localDate.getMinutes().toString().padStart(2, '0');
    setDateTime(`${hours}:${mins}`);
    setCapacity(group.capacity);
    setIsActive(group.isActive);
    setSelectedDays(group.days ? group.days.split(',').filter(Boolean).map(Number) : []);
    setIsModalOpen(true);
  };

  const handleDeleteGroup = async (id: string) => {
    if (!window.confirm('هل أنت متأكد من حذف هذه المجموعة؟ سيتم حذف جميع الحجوزات المرتبطة بها.')) return;

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

  const handleCopyLink = (groupId: string) => {
    if (!activeProject) return;
    const link = `${window.location.origin}/booking/${activeProject.id}`;
    void navigator.clipboard.writeText(link);
    setCopiedId(groupId);
    setTimeout(() => setCopiedId(null), 2000);
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

  const toggleDay = (dayIndex: number) => {
    setSelectedDays(prev => 
      prev.includes(dayIndex) 
        ? prev.filter(d => d !== dayIndex) 
        : [...prev, dayIndex].sort()
    );
  };

  const formatTime = (isoString: string) => {
    return new Date(isoString).toLocaleTimeString('ar-EG', { hour: '2-digit', minute: '2-digit' });
  };

  const formatDays = (days: string) => {
    if (!days) return '';
    return days.split(',').filter(Boolean).map(d => DAY_NAMES_SHORT[parseInt(d)] || '').join(' · ');
  };

  const sortedGroups = [...groups].sort((a, b) => {
    const getRank = (g: GroupAppointment) => {
      if (!g.isActive) return 3;
      if (g.bookedCount >= g.capacity) return 2;
      return 1;
    };
    const rankA = getRank(a);
    const rankB = getRank(b);
    if (rankA !== rankB) {
      return rankA - rankB;
    }
    return new Date(a.dateTime).getTime() - new Date(b.dateTime).getTime();
  });

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

        <button 
          onClick={() => {
            setEditingGroupId(null);
            setMode('offline');
            setDateTime('');
            setCapacity(5);
            setIsActive(true);
            setIsModalOpen(true);
            setSelectedDays([]);
          }}
          className={`${styles.btn} ${styles.btnPrimary}`}
          style={{ padding: '8px 16px', fontSize: '0.85rem' }}
        >
          <Plus size={16} />
          إضافة مجموعة جديدة
        </button>
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
            background: 'linear-gradient(135deg, hsla(var(--accent-primary-hsl), 0.1) 0%, rgba(5, 7, 12, 0.4) 100%)',
            border: '1px solid hsla(var(--accent-primary-hsl), 0.2)',
            borderRadius: 'var(--radius-lg)',
            boxShadow: '0 8px 32px 0 rgba(0, 243, 255, 0.05)',
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
                    <th style={{ padding: '12px 8px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>الوقت والأيام</th>
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
                                {formatDays(group.days)}
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
                              style={{ padding: '4px 8px', fontSize: '0.75rem', backgroundColor: 'rgba(0, 243, 255, 0.08)', color: 'hsl(var(--accent-primary))' }}
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
                              onClick={() => handleDeleteGroup(group.id)}
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
                <button
                  onClick={() => setSelectedGroup(null)}
                  className={`${styles.btn} ${styles.btnSecondary}`}
                  style={{ padding: '2px 8px', fontSize: '0.75rem' }}
                >
                  إغلاق القائمة
                </button>
              </div>

              {selectedGroup.bookings.length === 0 ? (
                <p style={{ fontSize: '0.85rem', color: 'hsl(var(--text-secondary))', textAlign: 'center', padding: '2rem 0' }}>
                  لا يوجد مشتركون مسجلون في هذه المجموعة بعد.
                </p>
              ) : (
                <div style={{ overflowX: 'auto' }}>
                  <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'right' }}>
                    <thead>
                      <tr style={{ borderBottom: '1px solid var(--border-subtle)' }}>
                        <th style={{ padding: '10px 6px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>اسم العميل</th>
                        <th style={{ padding: '10px 6px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>رقم الواتساب</th>
                        <th style={{ padding: '10px 6px', fontSize: '0.8rem', color: 'var(--text-soft)' }}>تاريخ الحجز</th>
                        <th style={{ padding: '10px 6px', fontSize: '0.8rem', color: 'var(--text-soft)', textAlign: 'center' }}>الدردشة</th>
                      </tr>
                    </thead>
                    <tbody>
                      {selectedGroup.bookings.map((booking) => (
                        <tr key={booking.id} style={{ borderBottom: '1px solid var(--border-subtle)' }}>
                          <td style={{ padding: '12px 6px', fontWeight: 600 }}>{booking.customerName}</td>
                          <td style={{ padding: '12px 6px', fontSize: '0.85rem' }}>+{booking.customerPhone}</td>
                          <td style={{ padding: '12px 6px', fontSize: '0.85rem', color: 'hsl(var(--text-secondary))' }}>
                            {new Date(booking.createdAt).toLocaleDateString('ar-EG', { year: 'numeric', month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' })}
                          </td>
                          <td style={{ padding: '12px 6px', textAlign: 'center' }}>
                            <a 
                              href={`/inbox?customerId=${booking.customerId}`}
                              className={`${styles.btn} ${styles.btnSecondary}`}
                              style={{ padding: '4px 10px', fontSize: '0.75rem', backgroundColor: 'rgba(0, 243, 255, 0.05)' }}
                            >
                              فتح المحادثة
                            </a>
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
          <div className="glass-panel" style={{ width: '100%', maxWidth: '460px', padding: 'var(--space-xl)', display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderBottom: '1px solid var(--border-subtle)', paddingBottom: 'var(--space-sm)' }}>
              <h3 style={{ fontSize: '1.1rem', fontWeight: 600 }}>
                {editingGroupId ? 'تعديل مجموعة مواعيد' : 'إنشاء مجموعة جديدة'}
              </h3>
              <button 
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
                <label className={styles.label}>الوقت</label>
                <input 
                  type="time" 
                  value={dateTime} 
                  onChange={(e) => setDateTime(e.target.value)} 
                  className={styles.input} 
                  required 
                />
              </div>

              <div className={styles.formGroup}>
                <label className={styles.label}>الأيام</label>
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
    </div>
  );
}
