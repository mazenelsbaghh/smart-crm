'use client';

import React, { useState, useEffect } from 'react';
import { Conversation, Message } from '../../../types/chat';
import { Customer } from '../../../services/crm';
import { api } from '../../../services/api';
import { AiReplyIndicator, ActionButton } from '../../../components/shared/InboxSharedElements';
import { 
  Send, 
  User, 
  Sparkles,
  Phone,
  MessageSquare,
  Mail,
  Calendar,
  FolderOpen,
  FileText,
  Paperclip,
  Smile,
  ChevronRight,
  Briefcase,
  Plus,
  Check,
  Trash2
} from 'lucide-react';
import styles from '../inbox.module.css';

interface FollowUp {
  id: string;
  customerId: string;
  dueDate: string;
  status: 'Pending' | 'Completed' | 'Missed';
  notes: string;
  type?: 'Nurturing' | 'AppointmentReminder';
  appointmentTime?: string;
  tone?: string;
}

interface ChatWorkspaceProps {
  activeConv: Conversation | null;
  customer: Customer | null;
  messages: Message[];
  inputMessage: string;
  setInputMessage: (msg: string) => void;
  handleSend: () => void;
  sending: boolean;
  isAiTyping: boolean;
  aiTypingStage: 'generating' | 'typing';
  aiTypingCountdown: number;
  channel: 'WhatsApp' | 'Messenger' | 'Comments';
  messageInputRef: React.RefObject<HTMLTextAreaElement | null>;
  messageEndRef: React.RefObject<HTMLDivElement | null>;
  // For Comments Channel
  publicComment?: string;
  setPublicComment?: (val: string) => void;
  privateDM?: string;
  setPrivateDM?: (val: string) => void;
  reaction?: 'LIKE' | 'LOVE' | null;
  setReaction?: (val: 'LIKE' | 'LOVE' | null) => void;
}

export default function ChatWorkspace({
  activeConv,
  customer,
  messages,
  inputMessage,
  setInputMessage,
  handleSend,
  sending,
  isAiTyping,
  aiTypingStage,
  aiTypingCountdown,
  channel,
  messageInputRef,
  messageEndRef,
  publicComment,
  setPublicComment,
  privateDM,
  setPrivateDM,
  reaction,
  setReaction
}: ChatWorkspaceProps) {
  
  const [activeTab, setActiveTab] = useState<'Timeline' | 'Conversation' | 'Notes' | 'Analytics' | 'Orders' | 'Files' | 'History'>('Conversation');
  const [notesText, setNotesText] = useState(customer?.notes || '');
  const [now] = useState(() => Date.now());

  // Real Follow-Up states
  const [followUps, setFollowUps] = useState<FollowUp[]>([]);
  const [loadingFollowUps, setLoadingFollowUps] = useState(false);
  const [showAddForm, setShowAddForm] = useState(false);
  const [newDueDate, setNewDueDate] = useState('');
  const [newType, setNewType] = useState<'Nurturing' | 'AppointmentReminder'>('Nurturing');
  const [newNotes, setNewNotes] = useState('');
  const [newApptTime, setNewApptTime] = useState('');
  const [creatingFollowUp, setCreatingFollowUp] = useState(false);

  // Fetch customer follow-ups on load/change
  useEffect(() => {
    if (!customer?.id || !activeConv?.projectId) return;

    let active = true;
    const fetchFollowUps = async () => {
      setLoadingFollowUps(true);
      try {
        const response = await api.get<FollowUp[]>(`/api/projects/${activeConv.projectId}/follow-ups`);
        if (active) {
          const filtered = response.data.filter(f => f.customerId === customer.id);
          setFollowUps(filtered);
        }
      } catch (err) {
        console.error('Error loading customer follow-ups', err);
      } finally {
        if (active) setLoadingFollowUps(false);
      }
    };

    fetchFollowUps();

    return () => {
      active = false;
    };
  }, [customer?.id, activeConv?.projectId]);

  const handleAddFollowUp = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!customer?.id || !activeConv?.projectId || creatingFollowUp) return;
    if (newType === 'Nurturing' && !newDueDate) return;
    if (newType === 'AppointmentReminder' && !newApptTime) return;

    setCreatingFollowUp(true);
    try {
      const payload = {
        notes: newNotes,
        type: newType,
        dueDate: newType === 'Nurturing' 
          ? new Date(newDueDate).toISOString() 
          : new Date(newApptTime).toISOString(),
        appointmentTime: newType === 'AppointmentReminder' 
          ? new Date(newApptTime).toISOString() 
          : undefined,
        tone: 'Default'
      };

      const response = await api.post(`/api/customers/${customer.id}/follow-ups`, payload);
      setFollowUps(prev => [...prev, response.data]);
      
      // Reset form
      setNewDueDate('');
      setNewApptTime('');
      setNewNotes('');
      setShowAddForm(false);
    } catch (err) {
      console.error('Failed to create follow-up', err);
    } finally {
      setCreatingFollowUp(false);
    }
  };

  const handleCompleteFollowUp = async (id: string) => {
    try {
      await api.post(`/api/follow-ups/${id}/complete`);
      setFollowUps(prev => prev.map(f => f.id === id ? { ...f, status: 'Completed' as const } : f));
    } catch (err) {
      console.error('Failed to complete follow-up', err);
    }
  };

  const handleDeleteFollowUp = async (id: string) => {
    try {
      await api.delete(`/api/follow-ups/${id}`);
      setFollowUps(prev => prev.filter(f => f.id !== id));
    } catch (err) {
      console.error('Failed to delete follow-up', err);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const isWithin24hWindow = (lastMessageAt: string): boolean => {
    const diff = now - new Date(lastMessageAt).getTime();
    return diff < 24 * 60 * 60 * 1000;
  };

  const formatEgyptTime = (dateStr: string) => {
    try {
      const date = new Date(dateStr);
      return date.toLocaleTimeString('ar-EG-u-nu-latn', {
        timeZone: 'Africa/Cairo',
        hour: '2-digit',
        minute: '2-digit',
        hour12: true
      });
    } catch {
      return dateStr;
    }
  };

  if (!activeConv) {
    return (
      <div className={styles.noActiveChat}>
        <div className={styles.noActiveChatIconContainer}>
          <svg viewBox="0 0 100 100" className={styles.noActiveLogo}>
            <path
              fill="#E8E8E8"
              d="M50 0 L60 30 L90 20 L70 50 L90 80 L60 70 L50 100 L40 70 L10 80 L30 50 L10 20 L40 30 Z"
            />
          </svg>
        </div>
        <h3>نظام إدارة المحادثات والعملاء</h3>
        <p>الرجاء تحديد محادثة عميل من قائمة العمل للبدء في الرد وعرض تفاصيل CRM.</p>
      </div>
    );
  }

  const customerName = customer?.name || activeConv.customer.facebookName || activeConv.customer.name || 'عميل غير معروف';
  const customerCity = customer?.city || 'القاهرة';
  const isMsgWindowOpen = channel !== 'Messenger' || isWithin24hWindow(activeConv.lastMessageAt);

  // Mock timeline entries
  const timelineEntries = [
    {
      id: '1',
      date: '12 مايو',
      title: 'تم تقديم المعلومات للعميل',
      description: 'تم إرسال تفاصيل الكورس ومحتويات البرنامج التدريبي.',
      manager: 'Marty C.',
      pill: 'Discovery',
      pillClass: styles.pillDiscovery
    },
    {
      id: '2',
      date: '15 مايو',
      title: 'جاري استكمال بيانات العميل والتسجيل',
      description: 'العميل مهتم ويرغب في معرفة طرق الدفع المتاحة.',
      manager: 'Marty C.',
      pill: 'Negotiation',
      pillClass: styles.pillNegotiation
    }
  ];

  return (
    <div className={styles.chatPanel}>
      {/* Workspace Header */}
      <div className={styles.workspaceHeader}>
        <div className={styles.headerTopRow}>
          <div className={styles.headerProfile}>
            <div className={styles.headerAvatar}>
              <User size={36} />
            </div>
            <div className={styles.customerInfoBlock}>
              <h2 className={styles.workspaceCustomerName}>{customerName}</h2>
              <p className={styles.customerSubDetails}>
                <span>Product manager — {customerCity}</span>
                <span className={styles.dividerDot}>•</span>
                <span>{customer?.phoneNumber || activeConv.customer.phone || 'قناة فيسبوك'}</span>
                <span className={styles.dividerDot}>•</span>
                <span>{customerCity.toLowerCase()}@example.com</span>
              </p>
            </div>
          </div>

          <div className={styles.headerManagerAssignee}>
            <div className={styles.managerProfile}>
              <div className={styles.managerAvatar}>
                <User size={18} />
              </div>
              <div className={styles.managerNameBlock}>
                <span className={styles.managerLabel}>Manager</span>
                <span className={styles.managerName}>Marty C.</span>
              </div>
            </div>

            <div className={styles.stagePillsRow}>
              <span className={`${styles.statusPill} ${styles.statusPillHigh}`}>High</span>
              <span className={`${styles.statusPill} ${styles.statusPillWarm}`}>Warm</span>
            </div>
          </div>
        </div>

        {/* Quick action buttons row + tabs */}
        <div className={styles.headerActionsRow}>
          <div className={styles.circularActionsGroup}>
            <button type="button" className={styles.circularBtn} title="اتصال">
              <Phone size={16} />
            </button>
            <button type="button" className={styles.circularBtn} title="رسالة واتساب">
              <MessageSquare size={16} />
            </button>
            <button type="button" className={styles.circularBtn} title="إرسال بريد إلكتروني">
              <Mail size={16} />
            </button>
            <button type="button" className={styles.circularBtn} title="جدولة موعد">
              <Calendar size={16} />
            </button>
            <button type="button" className={styles.circularBtn} title="ملفات العميل">
              <FolderOpen size={16} />
            </button>
          </div>

          {/* Deal ID/Info pill */}
          <div className={styles.dealPill}>
            <Briefcase size={12} style={{ marginLeft: '4px' }} />
            <span>الصفقة #3263627</span>
          </div>

          {/* Workspace Tabs navigation */}
          <div className={styles.workspaceTabs}>
            {(['Timeline', 'Conversation', 'Notes', 'Analytics', 'Orders', 'Files', 'History'] as const).map(tab => (
              <button
                key={tab}
                type="button"
                className={`${styles.tabBtn} ${activeTab === tab ? styles.tabBtnActive : ''}`}
                onClick={() => setActiveTab(tab)}
              >
                {tab === 'Timeline' && 'Summary'}
                {tab === 'Conversation' && 'Conversation'}
                {tab === 'Notes' && 'Notes'}
                {tab === 'Analytics' && 'Analytics'}
                {tab === 'Orders' && 'Details'}
                {tab === 'Files' && 'Files'}
                {tab === 'History' && 'History'}
              </button>
            ))}
          </div>
        </div>
      </div>

      {/* Main Workspace Display Content */}
      <div className={styles.workspaceBody}>
        {activeTab === 'Conversation' && (
          <>
            {/* Conversation/Chat Messages view */}
            <div className={styles.messagesContainer}>
              {messages.length === 0 ? (
                <div className={styles.emptyMessages}>
                  <p>لا توجد رسائل سابقة. أرسل رسالة لبدء المحادثة.</p>
                </div>
              ) : (
                messages.map((msg) => {
                  const isIncoming = msg.senderType === 'Customer';
                  return (
                    <div
                      key={msg.id}
                      className={`${styles.msgRow} ${isIncoming ? styles.msgRowIncoming : styles.msgRowOutgoing}`}
                    >
                      <div className={`${styles.msgBubble} ${isIncoming ? styles.msgBubbleIncoming : styles.msgBubbleOutgoing}`}>
                        {msg.senderType === 'AI' && (
                          <div className={styles.aiBadgeRow}>
                            <Sparkles size={11} className={styles.typingSparkle} />
                            <span>مساعد ذكي</span>
                          </div>
                        )}
                        <p className={styles.messageTextContent}>{msg.content}</p>
                        <span className={styles.messageTime}>{formatEgyptTime(msg.createdAt)}</span>
                      </div>
                    </div>
                  );
                })
              )}
              <AiReplyIndicator 
                isAiTyping={isAiTyping} 
                aiTypingStage={aiTypingStage} 
                aiTypingCountdown={aiTypingCountdown} 
              />
              <div ref={messageEndRef} />
            </div>

            {/* Composer Section */}
            {channel === 'Comments' ? (
              <div className={styles.commentsComposer}>
                <div className={styles.commentsInputsRow}>
                  {/* Public Comment Input */}
                  <div className={styles.commentInputWrapper}>
                    <label className={styles.commentLabel}>الرد العام (على الكومنت)</label>
                    <textarea
                      ref={messageInputRef}
                      className={styles.commentTextarea}
                      placeholder="اكتب رد عام للتعليق..."
                      value={publicComment || ''}
                      onChange={(e) => setPublicComment && setPublicComment(e.target.value)}
                      disabled={sending}
                    />
                  </div>
                  
                  {/* Private DM Input */}
                  <div className={styles.commentInputWrapper}>
                    <label className={styles.commentLabel}>الرسالة الخاصة (في ماسنجر)</label>
                    <textarea
                      className={styles.commentTextarea}
                      placeholder="اكتب رسالة خاصة للمستلم..."
                      value={privateDM || ''}
                      onChange={(e) => setPrivateDM && setPrivateDM(e.target.value)}
                      disabled={sending}
                    />
                  </div>
                </div>

                <div className={styles.commentsActionsRow}>
                  {/* Reaction Selector */}
                  <div className={styles.reactionSelector}>
                    <span className={styles.reactionLabel}>تفاعل (ريأكت):</span>
                    <button
                      type="button"
                      className={`${styles.reactionBtn} ${reaction === 'LIKE' ? styles.reactionBtnActive : ''}`}
                      onClick={() => setReaction && setReaction(reaction === 'LIKE' ? null : 'LIKE')}
                      disabled={sending}
                    >
                      👍 Like
                    </button>
                    <button
                      type="button"
                      className={`${styles.reactionBtn} ${reaction === 'LOVE' ? styles.reactionBtnActive : ''}`}
                      onClick={() => setReaction && setReaction(reaction === 'LOVE' ? null : 'LOVE')}
                      disabled={sending}
                    >
                      ❤️ Love
                    </button>
                  </div>

                  <button
                    type="button"
                    className={styles.commentSendBtn}
                    onClick={handleSend}
                    disabled={sending || (!publicComment?.trim() && !privateDM?.trim())}
                  >
                    {sending ? 'جاري الإرسال...' : 'إرسال الرد المجمع'}
                  </button>
                </div>
              </div>
            ) : (
              <div className={styles.messageComposer}>
                <button type="button" className={styles.composerToolBtn} title="إرفاق ملف">
                  <Paperclip size={18} />
                </button>
                
                <textarea
                  ref={messageInputRef}
                  className={styles.messageInput}
                  placeholder={isMsgWindowOpen ? "اكتب رسالة هنا للرد..." : "⚠️ انتهت نافذة الـ 24 ساعة للماسنجر"}
                  value={inputMessage}
                  onChange={(e) => setInputMessage(e.target.value)}
                  onKeyDown={handleKeyDown}
                  disabled={!isMsgWindowOpen || sending}
                />

                <button type="button" className={styles.composerToolBtn} title="إيموجي">
                  <Smile size={18} />
                </button>

                <button
                  type="button"
                  className={styles.composerSendBtn}
                  onClick={handleSend}
                  disabled={sending || !inputMessage.trim() || !isMsgWindowOpen}
                >
                  <Send size={16} />
                </button>
              </div>
            )}
          </>
        )}

        {activeTab === 'Timeline' && (
          <div className={styles.timelineContainer}>
            <div className={styles.timelineHeaderRow}>
              <h4>جدول زمن المتابعة والتقدم للعميل ({customerName})</h4>
              <ActionButton 
                variant="accent" 
                size="sm" 
                icon={Plus} 
                onClick={() => setShowAddForm(!showAddForm)}
              >
                {showAddForm ? 'إلغاء' : 'جدولة متابعة جديدة'}
              </ActionButton>
            </div>

            {showAddForm && (
              <form onSubmit={handleAddFollowUp} className={styles.quickFollowUpForm}>
                <div className={styles.followUpInputsGrid}>
                  <div className={styles.commentInputWrapper}>
                    <label className={styles.commentLabel}>نوع الإجراء</label>
                    <select
                      value={newType}
                      onChange={(e) => setNewType(e.target.value as 'Nurturing' | 'AppointmentReminder')}
                      className={styles.commentTextarea}
                      style={{ height: '38px', padding: '6px 12px' }}
                    >
                      <option value="Nurturing">متابعة لتنشيط العميل (Nurturing)</option>
                      <option value="AppointmentReminder">تذكير بموعد / كورس (Reminder)</option>
                    </select>
                  </div>

                  {newType === 'Nurturing' ? (
                    <div className={styles.commentInputWrapper}>
                      <label className={styles.commentLabel}>تاريخ ووقت المتابعة</label>
                      <input 
                        type="datetime-local" 
                        value={newDueDate}
                        onChange={(e) => setNewDueDate(e.target.value)}
                        className={styles.commentTextarea}
                        style={{ height: '38px' }}
                        required
                      />
                    </div>
                  ) : (
                    <div className={styles.commentInputWrapper}>
                      <label className={styles.commentLabel}>تاريخ ووقت الكورس / الموعد</label>
                      <input 
                        type="datetime-local" 
                        value={newApptTime}
                        onChange={(e) => setNewApptTime(e.target.value)}
                        className={styles.commentTextarea}
                        style={{ height: '38px' }}
                        required
                      />
                    </div>
                  )}

                  <div className={styles.commentInputWrapper} style={{ gridColumn: 'span 2' }}>
                    <label className={styles.commentLabel}>رسالة المتابعة / ملاحظات</label>
                    <input 
                      type="text" 
                      placeholder="اكتب تفاصيل أو ملاحظات التذكير..."
                      value={newNotes}
                      onChange={(e) => setNewNotes(e.target.value)}
                      className={styles.commentTextarea}
                      style={{ height: '38px' }}
                    />
                  </div>
                </div>

                <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '12px' }}>
                  <ActionButton type="submit" variant="accent" size="sm" disabled={creatingFollowUp}>
                    {creatingFollowUp ? 'جاري الحفظ...' : 'حفظ المهمة المجدولة'}
                  </ActionButton>
                </div>
              </form>
            )}

            {/* Follow-Ups list table */}
            <div className={styles.followUpTableSection}>
              <h5>المتابعات والمهام النشطة للعميل</h5>
              {loadingFollowUps ? (
                <p style={{ fontSize: '0.85rem', color: '#7D7D7D', textAlign: 'center', padding: '16px' }}>جاري تحميل المتابعات...</p>
              ) : followUps.length === 0 ? (
                <div style={{ fontSize: '0.85rem', color: '#7D7D7D', textAlign: 'center', padding: '24px', backgroundColor: '#F8F8F6', borderRadius: '8px', border: '1px dashed #E8E8E8' }}>
                  لا توجد مهام متابعة مجدولة نشطة حالياً.
                </div>
              ) : (
                <div className={styles.sharedTableContainer}>
                  <table className={styles.sharedTable}>
                    <thead>
                      <tr>
                        <th>التاريخ والوقت</th>
                        <th>النوع</th>
                        <th>رسالة المتابعة</th>
                        <th>الحالة</th>
                        <th style={{ textAlign: 'center' }}>الإجراءات</th>
                      </tr>
                    </thead>
                    <tbody>
                      {followUps.map(f => (
                        <tr key={f.id}>
                          <td>{new Date(f.dueDate).toLocaleDateString('ar-EG')} {new Date(f.dueDate).toLocaleTimeString('ar-EG', { hour: '2-digit', minute: '2-digit' })}</td>
                          <td>
                            <span className={f.type === 'AppointmentReminder' ? styles.badgeReminder : styles.badgeNurture}>
                              {f.type === 'AppointmentReminder' ? 'تذكير موعد' : 'متابعة عميل'}
                            </span>
                          </td>
                          <td>{f.notes || 'إرسال تلقائي'}</td>
                          <td>
                            <span className={`${styles.badgeStatus} ${f.status === 'Completed' ? styles.badgeStatusCompleted : f.status === 'Missed' ? styles.badgeStatusMissed : styles.badgeStatusPending}`}>
                              {f.status === 'Completed' ? 'مكتملة' : f.status === 'Missed' ? 'فائتة' : 'معلقة'}
                            </span>
                          </td>
                          <td style={{ display: 'flex', gap: '8px', justifyContent: 'center' }}>
                            {f.status === 'Pending' && (
                              <button 
                                type="button" 
                                className={styles.inlineActionBtnCheck} 
                                title="إكمال المهمة"
                                onClick={() => handleCompleteFollowUp(f.id)}
                              >
                                <Check size={14} />
                              </button>
                            )}
                            <button 
                              type="button" 
                              className={styles.inlineActionBtnDelete} 
                              title="حذف المهمة"
                              onClick={() => handleDeleteFollowUp(f.id)}
                            >
                              <Trash2 size={14} />
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>

            <div className={styles.timelineDivider} />
            
            <div className={styles.timelineList}>
              {timelineEntries.map(entry => (
                <div key={entry.id} className={styles.timelineItem}>
                  <div className={styles.timelineDotLine}>
                    <div className={styles.timelineDot}></div>
                    <span className={styles.timelineDateLabel}>{entry.date}</span>
                  </div>

                  <div className={styles.timelineCard}>
                    <div className={styles.timelineCardHeader}>
                      <span className={`${styles.timelinePill} ${entry.pillClass}`}>{entry.pill}</span>
                      <span className={styles.timelineManagerText}>{entry.manager} <ChevronRight size={12} style={{ display: 'inline' }} /></span>
                    </div>
                    <h5 className={styles.timelineCardTitle}>{entry.title}</h5>
                    <p className={styles.timelineCardDesc}>{entry.description}</p>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}

        {activeTab === 'Notes' && (
          <div className={styles.notesContainer}>
            <h4>ملاحظات حول العميل</h4>
            <p>تساعدك هذه المساحة على تدوين أهم النقاط والاهتمامات الخاصة بالعميل ومتابعتها.</p>
            <textarea
              className={styles.notesTextarea}
              placeholder="اكتب ملاحظاتك هنا..."
              value={notesText}
              onChange={(e) => setNotesText(e.target.value)}
            />
            <button 
              type="button" 
              className={styles.saveNotesBtn}
              onClick={() => {
                // Trigger client save callback if needed
              }}
            >
              حفظ الملاحظات
            </button>
          </div>
        )}

        {(activeTab === 'Analytics' || activeTab === 'Orders' || activeTab === 'Files' || activeTab === 'History') && (
          <div className={styles.tabPlaceholder}>
            <FileText size={48} className={styles.placeholderIcon} />
            <h4>لا يوجد محتوى حالي في تبويب {activeTab}</h4>
            <p>سيتم ربط هذا الجزء بالخلفية قريباً.</p>
          </div>
        )}
      </div>
    </div>
  );
}
