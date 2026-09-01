'use client';

import { useCallback, useLayoutEffect, useMemo, useRef, useState } from 'react';

type ActionUiState = {
  projectId?: string;
  busy: boolean;
  notice: string | null;
  actionError: string | null;
  connectionFinished: boolean;
};

type OperationToken = { projectId?: string; generation: number };

const actionFailureMessage = 'لم يتم تنفيذ الإجراء. راجع شروط الجاهزية والصلاحيات ثم أعد المحاولة. لم نغيّر الحالة المعروضة.';
const refreshFailureMessage = 'تم تنفيذ الإجراء، لكن تعذّر تحديث البيانات المعروضة. احتفظنا بآخر بيانات صحيحة؛ اضغط تحديث للمحاولة مرة أخرى.';

const emptyActionState = (projectId?: string): ActionUiState => ({
  projectId,
  busy: false,
  notice: null,
  actionError: null,
  connectionFinished: false,
});

export function useAdManagerActions(projectId?: string) {
  const [storedState, setStoredState] = useState<ActionUiState>(() => emptyActionState(projectId));
  const currentProject = useRef(projectId);
  const actionGeneration = useRef(0);
  const connectionGeneration = useRef(0);

  useLayoutEffect(() => {
    if (currentProject.current === projectId) return;
    currentProject.current = projectId;
    actionGeneration.current += 1;
    connectionGeneration.current += 1;
    setStoredState(emptyActionState(projectId));
  }, [projectId]);

  const stateForCurrentProject = useCallback((state: ActionUiState) =>
    state.projectId === projectId ? state : emptyActionState(projectId), [projectId]);

  const ownsAction = useCallback((token: OperationToken) =>
    currentProject.current === token.projectId && actionGeneration.current === token.generation, []);

  const runAction = useCallback(async (
    action: () => Promise<unknown>,
    successMessage: string,
    refreshAfterMutation: () => Promise<void>,
  ) => {
    if (currentProject.current !== projectId) return;
    const token = { projectId, generation: ++actionGeneration.current };
    setStoredState(current => ({ ...stateForCurrentProject(current), busy: true, notice: null, actionError: null }));
    let actionSucceeded = false;
    try {
      await action();
      if (!ownsAction(token)) return;
      actionSucceeded = true;
      setStoredState(current => ({ ...stateForCurrentProject(current), notice: successMessage }));
      await refreshAfterMutation();
    } catch {
      if (!ownsAction(token)) return;
      setStoredState(current => ({
        ...stateForCurrentProject(current),
        notice: null,
        actionError: actionSucceeded ? refreshFailureMessage : actionFailureMessage,
      }));
    } finally {
      if (!ownsAction(token)) return;
      setStoredState(current => ({ ...stateForCurrentProject(current), busy: false }));
    }
  }, [ownsAction, projectId, stateForCurrentProject]);

  const finishFacebookConnection = useCallback(async (
    message: string,
    refreshAfterMutation: () => Promise<void>,
  ) => {
    if (currentProject.current !== projectId) return;
    const token = { projectId, generation: ++connectionGeneration.current };
    setStoredState(current => ({ ...stateForCurrentProject(current), actionError: null }));
    await refreshAfterMutation();
    const ownsConnection = currentProject.current === token.projectId
      && connectionGeneration.current === token.generation;
    if (!ownsConnection) return;
    setStoredState(current => ({
      ...stateForCurrentProject(current),
      connectionFinished: true,
      notice: message,
    }));
  }, [projectId, stateForCurrentProject]);

  const visibleState = useMemo(() => stateForCurrentProject(storedState), [stateForCurrentProject, storedState]);
  return { ...visibleState, runAction, finishFacebookConnection };
}
