import type { JSX } from 'react';
import { useRouteError } from 'react-router-dom';

import { ApiNotFound } from '../api/problem';
import { NotFound } from './NotFound';

export function RouteError(): JSX.Element {
  const error = useRouteError();

  if (error instanceof ApiNotFound) {
    return <NotFound />;
  }

  const message = error instanceof Error ? error.message : 'Something went wrong.';

  return (
    <section role="alert">
      <h1>Something went wrong</h1>
      <p>{message}</p>
      <button
        type="button"
        onClick={() => {
          window.location.reload();
        }}
      >
        Reload
      </button>
    </section>
  );
}
