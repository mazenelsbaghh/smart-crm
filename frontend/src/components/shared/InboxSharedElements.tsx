'use client';

import React from 'react';
import { Sparkles, LucideIcon } from 'lucide-react';
import styles from '../../packages/inbox/inbox.module.css';

// 1. AI Reply Indicator Component
interface AiReplyIndicatorProps {
  isAiTyping: boolean;
  aiTypingStage: 'generating' | 'typing';
  aiTypingCountdown: number;
}

export function AiReplyIndicator({ isAiTyping, aiTypingStage, aiTypingCountdown }: AiReplyIndicatorProps) {
  if (!isAiTyping) return null;
  return (
    <div className={styles.msgRowOutgoing}>
      <div className={`${styles.msgBubble} ${styles.msgBubbleAI}`}>
        <div className={styles.aiBadgeRow}>
          <Sparkles size={12} className={styles.typingSparkle} />
          <span>الذكاء الاصطناعي</span>
        </div>
        <div className={styles.typingDots}>
          {aiTypingStage === 'generating' ? (
            <span>جاري التفكير وتوليد الرد...</span>
          ) : (
            <span>جاري الرد تلقائياً خلال {aiTypingCountdown} ثوانٍ</span>
          )}
          <span className={styles.dot}>.</span>
          <span className={styles.dot}>.</span>
          <span className={styles.dot}>.</span>
        </div>
      </div>
    </div>
  );
}

// 2. Typing Indicator Component (for client/human replies)
interface TypingIndicatorProps {
  isTyping: boolean;
  label?: string;
}

export function TypingIndicator({ isTyping, label = 'جاري الكتابة...' }: TypingIndicatorProps) {
  if (!isTyping) return null;
  return (
    <div className={styles.msgRowIncoming}>
      <div className={styles.msgBubble}>
        <div className={styles.typingDots}>
          <span>{label}</span>
          <span className={styles.dot}>.</span>
          <span className={styles.dot}>.</span>
          <span className={styles.dot}>.</span>
        </div>
      </div>
    </div>
  );
}

// 3. Action Button Component
interface ActionButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'accent' | 'outline' | 'glass';
  size?: 'sm' | 'md' | 'lg';
  isCircular?: boolean;
  icon?: LucideIcon;
  children?: React.ReactNode;
}

export function ActionButton({ 
  variant = 'primary', 
  size = 'md', 
  isCircular = false, 
  icon: Icon, 
  children, 
  className = '', 
  ...props 
}: ActionButtonProps) {
  
  // Resolve CSS class names
  let variantClass = styles.sharedBtn;
  if (variant === 'accent') variantClass = styles.sharedBtnAccent;
  else if (variant === 'secondary') variantClass = styles.sharedBtnSecondary;
  else if (variant === 'outline') variantClass = styles.sharedBtnOutline;
  else if (variant === 'glass') variantClass = styles.sharedBtnGlass;
  
  const sizeClass = size === 'sm' ? styles.sharedBtnSm : size === 'lg' ? styles.sharedBtnLg : '';
  const circularClass = isCircular ? styles.sharedBtnCircular : '';
  
  return (
    <button 
      type="button" 
      className={`${styles.sharedBtn} ${variantClass} ${sizeClass} ${circularClass} ${className}`} 
      {...props}
    >
      {Icon && <Icon size={size === 'sm' ? 14 : size === 'lg' ? 20 : 16} />}
      {children}
    </button>
  );
}
