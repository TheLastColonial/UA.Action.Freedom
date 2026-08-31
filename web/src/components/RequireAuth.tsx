import type { JSX } from 'react';
import { useEffect } from 'react';
import { Outlet } from 'react-router-dom';

import { useAuth } from '../auth/useAuth';
import { PageSkeleton } from './PageSkeleton';

/**
 * Gate for the whole app. An unauthenticated visitor is sent to the identity provider; a
 * signed-in user whose role lacks a section is shown <NotAuthorized/> by that route, not
 * redirected — redirecting there would loop.
 */
export function RequireAuth(): JSX.Element {
  const auth = useAuth();

  useEffect(() => {
    if (!auth.isLoading && !auth.isAuthenticated) {
      auth.signIn();
    }
  }, [auth]);

  if (auth.isLoading || !auth.isAuthenticated) {
    return <PageSkeleton />;
  }

  return <Outlet />;
}
