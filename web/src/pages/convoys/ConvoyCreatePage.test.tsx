import type { RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { convoyApi } from '../../test/msw/convoys';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { convoyRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'convoys', children: convoyRoutes },
];

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('rejects an arrival before departure', async () => {
  worker.use(...convoyApi([]).handlers);
  const screen = renderWithProviders(null, {
    routes,
    route: '/convoys/new',
    roles: ['Dispatcher'],
  });

  await screen.getByLabelText('Departs').fill('2026-03-06T08:00');
  await screen.getByLabelText('Expected arrival').fill('2026-03-01T20:00');
  await screen.getByRole('button', { name: 'Create convoy' }).click();

  await expect
    .element(screen.getByText("'Expected end' must be after 'Start'."))
    .toBeInTheDocument();
});

test('creates a convoy and opens it', async () => {
  worker.use(...convoyApi([]).handlers);
  const screen = renderWithProviders(null, {
    routes,
    route: '/convoys/new',
    roles: ['Dispatcher'],
  });

  await screen.getByLabelText('Departs').fill('2026-03-01T08:00');
  await screen.getByLabelText('Expected arrival').fill('2026-03-06T20:00');
  await screen.getByRole('button', { name: 'Create convoy' }).click();

  await expect.element(screen.getByRole('heading', { name: /Convoy #/ })).toBeInTheDocument();
});
