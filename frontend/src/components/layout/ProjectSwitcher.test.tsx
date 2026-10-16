import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../context/auth-context';
import { api } from '../../services/api';
import ProjectSwitcher from './ProjectSwitcher';

const first = { id: 'first', name: 'المشروع الأول', settings: null };
const second = { id: 'second', name: 'المشروع الثاني' };

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('user', JSON.stringify({ id: 'owner', email: 'owner@example.test', role: 'Owner' }));
  localStorage.setItem('refreshToken', 'original-session');
  vi.spyOn(api, 'get').mockImplementation(async url => {
    if (url === '/api/projects') return { data: [first] } as never;
    if (url === '/api/projects/first') return { data: first } as never;
    if (url === '/api/auth/projects') return { data: [first, second] } as never;
    throw new Error(`Unexpected URL: ${url}`);
  });
});

it('opens authorized projects from the header and preserves the active project when switching fails', async () => {
  const post = vi.spyOn(api, 'post').mockRejectedValue(new Error('offline'));
  render(<AuthProvider><ProjectSwitcher /></AuthProvider>);
  const trigger = await screen.findByRole('button', { name: /المشروع الحالي: المشروع الأول/ });
  await userEvent.click(trigger);
  await userEvent.click(await screen.findByRole('button', { name: 'المشروع الثاني' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('تعذر فتح المشروع');
  expect(post).toHaveBeenCalledWith('/api/auth/projects/switch', { projectId: 'second', refreshToken: 'original-session' });
  expect(JSON.parse(localStorage.getItem('activeProject')!)).toEqual(first);
  expect(localStorage.getItem('refreshToken')).toBe('original-session');
  await userEvent.keyboard('{Escape}');
  expect(trigger).toHaveAttribute('aria-expanded', 'false');
  expect(trigger).toHaveFocus();
});

it('shows a failed owner verification without switching or saving the supplied password', async () => {
  const post = vi.spyOn(api, 'post').mockRejectedValue({ response: { status: 400, data: { error: 'بيانات المالك غير صحيحة' } } });
  render(<AuthProvider><ProjectSwitcher /></AuthProvider>);
  await userEvent.click(await screen.findByRole('button', { name: /المشروع الحالي: المشروع الأول/ }));
  await userEvent.click(await screen.findByRole('button', { name: 'ربط مشروع آخر' }));
  await userEvent.type(screen.getByLabelText('بريد مالك المشروع'), 'another@example.test');
  await userEvent.type(screen.getByLabelText('كلمة المرور'), 'owner-password');
  await userEvent.click(screen.getByRole('button', { name: 'ربط وفتح المشروع' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('بيانات المالك غير صحيحة');
  expect(post).toHaveBeenCalledTimes(1);
  expect(post).toHaveBeenCalledWith('/api/auth/projects/link', { email: 'another@example.test', password: 'owner-password' });
  await waitFor(() => expect(screen.getByRole('button', { name: 'ربط وفتح المشروع' })).toBeEnabled());
  expect(Array.from({ length: localStorage.length }, (_, index) => localStorage.getItem(localStorage.key(index)!)).join('')).not.toContain('owner-password');
});

it('creates a project with a name and offers switching again without duplicating it after a switch failure', async () => {
  const post = vi.spyOn(api, 'post').mockImplementation(async url => {
    if (url === '/api/auth/projects') return { data: { projectId: 'new-project', name: 'المشروع الجديد' } } as never;
    throw new Error('switch temporarily unavailable');
  });
  render(<AuthProvider><ProjectSwitcher /></AuthProvider>);
  await userEvent.click(await screen.findByRole('button', { name: /المشروع الحالي: المشروع الأول/ }));
  await userEvent.click(await screen.findByRole('button', { name: 'إنشاء مشروع جديد' }));
  expect(screen.queryByLabelText('كلمة المرور')).not.toBeInTheDocument();
  const submit = screen.getByRole('button', { name: 'إنشاء وفتح المشروع' });
  expect(submit).toBeDisabled();
  await userEvent.type(screen.getByLabelText('اسم المشروع الجديد'), 'المشروع الجديد');
  await userEvent.click(submit);
  expect(await screen.findByRole('alert')).toHaveTextContent('تعذر فتح المشروع');
  await userEvent.click(screen.getByRole('button', { name: 'المشروع الجديد' }));
  await waitFor(() => expect(post).toHaveBeenCalledTimes(3));
  expect(post.mock.calls.filter(([url]) => url === '/api/auth/projects')).toHaveLength(1);
  expect(post).toHaveBeenCalledWith('/api/auth/projects', { name: 'المشروع الجديد' });
});
