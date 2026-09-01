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
  hasMore?: boolean;
  loadingMore?: boolean;
  onLoadMore?: () => void;
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
  onRetry,
  hasMore = false,
  loadingMore = false,
  onLoadMore,
}: ConversationListProps) {

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

  const searchPlaceholder = channel === 'WhatsApp'
    ? 'بحث بالاسم أو رقم واتساب...'
    : channel === 'Messenger'
      ? 'بحث بالاسم أو حساب ماسنجر...'
      : 'بحث في أصحاب التعليقات...';

  return (
    <div className={styles.conversationPanel}>
      {/* Chat List Header */}
      <div className={styles.chatListHeader}>
        <div className={styles.chatListTitleRow}>
          <h2 className={styles.chatListTitle}>المحادثات</h2>
          <button
            type="button"
            className={styles.newChatBtn}
            disabled
            aria-label="محادثة جديدة، غير متاحة حاليًا"
            title="إنشاء محادثة جديدة غير مدعوم من هذه الشاشة"
          >
            <SquarePen size={18} />
          </button>
        </div>

        {/* Search Bar */}
        <div className={styles.searchContainer}>
          <Search size={18} className={styles.searchBarIcon} />
          <input
            ref={searchInputRef}
            type="text"
            aria-label={`بحث في محادثات ${channel === 'WhatsApp' ? 'واتساب' : channel === 'Messenger' ? 'ماسنجر' : 'التعليقات'}`}
            placeholder={searchPlaceholder}
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className={styles.searchBarInput}
          />
        </div>

        {/* Filters/Tabs Scroll */}
        <div className={styles.tabsFilterScroll}>
          {Object.keys(statusLabels).map((key) => {
            const isActive = filterStatus === key;
            return (
              <button
                key={key}
                type="button"
                className={`${styles.tabFilterBtn} ${isActive ? styles.tabFilterBtnActive : ''}`}
                onClick={() => setFilterStatus(key)}
                aria-pressed={isActive}
              >
                {statusLabels[key] || key}
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
            const avatarInitial = (customerName || 'ع')[0].toUpperCase();

            return (
              <button
                key={conv.id}
                type="button"
                className={`${styles.chatListItem} ${isActive ? styles.chatListItemActive : ''}`}
                onClick={() => setActiveConv(conv)}
                aria-pressed={isActive}
                aria-label={`${customerName}، ${statusLabels[conv.status] || conv.status}${channel === 'WhatsApp' && conv.whatsAppAccountName ? `، عبر ${conv.whatsAppAccountName}` : ''}، آخر نشاط ${formatEgyptTime(conv.lastMessageAt)}`}
              >
                {/* Avatar with status indicator */}
                <div className={styles.avatarContainer}>
                  <div className={styles.avatarCircle}>
                    {avatarInitial}
                  </div>
                </div>

                {/* Content details */}
                <div className={styles.itemMainContent}>
                  <div className={styles.itemNameRow}>
                    <span className={styles.chatPartnerName}>{customerName}</span>
                    <span className={styles.chatTime}>{formatEgyptTime(conv.lastMessageAt)}</span>
                  </div>

                  <div className={styles.itemSnippetRow}>
                    <p className={styles.chatSnippet}>
                      {conv.customer.label ? `تصنيف CRM: ${conv.customer.label}` : 'لا يتوفر ملخص للرسالة في هذا المصدر'}
                    </p>
                    
                    {/* Unread count badge if active/unread */}
                    {conv.unreadCount > 0 && (
                      <span className={styles.unreadBadge} aria-label={`${conv.unreadCount} رسائل غير مقروءة`}>
                        {conv.unreadCount.toLocaleString('ar-EG')}
                      </span>
                    )}
                  </div>

                  {/* Metadata & Tag row */}
                  <div className={styles.itemFooterRow}>
                    <span className={`${styles.tagPill} ${styles.tagCommunicated}`}>
                      {statusLabels[conv.status] || conv.status}
                    </span>
                    
                    <span className={styles.chatChannelIcon} style={{ display: 'flex', alignItems: 'center', gap: '4px', fontSize: '0.75rem', fontWeight: 500 }}>
                      {channel === 'WhatsApp' && (
                        <>
                          <MessageSquare size={12} style={{ color: '#25D366' }} />
                          <span>واتساب{conv.whatsAppAccountName ? ` · ${conv.whatsAppAccountName}` : ''}</span>
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
        {!loading && !loadError && conversations.length > 0 && hasMore && onLoadMore && (
          <button
            type="button"
            className={styles.retryConversationsBtn}
            onClick={onLoadMore}
            disabled={loadingMore}
          >
            {loadingMore ? 'جاري تحميل الأقدم...' : 'تحميل محادثات أقدم'}
          </button>
        )}
      </div>
    </div>
  );
}
