interface IsolatedSibling {
  element: HTMLElement;
  hadInert: boolean;
  ariaHidden: string | null;
}

/**
 * Makes every branch outside a modal unavailable to pointer and assistive-
 * technology navigation, while preserving pre-existing accessibility state.
 */
export function isolateModal(modalRoot: HTMLElement) {
  const isolated: IsolatedSibling[] = [];
  let activeBranch: HTMLElement | null = modalRoot;

  while (activeBranch && activeBranch !== document.body) {
    const parent: HTMLElement | null = activeBranch.parentElement;
    if (!parent) break;

    for (const sibling of Array.from(parent.children)) {
      if (sibling === activeBranch || !(sibling instanceof HTMLElement)) continue;
      isolated.push({
        element: sibling,
        hadInert: sibling.hasAttribute('inert'),
        ariaHidden: sibling.getAttribute('aria-hidden'),
      });
      sibling.setAttribute('inert', '');
      sibling.setAttribute('aria-hidden', 'true');
    }

    activeBranch = parent;
  }

  return () => {
    for (const { element, hadInert, ariaHidden } of isolated.reverse()) {
      if (!hadInert) element.removeAttribute('inert');
      if (ariaHidden === null) element.removeAttribute('aria-hidden');
      else element.setAttribute('aria-hidden', ariaHidden);
    }
  };
}
