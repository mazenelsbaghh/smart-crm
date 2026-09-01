'use client';

import React, { Suspense } from 'react';
import { useSearchParams } from 'next/navigation';
import { useAuth } from '../../../context/auth-context';
import CommentsInbox from '../../../packages/inbox/CommentsInbox';

function CommentsInboxRoute() {
  const { activeProject } = useAuth();
  const conversationId = useSearchParams().get('conversationId') ?? '';
  return <CommentsInbox key={`${activeProject?.id ?? 'none'}:${conversationId}`} />;
}

export default function CommentsInboxPage() {
  return (
    <Suspense fallback={<div role="status" aria-live="polite">جاري تحميل محادثات التعليقات…</div>}>
      <CommentsInboxRoute />
    </Suspense>
  );
}
