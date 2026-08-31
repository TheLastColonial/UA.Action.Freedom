import type { JSX } from 'react';

/** Stand-in until a slice is built out (Phases C–G). */
export function Placeholder({ title }: { title: string }): JSX.Element {
  return (
    <section>
      <h1>{title}</h1>
      <p>This section is not built yet.</p>
    </section>
  );
}
