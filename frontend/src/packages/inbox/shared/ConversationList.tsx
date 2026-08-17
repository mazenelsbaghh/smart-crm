'use client';

import React from 'react';
import { Conversation } from '../../../types/chat';
import { 
  Search, 
  MessageSquare,
  MessageCircle,
  MessageSquareMore,
  AlertTriangle,
  SquarePen,
  RefreshCw
} from 'lucide-react';
import styles from '../inbox.module.css';

interface ConversationListProps {
  conversations: Conversation[];
  activeConv: Conversation | null;
  setActiveConv: (conv: Conversation | null) => void;
  searchQuery: string;
  setSearchQuery: (query: string) => void;
  filterStatus: string;
  setFilterStatus: (status: string) => void;
  channel: 'WhatsApp' | 'Messenger' | 'Comments';
  searchInputRef: React.RefObject<HTMLInputElement | null>;
  statusLabels: Record<string, string>;
  loading?: boolean;
  loadError?: string | null;
  onRetry?: () => void;
}

export default function ConversationList({
  conversations,
  activeConv,
  setActiveConv,
  searchQuery,
  setSearchQuery,
  filterStatus,
  setFilterStatus,
  channel,
  searchInputRef,
  statusLabels,
  loading = false,
  loadError,
  onRetry
}: ConversationListProps) {

  // Fix React 19 render purity check: capture current timestamp once on mount
  const [now] = React.useState(() => Date.now());

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

  const isWithin24hWindow = (lastMessageAt: string): boolean => {
    const diff = now - new Date(lastMessageAt).getTime();
    return diff < 24 * 60 * 60 * 1000;
  };

  // Helper to determine customer tags dynamically
  const getCustomerTag = (conv: Conversation) => {
    // Simulate label tagging based on status and conversation properties
    if (conv.status === 'Open') return 'مؤهل للشراء';
    if (conv.status === 'Pending') return 'عميل حالي';
    return 'تم التواصل';
  };

  const getTagColorClass = (tag: string) => {
    if (tag === 'مؤهل للشراء' || tag === 'VIP Lead') return styles.tagQualified;
    if (tag === 'عميل حالي') return styles.tagCurrent;
    return styles.tagCommunicated;
  };

  return (
    <div className={styles.conversationPanel}>
      {/* Chat List Header */}
      <div className={styles.chatListHeader}>
        <div className={styles.chatListTitleRow}>
          <h2 className={styles.chatListTitle}>المحادثات</h2>
          <button type="button" className={styles.newChatBtn} title="محادثة جديدة">
            <SquarePen size={18} />
          </button>
        </div>

        {/* Search Bar */}
        <div className={styles.searchContainer}>
          <Search size={18} className={styles.searchBarIcon} />
          <input
            ref={searchInputRef}
            type="text"
            placeholder="بحث بالاسم أو رقم واتساب..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className={styles.searchBarInput}
          />
        </div>

        {/* Filters/Tabs Scroll */}
        <div className={styles.tabsFilterScroll}>
          {Object.keys(statusLabels).map((key) => {
            let label = 'الكل';
            if (key === 'Open') label = 'غير مقروء';
            else if (key === 'Pending') label = 'مؤهل للشراء';
            else if (key === 'Resolved') label = 'متابعة';

            const isActive = filterStatus === key;
            return (
              <button
                key={key}
                type="button"
                className={`${styles.tabFilterBtn} ${isActive ? styles.tabFilterBtnActive : ''}`}
                onClick={() => setFilterStatus(key)}
              >
                {label}
              </button>
            );
          })}
        </div>
      </div>

      {/* Scrollable Conversation List */}
      <div className={styles.conversationListScroll}>
        {loading ? (
          Array.from({ length: 5 }).map((_, idx) => (
            <div
              key={idx}
              className={styles.chatListItem}
              style={{ pointerEvents: 'none' }}
            >
              {/* Avatar placeholder */}
              <div className={styles.avatarContainer}>
                <div className={`${styles.avatarCircle} ${styles.skeleton} ${styles.skeletonCircle}`} style={{ width: '40px', height: '40px' }} />
              </div>

              {/* Content details placeholder */}
              <div className={styles.itemMainContent} style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                <div className={styles.itemNameRow}>
                  <div className={styles.skeleton} style={{ width: '80px', height: '14px' }} />
                  <div className={styles.skeleton} style={{ width: '40px', height: '10px' }} />
                </div>

                <div className={styles.skeleton} style={{ width: '100%', height: '10px' }} />
                
                <div className={styles.itemFooterRow} style={{ marginTop: '4px' }}>
                  <div className={styles.skeleton} style={{ width: '60px', height: '18px', borderRadius: '12px' }} />
                  <div className={styles.skeleton} style={{ width: '50px', height: '12px' }} />
                </div>
              </div>
            </div>
          ))
        ) : loadError ? (
          <div className={styles.conversationErrorState} role="alert">
            <AlertTriangle size={30} aria-hidden="true" />
            <p>{loadError}</p>
            {onRetry && (
              <button type="button" className={styles.retryConversationsBtn} onClick={onRetry}>
                <RefreshCw size={16} aria-hidden="true" />
                إعادة المحاولة
              </button>
            )}
          </div>
        ) : conversations.length === 0 ? (
          <div className={styles.emptyState}>
            <MessageSquare size={36} style={{ color: 'var(--text-soft)', marginBottom: '8px' }} />
            <p>لا توجد محادثات متطابقة</p>
          </div>
        ) : (
          conversations.map(conv => {
            const isActive = activeConv?.id === conv.id;
            const customerName = conv.customer.facebookName || conv.customer.name || 'عميل غير معروف';
            const customerTag = getCustomerTag(conv);
            const tagClass = getTagColorClass(customerTag);
            const avatarInitial = (customerName || 'ع')[0].toUpperCase();

            // Simulate online state based on time
            const isOnline = isWithin24hWindow(conv.lastMessageAt);

            return (
              <button
                key={conv.id}
                type="button"
                className={`${styles.chatListItem} ${isActive ? styles.chatListItemActive : ''}`}
                onClick={() => setActiveConv(conv)}
              >
                {/* Avatar with status indicator */}
                <div className={styles.avatarContainer}>
                  <div className={styles.avatarCircle}>
                    {avatarInitial}
                  </div>
                  {isOnline && <span className={styles.statusDotOnline}></span>}
                </div>

                {/* Content details */}
                <div className={styles.itemMainContent}>
                  <div className={styles.itemNameRow}>
                    <span className={styles.chatPartnerName}>{customerName}</span>
                    <span className={styles.chatTime}>{formatEgyptTime(conv.lastMessageAt)}</span>
                  </div>

                  <div className={styles.itemSnippetRow}>
                    <p className={styles.chatSnippet}>
                      {conv.status === 'Open' ? 'نعم، من فضلك قم بإنشاء مسودة...' : 'شكراً لك، سأقوم بمراجعة الملف غداً.'}
                    </p>
                    
                    {/* Unread count badge if active/unread */}
                    {conv.status === 'Open' && (
                      <span className={styles.unreadBadge}>٢</span>
                    )}
                  </div>

                  {/* Metadata & Tag row */}
                  <div className={styles.itemFooterRow}>
                    <span className={`${styles.tagPill} ${tagClass}`}>
                      {customerTag}
                    </span>
                    
                    <span className={styles.chatChannelIcon} style={{ display: 'flex', alignItems: 'center', gap: '4px', fontSize: '0.75rem', fontWeight: 500 }}>
                      {channel === 'WhatsApp' && (
                        <>
                          <MessageSquare size={12} style={{ color: '#25D366' }} />
                          <span>واتساب</span>
                        </>
                      )}
                      {channel === 'Messenger' && (
                        <>
                          <MessageCircle size={12} style={{ color: '#0084FF' }} />
                          <span>ماسنجر</span>
                        </>
                      )}
                      {channel === 'Comments' && (
                        <>
                          <MessageSquareMore size={12} style={{ color: '#FF9900' }} />
                          <span>تعليق</span>
                        </>
                      )}
                    </span>
                  </div>
                </div>
              </button>
            );
          })
        )}
      </div>
    </div>
  );
}
