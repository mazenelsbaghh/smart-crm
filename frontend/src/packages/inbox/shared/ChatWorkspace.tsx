'use client';

import React, { useState, useEffect } from 'react';
import { Conversation, Message } from '../../../types/chat';
import { Customer } from '../../../services/crm';
import { api } from '../../../services/api';
import { useToast } from '../../../context/toast-context';
import { AiReplyIndicator, ActionButton } from '../../../components/shared/InboxSharedElements';
import ConfirmDialog from '../../../components/shared/ConfirmDialog';
import { 
  Send, 
  User, 
  Sparkles,
  ChevronRight,
  Plus,
  Check,
  Trash2,
  ShieldBan
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

const conversationStatusAr: Record<Conversation['status'], string> = {
  Open: 'مفتوحة',
  Pending: 'قيد المتابعة',
  Resolved: 'تم حلها',
  Closed: 'مغلقة',
};

interface ChatWorkspaceProps {
  activeConv: Conversation | null;
  customer: Customer | null;
  messages: Message[];
  inputMessage: string;
  setInputMessage: (msg: string) => void;
  handleSend: () => void;
  sending: boolean;
  isAiTyping: boolean;
  aiTypingStage: 'generating' | 'typing' | null;
  aiTypingCountdown: number | null;
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
  setActiveConv?: (conv: Conversation | null) => void;
  onUpdateCustomer: (fields: Partial<Customer>) => Promise<void>;
  updating: boolean;
  hasOlderMessages?: boolean;
  loadingOlderMessages?: boolean;
  onLoadOlderMessages?: () => void;
  messageLoadError?: string | null;
  onRetryMessages?: () => void;
  messagesLoading?: boolean;
  onOpenDetails?: (trigger: HTMLButtonElement) => void;
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
  setReaction,
  setActiveConv,
  onUpdateCustomer,
  updating,
  hasOlderMessages = false,
  loadingOlderMessages = false,
  onLoadOlderMessages,
  messageLoadError,
  onRetryMessages,
  messagesLoading = false,
  onOpenDetails,
}: ChatWorkspaceProps) {
  const { showToast } = useToast();
  const [activeTab, setActiveTab] = useState<'Timeline' | 'Conversation' | 'Notes'>('Conversation');
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
  const [followUpError, setFollowUpError] = useState<string | null>(null);
  const [followUpToDelete, setFollowUpToDelete] = useState<string | null>(null);
  const [pendingBlacklistState, setPendingBlacklistState] = useState<boolean | null>(null);

  // Fetch customer follow-ups on load/change
  useEffect(() => {
    if (!customer?.id || !activeConv?.projectId) return;

    let active = true;
    const fetchFollowUps = async () => {
      setLoadingFollowUps(true);
      setFollowUpError(null);
      try {
        const response = await api.get<FollowUp[]>(`/api/projects/${activeConv.projectId}/follow-ups`);
        if (active) {
          const filtered = response.data.filter(f => f.customerId === customer.id);
          setFollowUps(filtered);
        }
      } catch (err) {
        console.error('Error loading customer follow-ups', err);
        if (active) setFollowUpError('تعذر تحميل المتابعات لهذا العميل.');
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
      showToast('تمت جدولة المتابعة', 'success');
    } catch (err) {
      console.error('Failed to create follow-up', err);
      showToast('تعذر جدولة المتابعة. راجع البيانات وحاول مرة أخرى.', 'error');
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
      showToast('تعذر إكمال المتابعة.', 'error');
    }
  };

  const deleteSelectedFollowUp = async () => {
    if (!followUpToDelete) return;
    try {
      await api.delete(`/api/follow-ups/${followUpToDelete}`);
      setFollowUps(prev => prev.filter(f => f.id !== followUpToDelete));
      showToast('تم حذف المتابعة', 'success');
    } catch (err) {
      console.error('Failed to delete follow-up', err);
      showToast('تعذر حذف المتابعة.', 'error');
    } finally {
      setFollowUpToDelete(null);
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
  const isMsgWindowOpen = channel !== 'Messenger' || isWithin24hWindow(activeConv.lastMessageAt);

  return (
    <div className={styles.chatPanel}>
      {/* Workspace Header */}
      <div className={styles.workspaceHeader}>
        <div className={styles.headerTopRow}>
          <div className={styles.headerProfile}>
            {setActiveConv && (
              <button
                type="button"
                className={styles.mobileBackBtn}
                onClick={() => setActiveConv(null)}
                aria-label="رجوع لقائمة المحادثات"
              >
                <ChevronRight size={24} />
              </button>
            )}
            <div className={styles.headerAvatar}>
              <User size={36} />
            </div>
            <div className={styles.customerInfoBlock}>
              <h2 className={styles.workspaceCustomerName}>{customerName}</h2>
              <p className={styles.customerSubDetails}>
                <span>{customer?.phoneNumber || activeConv.customer.phone || 'بيانات الاتصال غير متاحة'}</span>
                {customer?.city && <><span className={styles.dividerDot}>•</span><span>{customer.city}</span></>}
                {activeConv.whatsAppAccountName && <><span className={styles.dividerDot}>•</span><span>عبر {activeConv.whatsAppAccountName}</span></>}
              </p>
            </div>
          </div>

          <div className={styles.headerManagerAssignee}>
            <div className={styles.managerProfile}>
              <div className={styles.managerAvatar}>
                <User size={18} />
              </div>
              <div className={styles.managerNameBlock}>
                <span className={styles.managerLabel}>المكلّف بالمحادثة</span>
                <span className={styles.managerName}>{activeConv.assignedAgentName || 'غير مُعيّن'}</span>
              </div>
            </div>

            <div className={styles.stagePillsRow}>
              <span className={`${styles.statusPill} ${styles.statusPillWarm}`}>{conversationStatusAr[activeConv.status]}</span>
            </div>
          </div>
        </div>

        {/* Quick action buttons row + tabs */}
        <div className={styles.headerActionsRow}>
          {onOpenDetails && (
            <button
              type="button"
              className={styles.responsiveDetailsTrigger}
              onClick={(event) => onOpenDetails(event.currentTarget)}
              aria-haspopup="dialog"
            >
              <User size={16} aria-hidden="true" />
              بيانات العميل
            </button>
          )}
          <button
            type="button"
            className={`${styles.aiBlockToggle} ${customer?.isBlacklisted ? styles.aiBlockToggleActive : ''}`}
            onClick={() => setPendingBlacklistState(!customer?.isBlacklisted)}
            disabled={updating || !customer}
            aria-pressed={customer?.isBlacklisted ?? false}
          >
            <ShieldBan size={16} />
            {customer?.isBlacklisted ? 'الرد الآلي محظور' : 'حظر الرد الآلي'}
          </button>

          {/* Workspace Tabs navigation */}
          <div className={styles.workspaceTabs} role="tablist" aria-label="أقسام المحادثة">
            {(['Conversation', 'Timeline', 'Notes'] as const).map(tab => (
              <button
                key={tab}
                type="button"
                className={`${styles.tabBtn} ${activeTab === tab ? styles.tabBtnActive : ''}`}
                onClick={() => setActiveTab(tab)}
                role="tab"
                aria-selected={activeTab === tab}
                aria-controls={`chat-panel-${tab}`}
                id={`chat-tab-${tab}`}
              >
                {tab === 'Timeline' && 'الملخص'}
                {tab === 'Conversation' && 'المحادثة'}
                {tab === 'Notes' && 'الملاحظات'}
              </button>
            ))}
          </div>
        </div>
      </div>

      {/* Main Workspace Display Content */}
      <div className={styles.workspaceBody}>
        {activeTab === 'Conversation' && (
          <section
            id="chat-panel-Conversation"
            role="tabpanel"
            aria-labelledby="chat-tab-Conversation"
          >
            {/* Conversation/Chat Messages view */}
            <div className={styles.messagesContainer} role="log" aria-live="polite" aria-label={`سجل محادثة ${customerName}`}>
              {messagesLoading ? (
                <div className={styles.emptyMessages} role="status" aria-busy="true">
                  <p>جاري تحميل رسائل المحادثة...</p>
                </div>
              ) : messageLoadError ? (
                <div className={styles.emptyMessages} role="alert">
                  <p>{messageLoadError}</p>
                  {onRetryMessages && (
                    <button type="button" className={styles.retryConversationsBtn} onClick={onRetryMessages}>
                      إعادة تحميل المحادثة
                    </button>
                  )}
                </div>
              ) : null}
              {!messagesLoading && hasOlderMessages && onLoadOlderMessages && (
                <button
                  type="button"
                  className={styles.retryConversationsBtn}
                  onClick={onLoadOlderMessages}
                  disabled={loadingOlderMessages}
                >
                  {loadingOlderMessages ? 'جاري تحميل الرسائل الأقدم...' : 'تحميل رسائل أقدم'}
                </button>
              )}
              {!messagesLoading && !messageLoadError && messages.length === 0 ? (
                <div className={styles.emptyMessages}>
                  <p>لا توجد رسائل سابقة. أرسل رسالة لبدء المحادثة.</p>
                </div>
              ) : !messagesLoading && !messageLoadError ? (
                messages.map((msg) => {
                  const isIncoming = msg.senderType === 'Customer';
                  return (
                    <div
                      key={msg.id}
                      className={`${styles.msgRow} ${isIncoming ? styles.msgRowIncoming : styles.msgRowOutgoing}`}
                      aria-label={`${isIncoming ? customerName : msg.senderType === 'AI' ? 'المساعد الذكي' : 'الموظف'}، ${formatEgyptTime(msg.createdAt)}`}
                    >
                      <div className={`${styles.msgBubble} ${
                        isIncoming 
                          ? styles.msgBubbleIncoming 
                          : msg.senderType === 'AI' 
                            ? styles.msgBubbleAI 
                            : styles.msgBubbleOutgoing
                      }`}>
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
              ) : null}
              {isAiTyping && aiTypingStage === null ? (
                <div className={styles.msgRowOutgoing} role="status" aria-live="polite">
                  <div className={`${styles.msgBubble} ${styles.msgBubbleAI}`}>
                    <div className={styles.aiBadgeRow}><Sparkles size={12} aria-hidden="true" /><span>الذكاء الاصطناعي</span></div>
                    <span>يجري إعداد رد؛ مرحلة المعالجة ووقت الانتهاء غير متاحين من المصدر.</span>
                  </div>
                </div>
              ) : isAiTyping && aiTypingStage === 'typing' && aiTypingCountdown === null ? (
                <div className={styles.msgRowOutgoing} role="status" aria-live="polite">
                  <div className={`${styles.msgBubble} ${styles.msgBubbleAI}`}>
                    <div className={styles.aiBadgeRow}><Sparkles size={12} aria-hidden="true" /><span>الذكاء الاصطناعي</span></div>
                    <span>جاري كتابة الرد؛ وقت الانتهاء غير متاح من المصدر.</span>
                  </div>
                </div>
              ) : (
                <AiReplyIndicator
                  isAiTyping={isAiTyping}
                  aiTypingStage={aiTypingStage ?? 'generating'}
                  aiTypingCountdown={aiTypingCountdown ?? 0}
                />
              )}
              <div ref={messageEndRef} />
            </div>

            {/* Composer Section */}
            {channel === 'Comments' ? (
              <div className={styles.commentsComposer}>
                <div className={styles.commentsInputsRow}>
                  {/* Public Comment Input */}
                  <div className={styles.commentInputWrapper}>
                    <label htmlFor="public-comment-reply" className={styles.commentLabel}>الرد العام على التعليق</label>
                    <textarea
                      id="public-comment-reply"
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
                    <label htmlFor="private-comment-reply" className={styles.commentLabel}>الرسالة الخاصة في ماسنجر</label>
                    <textarea
                      id="private-comment-reply"
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
                      aria-pressed={reaction === 'LIKE'}
                    >
                      👍 إعجاب
                    </button>
                    <button
                      type="button"
                      className={`${styles.reactionBtn} ${reaction === 'LOVE' ? styles.reactionBtnActive : ''}`}
                      onClick={() => setReaction && setReaction(reaction === 'LOVE' ? null : 'LOVE')}
                      disabled={sending}
                      aria-pressed={reaction === 'LOVE'}
                    >
                      ❤️ أحببته
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
                <textarea
                  ref={messageInputRef}
                  className={styles.messageInput}
                  placeholder={isMsgWindowOpen ? "اكتب رسالة هنا للرد..." : "انتهت نافذة الـ 24 ساعة للماسنجر"}
                  value={inputMessage}
                  onChange={(e) => setInputMessage(e.target.value)}
                  onKeyDown={handleKeyDown}
                  disabled={!isMsgWindowOpen || sending}
                  aria-label={`اكتب ردًا إلى ${customerName}`}
                />

                <button
                  type="button"
                  className={styles.composerSendBtn}
                  onClick={handleSend}
                  disabled={sending || !inputMessage.trim() || !isMsgWindowOpen}
                  aria-label={sending ? 'جاري إرسال الرسالة' : 'إرسال الرسالة'}
                >
                  <Send size={16} />
                </button>
              </div>
            )}
          </section>
        )}

        {activeTab === 'Timeline' && (
          <section
            className={styles.timelineContainer}
            id="chat-panel-Timeline"
            role="tabpanel"
            aria-labelledby="chat-tab-Timeline"
          >
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
                    <label htmlFor="follow-up-type" className={styles.commentLabel}>نوع الإجراء</label>
                    <select
                      id="follow-up-type"
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
                      <label htmlFor="follow-up-due-at" className={styles.commentLabel}>تاريخ ووقت المتابعة، بتوقيت جهازك</label>
                      <input 
                        id="follow-up-due-at"
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
                      <label htmlFor="follow-up-appointment-at" className={styles.commentLabel}>تاريخ ووقت الموعد، بتوقيت جهازك</label>
                      <input 
                        id="follow-up-appointment-at"
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
                    <label htmlFor="follow-up-notes" className={styles.commentLabel}>رسالة المتابعة أو الملاحظات</label>
                    <input 
                      id="follow-up-notes"
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
              {followUpError ? (
                <div role="alert" className={styles.emptyMessages}>{followUpError}</div>
              ) : loadingFollowUps ? (
                <p style={{ fontSize: '0.85rem', color: '#7D7D7D', textAlign: 'center', padding: '16px' }}>جاري تحميل المتابعات...</p>
              ) : followUps.length === 0 ? (
                <div style={{ fontSize: '0.85rem', color: '#7D7D7D', textAlign: 'center', padding: '24px', backgroundColor: '#F8F8F6', borderRadius: '8px', border: '1px dashed #E8E8E8' }}>
                  لا توجد مهام متابعة مجدولة نشطة حالياً.
                </div>
              ) : (
                <div className={styles.sharedTableContainer}>
                  <table className={styles.sharedTable}>
                    <caption className="sr-only">المتابعات الفعلية المسجلة للعميل</caption>
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
                          <td>{new Date(f.dueDate).toLocaleString('ar-EG', { timeZone: 'Africa/Cairo', dateStyle: 'medium', timeStyle: 'short' })}</td>
                          <td>
                            <span className={f.type === 'AppointmentReminder' ? styles.badgeReminder : styles.badgeNurture}>
                              {f.type === 'AppointmentReminder' ? 'تذكير موعد' : 'متابعة عميل'}
                            </span>
                          </td>
                          <td>{f.notes || 'لا توجد ملاحظات'}</td>
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
                                aria-label="تحديد المتابعة كمكتملة"
                                onClick={() => handleCompleteFollowUp(f.id)}
                              >
                                <Check size={14} />
                              </button>
                            )}
                            <button 
                              type="button" 
                              className={styles.inlineActionBtnDelete} 
                              title="حذف المهمة"
                              aria-label="حذف المتابعة"
                              onClick={() => setFollowUpToDelete(f.id)}
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
          </section>
        )}

        {activeTab === 'Notes' && (
          <section
            className={styles.notesContainer}
            id="chat-panel-Notes"
            role="tabpanel"
            aria-labelledby="chat-tab-Notes"
          >
            <h4>ملاحظات حول العميل</h4>
            <p>تساعدك هذه المساحة على تدوين أهم النقاط والاهتمامات الخاصة بالعميل ومتابعتها.</p>
            <textarea
              aria-label={`ملاحظات العميل ${customerName}`}
              className={styles.notesTextarea}
              placeholder="اكتب ملاحظاتك هنا..."
              value={notesText}
              onChange={(e) => setNotesText(e.target.value)}
            />
            <button 
              type="button" 
              className={styles.saveNotesBtn}
              onClick={async () => {
                await onUpdateCustomer({ notes: notesText });
              }}
              disabled={updating || !customer}
            >
              {updating ? 'جاري الحفظ...' : 'حفظ الملاحظات'}
            </button>
          </section>
        )}
      </div>
      <ConfirmDialog
        isOpen={followUpToDelete !== null}
        title="حذف المتابعة"
        message="سيتم حذف هذه المتابعة نهائيًا. هل تريد المتابعة؟"
        confirmLabel="حذف المتابعة"
        onConfirm={() => void deleteSelectedFollowUp()}
        onCancel={() => setFollowUpToDelete(null)}
      />
      <ConfirmDialog
        isOpen={pendingBlacklistState !== null}
        title={pendingBlacklistState ? 'حظر الرد الآلي' : 'إلغاء حظر الرد الآلي'}
        message={pendingBlacklistState
          ? `سيتم منع الردود الآلية عن ${customerName} حتى إلغاء الحظر يدويًا.`
          : `سيُسمح مجددًا بالردود الآلية عن ${customerName} وفق إعدادات المشروع.`}
        confirmLabel={pendingBlacklistState ? 'تأكيد الحظر' : 'تأكيد إلغاء الحظر'}
        onConfirm={() => {
          const nextState = pendingBlacklistState;
          setPendingBlacklistState(null);
          if (nextState !== null) void onUpdateCustomer({ isBlacklisted: nextState });
        }}
        onCancel={() => setPendingBlacklistState(null)}
      />
    </div>
  );
}
