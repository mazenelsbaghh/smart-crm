'use client';

import React, { useState, useEffect } from 'react';
import { useAuth } from '../../context/auth-context';
import { ChevronDown, Menu, Search, Bell, Mail, Maximize, Settings, Sun, Moon } from 'lucide-react';
import Tooltip from '../shared/Tooltip';
import styles from './layout.module.css';

interface HeaderProps {
  onMenuClick: () => void;
}

export default function Header({ onMenuClick }: HeaderProps) {
  const { activeProject, projects, switchProject } = useAuth();
  const [projectDropdownOpen, setProjectDropdownOpen] = useState(false);

  useEffect(() => {
    document.body.classList.remove('light-theme');
  }, []);

  return (
    <header className={styles.header}>
      {/* Mobile hamburger */}
      <button 
        type="button" 
        className={styles.mobileHamburger} 
        onClick={onMenuClick}
        aria-label="فتح القائمة الجانبية"
        style={{ background: 'none', border: 'none', padding: 0 }}
      >
        <Menu size={24} />
      </button>

      {/* Search Input Bar (CrmX Style) */}
      <div className={styles.searchBarContainer}>
        <Search size={16} className={styles.searchIcon} />
        <input 
          type="text" 
          placeholder="بحث..." 
          className={styles.headerSearchInput} 
        />
        <kbd style={{ position: 'absolute', left: '10px', top: '50%', transform: 'translateY(-50%)', pointerEvents: 'none' }}>⌘K</kbd>
      </div>

      {/* Right Toolbar Actions */}
      <div className={styles.headerToolbar}>
        {/* Project selector */}
        <div className={styles.projectSelectorContainer}>
          <button 
            type="button"
            className={styles.projectDropdownTrigger}
            onClick={() => setProjectDropdownOpen(!projectDropdownOpen)}
            style={{ background: 'none', border: 'none', display: 'flex', alignItems: 'center' }}
          >
            <span className={styles.projectLabel}>المشروع:</span>
            <span className={styles.projectName}>{activeProject?.name || 'لا يوجد مشروع محدد'}</span>
            <ChevronDown size={14} />
          </button>
          
          {projectDropdownOpen && (
            <div className={styles.projectDropdown}>
              {projects.map((proj) => (
                <button
                  key={proj.id}
                  type="button"
                  onClick={() => {
                    switchProject(proj.id);
                    setProjectDropdownOpen(false);
                  }}
                  className={`${styles.dropdownItem} ${
                    activeProject?.id === proj.id ? styles.dropdownItemActive : ''
                  }`}
                  style={{ background: 'none', border: 'none', width: '100%', display: 'block' }}
                >
                  {proj.name}
                </button>
              ))}
            </div>
          )}
        </div>

        {/* Toolbar Icons */}
        <Tooltip content="الرسائل الواردة • تواصل مباشر مع عملائك" position="bottom">
          <button type="button" className={styles.toolbarIconBtn} style={{ background: 'none', border: 'none', padding: 0 }}>
            <Mail size={18} />
          </button>
        </Tooltip>
        <Tooltip content="مركز التنبيهات • كل شيء يسير على ما يرام!" position="bottom">
          <button type="button" className={styles.toolbarIconBtn} style={{ background: 'none', border: 'none', padding: 0 }}>
            <Bell size={18} />
          </button>
        </Tooltip>
        <Tooltip content="ملء الشاشة • تخلص من المشتتات" position="bottom">
          <button type="button" className={styles.toolbarIconBtn} style={{ background: 'none', border: 'none', padding: 0 }}>
            <Maximize size={18} />
          </button>
        </Tooltip>

        <Tooltip content="الإعدادات العامة • تخصيص تفضيلاتك" position="bottom">
          <button type="button" className={styles.toolbarIconBtn} style={{ background: 'none', border: 'none', padding: 0 }}>
            <Settings size={18} />
          </button>
        </Tooltip>
      </div>
    </header>
  );
}
