'use client';

import React, { useId } from 'react';
import styles from './tooltip.module.css';

interface TooltipProps {
  content: React.ReactNode;
  children: React.ReactElement<{ 'aria-describedby'?: string }>;
  position?: 'top' | 'bottom' | 'left' | 'right';
  style?: React.CSSProperties;
  className?: string;
}

export default function Tooltip({ content, children, position = 'bottom', style, className }: TooltipProps) {
  const tooltipId = useId();
  const describedBy = [children.props['aria-describedby'], tooltipId].filter(Boolean).join(' ');

  return (
    <div className={`${styles.tooltipWrapper} ${className || ''}`} style={style}>
      {React.cloneElement(children, { 'aria-describedby': describedBy })}
      <div id={tooltipId} role="tooltip" className={`${styles.tooltipTip} ${styles[position]}`}>
        {content}
      </div>
    </div>
  );
}
