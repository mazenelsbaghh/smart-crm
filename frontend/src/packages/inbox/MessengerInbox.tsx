'use client';

import React, { useState, useEffect, useRef } from 'react';
import { useSearchParams } from 'next/navigation';
import { useAuth } from '../../context/auth-context';
import { useToast } from '../../context/toast-context';
import { api } from '../../services/api';
import { SignalRService } from '../../services/signalr';
import { Conversation, Message } from '../../types/chat';
import { Customer } from '../../services/crm';
import InboxLayout from './InboxLayout';
import { resolveConversationDeepLink } from './conversation-deep-link';
import { mergeConversationMessages } from './conversation-messages';

const statusLabels: Record<string, string> = {
  All: 'الكل',
  Open: 'مفتوحة',
  Pending: 'قيد المتابعة',
  Resolved: 'تم حلها',
  Closed: 'مغلقة',
};
const CONVERSATION_PAGE_SIZE = 20;
const MESSAGE_PAGE_SIZE = 30;

export default function MessengerInbox() {
  const { activeProject, loading: authLoading, refreshProjects } = useAuth();
  const { showToast } = useToast();
  const requestedConversationId = useSearchParams().get('conversationId');
  
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [activeConv, setActiveConv] = useState<Conversation | null>(null);
  const [activeCustomer, setActiveCustomer] = useState<Customer | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [loadedConversationId, setLoadedConversationId] = useState<string | null>(null);
  const [inputMessage, setInputMessage] = useState('');
  const [sending, setSending] = useState(false);
  const [filterStatus, setFilterStatus] = useState<string>('All');
  const [searchQuery, setSearchQuery] = useState('');
  const [debouncedSearchQuery, setDebouncedSearchQuery] = useState('');
  const [loading, setLoading] = useState(true);
  const [updating, setUpdating] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);
  const [hasMoreConversations, setHasMoreConversations] = useState(false);
  const [loadingMoreConversations, setLoadingMoreConversations] = useState(false);
  const [hasOlderMessages, setHasOlderMessages] = useState(false);
  const [loadingOlderMessages, setLoadingOlderMessages] = useState(false);
  const [messageLoadError, setMessageLoadError] = useState<{ conversationId: string; message: string } | null>(null);
  const [messageReloadToken, setMessageReloadToken] = useState(0);
  const [loadingMessagesFor, setLoadingMessagesFor] = useState<string | null>(null);

  // AI Typing States
  const [aiTypingConversations, setAiTypingConversations] = useState<Record<string, boolean>>({});
  const [aiTypingStages, setAiTypingStages] = useState<Record<string, 'generating' | 'typing'>>({});
  const [aiTypingCountdowns, setAiTypingCountdowns] = useState<Record<string, number | null>>({});

  const signalRServiceRef = useRef<SignalRService | null>(null);
  const activeConvRef = useRef<Conversation | null>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);
  const messageInputRef = useRef<HTMLTextAreaElement>(null);
  const messageEndRef = useRef<HTMLDivElement>(null);
  const handledDeepLinkRef = useRef<string | null>(null);
  const loadedProjectIdRef = useRef<string | null>(null);
  const conversationQueryKeyRef = useRef('');
  const conversationPaginationControllerRef = useRef<AbortController | null>(null);
  const olderMessagesControllerRef = useRef<AbortController | null>(null);
  const messagesConversationIdRef = useRef<string | null>(null);
  const activeProjectId = activeProject?.id;

  const selectConversation = React.useCallback((conversation: Conversation | null) => {
    const nextConversationId = conversation?.id ?? null;
    if ((activeConvRef.current?.id ?? null) !== nextConversationId) {
      olderMessagesControllerRef.current?.abort();
      olderMessagesControllerRef.current = null;
      messagesConversationIdRef.current = nextConversationId;
      setMessages([]);
      setLoadedConversationId(null);
      setActiveCustomer(null);
      setMessageLoadError(null);
      setHasOlderMessages(false);
      setLoadingOlderMessages(false);
      setLoadingMessagesFor(null);
    }
    activeConvRef.current = conversation;
    setActiveConv(conversation);
  }, []);

  const resetConversationWorkspace = React.useCallback(() => {
    selectConversation(null);
  }, [selectConversation]);

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
      const target = e.target instanceof HTMLElement ? e.target : null;
      const isTextEntry = Boolean(target?.matches('input, textarea, select, [contenteditable="true"]'));

      // 1. "/" focuses search input
      if (e.key === '/' && !isTextEntry && !e.metaKey && !e.ctrlKey && !e.altKey) {
        e.preventDefault();
        searchInputRef.current?.focus();
      }

      // 2. Escape to blur active fields and close active conversation
      if (e.key === 'Escape') {
        if (target?.closest('[role="dialog"]')) return;
        if (isTextEntry && target) {
          target.blur();
          return;
        }
        selectConversation(null);
      }

      // 3. "R" focuses compose message input when conversation is active
      if ((e.key === 'r' || e.key === 'R' || e.key === 'ق') && activeConvRef.current && !isTextEntry && !e.metaKey && !e.ctrlKey && !e.altKey) {
        e.preventDefault();
        messageInputRef.current?.focus();
      }
    };

    window.addEventListener('keydown', handleGlobalKeyDown);
    return () => window.removeEventListener('keydown', handleGlobalKeyDown);
    function handleGlobalKeyDown(e: KeyboardEvent) {
      handleKeyDownGlobal(e);
    }
  }, [activeConv, selectConversation]);

  const conversationQueryKey = `${activeProjectId ?? ''}:${filterStatus}:${debouncedSearchQuery}`;
  React.useLayoutEffect(() => {
    conversationQueryKeyRef.current = conversationQueryKey;
  }, [conversationQueryKey]);

  // Fetch conversations with channel=Messenger and resolve an exact report deep link.
  useEffect(() => {
    const controller = new AbortController();
    conversationPaginationControllerRef.current?.abort();
    conversationPaginationControllerRef.current = null;
    const paginationResetTimer = window.setTimeout(() => setLoadingMoreConversations(false), 0);

    if (!activeProjectId) {
      loadedProjectIdRef.current = null;
      const resetTimer = window.setTimeout(resetConversationWorkspace, 0);
      return () => {
        window.clearTimeout(resetTimer);
        window.clearTimeout(paginationResetTimer);
        controller.abort();
        conversationPaginationControllerRef.current?.abort();
      };
    }
    if (!requestedConversationId) handledDeepLinkRef.current = null;

    const fetchConversations = async () => {
      if (loadedProjectIdRef.current !== activeProjectId) {
        loadedProjectIdRef.current = activeProjectId;
        handledDeepLinkRef.current = null;
        setConversations([]);
        setHasMoreConversations(false);
        resetConversationWorkspace();
      }
      const deepLinkKey = `${activeProjectId}:${requestedConversationId ?? ''}`;
      const shouldResolveDeepLink = Boolean(requestedConversationId)
        && handledDeepLinkRef.current !== deepLinkKey;
      if (shouldResolveDeepLink) resetConversationWorkspace();
      setLoading(true);
      setLoadError(null);

      try {
        const response = await api.get<Conversation[]>(`/api/projects/${activeProjectId}/conversations`, {
          params: {
            status: filterStatus === 'All' ? undefined : filterStatus,
            channel: 'Messenger',
            search: debouncedSearchQuery || undefined,
            limit: CONVERSATION_PAGE_SIZE,
          },
          signal: controller.signal,
          timeout: 15_000,
        });
        let nextConversations = response.data;

        if (shouldResolveDeepLink && requestedConversationId) {
          const resolution = await resolveConversationDeepLink({
            projectId: activeProjectId,
            channel: 'Messenger',
            conversationId: requestedConversationId,
            conversationPage: nextConversations,
            signal: controller.signal,
          });
          if (controller.signal.aborted) return;
          nextConversations = resolution.conversationPage;
          handledDeepLinkRef.current = deepLinkKey;
          if (resolution.conversation) {
            selectConversation(resolution.conversation);
          } else {
            showToast('المحادثة المطلوبة غير موجودة في ماسنجر أو لم تعد متاحة.', 'warning');
          }
        }

        if (controller.signal.aborted) return;
        setConversations(nextConversations);
        setHasMoreConversations(response.data.length === CONVERSATION_PAGE_SIZE);
      } catch (error) {
        if (controller.signal.aborted) return;
        console.error('Error loading Messenger conversations', error);
        setLoadError('تعذر تحميل محادثات ماسنجر. تحقق من الاتصال ثم أعد المحاولة.');
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    };

    fetchConversations();
    return () => {
      window.clearTimeout(paginationResetTimer);
      controller.abort();
      conversationPaginationControllerRef.current?.abort();
    };
  }, [activeProjectId, filterStatus, debouncedSearchQuery, reloadToken, requestedConversationId, resetConversationWorkspace, selectConversation, showToast]);

  const retryConversations = async () => {
    setLoadError(null);
    if (!activeProject) await refreshProjects();
    setReloadToken((current) => current + 1);
  };

  const loadMoreConversations = async () => {
    const oldestConversation = conversations.at(-1);
    if (!activeProjectId || !oldestConversation || loadingMoreConversations) return;
    const requestQueryKey = conversationQueryKeyRef.current;
    const controller = new AbortController();
    conversationPaginationControllerRef.current?.abort();
    conversationPaginationControllerRef.current = controller;
    setLoadingMoreConversations(true);
    try {
      const response = await api.get<Conversation[]>(`/api/projects/${activeProjectId}/conversations`, {
        params: {
          status: filterStatus === 'All' ? undefined : filterStatus,
          channel: 'Messenger',
          search: debouncedSearchQuery || undefined,
          before: oldestConversation.lastMessageAt,
          limit: CONVERSATION_PAGE_SIZE,
        },
        signal: controller.signal,
      });
      if (controller.signal.aborted || conversationQueryKeyRef.current !== requestQueryKey) return;
      setConversations((current) => {
        const knownIds = new Set(current.map((conversation) => conversation.id));
        return [...current, ...response.data.filter((conversation) => !knownIds.has(conversation.id))];
      });
      setHasMoreConversations(response.data.length === CONVERSATION_PAGE_SIZE);
    } catch (error) {
      if (controller.signal.aborted || conversationQueryKeyRef.current !== requestQueryKey) return;
      console.error('Error loading older Messenger conversations', error);
      showToast('تعذر تحميل محادثات ماسنجر الأقدم.', 'error');
    } finally {
      if (conversationPaginationControllerRef.current === controller) {
        conversationPaginationControllerRef.current = null;
        setLoadingMoreConversations(false);
      }
    }
  };

  // Fetch messages and customer details for active conversation
  useEffect(() => {
    if (!activeConv) {
      return;
    }
    const conversationId = activeConv.id;
    const customerId = activeConv.customer.id;
    const controller = new AbortController();
    let requestActive = true;
    const fetchData = async () => {
      olderMessagesControllerRef.current?.abort();
      olderMessagesControllerRef.current = null;
      setLoadingOlderMessages(false);
      if (messagesConversationIdRef.current !== conversationId) {
        messagesConversationIdRef.current = conversationId;
        setMessages([]);
        setLoadedConversationId(null);
      }
      setLoadingMessagesFor(conversationId);
      setMessageLoadError(null);
      try {
        const [msgResp, custResp] = await Promise.all([
          api.get<Message[]>(`/api/conversations/${conversationId}/messages`, {
            params: { limit: MESSAGE_PAGE_SIZE },
            signal: controller.signal,
          }),
          api.get(`/api/customers/${customerId}`, { signal: controller.signal })
        ]);
        if (!requestActive || controller.signal.aborted || activeConvRef.current?.id !== conversationId) return;
        setMessages((current) => mergeConversationMessages(msgResp.data, current));
        setLoadedConversationId(conversationId);
        setHasOlderMessages(msgResp.data.length === MESSAGE_PAGE_SIZE);
        setActiveCustomer(custResp.data);
        setTimeout(() => messageEndRef.current?.scrollIntoView({ behavior: 'smooth' }), 100);
      } catch (e) {
        if (!requestActive || controller.signal.aborted || activeConvRef.current?.id !== conversationId) return;
        console.error('Error loading messages or customer details', e);
        setMessageLoadError({ conversationId, message: 'تعذر تحميل رسائل ماسنجر أو بيانات العميل. لم يتم عرض سجل فارغ بديلًا عنها.' });
      } finally {
        if (requestActive && activeConvRef.current?.id === conversationId) setLoadingMessagesFor(null);
      }
    };
    fetchData();
    return () => {
      requestActive = false;
      controller.abort();
    };
  }, [activeConv, messageReloadToken]);

  const displayedCustomer = activeConv && activeCustomer?.id === activeConv.customer.id
    ? activeCustomer
    : null;
  const displayedMessages = loadedConversationId === activeConv?.id ? messages : [];

  const loadOlderMessages = async () => {
    const oldestMessage = messages[0];
    if (!activeConv || !oldestMessage || loadingOlderMessages) return;
    const conversationId = activeConv.id;
    const controller = new AbortController();
    olderMessagesControllerRef.current?.abort();
    olderMessagesControllerRef.current = controller;
    setLoadingOlderMessages(true);
    try {
      const response = await api.get<Message[]>(`/api/conversations/${conversationId}/messages`, {
        params: { before: oldestMessage.createdAt, limit: MESSAGE_PAGE_SIZE },
        signal: controller.signal,
      });
      if (controller.signal.aborted || activeConvRef.current?.id !== conversationId
        || messagesConversationIdRef.current !== conversationId) return;
      setMessages((current) => {
        if (messagesConversationIdRef.current !== conversationId) return current;
        return mergeConversationMessages(response.data, current);
      });
      setHasOlderMessages(response.data.length === MESSAGE_PAGE_SIZE);
    } catch (error) {
      if (controller.signal.aborted || activeConvRef.current?.id !== conversationId) return;
      console.error('Error loading older Messenger messages', error);
      showToast('تعذر تحميل رسائل ماسنجر الأقدم.', 'error');
    } finally {
      if (olderMessagesControllerRef.current === controller) {
        olderMessagesControllerRef.current = null;
        setLoadingOlderMessages(false);
      }
    }
  };

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
        if (signalRMsg.channel && signalRMsg.channel !== 'Messenger') return;
        
        // Update conversation list lastMessageAt
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
          if (messagesConversationIdRef.current !== currentActive.id) {
            messagesConversationIdRef.current = currentActive.id;
            setMessages([msg]);
          } else {
            setMessages((current) => mergeConversationMessages(current, [msg]));
          }
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
        setAiTypingCountdowns((prev) => ({
          ...prev,
          [convId]: isTyping && typeof estimatedSeconds === 'number' ? estimatedSeconds : null,
        }));
      });

      signalR.registerOnAITypingError((convId: string, message: string) => {
        showToast(message, 'error');
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

  const isAiTyping = activeConv
    ? aiTypingConversations[activeConv.id] ?? activeConv.isAiTyping ?? false
    : false;
  const aiTypingStage = activeConv
    ? aiTypingStages[activeConv.id] ?? activeConv.aiTypingStage ?? null
    : null;
  const aiTypingCountdown = activeConv
    ? Object.prototype.hasOwnProperty.call(aiTypingCountdowns, activeConv.id)
      ? aiTypingCountdowns[activeConv.id]
      : typeof activeConv.aiTypingCountdown === 'number' ? activeConv.aiTypingCountdown : null
    : null;

  useEffect(() => {
    let interval: NodeJS.Timeout;
    const conversationId = activeConv?.id;
    if (conversationId && isAiTyping && aiTypingStage === 'typing') {
      interval = setInterval(() => {
        setAiTypingCountdowns((prev) => {
          const current = prev[conversationId];
          return { ...prev, [conversationId]: current === null || current === undefined ? null : (current > 1 ? current - 1 : 1) };
        });
      }, 1000);
    }
    return () => {
      if (interval) clearInterval(interval);
    };
  }, [isAiTyping, aiTypingStage, activeConv?.id]);

  // Send reply
  const handleSend = async () => {
    if (!inputMessage.trim() || !activeConv || !activeProject || sending) return;
    const conversationId = activeConv.id;
    const content = inputMessage.trim();
    setSending(true);
    try {
      const response = await api.post<Message>(`/api/conversations/${conversationId}/messages`, {
        content,
        channel: 'Messenger'
      });
      if (activeConvRef.current?.id === conversationId) {
        setMessages((current) => mergeConversationMessages(current, [response.data]));
        setInputMessage((current) => current.trim() === content ? '' : current);
      }
      showToast('تم إرسال الرسالة', 'success');
      setTimeout(() => messageEndRef.current?.scrollIntoView({ behavior: 'smooth' }), 100);
    } catch {
      showToast('فشل إرسال الرسالة', 'error');
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

      showToast('تم تحديث بيانات العميل بنجاح', 'success');
    } catch (e) {
      console.error('Failed to update CRM info', e);
      showToast('فشل تحديث بيانات العميل', 'error');
    } finally {
      setUpdating(false);
    }
  };

  return (
    <InboxLayout
      channel="Messenger"
      customer={displayedCustomer}
      conversations={conversations}
      activeConv={activeConv}
      setActiveConv={selectConversation}
      messages={displayedMessages}
      inputMessage={inputMessage}
      setInputMessage={setInputMessage}
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
      messageInputRef={messageInputRef}
      messageEndRef={messageEndRef}
      onUpdateCustomer={handleUpdateCustomer}
      updating={updating}
      loading={loading && !!activeProject}
      loadError={!authLoading && !activeProject ? 'تعذر تحميل مساحة العمل. أعد المحاولة أو تواصل مع المدير.' : loadError}
      onRetryConversations={retryConversations}
      hasMoreConversations={hasMoreConversations}
      loadingMoreConversations={loadingMoreConversations}
      onLoadMoreConversations={() => void loadMoreConversations()}
      hasOlderMessages={loadedConversationId === activeConv?.id && hasOlderMessages}
      loadingOlderMessages={loadingOlderMessages}
      onLoadOlderMessages={() => void loadOlderMessages()}
      messageLoadError={messageLoadError?.conversationId === activeConv?.id ? messageLoadError?.message ?? null : null}
      onRetryMessages={() => setMessageReloadToken((current) => current + 1)}
      messagesLoading={loadingMessagesFor === activeConv?.id}
    />
  );
}
