import { Archive, CheckCheck, Clock3, MessageCircle } from 'lucide-react';
import type { ConversationStatus } from '../../../types/chat';
import styles from '../inbox.module.css';

const statusIcons = {
  Open: MessageCircle,
  Pending: Clock3,
  Resolved: CheckCheck,
  Closed: Archive,
};

export default function ConversationStatusBadge({ status, label }: { status: ConversationStatus; label: string }) {
  const Icon = statusIcons[status];
  return <span className={styles.statusPill} data-status={status}>
    <Icon size={13} aria-hidden="true" />{label}
  </span>;
}
