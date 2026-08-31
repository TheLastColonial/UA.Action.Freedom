import type { JSX } from 'react';
import { Link } from 'react-router-dom';

export function NotFound(): JSX.Element {
  return (
    <section>
      <h1>Not found</h1>
      <p>That page or record does not exist.</p>
      <Link to="/">Back to the dashboard</Link>
    </section>
  );
}
