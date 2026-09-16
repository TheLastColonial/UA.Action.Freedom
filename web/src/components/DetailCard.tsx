import type { JSX, ReactNode } from 'react';
import { useId } from 'react';

import './DetailCard.css';

interface DetailCardProps {
  title: string;
  children: ReactNode;
}

// The read-only counterpart to components/form/FormCard: same visual language, but a
// <section>+heading rather than a <fieldset>+<legend> — fieldset/legend is form semantics and
// would read oddly to a screen reader on a page with no form controls.
export function DetailCard({ title, children }: DetailCardProps): JSX.Element {
  const headingId = useId();

  return (
    <section className="detail-card" aria-labelledby={headingId}>
      <h2 id={headingId} className="detail-card__heading">
        {title}
      </h2>
      {children}
    </section>
  );
}
