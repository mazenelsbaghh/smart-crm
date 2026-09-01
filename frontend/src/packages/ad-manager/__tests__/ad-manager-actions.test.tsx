import { act, renderHook } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { useAdManagerActions } from '../hooks/use-ad-manager-actions';

const deferred = <T,>() => {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((done, fail) => { resolve = done; reject = fail; });
  return { promise, resolve, reject };
};

describe('useAdManagerActions project ownership', () => {
  it.each([
    ['resolves', false],
    ['rejects', true],
  ])('ignores project A when its action %s after project B starts', async (_scenario, rejects) => {
    const actionA = deferred<void>();
    const actionB = deferred<void>();
    const refreshA = vi.fn().mockResolvedValue(undefined);
    const refreshB = vi.fn().mockResolvedValue(undefined);
    const { result, rerender } = renderHook(({ project }) => useAdManagerActions(project), {
      initialProps: { project: 'project-a' },
    });
    let completionA!: Promise<void>;
    act(() => {
      completionA = result.current.runAction(() => actionA.promise, 'اكتملت عملية A', refreshA);
    });
    expect(result.current.busy).toBe(true);

    rerender({ project: 'project-b' });
    expect(result.current.busy).toBe(false);
    expect(result.current.notice).toBeNull();
    expect(result.current.actionError).toBeNull();
    let completionB!: Promise<void>;
    act(() => {
      completionB = result.current.runAction(() => actionB.promise, 'اكتملت عملية B', refreshB);
    });
    expect(result.current.busy).toBe(true);

    await act(async () => {
      if (rejects) actionA.reject(new Error('Project A failed'));
      else actionA.resolve();
      await completionA;
    });
    expect(result.current.busy).toBe(true);
    expect(result.current.notice).toBeNull();
    expect(result.current.actionError).toBeNull();
    expect(refreshA).not.toHaveBeenCalled();

    await act(async () => {
      actionB.resolve();
      await completionB;
    });
    expect(result.current.busy).toBe(false);
    expect(result.current.notice).toBe('اكتملت عملية B');
    expect(result.current.actionError).toBeNull();
    expect(refreshB).toHaveBeenCalledOnce();

    rerender({ project: 'project-a' });
    expect(result.current.busy).toBe(false);
    expect(result.current.notice).toBeNull();
    expect(result.current.actionError).toBeNull();
  });

  it('distinguishes a refresh failure from a failed mutation', async () => {
    const refresh = vi.fn().mockRejectedValue(new Error('Refresh failed'));
    const { result } = renderHook(() => useAdManagerActions('project-a'));
    let completion!: Promise<void>;

    act(() => {
      completion = result.current.runAction(
        () => Promise.resolve(),
        'تم تنفيذ الإجراء',
        refresh,
      );
    });

    await act(async () => completion);

    expect(result.current.busy).toBe(false);
    expect(result.current.notice).toBeNull();
    expect(result.current.actionError).toContain('تم تنفيذ الإجراء');
    expect(result.current.actionError).not.toContain('لم يتم تنفيذ الإجراء');
    expect(refresh).toHaveBeenCalledOnce();
  });

  it('does not commit an old Facebook connection after the project changes', async () => {
    const refreshA = deferred<void>();
    const refreshB = deferred<void>();
    const ignoredLateRefresh = vi.fn().mockResolvedValue(undefined);
    const { result, rerender } = renderHook(({ project }) => useAdManagerActions(project), {
      initialProps: { project: 'project-a' },
    });
    const oldFinishConnection = result.current.finishFacebookConnection;
    let completionA!: Promise<void>;
    act(() => {
      completionA = oldFinishConnection('تم ربط A', () => refreshA.promise);
    });
    expect(result.current.connectionFinished).toBe(false);

    rerender({ project: 'project-b' });
    expect(result.current.connectionFinished).toBe(false);
    await act(async () => oldFinishConnection('ربط A متأخر', ignoredLateRefresh));
    expect(ignoredLateRefresh).not.toHaveBeenCalled();

    let completionB!: Promise<void>;
    act(() => {
      completionB = result.current.finishFacebookConnection('تم ربط B', () => refreshB.promise);
    });
    expect(result.current.connectionFinished).toBe(false);
    expect(result.current.notice).toBeNull();

    await act(async () => {
      refreshA.resolve();
      await completionA;
    });
    expect(result.current.connectionFinished).toBe(false);
    expect(result.current.notice).toBeNull();

    await act(async () => {
      refreshB.resolve();
      await completionB;
    });
    expect(result.current.connectionFinished).toBe(true);
    expect(result.current.notice).toBe('تم ربط B');
  });
});
