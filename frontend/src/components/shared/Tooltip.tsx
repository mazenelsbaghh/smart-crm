'use client';

import React from 'react';
import styles from './tooltip.module.css';

interface TooltipProps {
  content: React.ReactNode;
  children: React.ReactElement;
  position?: 'top' | 'bottom' | 'left' | 'right';
  style?: React.CSSProperties;
  className?: string;
}

export default function Tooltip({ content, children, position = 'bottom', style, className }: TooltipProps) {
  return (
    <div className={`${styles.tooltipWrapper} ${className || ''}`} style={style}>
      {children}
      <div className={`${styles.tooltipTip} ${styles[position]}`}>
        {content}
      </div>
    </div>
  );
}
