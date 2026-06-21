'use client';

import React, { useState, useEffect, useRef } from 'react';
import { useAuth } from '../../context/auth-context';
import { useToast } from '../../context/toast-context';
import PhantomLoader from '../../components/shared/PhantomLoader';
import { api } from '../../services/api';
import { SignalRService } from '../../services/signalr';
import { Conversation, Message } from '../../types/chat';
import InboxLayout from './InboxLayout';
import { Customer } from '../../services/crm';

const statusLabels: Record<string, string> = {
  All: 'الكل',
  Open: 'مفتوحة',
  Pending: 'قيد المتابعة',
  Resolved: 'تم حلها',
  Closed: 'مغلقة',
};

export default function CommentsInbox() {
  const { activeProject } = useAuth();
  const { showToast } = useToast();
  
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [activeConv, setActiveConv] = useState<Conversation | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  
  // Comments specific composite reply states
  const [publicComment, setPublicComment] = useState('');
  const [privateDM, setPrivateDM] = useState('');
  const [reaction, setReaction] = useState<'LIKE' | 'LOVE' | null>('LOVE');
  
  const [sending, setSending] = useState(false);
  const [filterStatus, setFilterStatus] = useState<string>('All');
  const [searchQuery, setSearchQuery] = useState('');
  const [debouncedSearchQuery, setDebouncedSearchQuery] = useState('');
  const [loading, setLoading] = useState(true);
  const [updating, setUpdating] = useState(false);

  // AI Typing States
  const [aiTypingConversations, setAiTypingConversations] = useState<Record<string, boolean>>({});
  const [aiTypingStages, setAiTypingStages] = useState<Record<string, 'generating' | 'typing'>>({});
  const [aiTypingCountdown, setAiTypingCountdown] = useState(10);

  const signalRServiceRef = useRef<SignalRService | null>(null);
  const activeConvRef = useRef<Conversation | null>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);
  const publicCommentInputRef = useRef<HTMLTextAreaElement>(null);
  const messageEndRef = useRef<HTMLDivElement>(null);

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
    // eslint-disable-next-line react-hooks/set-state-in-effect
    fetchConversations();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeProject, filterStatus, debouncedSearchQuery]);

  const [activeCustomer, setActiveCustomer] = useState<Customer | null>(null);

  // Fetch messages and customer details for active conversation
  useEffect(() => {
    if (!activeConv) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setActiveCustomer(null);
      return;
    }
    const fetchData = async () => {
      try {
        const [msgResp, custResp] = await Promise.all([
          api.get<Message[]>(`/api/conversations/${activeConv.id}/messages`),
          api.get(`/api/customers/${activeConv.customer.id}`)
        ]);
        setMessages(msgResp.data);
        setActiveCustomer(custResp.data);
        setTimeout(() => messageEndRef.current?.scrollIntoView({ behavior: 'smooth' }), 100);
      } catch (e) {
        console.error('Error loading messages or customer details', e);
      }
    };
    fetchData();
  }, [activeConv]);

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
        const signalRMsg = msg as Message & { channel?: string };
        if (signalRMsg.channel && signalRMsg.channel !== 'FacebookComment') return;
        
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeProject]);

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
      showToast('تم إرسال الرد بنجاح', 'success');
      // Trigger refresh of messages
      const msgResp = await api.get<Message[]>(`/api/conversations/${activeConv.id}/messages`);
      setMessages(msgResp.data);
    } catch {
      showToast('فشل إرسال الرد', 'error');
    } finally {
      setSending(false);
    }
  };

  // Update CRM Customer details
  const handleUpdateCustomer = async (fields: Partial<Customer>) => {
    if (!activeConv) return;
    setUpdating(true);
    try {
      const response = await api.put(`/api/customers/${activeConv.customer.id}`, fields);
      
      // Update local state
      setActiveCustomer(response.data);
      setActiveConv(prev => {
        if (!prev) return null;
        return {
          ...prev,
          customer: {
            ...prev.customer,
            ...response.data
          }
        };
      });

      setConversations(prev => prev.map(c => {
        if (c.id === activeConv.id) {
          return {
            ...c,
            customer: {
              ...c.customer,
              ...response.data
            }
          };
        }
        return c;
      }));

      showToast('تم تحديث بيانات العميل بنجاح ✨', 'success');
    } catch (e) {
      console.error('Failed to update CRM info', e);
      showToast('فشل تحديث بيانات العميل', 'error');
    } finally {
      setUpdating(false);
    }
  };

  if (loading) return (
    <PhantomLoader loading={true}>
      <div style={{ height: '100vh' }} />
    </PhantomLoader>
  );

  return (
    <InboxLayout
      channel="Comments"
      customer={activeCustomer}
      conversations={conversations}
      activeConv={activeConv}
      setActiveConv={setActiveConv}
      messages={messages}
      inputMessage=""
      setInputMessage={() => {}}
      handleSend={handleSend}
      sending={sending}
      isAiTyping={isAiTyping}
      aiTypingStage={aiTypingStage}
      aiTypingCountdown={aiTypingCountdown}
      searchQuery={searchQuery}
      setSearchQuery={setSearchQuery}
      filterStatus={filterStatus}
      setFilterStatus={setFilterStatus}
      statusLabels={statusLabels}
      searchInputRef={searchInputRef}
      messageInputRef={publicCommentInputRef}
      messageEndRef={messageEndRef}
      onUpdateCustomer={handleUpdateCustomer}
      updating={updating}
      publicComment={publicComment}
      setPublicComment={setPublicComment}
      privateDM={privateDM}
      setPrivateDM={setPrivateDM}
      reaction={reaction}
      setReaction={setReaction}
    />
  );
}
