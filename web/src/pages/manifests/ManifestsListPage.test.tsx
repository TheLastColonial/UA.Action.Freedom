import type { RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeManifest } from '../../test/factories/manifest';
import { manifestApi } from '../../test/msw/manifests';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { manifestRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'manifests', children: manifestRoutes },
];

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('lists manifests with status and freeze state', async () => {
  worker.use(
    ...manifestApi([
      makeManifest({ id: 'UA-1', status: 'Created' }),
      makeManifest({ id: 'UA-2', status: 'Confirmed', frozen: true }),
    ]).handlers,
  );

  const screen = renderWithProviders(null, { routes, route: '/manifests', roles: ['Loader'] });

  await expect.element(screen.getByRole('link', { name: 'UA-1' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'UA-2' })).toBeInTheDocument();
  await expect.element(screen.getByText('Confirmed')).toBeInTheDocument();
});

test('only Admin/Dispatcher see "New manifest"', async () => {
  worker.use(...manifestApi([]).handlers);

  const asLoader = renderWithProviders(null, { routes, route: '/manifests', roles: ['Loader'] });
  await expect.element(asLoader.getByText('No manifests yet.')).toBeInTheDocument();
  await expect
    .element(asLoader.getByRole('link', { name: 'New manifest' }))
    .not.toBeInTheDocument();

  const asDispatcher = renderWithProviders(null, {
    routes,
    route: '/manifests',
    roles: ['Dispatcher'],
  });
  await expect
    .element(asDispatcher.getByRole('link', { name: 'New manifest' }))
    .toBeInTheDocument();
});
