'use client';

import React, { useState, useEffect, useRef } from 'react';
import { useAuth } from '../../context/auth-context';
import { useToast } from '../../context/toast-context';
import PhantomLoader from '../../components/shared/PhantomLoader';
import { api } from '../../services/api';
import { SignalRService } from '../../services/signalr';
import { Conversation, Message } from '../../types/chat';
import { 
  Search, 
  Send, 
  User, 
  MessageSquareMore,
  ThumbsUp,
  MessageCircle,
  Reply,
  Sparkles
} from 'lucide-react';
import styles from './inbox.module.css';

const statusLabels: Record<string, string> = {
  All: 'الكل',
  Open: 'مفتوحة',
  Pending: 'قيد المتابعة',
  Resolved: 'تم حلها',
  Closed: 'مغلقة',
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

export default function CommentsInbox() {
  const { activeProject } = useAuth();
  const { showToast } = useToast();
  
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [activeConv, setActiveConv] = useState<Conversation | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [publicComment, setPublicComment] = useState('');
  const [privateDM, setPrivateDM] = useState('');
  const [reaction, setReaction] = useState<'LIKE' | 'LOVE' | null>('LOVE');
  const [sending, setSending] = useState(false);
  const [aiTypingConversations, setAiTypingConversations] = useState<Record<string, boolean>>({});
  const [aiTypingStages, setAiTypingStages] = useState<Record<string, 'generating' | 'typing'>>({});
  const [aiTypingCountdown, setAiTypingCountdown] = useState(10);
  const [filterStatus, setFilterStatus] = useState<string>('All');
  const [searchQuery, setSearchQuery] = useState('');
  const [debouncedSearchQuery, setDebouncedSearchQuery] = useState('');
  const [loading, setLoading] = useState(true);

  const messageEndRef = useRef<HTMLDivElement>(null);
  const signalRServiceRef = useRef<SignalRService | null>(null);
  const activeConvRef = useRef<Conversation | null>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);
  const publicCommentInputRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    activeConvRef.current = activeConv;
  }, [activeConv]);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearchQuery(searchQuery), 300);
    return () => clearTimeout(timer);
  }, [searchQuery]);

  // Keyboard Shortcuts Listener
  useEffect(() => {
    const handleKeyDownGlobal = (e: KeyboardEvent) => {
      // 1. "/" focuses search input
      if (e.key === '/' && document.activeElement?.tagName !== 'INPUT' && document.activeElement?.tagName !== 'TEXTAREA') {
        e.preventDefault();
        searchInputRef.current?.focus();
      }

      // 2. Escape to blur active fields and close active conversation
      if (e.key === 'Escape') {
        if (document.activeElement instanceof HTMLElement) {
          document.activeElement.blur();
        }
        setActiveConv(null);
      }

      // 3. "R" focuses public reply textarea when conversation is active
      if ((e.key === 'r' || e.key === 'R' || e.key === 'ق') && activeConvRef.current && document.activeElement?.tagName !== 'INPUT' && document.activeElement?.tagName !== 'TEXTAREA') {
        e.preventDefault();
        publicCommentInputRef.current?.focus();
      }
    };

    window.addEventListener('keydown', handleGlobalKeyDown);
    return () => window.removeEventListener('keydown', handleGlobalKeyDown);
    function handleGlobalKeyDown(e: KeyboardEvent) {
      handleKeyDownGlobal(e);
    }
  }, [activeConv]);

  // Fetch conversations with channel=FacebookComment
  const fetchConversations = async () => {
    if (!activeProject) return;
    try {
      const response = await api.get<Conversation[]>(`/api/projects/${activeProject.id}/conversations`, {
        params: {
          status: filterStatus === 'All' ? undefined : filterStatus,
          channel: 'FacebookComment',
          search: debouncedSearchQuery || undefined,
          limit: 20
        }
      });
      setConversations(response.data);
    } catch (e) {
      console.error('Error loading Comment conversations', e);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchConversations();
  }, [activeProject, filterStatus, debouncedSearchQuery]);

  // Fetch messages for active conversation
  useEffect(() => {
    if (!activeConv) return;
    const fetchMessages = async () => {
      try {
        const response = await api.get<Message[]>(`/api/conversations/${activeConv.id}/messages`);
        setMessages(response.data);
        setTimeout(() => messageEndRef.current?.scrollIntoView({ behavior: 'smooth' }), 100);
      } catch (e) {
        console.error('Error loading messages', e);
      }
    };
    fetchMessages();
  }, [activeConv?.id]);

  const isAiTyping = activeConv ? !!aiTypingConversations[activeConv.id] : false;
  const aiTypingStage = activeConv ? aiTypingStages[activeConv.id] || 'generating' : 'generating';

  useEffect(() => {
    let interval: NodeJS.Timeout;
    if (isAiTyping && aiTypingStage === 'typing') {
      interval = setInterval(() => {
        setAiTypingCountdown((prev) => (prev > 1 ? prev - 1 : 1));
      }, 1000);
    }
    return () => clearInterval(interval);
  }, [isAiTyping, aiTypingStage, activeConv?.id]);

  // SignalR for real-time updates
  useEffect(() => {
    if (!activeProject) return;
    const token = localStorage.getItem('accessToken');
    if (!token) return;

    const signalR = new SignalRService(activeProject.id, token);
    signalRServiceRef.current = signalR;
    let disposed = false;

    const initSignalR = async () => {
      signalR.registerOnMessage((msg: Message) => {
        if ((msg as any).channel && (msg as any).channel !== 'FacebookComment') return;
        
        setConversations(prev => {
          const idx = prev.findIndex(c => c.id === msg.conversationId);
          if (idx >= 0) {
            const updated = [...prev];
            updated[idx] = { ...updated[idx], lastMessageAt: msg.createdAt };
            return updated.sort((a, b) => new Date(b.lastMessageAt).getTime() - new Date(a.lastMessageAt).getTime());
          }
          return prev;
        });
        
        const currentActive = activeConvRef.current;
        if (currentActive && msg.conversationId === currentActive.id) {
          setMessages(prev => {
            if (prev.find(m => m.id === msg.id)) return prev;
            return [...prev, msg];
          });
        }
        
        setTimeout(() => messageEndRef.current?.scrollIntoView({ behavior: 'smooth' }), 100);
      });

      signalR.registerOnAITyping((convId: string, isTyping: boolean, estimatedSeconds?: number, stage?: 'generating' | 'typing') => {
        setAiTypingConversations((prev) => ({
          ...prev,
          [convId]: isTyping
        }));
        if (stage) {
          setAiTypingStages((prev) => ({
            ...prev,
            [convId]: stage
          }));
        }
        if (isTyping) {
          setAiTypingCountdown(estimatedSeconds ?? 10);
        }
      });

      signalR.registerOnAITypingError((convId: string, message: string) => {
        showToast(message, 'error');
        setAiTypingConversations((prev) => ({
          ...prev,
          [convId]: false
        }));
      });

      if (!disposed) {
        try {
          await signalR.start();
        } catch (err) {
          console.error('SignalR connection error:', err);
        }
      }
    };

    initSignalR();

    return () => {
      disposed = true;
      signalR.stop();
    };
  }, [activeProject]);

  // Send composite reply (public comment + private DM + reaction)
  const handleSend = async () => {
    if ((!publicComment.trim() && !privateDM.trim()) || !activeConv || !activeProject || sending) return;
    setSending(true);
    try {
      await api.post(`/api/projects/${activeProject.id}/conversations/${activeConv.id}/comment-reply`, {
        publicComment: publicComment.trim() || null,
        privateDM: privateDM.trim() || null,
        reaction: reaction
      });
      setPublicComment('');
      setPrivateDM('');
      showToast('تم إرسال الرد', 'success');
    } catch (e) {
      showToast('فشل إرسال الرد', 'error');
    } finally {
      setSending(false);
    }
  };

  if (loading) return (
    <PhantomLoader loading={true}>
      <div style={{ height: '100vh' }} />
    </PhantomLoader>
  );

  return (
    <div className={styles.inboxContainer}>
      {/* Panel 1: Conversation List */}
      <div className={styles.conversationPanel}>
        <div className={styles.panelHeader}>
          <h2 className={styles.panelTitle}>
            <MessageSquareMore size={20} style={{ marginLeft: '8px' }} />
            صندوق التعليقات
          </h2>
          <div className={styles.searchBox}>
            <Search size={14} />
            <input
              ref={searchInputRef}
              type="text"
              placeholder="بحث بالاسم..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className={styles.searchInput}
            />
          </div>
        </div>
        
        <div className={styles.statusFilter}>
          {Object.entries(statusLabels).map(([key, label]) => (
            <button
              key={key}
              className={`${styles.statusBtn} ${filterStatus === key ? styles.statusBtnActive : ''}`}
              onClick={() => setFilterStatus(key)}
            >
              {label}
            </button>
          ))}
        </div>

        <div className={styles.conversationList}>
          {conversations.length === 0 ? (
            <div className={styles.emptyState}>
              <MessageSquareMore size={48} />
              <p>لا توجد تعليقات</p>
              <span>ابدأ بربط صفحة فيسبوك من الإعدادات</span>
            </div>
          ) : (
            conversations.map(conv => (
              <button
                key={conv.id}
                type="button"
                className={`${styles.conversationItem} ${activeConv?.id === conv.id ? styles.active : ''}`}
                onClick={() => setActiveConv(conv)}
                style={{ background: 'none', border: 'none', width: '100%', textAlign: 'right', display: 'flex', font: 'inherit', color: 'inherit' }}
              >
                <div className={styles.avatar}>
                  <User size={20} />
                </div>
                <div className={styles.conversationInfo}>
                  <div className={styles.conversationHeader}>
                    <span className={styles.customerName}>{conv.customer.facebookName || conv.customer.name}</span>
                    <span className={styles.timestamp}>{formatEgyptTime(conv.lastMessageAt)}</span>
                  </div>
                  <div className={styles.conversationMeta}>
                    <span className={`${styles.statusBadge} ${styles[`status${conv.status}`]}`}>{statusLabels[conv.status] || conv.status}</span>
                  </div>
                </div>
              </button>
            ))
          )}
        </div>
      </div>

      {/* Panel 2: Chat + Comment Reply */}
      <div className={styles.chatPanel}>
        {activeConv ? (
          <>
            <div className={styles.chatHeader}>
              <div className={styles.chatHeaderInfo}>
                <h3>{activeConv.customer.facebookName || activeConv.customer.name}</h3>
                <div className={styles.chatHeaderMeta}>
                  <MessageSquareMore size={14} />
                  <span>تعليقات فيسبوك</span>
                </div>
              </div>
            </div>

            <div className={styles.messagesContainer}>
              {messages.map((msg) => (
                <div
                  key={msg.id}
                  className={`${styles.messageBubble} ${msg.senderType === 'Customer' ? styles.incoming : styles.outgoing}`}
                >
                  {msg.facebookPostId && msg.senderType === 'Customer' && (
                    <div className={styles.postContext}>
                      📎 تعليق على منشور
                    </div>
                  )}
                  <p className={styles.messageContent}>{msg.content}</p>
                  <span className={styles.messageTime}>{formatEgyptTime(msg.createdAt)}</span>
                </div>
              ))}
              {isAiTyping && (
                <div className={`${styles.messageBubble} ${styles.outgoing} ${styles.aiTyping}`}>
                  <div className={styles.aiTypingHeader}>
                    <Sparkles size={12} className={styles.typingSparkle} style={{ marginLeft: '4px' }} />
                    {aiTypingStage === 'generating' ? (
                      <span>جاري التفكير وتوليد الرد...</span>
                    ) : (
                      <span>جاري الرد تلقائياً خلال {aiTypingCountdown} ثوانٍ</span>
                    )}
                  </div>
                  <div className={styles.typingDots}>
                    <div className={styles.typingDot} />
                    <div className={styles.typingDot} />
                    <div className={styles.typingDot} />
                  </div>
                </div>
              )}
              <div ref={messageEndRef} />
            </div>

            {/* Triple-action reply panel */}
            <div className={styles.commentReplyPanel}>
              <div className={styles.replyRow}>
                <div className={styles.replySection}>
                  <label className={styles.replyLabel}>
                    <Reply size={14} /> رد عام (تعليق)
                  </label>
                  <textarea
                    ref={publicCommentInputRef}
                    className={styles.messageInput}
                    placeholder="اكتب رد عام على التعليق..."
                    value={publicComment}
                    onChange={(e) => setPublicComment(e.target.value)}
                    rows={2}
                  />
                </div>
                <div className={styles.replySection}>
                  <label className={styles.replyLabel}>
                    <MessageCircle size={14} /> رسالة خاصة (DM)
                  </label>
                  <textarea
                    className={styles.messageInput}
                    placeholder="اكتب رسالة خاصة للعميل..."
                    value={privateDM}
                    onChange={(e) => setPrivateDM(e.target.value)}
                    rows={2}
                  />
                </div>
              </div>
              <div className={styles.replyActions}>
                <div className={styles.reactionSelector}>
                  <button
                    type="button"
                    className={`${styles.reactionBtn} ${reaction === 'LOVE' ? styles.active : ''}`}
                    onClick={() => setReaction(reaction === 'LOVE' ? null : 'LOVE')}
                  >
                    ❤️ تفاعل لاف
                  </button>
                </div>
                <button
                  className={styles.sendBtn}
                  onClick={handleSend}
                  disabled={sending || (!publicComment.trim() && !privateDM.trim())}
                >
                  <Send size={18} />
                  إرسال الرد
                </button>
              </div>
            </div>
          </>
        ) : (
          <div className={styles.noChatSelected}>
            <MessageSquareMore size={64} />
            <h3>صندوق التعليقات</h3>
            <p>اختر محادثة من القائمة لعرض التعليقات والرد عليها</p>
          </div>
        )}
      </div>
    </div>
  );
}
