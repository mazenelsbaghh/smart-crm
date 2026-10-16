'use client';

import React, { useEffect, useRef, useState } from 'react';
import { Conversation, Message } from '../../types/chat';
import Dialog from '../../components/shared/Dialog';
import Button from '../../components/ui/Button';
import AppShell from '../../components/layout/AppShell';
import ConversationList from './shared/ConversationList';
import ChatWorkspace from './shared/ChatWorkspace';
import ContextSidebar from './shared/ContextSidebar';
import styles from './inbox.module.css';

import { Customer } from '../../services/crm';

interface InboxLayoutProps {
  channel: 'WhatsApp' | 'Messenger' | 'Comments';
  customer: Customer | null;
  conversations: Conversation[];
  activeConv: Conversation | null;
  setActiveConv: (conv: Conversation | null) => void;
  messages: Message[];
  inputMessage: string;
  setInputMessage: (msg: string) => void;
  handleSend: () => void;
  sending: boolean;
  isAiTyping: boolean;
  aiTypingStage: 'generating' | 'typing' | null;
  aiTypingCountdown: number | null;
  searchQuery: string;
  setSearchQuery: (query: string) => void;
  filterStatus: string;
  setFilterStatus: (status: string) => void;
  statusLabels: Record<string, string>;
  searchInputRef: React.RefObject<HTMLInputElement | null>;
  messageInputRef: React.RefObject<HTMLTextAreaElement | null>;
  messageEndRef: React.RefObject<HTMLDivElement | null>;
  onUpdateCustomer: (fields: Partial<Customer>) => Promise<void>;
  updating: boolean;
  loading?: boolean;
  loadError?: string | null;
  onRetryConversations?: () => void;
  hasMoreConversations?: boolean;
  loadingMoreConversations?: boolean;
  onLoadMoreConversations?: () => void;
  hasOlderMessages?: boolean;
  loadingOlderMessages?: boolean;
  onLoadOlderMessages?: () => void;
  messageLoadError?: string | null;
  onRetryMessages?: () => void;
  messagesLoading?: boolean;
  // For Comments Channel
  publicComment?: string;
  setPublicComment?: (val: string) => void;
  privateDM?: string;
  setPrivateDM?: (val: string) => void;
  reaction?: 'LIKE' | 'LOVE' | null;
  setReaction?: (val: 'LIKE' | 'LOVE' | null) => void;
}

export default function InboxLayout({
  channel,
  customer,
  conversations,
  activeConv,
  setActiveConv,
  messages,
  inputMessage,
  setInputMessage,
  handleSend,
  sending,
  isAiTyping,
  aiTypingStage,
  aiTypingCountdown,
  searchQuery,
  setSearchQuery,
  filterStatus,
  setFilterStatus,
  statusLabels,
  searchInputRef,
  messageInputRef,
  messageEndRef,
  onUpdateCustomer,
  updating,
  loading = false,
  loadError,
  onRetryConversations,
  hasMoreConversations,
  loadingMoreConversations,
  onLoadMoreConversations,
  hasOlderMessages,
  loadingOlderMessages,
  onLoadOlderMessages,
  messageLoadError,
  onRetryMessages,
  messagesLoading,
  publicComment,
  setPublicComment,
  privateDM,
  setPrivateDM,
  reaction,
  setReaction
}: InboxLayoutProps) {
  
  const previousConversationIdRef = useRef<string | null>(null);
  const [detailsOpen, setDetailsOpen] = useState(false);

  useEffect(() => {
    if (previousConversationIdRef.current === activeConv?.id) return;
    previousConversationIdRef.current = activeConv?.id ?? null;
    const closeTimer = window.setTimeout(() => setDetailsOpen(false), 0);
    return () => window.clearTimeout(closeTimer);
  }, [activeConv?.id]);

  const customerDetails = activeConv && <ContextSidebar
    key={customer?.id || 'empty-context'} activeConv={activeConv} customer={customer}
    onUpdateCustomer={onUpdateCustomer} updating={updating} />;

  return (
    <AppShell workspace mobileConversation={Boolean(activeConv)}>
    <div className={`${styles.inboxContainer} ${activeConv ? styles.hasActiveConv : ''}`}>

      <ConversationList
        conversations={conversations}
        activeConv={activeConv}
        setActiveConv={setActiveConv}
        searchQuery={searchQuery}
        setSearchQuery={setSearchQuery}
        filterStatus={filterStatus}
        setFilterStatus={setFilterStatus}
        channel={channel}
        searchInputRef={searchInputRef}
        statusLabels={statusLabels}
        loading={loading}
        loadError={loadError}
        onRetry={onRetryConversations}
        hasMore={hasMoreConversations}
        loadingMore={loadingMoreConversations}
        onLoadMore={onLoadMoreConversations}
      />

      <ChatWorkspace
        key={activeConv?.id || 'empty-chat'}
        activeConv={activeConv}
        customer={customer}
        messages={messages}
        inputMessage={inputMessage}
        setInputMessage={setInputMessage}
        handleSend={handleSend}
        sending={sending}
        isAiTyping={isAiTyping}
        aiTypingStage={aiTypingStage}
        aiTypingCountdown={aiTypingCountdown}
        channel={channel}
        messageInputRef={messageInputRef}
        messageEndRef={messageEndRef}
        publicComment={publicComment}
        setPublicComment={setPublicComment}
        privateDM={privateDM}
        setPrivateDM={setPrivateDM}
        reaction={reaction}
        setReaction={setReaction}
        setActiveConv={setActiveConv}
        onUpdateCustomer={onUpdateCustomer}
        updating={updating}
        hasOlderMessages={hasOlderMessages}
        loadingOlderMessages={loadingOlderMessages}
        onLoadOlderMessages={onLoadOlderMessages}
        messageLoadError={messageLoadError}
        onRetryMessages={onRetryMessages}
        messagesLoading={messagesLoading}
        onOpenDetails={() => setDetailsOpen(true)}
      />

      {activeConv && <Dialog label="بيانات العميل" title="بيانات العميل" closeLabel="إغلاق بيانات العميل" placement="drawer" open={detailsOpen} onClose={() => setDetailsOpen(false)}
        footer={<Button variant="secondary" onClick={() => setDetailsOpen(false)}>العودة للمحادثة</Button>}>
        <div className={styles.customerDetailsBody}>{customerDetails}</div>
      </Dialog>}
    </div>
    </AppShell>
  );
}
