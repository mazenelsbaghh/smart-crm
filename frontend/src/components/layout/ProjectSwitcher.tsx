'use client';

import { useEffect, useRef, useState } from 'react';
import { Check, ChevronDown, Plus } from 'lucide-react';
import { useAuth } from '../../context/auth-context';
import { api } from '../../services/api';
import { authErrorMessage } from '../../packages/auth/auth-error';
import styles from './layout.module.css';

interface ProjectOption { id: string; name: string }

export default function ProjectSwitcher() {
  const { activeProject, switchProject, user } = useAuth();
  const [open, setOpen] = useState(false);
  const [projects, setProjects] = useState<ProjectOption[]>([]);
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [formMode, setFormMode] = useState<'create' | 'link' | null>(null);
  const [projectName, setProjectName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [retry, setRetry] = useState(0);
  const container = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    void api.get<ProjectOption[]>('/api/auth/projects', { signal: controller.signal })
      .then(response => setProjects(response.data))
      .catch(failure => {
        if (!controller.signal.aborted) setError(authErrorMessage(failure, 'تعذر تحميل المشاريع. حاول مرة أخرى.'));
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [open, retry]);

  useEffect(() => {
    if (!open || busy) return;
    const dismiss = (event: PointerEvent) => {
      if (!container.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('pointerdown', dismiss);
    return () => document.removeEventListener('pointerdown', dismiss);
  }, [open, busy]);

  const selectProject = async (projectId: string) => {
    if (projectId === activeProject?.id) { setOpen(false); return; }
    setBusy(true);
    setError('');
    try {
      await switchProject(projectId);
    } catch (failure) {
      setError(authErrorMessage(failure, 'تعذر فتح المشروع. حاول مرة أخرى.'));
      setBusy(false);
    }
  };

  const createProject = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError('');
    try {
      const response = await api.post<{ projectId: string; name: string }>('/api/auth/projects', { name: projectName.trim() });
      setProjects(current => [...current, { id: response.data.projectId, name: response.data.name }]);
      setFormMode(null);
      setProjectName('');
      await selectProject(response.data.projectId);
    } catch (failure) {
      setError(authErrorMessage(failure, 'تعذر إنشاء المشروع. حاول مرة أخرى.'));
    } finally { setBusy(false); }
  };

  const linkProject = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError('');
    try {
      const response = await api.post<{ projectId: string }>('/api/auth/projects/link', { email, password });
      setPassword('');
      await switchProject(response.data.projectId);
      setOpen(false);
    } catch (failure) {
      setError(authErrorMessage(failure, 'تعذر ربط المشروع. تحقق من بيانات مالك المشروع.'));
    } finally { setBusy(false); }
  };

  return (
    <div ref={container} className={styles.projectSelectorContainer} onKeyDown={event => {
      if (event.key === 'Escape' && !busy) { setOpen(false); trigger.current?.focus(); }
    }} onBlur={event => {
      if (!busy && !event.currentTarget.contains(event.relatedTarget)) setOpen(false);
    }}>
      <button ref={trigger} type="button" className={styles.projectDropdownTrigger}
        aria-label={`تغيير المشروع، المشروع الحالي: ${activeProject?.name || 'غير محدد'}`}
        aria-expanded={open} aria-controls="project-switcher" disabled={busy}
        onClick={() => {
          setOpen(!open); setLoading(true); setError(''); setFormMode(null); setProjectName(''); setPassword('');
        }}>
        <span className={styles.projectLabel}>المشروع</span>
        <span className={styles.projectName}>{activeProject?.name || 'اختر مشروعًا'}</span>
        <ChevronDown size={16} aria-hidden="true" />
      </button>
      {open && <div id="project-switcher" className={styles.projectDropdown} aria-label="تغيير المشروع" aria-busy={busy || loading}>
        {loading ? <p className={styles.dropdownEmpty} role="status">تحميل المشاريع...</p> : projects.map(project => (
          <button type="button" key={project.id} disabled={busy}
            className={`${styles.dropdownItem} ${project.id === activeProject?.id ? styles.dropdownItemActive : ''}`}
            aria-current={project.id === activeProject?.id ? 'true' : undefined}
            onClick={() => void selectProject(project.id)}>
            <span>{project.name}</span>{project.id === activeProject?.id && <Check size={16} aria-label="المشروع الحالي" />}
          </button>
        ))}
        {busy && <p className={styles.dropdownEmpty} role="status">جاري فتح المشروع...</p>}
        {error && <div className={styles.projectError} role="alert">{error}
          {!formMode && <button type="button" className={styles.dropdownItem} disabled={busy} onClick={() => {
            setError(''); setLoading(true); setRetry(retry + 1);
          }}>إعادة المحاولة</button>}
        </div>}
        {!formMode && (user?.role === 'Owner' || user?.role === 'Admin') && <button type="button" className={`${styles.dropdownItem} ${styles.dropdownItemActive}`} disabled={busy} onClick={() => { setFormMode('create'); setError(''); }}>
          <Plus size={16} aria-hidden="true" /> إنشاء مشروع جديد
        </button>}
        {formMode === 'create' && <form className={styles.projectLinkForm} onSubmit={event => void createProject(event)}>
          <p>المشروع الجديد هيكون مستقل ومرتبط بحسابك الحالي.</p>
          <label>اسم المشروع الجديد<input type="text" value={projectName} maxLength={120} required disabled={busy} onChange={event => setProjectName(event.target.value)} /></label>
          <button type="submit" className={`${styles.dropdownItem} ${styles.dropdownItemActive}`} disabled={busy || !projectName.trim()}>إنشاء وفتح المشروع</button>
        </form>}
        {!formMode && <button type="button" className={styles.dropdownItem} disabled={busy} onClick={() => { setFormMode('link'); setError(''); }}>
          <Plus size={16} aria-hidden="true" /> ربط مشروع آخر
        </button>}
        {formMode === 'link' && <form className={styles.projectLinkForm} onSubmit={event => void linkProject(event)}>
          <p>اربط المشروع مرة واحدة ببيانات حساب مالكه، ثم ادخل عليه من حسابك الحالي.</p>
          <label>بريد مالك المشروع<input type="email" value={email} autoComplete="off" required disabled={busy} onChange={event => setEmail(event.target.value)} /></label>
          <label>كلمة المرور<input type="password" value={password} autoComplete="off" required disabled={busy} onChange={event => setPassword(event.target.value)} /></label>
          <button type="submit" className={styles.dropdownItem} disabled={busy}>ربط وفتح المشروع</button>
        </form>}
        {formMode && <button type="button" className={styles.dropdownItem} disabled={busy} onClick={() => { setFormMode(null); setError(''); setPassword(''); }}>رجوع للمشاريع</button>}
      </div>}
    </div>
  );
}
