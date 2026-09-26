import type { JSX } from 'react';

import './Spinner.css';

interface SpinnerProps {
  /** Read out by screen readers in place of the old "Loading…" text; never shown. */
  readonly label?: string;
  readonly size?: 'sm' | 'md' | 'lg';
}

export function Spinner({ label = 'Loading…', size = 'md' }: SpinnerProps): JSX.Element {
  return (
    <span className={`spinner spinner--${size}`}>
      <span className="spinner__ring" aria-hidden="true" />
      <span className="visually-hidden">{label}</span>
    </span>
  );
}
