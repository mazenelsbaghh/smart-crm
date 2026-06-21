'use client';

import React, { useState } from 'react';
import { Conversation, Message } from '../../../types/chat';
import { Customer } from '../../../services/crm';
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
  Briefcase
} from 'lucide-react';
import styles from '../inbox.module.css';

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
              {isAiTyping && (
                <div className={styles.msgRowOutgoing}>
                  <div className={`${styles.msgBubble} ${styles.msgBubbleAI}`}>
                    <div className={styles.aiBadgeRow}>
                      <Sparkles size={12} className={styles.typingSparkle} />
                      <span>الذكاء الاصطناعي</span>
                    </div>
                    <div className={styles.typingDots}>
                      {aiTypingStage === 'generating' ? (
                        <span>جاري التفكير وتوليد الرد...</span>
                      ) : (
                        <span>جاري الرد تلقائياً خلال {aiTypingCountdown} ثوانٍ</span>
                      )}
                      <span className={styles.dot}>.</span>
                      <span className={styles.dot}>.</span>
                      <span className={styles.dot}>.</span>
                    </div>
                  </div>
                </div>
              )}
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
              <h4>جدول زمن المتابعة والتقدم للعميل</h4>
            </div>
            
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
