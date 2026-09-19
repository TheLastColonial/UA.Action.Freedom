import type { JSX } from 'react';
import { useEffect } from 'react';
import { Outlet, useLocation } from 'react-router-dom';

import { useAuth } from '../auth/useAuth';
import { PageSkeleton } from './PageSkeleton';

/**
 * Gate for the whole app. An unauthenticated visitor is sent to the identity provider; a
 * signed-in user whose role lacks a section is shown <NotAuthorized/> by that route, not
 * redirected — redirecting there would loop.
 */
export function RequireAuth(): JSX.Element {
  const auth = useAuth();
  const { pathname, search } = useLocation();

  useEffect(() => {
    if (!auth.isLoading && !auth.isAuthenticated) {
      auth.signIn(`${pathname}${search}`);
    }
  }, [auth, pathname, search]);

  if (auth.isLoading || !auth.isAuthenticated) {
    return <PageSkeleton />;
  }

  return <Outlet />;
}
