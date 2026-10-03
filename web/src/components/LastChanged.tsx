import type { JSX } from 'react';

interface LastChangedProps {
  by: string | null;
  at: string | null;
}

// "Last changed by Olena Shevchenko on 2026-10-03 18:04" (ADR 0017). Says nothing for a record
// nobody has changed since it was seeded. The name is whatever the API sent: a volunteer who has
// since been erased already arrives as "Former volunteer", so there is nothing to work out here.
// A change made by a login with no linked volunteer has a time and no name.
export function LastChanged({ by, at }: LastChangedProps): JSX.Element | null {
  if (at === null) {
    return null;
  }

  const when = `${at.slice(0, 10)} ${at.slice(11, 16)}`;

  return (
    <p style={{ color: 'var(--color-text-muted)', margin: '0 0 var(--space-4) 0' }}>
      Last changed {by === null ? '' : `by ${by} `}on {when} UTC
    </p>
  );
}
