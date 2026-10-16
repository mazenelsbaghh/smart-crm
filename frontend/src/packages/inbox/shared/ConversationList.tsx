'use client';

import React from 'react';
import Link from 'next/link';
import SearchField from '../../../components/ui/SearchField';
import EmptyState from '../../../components/ui/EmptyState';
import { Conversation } from '../../../types/chat';
import { 
  MessageSquare,
  MessageCircle,
  MessageSquareMore,
  AlertTriangle,
  RefreshCw
} from 'lucide-react';
import styles from '../inbox.module.css';
import ConversationStatusBadge from './ConversationStatusBadge';

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
  const hasFilters = Boolean(searchQuery.trim()) || filterStatus !== 'All';

  return (
    <div className={styles.conversationPanel}>
      <div className={styles.chatListHeader}>
        <div className={styles.chatListTitleRow}>
          <h2 className={styles.chatListTitle}>المحادثات</h2>
        </div>

        <nav className={styles.channelTabs} aria-label="قنوات المحادثات">
          {[
            { path: '/inbox', key: 'WhatsApp', label: 'واتساب', icon: MessageSquare },
            { path: '/inbox/messenger', key: 'Messenger', label: 'ماسنجر', icon: MessageCircle },
            { path: '/inbox/comments', key: 'Comments', label: 'التعليقات', icon: MessageSquareMore },
          ].map(destination => <Link key={destination.key} href={destination.path} data-dashboard-navigation
            aria-current={channel === destination.key ? 'page' : undefined}><destination.icon size={15} aria-hidden="true" />{destination.label}</Link>)}
        </nav>

        <SearchField inputRef={searchInputRef} label={`بحث في محادثات ${channel === 'WhatsApp' ? 'واتساب' : channel === 'Messenger' ? 'ماسنجر' : 'التعليقات'}`}
          placeholder={searchPlaceholder} value={searchQuery} onChange={event => setSearchQuery(event.target.value)} />

        <label className={styles.conversationStatusFilter}>
          <span>الحالة</span>
          <select value={filterStatus} onChange={event => setFilterStatus(event.target.value)} aria-label="حالة المحادثات">
            {Object.entries(statusLabels).map(([status, label]) => (
              <option key={status} value={status}>{status === 'All' ? 'كل المحادثات' : label}</option>
            ))}
          </select>
        </label>
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
          <EmptyState
            title={hasFilters ? 'لا توجد محادثات بهذه التصفية' : 'لا توجد محادثات في هذه القناة'}
            description={hasFilters ? 'جرّب اسمًا آخر أو اعرض كل الحالات.' : 'ستظهر المحادثات هنا عند وصول الرسائل.'}
            action={hasFilters && <button type="button" className={styles.retryConversationsBtn}
              onClick={() => { setSearchQuery(''); setFilterStatus('All'); }}>مسح البحث والتصفية</button>}
          />
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
                data-unread={conv.unreadCount !== null && conv.unreadCount > 0}
                onClick={() => setActiveConv(conv)}
                aria-pressed={isActive}
                aria-label={`${customerName}، ${statusLabels[conv.status] || conv.status}${channel === 'WhatsApp' && conv.whatsAppAccountName ? `، عبر ${conv.whatsAppAccountName}` : ''}، آخر نشاط ${formatEgyptTime(conv.lastMessageAt)}`}
              >
                <div className={styles.avatarContainer}>
                  <div className={styles.avatarCircle} aria-hidden="true">
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
                      {conv.customer.label ? `تصنيف CRM: ${conv.customer.label}` : (
                        conv.customer.phone ? <bdi dir="ltr">{conv.customer.phone}</bdi> : conv.assignedAgentName
                      )}
                    </p>
                    
                    {/* Unread count badge if active/unread */}
                    {conv.unreadCount !== null && conv.unreadCount > 0 && (
                      <span className={styles.unreadBadge} aria-label={`${conv.unreadCount} رسائل غير مقروءة`}>
                        {conv.unreadCount.toLocaleString('ar-EG')} غير مقروءة
                      </span>
                    )}
                  </div>

                  {/* Metadata & Tag row */}
                  <div className={styles.itemFooterRow}>
                    <ConversationStatusBadge status={conv.status} label={statusLabels[conv.status] || conv.status} />
                    {channel === 'WhatsApp' && conv.whatsAppAccountName ? (
                      <span className={styles.chatChannelIcon}>
                        <MessageSquare size={12} aria-hidden="true" />{conv.whatsAppAccountName}
                      </span>
                    ) : conv.assignedAgentName && <span className={styles.chatChannelIcon}>{conv.assignedAgentName}</span>}
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
