'use client';

import React, { useRef, useEffect } from 'react';
import { Conversation, Message } from '../../types/chat';
import ThinSidebar from './shared/ThinSidebar';
import ConversationList from './shared/ConversationList';
import ChatWorkspace from './shared/ChatWorkspace';
import ContextSidebar from './shared/ContextSidebar';
import styles from './inbox.module.css';

// Import GSAP
import { gsap } from 'gsap';
import { useGSAP } from '@gsap/react';

import { Customer } from '../../services/crm';

// Register GSAP plugins (if any)
if (typeof window !== 'undefined') {
  gsap.registerPlugin(useGSAP);
}

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
  publicComment,
  setPublicComment,
  privateDM,
  setPrivateDM,
  reaction,
  setReaction
}: InboxLayoutProps) {
  
  const containerRef = useRef<HTMLDivElement>(null);

  // GSAP Animations
  useGSAP(() => {
    // 1. Staggered fade-up and slide-in for top KPI metric cards
    gsap.fromTo(
      `.${styles.kpiCard}`,
      { opacity: 0, y: 30 },
      { opacity: 1, y: 0, duration: 0.4, stagger: 0.08, ease: 'power2.out' }
    );

    // 2. Staggered fade-in for customer conversation list items
    gsap.fromTo(
      `.${styles.conversationItem}`,
      { opacity: 0, x: -20 },
      { opacity: 1, x: 0, duration: 0.35, stagger: 0.05, ease: 'power2.out', delay: 0.2 }
    );

    // 3. Slide-in from right for right sidebar context panel cards
    gsap.fromTo(
      [
        `.${styles.crmIntelligenceCard}`,
        `.${styles.tasksCard}`,
        `.${styles.aiInsightsCard}`,
        `.${styles.automationsCard}`
      ],
      { opacity: 0, x: 40 },
      { opacity: 1, x: 0, duration: 0.4, stagger: 0.1, ease: 'power2.out', delay: 0.1 }
    );

    // 4. Micro-bounce zoom/rotation effects on hover for circular action buttons
    const buttons = containerRef.current?.querySelectorAll(`.${styles.circularBtn}`);
    buttons?.forEach(btn => {
      btn.addEventListener('mouseenter', () => {
        gsap.to(btn, { scale: 1.15, rotation: 8, duration: 0.25, ease: 'back.out(2.2)' });
      });
      btn.addEventListener('mouseleave', () => {
        gsap.to(btn, { scale: 1, rotation: 0, duration: 0.2, ease: 'power2.out' });
      });
    });
  }, { scope: containerRef });

  // Slide-in for central chat workspace when selected conversation changes
  useEffect(() => {
    if (activeConv) {
      gsap.fromTo(
        `.${styles.workspaceHeader}`,
        { opacity: 0, y: -20 },
        { opacity: 1, y: 0, duration: 0.3, ease: 'power2.out' }
      );
      gsap.fromTo(
        `.${styles.messagesContainer}`,
        { opacity: 0 },
        { opacity: 1, duration: 0.35, ease: 'power1.out' }
      );
    }
  }, [activeConv]);

  return (
    <div ref={containerRef} className={`${styles.inboxContainer} ${activeConv ? styles.hasActiveConv : ''}`}>
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
      />

      {/* 4. Right CRM details and metrics side-panel */}
      <ContextSidebar
        key={customer?.id || 'empty-context'}
        activeConv={activeConv}
        customer={customer}
        onUpdateCustomer={onUpdateCustomer}
        updating={updating}
      />
    </div>
  );
}
