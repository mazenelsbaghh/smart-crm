import type { PhantomUiAttributes } from '@aejkatappaja/phantom-ui';

declare module 'react/jsx-runtime' {
  namespace JSX {
    interface IntrinsicElements {
      'phantom-ui': PhantomUiAttributes;
    }
  }
}
