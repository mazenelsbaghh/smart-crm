'use client';

import React, { useState, useEffect, useRef } from 'react';
import { useAuth } from '../../context/auth-context';
import { useToast } from '../../context/toast-context';
import PhantomLoader from '../../components/shared/PhantomLoader';
import { api } from '../../services/api';
import { SignalRService } from '../../services/signalr';
import { Conversation, Message, ConversationStatus } from '../../types/chat';
import { 
  Search, 
  Send, 
  User, 
  MessageCircle,
  Clock,
  AlertTriangle
} from 'lucide-react';
import styles from './inbox.module.css';

const statusLabels: Record<string, string> = {
  All: 'الكل',
  Open: 'مفتوحة',
  Pending: 'قيد المتابعة',
  Resolved: 'تم حلها',
  Closed: 'مغلقة',
};

const formatEgyptDateTime = (dateStr: string) => {
  try {
    const date = new Date(dateStr);
    return date.toLocaleString('ar-EG-u-nu-latn', {
      timeZone: 'Africa/Cairo',
      year: 'numeric',
      month: 'numeric',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: true
    });
  } catch {
    return dateStr;
  }
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

/** Check if the last customer message is within 24h (Messenger messaging window) */
const isWithin24hWindow = (lastMessageAt: string): boolean => {
  const diff = Date.now() - new Date(lastMessageAt).getTime();
  return diff < 24 * 60 * 60 * 1000;
};

export default function MessengerInbox() {
  const { activeProject } = useAuth();
  const { showToast } = useToast();
  
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [activeConv, setActiveConv] = useState<Conversation | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [inputMessage, setInputMessage] = useState('');
  const [sending, setSending] = useState(false);
  const [filterStatus, setFilterStatus] = useState<string>('All');
  const [searchQuery, setSearchQuery] = useState('');
  const [debouncedSearchQuery, setDebouncedSearchQuery] = useState('');
  const [hasMoreConvs, setHasMoreConvs] = useState(true);
  const [loading, setLoading] = useState(true);

  const messageEndRef = useRef<HTMLDivElement>(null);
  const signalRServiceRef = useRef<SignalRService | null>(null);
  const activeConvRef = useRef<Conversation | null>(null);

  useEffect(() => {
    activeConvRef.current = activeConv;
  }, [activeConv]);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearchQuery(searchQuery), 300);
    return () => clearTimeout(timer);
  }, [searchQuery]);

  // Fetch conversations with channel=Messenger
  const fetchConversations = async () => {
    if (!activeProject) return;
    try {
      const response = await api.get<Conversation[]>(`/api/projects/${activeProject.id}/conversations`, {
        params: {
          status: filterStatus === 'All' ? undefined : filterStatus,
          channel: 'Messenger',
          search: debouncedSearchQuery || undefined,
          limit: 20
        }
      });
      setConversations(response.data);
      setHasMoreConvs(response.data.length === 20);
    } catch (e) {
      console.error('Error loading Messenger conversations', e);
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
        // Only handle Messenger messages
        if ((msg as any).channel && (msg as any).channel !== 'Messenger') return;
        
        // Update conversation list
        setConversations(prev => {
          const idx = prev.findIndex(c => c.id === msg.conversationId);
          if (idx >= 0) {
            const updated = [...prev];
            updated[idx] = { ...updated[idx], lastMessageAt: msg.createdAt };
            return updated.sort((a, b) => new Date(b.lastMessageAt).getTime() - new Date(a.lastMessageAt).getTime());
          }
          return prev;
        });
        
        // Update messages if viewing this conversation
        const currentActive = activeConvRef.current;
        if (currentActive && msg.conversationId === currentActive.id) {
          setMessages(prev => {
            if (prev.find(m => m.id === msg.id)) return prev;
            return [...prev, msg];
          });
        }
        
        setTimeout(() => messageEndRef.current?.scrollIntoView({ behavior: 'smooth' }), 100);
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

  // Send reply
  const handleSend = async () => {
    if (!inputMessage.trim() || !activeConv || !activeProject || sending) return;
    setSending(true);
    try {
      await api.post(`/api/conversations/${activeConv.id}/messages`, {
        content: inputMessage,
        channel: 'Messenger'
      });
      setInputMessage('');
      showToast('تم إرسال الرسالة', 'success');
    } catch (e) {
      showToast('فشل إرسال الرسالة', 'error');
    } finally {
      setSending(false);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
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
            <MessageCircle size={20} style={{ marginLeft: '8px' }} />
            صندوق الماسنجر
          </h2>
          <div className={styles.searchBox}>
            <Search size={14} />
            <input
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
              <MessageCircle size={48} />
              <p>لا توجد محادثات ماسنجر</p>
              <span>ابدأ بربط صفحة فيسبوك من الإعدادات</span>
            </div>
          ) : (
            conversations.map(conv => (
              <div
                key={conv.id}
                className={`${styles.conversationItem} ${activeConv?.id === conv.id ? styles.active : ''}`}
                onClick={() => setActiveConv(conv)}
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
                    {!isWithin24hWindow(conv.lastMessageAt) && (
                      <span className={styles.windowBadge} title="انتهت نافذة الـ 24 ساعة">
                        <AlertTriangle size={12} /> 24h
                      </span>
                    )}
                  </div>
                </div>
              </div>
            ))
          )}
        </div>
      </div>

      {/* Panel 2: Chat */}
      <div className={styles.chatPanel}>
        {activeConv ? (
          <>
            <div className={styles.chatHeader}>
              <div className={styles.chatHeaderInfo}>
                <h3>{activeConv.customer.facebookName || activeConv.customer.name}</h3>
                <div className={styles.chatHeaderMeta}>
                  <MessageCircle size={14} />
                  <span>ماسنجر</span>
                  {!isWithin24hWindow(activeConv.lastMessageAt) && (
                    <span className={styles.windowWarning}>
                      <AlertTriangle size={14} /> انتهت نافذة الماسنجر (24 ساعة)
                    </span>
                  )}
                </div>
              </div>
            </div>

            <div className={styles.messagesContainer}>
              {messages.map((msg) => (
                <div
                  key={msg.id}
                  className={`${styles.messageBubble} ${msg.senderType === 'Customer' ? styles.incoming : styles.outgoing}`}
                >
                  <p className={styles.messageContent}>{msg.content}</p>
                  <span className={styles.messageTime}>{formatEgyptTime(msg.createdAt)}</span>
                </div>
              ))}
              <div ref={messageEndRef} />
            </div>

            <div className={styles.messageComposer}>
              <textarea
                className={styles.messageInput}
                placeholder={isWithin24hWindow(activeConv.lastMessageAt) ? "اكتب رسالة..." : "⚠️ نافذة الـ 24 ساعة انتهت — لا يمكن إرسال رسائل"}
                value={inputMessage}
                onChange={(e) => setInputMessage(e.target.value)}
                onKeyDown={handleKeyDown}
                disabled={!isWithin24hWindow(activeConv.lastMessageAt)}
              />
              <button
                className={styles.sendBtn}
                onClick={handleSend}
                disabled={sending || !inputMessage.trim() || !isWithin24hWindow(activeConv.lastMessageAt)}
              >
                <Send size={18} />
              </button>
            </div>
          </>
        ) : (
          <div className={styles.noChatSelected}>
            <MessageCircle size={64} />
            <h3>صندوق الماسنجر</h3>
            <p>اختر محادثة من القائمة لعرض الرسائل</p>
          </div>
        )}
      </div>
    </div>
  );
}
