import type { JSX } from 'react';

export function PageSkeleton(): JSX.Element {
  return (
    <div role="status" aria-live="polite" aria-busy="true">
      Loading…
    </div>
  );
}
