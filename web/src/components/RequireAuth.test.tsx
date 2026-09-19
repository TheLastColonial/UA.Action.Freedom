import { RouterProvider, createMemoryRouter } from 'react-router-dom';
import { render } from 'vitest-browser-react';
import { expect, test, vi } from 'vitest';

import { AuthContext } from '../auth/AuthContext';
import type { FreedomAuth } from '../auth/AuthContext';
import { deriveIdentity } from '../auth/identity';
import { RequireAuth } from './RequireAuth';

function renderAt(path: string, auth: Partial<FreedomAuth>) {
  const value: FreedomAuth = {
    ...deriveIdentity({ sub: 'test-sub', roles: ['Mechanic'] }),
    isLoading: false,
    isAuthenticated: false,
    signIn: vi.fn(),
    signOut: vi.fn(),
    getAccessToken: () => undefined,
    ...auth,
  };
  const router = createMemoryRouter(
    [{ element: <RequireAuth />, children: [{ path: '*', element: <p>the page</p> }] }],
    { initialEntries: [path] },
  );
  const screen = render(
    <AuthContext.Provider value={value}>
      <RouterProvider router={router} />
    </AuthContext.Provider>,
  );
  return { screen, signIn: value.signIn };
}

test('sends a signed-out visitor to sign in, remembering the page they asked for', async () => {
  const { screen, signIn } = renderAt('/vehicles/VIN-1/servicing?from=list', {});

  await expect.element((await screen).getByText('the page')).not.toBeInTheDocument();
  expect(signIn).toHaveBeenCalledWith('/vehicles/VIN-1/servicing?from=list');
});

test('shows the page to a signed-in user without signing in again', async () => {
  const { screen, signIn } = renderAt('/vehicles', { isAuthenticated: true });

  await expect.element((await screen).getByText('the page')).toBeInTheDocument();
  expect(signIn).not.toHaveBeenCalled();
});

test('waits while the session is still being restored', async () => {
  const { screen, signIn } = renderAt('/vehicles', { isLoading: true });

  await expect.element((await screen).getByText('the page')).not.toBeInTheDocument();
  expect(signIn).not.toHaveBeenCalled();
});
