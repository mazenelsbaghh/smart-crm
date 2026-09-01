import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import ConfirmDialog from './ConfirmDialog';
import Tooltip from './Tooltip';
import { ToastProvider, useToast } from '../../context/toast-context';

describe('accessible shared feedback primitives', () => {
  it('associates a keyboard-reachable trigger with its tooltip', () => {
    render(<Tooltip content="شرح الإجراء"><button type="button">الإجراء</button></Tooltip>);

    const trigger = screen.getByRole('button', { name: 'الإجراء' });
    const tooltip = screen.getByRole('tooltip');
    expect(trigger).toHaveAttribute('aria-describedby', tooltip.id);
  });

  it('focuses the safe dialog action, closes with Escape, and restores focus', async () => {
    const onCancel = vi.fn();
    const opener = document.createElement('button');
    document.body.append(opener);
    opener.focus();

    const { unmount } = render(
      <ConfirmDialog
        isOpen
        title="تأكيد"
        message="هل تريد المتابعة؟"
        onConfirm={vi.fn()}
        onCancel={onCancel}
      />,
    );

    await waitFor(() => expect(screen.getByRole('button', { name: 'إلغاء' })).toHaveFocus());
    expect(opener).toHaveAttribute('inert');
    expect(opener).toHaveAttribute('aria-hidden', 'true');
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onCancel).toHaveBeenCalledOnce();
    unmount();
    expect(opener).toHaveFocus();
    expect(opener).not.toHaveAttribute('inert');
    expect(opener).not.toHaveAttribute('aria-hidden');
    opener.remove();
  });

  it('announces errors assertively', () => {
    function Trigger() {
      const { showToast } = useToast();
      return <button type="button" onClick={() => showToast('تعذر الحفظ', 'error')}>اعرض الخطأ</button>;
    }

    render(<ToastProvider><Trigger /></ToastProvider>);
    fireEvent.click(screen.getByRole('button', { name: 'اعرض الخطأ' }));
    expect(screen.getByRole('alert')).toHaveTextContent('تعذر الحفظ');
    expect(screen.getByRole('alert')).toHaveAttribute('aria-live', 'assertive');
  });
});
