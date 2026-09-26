import type { JSX } from 'react';

import './PageSkeleton.css';
import { Spinner } from './Spinner';

// Centred in the viewport, not in whatever box it happens to render in: the same spinner shows
// while the session is restored (no shell yet) and while a page's data loads (inside the shell),
// and it should sit in the same place both times.
export function PageSkeleton(): JSX.Element {
  return (
    <div className="page-skeleton" role="status" aria-live="polite" aria-busy="true">
      <Spinner size="lg" />
    </div>
  );
}
