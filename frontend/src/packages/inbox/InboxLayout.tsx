'use client';

import React, { useRef } from 'react';
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
  aiTypingStage: 'generating' | 'typing';
  aiTypingCountdown: number;
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
  publicComment,
  setPublicComment,
  privateDM,
  setPrivateDM,
  reaction,
  setReaction
}: InboxLayoutProps) {
  
  const containerRef = useRef<HTMLDivElement>(null);



  return (
    <div ref={containerRef} className={`${styles.inboxContainer} ${activeConv ? styles.hasActiveConv : ''}`}>
      <InboxMobileToolbar onProjectSwitch={() => setActiveConv(null)} />

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
      />

      {/* 4. Right CRM details and metrics side-panel */}
      {activeConv && (
        <ContextSidebar
          key={customer?.id || 'empty-context'}
          activeConv={activeConv}
          customer={customer}
          onUpdateCustomer={onUpdateCustomer}
          updating={updating}
        />
      )}
    </div>
  );
}
