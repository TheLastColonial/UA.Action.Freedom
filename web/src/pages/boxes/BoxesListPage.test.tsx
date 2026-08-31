import type { RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeBox } from '../../test/factories/box';
import { boxApi } from '../../test/msw/boxes';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { boxRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'boxes', children: boxRoutes },
];

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('lists boxes with their validation state', async () => {
  worker.use(
    ...boxApi([
      makeBox({ id: 1, validated: false }),
      makeBox({ id: 2, validated: true, weightKg: 12 }),
    ]).handlers,
  );

  const screen = renderWithProviders(null, { routes, route: '/boxes', roles: ['Loader'] });

  await expect.element(screen.getByRole('link', { name: '#1' })).toBeInTheDocument();
  await expect.element(screen.getByText('Validated')).toBeInTheDocument();
  await expect.element(screen.getByText('Open')).toBeInTheDocument();
});

test('a Loader can add boxes but a Purchaser cannot', async () => {
  worker.use(...boxApi([]).handlers);

  const asPurchaser = renderWithProviders(null, { routes, route: '/boxes', roles: ['Purchaser'] });
  await expect.element(asPurchaser.getByText('No boxes packed yet.')).toBeInTheDocument();
  await expect.element(asPurchaser.getByRole('link', { name: 'New box' })).not.toBeInTheDocument();

  const asLoader = renderWithProviders(null, { routes, route: '/boxes', roles: ['Loader'] });
  await expect.element(asLoader.getByRole('link', { name: 'New box' })).toBeInTheDocument();
});
