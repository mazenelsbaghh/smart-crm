'use client';

import React, { useEffect, useRef, useState } from 'react';
import { X } from 'lucide-react';
import { Conversation, Message } from '../../types/chat';
import ThinSidebar from './shared/ThinSidebar';
import InboxMobileToolbar from './shared/InboxMobileToolbar';
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
  
  const containerRef = useRef<HTMLDivElement>(null);
  const detailsReturnFocusRef = useRef<HTMLButtonElement | null>(null);
  const detailsCloseButtonRef = useRef<HTMLButtonElement | null>(null);
  const previousConversationIdRef = useRef<string | null>(null);
  const [detailsOpen, setDetailsOpen] = useState(false);

  const closeDetails = () => {
    setDetailsOpen(false);
    window.setTimeout(() => detailsReturnFocusRef.current?.focus(), 0);
  };

  useEffect(() => {
    if (previousConversationIdRef.current === activeConv?.id) return;
    previousConversationIdRef.current = activeConv?.id ?? null;
    const closeTimer = window.setTimeout(() => setDetailsOpen(false), 0);
    return () => window.clearTimeout(closeTimer);
  }, [activeConv?.id]);

  useEffect(() => {
    if (!detailsOpen) return;
    const focusTimer = window.setTimeout(() => detailsCloseButtonRef.current?.focus(), 0);
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      setDetailsOpen(false);
      window.setTimeout(() => detailsReturnFocusRef.current?.focus(), 0);
    };
    window.addEventListener('keydown', closeOnEscape);
    return () => {
      window.clearTimeout(focusTimer);
      window.removeEventListener('keydown', closeOnEscape);
    };
  }, [detailsOpen]);

  const keepDetailsFocusInside = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'Tab') return;
    const focusable = Array.from(event.currentTarget.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), textarea:not(:disabled), select:not(:disabled), [href]'));
    if (focusable.length === 0) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };



  return (
    <div ref={containerRef} className={`${styles.inboxContainer} ${activeConv ? styles.hasActiveConv : ''}`}>
      <InboxMobileToolbar />

      {/* 1. Thin vertical Sidebar navigation */}
      <ThinSidebar />

      {/* 2. Scrollable list of metrics and customers */}
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

      {/* 3. Central chat workspace view */}
      <ChatWorkspace
        key={customer?.id || 'empty-chat'}
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
        onOpenDetails={(trigger) => {
          detailsReturnFocusRef.current = trigger;
          setDetailsOpen(true);
        }}
      />

      {/* 4. Right CRM details and metrics side-panel */}
      {activeConv && (
        <div
          className={`${styles.contextPane} ${detailsOpen ? styles.contextPaneOpen : ''}`}
          role={detailsOpen ? 'dialog' : undefined}
          aria-modal={detailsOpen ? 'true' : undefined}
          aria-labelledby={detailsOpen ? 'responsive-customer-details-title' : undefined}
          onClick={detailsOpen ? closeDetails : undefined}
          onKeyDown={detailsOpen ? keepDetailsFocusInside : undefined}
        >
          <div className={styles.contextPaneSurface} onClick={(event) => event.stopPropagation()}>
            <div className={styles.contextPaneHeader}>
              <h2 id="responsive-customer-details-title">بيانات العميل</h2>
              <button ref={detailsCloseButtonRef} type="button" onClick={closeDetails} aria-label="إغلاق بيانات العميل">
                <X size={20} aria-hidden="true" />
              </button>
            </div>
            <ContextSidebar
              key={customer?.id || 'empty-context'}
              activeConv={activeConv}
              customer={customer}
              onUpdateCustomer={onUpdateCustomer}
              updating={updating}
            />
          </div>
        </div>
      )}
    </div>
  );
}
