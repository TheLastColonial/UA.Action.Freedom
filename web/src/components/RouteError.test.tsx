import { RouterProvider, createMemoryRouter } from 'react-router-dom';
import { render } from 'vitest-browser-react';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';

import { ApiNotFound } from '../api/problem';
import { RouteError } from './RouteError';

function renderFailingRoute(error: unknown) {
  const router = createMemoryRouter([
    {
      path: '/',
      loader: () => {
        throw error;
      },
      element: <p>unreachable</p>,
      errorElement: <RouteError />,
    },
  ]);
  return render(<RouterProvider router={router} />);
}

beforeEach(() => {
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
  vi.spyOn(console, 'warn').mockImplementation(() => undefined);
});
afterEach(() => {
  vi.restoreAllMocks();
});

test('a resource that does not exist reads as Not found, not as a crash', async () => {
  const screen = await renderFailingRoute(new ApiNotFound());

  await expect.element(screen.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});

test('any other failure says what went wrong and offers a reload', async () => {
  const screen = await renderFailingRoute(new Error('The server could not be reached'));

  await expect
    .element(screen.getByRole('alert'))
    .toHaveTextContent('The server could not be reached');
  await expect.element(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument();
});
