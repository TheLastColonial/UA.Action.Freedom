import { QueryClientProvider } from '@tanstack/react-query';
import type { QueryClient } from '@tanstack/react-query';
import type { ReactElement, ReactNode } from 'react';
import { RouterProvider, createMemoryRouter } from 'react-router-dom';
import type { RouteObject } from 'react-router-dom';
import { render } from 'vitest-browser-react';

import { configureApiClient } from '../api/client';
import { createQueryClient } from '../api/queryClient';
import { AuthContext } from '../auth/AuthContext';
import type { FreedomAuth } from '../auth/AuthContext';
import { deriveIdentity } from '../auth/identity';
import type { Role } from '../auth/roles';

export interface RenderOptions {
  /** App roles the signed-in user carries. Empty = signed in with no roles. */
  roles?: readonly Role[];
  sub?: string;
  /** Initial URL. Ignored when `routes` is given with its own entries. */
  route?: string;
  routes?: RouteObject[];
  queryClient?: QueryClient;
  accessToken?: string;
}

function makeAuth(roles: readonly Role[], sub: string, accessToken: string): FreedomAuth {
  return {
    ...deriveIdentity({ sub, roles }),
    isLoading: false,
    isAuthenticated: true,
    signIn: () => undefined,
    signOut: () => undefined,
    getAccessToken: () => accessToken,
  };
}

export function renderWithProviders(
  ui: ReactNode,
  options: RenderOptions = {},
): ReturnType<typeof render> {
  const {
    roles = [],
    sub = 'test-sub',
    route = '/',
    routes,
    queryClient = createQueryClient(),
    accessToken = 'test-token',
  } = options;

  configureApiClient({ getAccessToken: () => accessToken, onUnauthorized: () => undefined });

  const router = createMemoryRouter(routes ?? [{ path: '*', element: ui as ReactElement }], {
    initialEntries: [route],
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <AuthContext.Provider value={makeAuth(roles, sub, accessToken)}>
        <RouterProvider router={router} />
      </AuthContext.Provider>
    </QueryClientProvider>,
  );
}

export function wrapWithProviders(children: ReactNode, options: RenderOptions = {}): ReactElement {
  const {
    roles = [],
    sub = 'test-sub',
    queryClient = createQueryClient(),
    accessToken = 'test-token',
  } = options;
  return (
    <QueryClientProvider client={queryClient}>
      <AuthContext.Provider value={makeAuth(roles, sub, accessToken)}>
        {children}
      </AuthContext.Provider>
    </QueryClientProvider>
  );
}
