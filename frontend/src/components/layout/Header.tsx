'use client';

import React, { useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import Link from 'next/link';
import { Expand, Mail, Menu, Minimize, Moon, Search, Settings, Sun } from 'lucide-react';
import { useAuth } from '../../context/auth-context';
import { useToast } from '../../context/toast-context';
import { navigationItemsForRole } from '../../config/navigation';
import Tooltip from '../shared/Tooltip';
import styles from './layout.module.css';

interface HeaderProps {
  onMenuClick: () => void;
  isMenuOpen?: boolean;
}

const THEME_EVENT = 'smart-customer-theme-change';

function subscribeToTheme(onStoreChange: () => void) {
  window.addEventListener('storage', onStoreChange);
  window.addEventListener(THEME_EVENT, onStoreChange);
  return () => {
    window.removeEventListener('storage', onStoreChange);
    window.removeEventListener(THEME_EVENT, onStoreChange);
  };
}

function getLightThemeSnapshot() {
  return window.localStorage.getItem('theme') === 'light';
}

export default function Header({ onMenuClick, isMenuOpen = false }: HeaderProps) {
  const { activeProject, user } = useAuth();
  const { showToast } = useToast();
  const [query, setQuery] = useState('');
  const [searchOpen, setSearchOpen] = useState(false);
  const [activeSearchIndex, setActiveSearchIndex] = useState(0);
  const [isFullscreen, setIsFullscreen] = useState(false);
  const searchInputRef = useRef<HTMLInputElement>(null);
  const searchContainerRef = useRef<HTMLDivElement>(null);
  const isLight = useSyncExternalStore(subscribeToTheme, getLightThemeSnapshot, () => false);

  const filteredNavigation = useMemo(() => {
    const availableNavigation = navigationItemsForRole(user?.role);
    const normalized = query.trim().toLocaleLowerCase('ar');
    if (!normalized) return availableNavigation.slice(0, 6);
    return availableNavigation
      .filter((item) => item.name.toLocaleLowerCase('ar').includes(normalized))
      .slice(0, 8);
  }, [query, user?.role]);

  useEffect(() => {
    const savedTheme = window.localStorage.getItem('theme');
    if (savedTheme !== 'light') window.localStorage.setItem('theme', 'dark');
    document.body.classList.toggle('light-theme', savedTheme === 'light');
    document.body.classList.toggle('dark-theme', savedTheme !== 'light');
  }, [isLight]);

  useEffect(() => {
    const onFullscreenChange = () => setIsFullscreen(Boolean(document.fullscreenElement));
    document.addEventListener('fullscreenchange', onFullscreenChange);
    return () => document.removeEventListener('fullscreenchange', onFullscreenChange);
  }, []);

  useEffect(() => {
    const handleGlobalKeyDown = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLocaleLowerCase() === 'k') {
        event.preventDefault();
        searchInputRef.current?.focus();
        setSearchOpen(true);
      }
      if (event.key === 'Escape') {
        setSearchOpen(false);
      }
    };
    const dismissOpenMenusOnPointerDown = (event: PointerEvent) => {
      if (!searchContainerRef.current?.contains(event.target as Node)) {
        setSearchOpen(false);
      }
    };
    window.addEventListener('keydown', handleGlobalKeyDown);
    window.addEventListener('pointerdown', dismissOpenMenusOnPointerDown);
    return () => {
      window.removeEventListener('keydown', handleGlobalKeyDown);
      window.removeEventListener('pointerdown', dismissOpenMenusOnPointerDown);
    };
  }, []);

  const closeSearch = () => {
    setSearchOpen(false);
    setActiveSearchIndex(0);
    setQuery('');
  };

  const toggleTheme = () => {
    const nextTheme = isLight ? 'dark' : 'light';
    window.localStorage.setItem('theme', nextTheme);
    window.dispatchEvent(new Event(THEME_EVENT));
  };

  const toggleFullscreen = async () => {
    try {
      if (document.fullscreenElement) await document.exitFullscreen();
      else await document.documentElement.requestFullscreen();
    } catch (fullscreenError) {
      console.error('Fullscreen request failed', fullscreenError);
      showToast('تعذر تغيير وضع ملء الشاشة في هذا المتصفح.', 'warning');
    }
  };

  return (
    <header className={styles.header}>
      <button
        type="button"
        className={styles.mobileHamburger}
        onClick={onMenuClick}
        aria-label="فتح قائمة التنقل"
        aria-expanded={isMenuOpen}
        aria-controls="mobile-navigation-drawer"
      >
        <Menu size={22} aria-hidden="true" />
      </button>

      <div ref={searchContainerRef} className={styles.searchBarContainer}>
        <Search size={16} className={styles.searchIcon} aria-hidden="true" />
        <input
          ref={searchInputRef}
          type="search"
          value={query}
          placeholder="ابحث عن صفحة..."
          className={styles.headerSearchInput}
          role="combobox"
          aria-label="البحث في صفحات لوحة التحكم"
          aria-expanded={searchOpen}
          aria-haspopup="listbox"
          aria-controls="header-search-results"
          aria-autocomplete="list"
          aria-activedescendant={searchOpen && filteredNavigation[activeSearchIndex] ? `header-search-option-${activeSearchIndex}` : undefined}
          onFocus={() => {
            setSearchOpen(true);
            setActiveSearchIndex(0);
          }}
          onChange={(event) => {
            setQuery(event.target.value);
            setSearchOpen(true);
            setActiveSearchIndex(0);
          }}
          onKeyDown={(event) => {
            if (event.key === 'ArrowDown' && filteredNavigation.length > 0) {
              event.preventDefault();
              setSearchOpen(true);
              setActiveSearchIndex((index) => (index + 1) % filteredNavigation.length);
            }
            if (event.key === 'ArrowUp' && filteredNavigation.length > 0) {
              event.preventDefault();
              setSearchOpen(true);
              setActiveSearchIndex((index) => (index - 1 + filteredNavigation.length) % filteredNavigation.length);
            }
            if (event.key === 'Home' && filteredNavigation.length > 0) {
              event.preventDefault();
              setActiveSearchIndex(0);
            }
            if (event.key === 'End' && filteredNavigation.length > 0) {
              event.preventDefault();
              setActiveSearchIndex(filteredNavigation.length - 1);
            }
            if (event.key === 'Enter' && filteredNavigation[activeSearchIndex]) {
              event.preventDefault();
              document.getElementById(`header-search-option-${activeSearchIndex}`)?.click();
            }
            if (event.key === 'Escape') setSearchOpen(false);
          }}
        />
        <kbd className={styles.kbdBadge} aria-hidden="true">⌘K</kbd>
        {searchOpen && (
          <div id="header-search-results" className={styles.searchResults} role="listbox" aria-label="نتائج البحث">
            {filteredNavigation.length > 0 ? filteredNavigation.map((item, index) => {
              const Icon = item.icon;
              return (
                <Link
                  id={`header-search-option-${index}`}
                  key={item.path}
                  href={item.path}
                  role="option"
                  tabIndex={-1}
                  aria-selected={activeSearchIndex === index}
                  onPointerMove={() => setActiveSearchIndex(index)}
                  onClick={closeSearch}
                  data-dashboard-navigation
                >
                  <Icon size={16} aria-hidden="true" />
                  <span>{item.name}</span>
                </Link>
              );
            }) : <p>لا توجد صفحة بهذا الاسم.</p>}
          </div>
        )}
      </div>

      <div className={styles.headerToolbar}>
        <div
          className={styles.projectSelectorContainer}
          role="group"
          aria-label={`المشروع الحالي: ${activeProject?.name || 'غير محدد'}`}
        >
          <div className={styles.projectDropdownTrigger}>
            <span className={styles.projectLabel}>المشروع</span>
            <span className={styles.projectName}>{activeProject?.name || 'لا يوجد مشروع'}</span>
          </div>
        </div>

        <Tooltip content="فتح صندوق الوارد" position="bottom">
          <Link href="/inbox" className={styles.toolbarIconBtn} aria-label="فتح صندوق الوارد" data-dashboard-navigation>
            <Mail size={18} aria-hidden="true" />
          </Link>
        </Tooltip>
        <Tooltip content={isFullscreen ? 'الخروج من ملء الشاشة' : 'ملء الشاشة'} position="bottom">
          <button type="button" className={styles.toolbarIconBtn} onClick={() => void toggleFullscreen()} aria-label={isFullscreen ? 'الخروج من وضع ملء الشاشة' : 'تفعيل وضع ملء الشاشة'}>
            {isFullscreen ? <Minimize size={18} aria-hidden="true" /> : <Expand size={18} aria-hidden="true" />}
          </button>
        </Tooltip>
        <Tooltip content={isLight ? 'تفعيل الوضع الداكن' : 'تفعيل الوضع الفاتح'} position="bottom">
          <button type="button" className={styles.toolbarIconBtn} onClick={toggleTheme} aria-label={isLight ? 'تفعيل الوضع الداكن' : 'تفعيل الوضع الفاتح'} aria-pressed={isLight}>
            {isLight ? <Moon size={18} aria-hidden="true" /> : <Sun size={18} aria-hidden="true" />}
          </button>
        </Tooltip>
        {(user?.role === 'Owner' || user?.role === 'Admin') && (
          <Tooltip content="إعدادات المشروع" position="bottom">
            <Link href="/settings" className={styles.toolbarIconBtn} aria-label="فتح إعدادات المشروع" data-dashboard-navigation>
              <Settings size={18} aria-hidden="true" />
            </Link>
          </Tooltip>
        )}
      </div>
    </header>
  );
}
