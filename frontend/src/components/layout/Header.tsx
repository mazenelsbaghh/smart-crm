'use client';

import React, { useState } from 'react';
import { useAuth } from '../../context/auth-context';
import { ChevronDown, Menu, Search, Bell, Mail, Maximize, Settings } from 'lucide-react';
import styles from './layout.module.css';

interface HeaderProps {
  onMenuClick: () => void;
}

export default function Header({ onMenuClick }: HeaderProps) {
  const { activeProject, projects, switchProject } = useAuth();
  const [projectDropdownOpen, setProjectDropdownOpen] = useState(false);

  return (
    <header className={styles.header}>
      {/* Mobile hamburger */}
      <div className={styles.mobileHamburger} onClick={onMenuClick}>
        <Menu size={24} />
      </div>

      {/* Search Input Bar (CrmX Style) */}
      <div className={styles.searchBarContainer}>
        <Search size={16} className={styles.searchIcon} />
        <input 
          type="text" 
          placeholder="بحث..." 
          className={styles.headerSearchInput} 
        />
      </div>

      {/* Right Toolbar Actions */}
      <div className={styles.headerToolbar}>
        {/* Project selector */}
        <div className={styles.projectSelectorContainer}>
          <div 
            className={styles.projectDropdownTrigger}
            onClick={() => setProjectDropdownOpen(!projectDropdownOpen)}
          >
            <span className={styles.projectLabel}>المشروع:</span>
            <span className={styles.projectName}>{activeProject?.name || 'لا يوجد مشروع محدد'}</span>
            <ChevronDown size={14} />
          </div>
          
          {projectDropdownOpen && (
            <div className={styles.projectDropdown}>
              {projects.map((proj) => (
                <div
                  key={proj.id}
                  onClick={() => {
                    switchProject(proj.id);
                    setProjectDropdownOpen(false);
                  }}
                  className={`${styles.dropdownItem} ${
                    activeProject?.id === proj.id ? styles.dropdownItemActive : ''
                  }`}
                >
                  {proj.name}
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Toolbar Icons */}
        <div className={styles.toolbarIconBtn} title="الرسائل">
          <Mail size={18} />
        </div>
        <div className={styles.toolbarIconBtn} title="الإشعارات">
          <Bell size={18} />
        </div>
        <div className={styles.toolbarIconBtn} title="ملء الشاشة">
          <Maximize size={18} />
        </div>
        <div className={styles.toolbarIconBtn} title="الإعدادات">
          <Settings size={18} />
        </div>
      </div>
    </header>
  );
}
