import type { JSX } from 'react';

export function NotAuthorized(): JSX.Element {
  return (
    <section>
      <h1>Not authorised</h1>
      <p>Your account does not have access to this section. Ask an administrator if you need it.</p>
    </section>
  );
}
