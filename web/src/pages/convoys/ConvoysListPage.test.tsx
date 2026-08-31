import type { RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeConvoy } from '../../test/factories/convoy';
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

test('lists convoys with their truck-list state', async () => {
  worker.use(
    ...convoyApi([
      makeConvoy({ id: 1, truckListPublished: false }),
      makeConvoy({ id: 2, truckListPublished: true, truckListPublishedAt: '2026-02-01T00:00:00' }),
    ]).handlers,
  );

  const screen = renderWithProviders(null, { routes, route: '/convoys', roles: ['Dispatcher'] });

  await expect.element(screen.getByRole('link', { name: '#1' })).toBeInTheDocument();
  await expect.element(screen.getByText('Published')).toBeInTheDocument();
  await expect.element(screen.getByText('Open')).toBeInTheDocument();
});

test('only a writer sees "New convoy"', async () => {
  worker.use(...convoyApi([]).handlers);

  const asLoader = renderWithProviders(null, { routes, route: '/convoys', roles: ['Loader'] });
  await expect.element(asLoader.getByText('No convoys planned yet.')).toBeInTheDocument();
  await expect.element(asLoader.getByRole('link', { name: 'New convoy' })).not.toBeInTheDocument();

  const asDispatcher = renderWithProviders(null, {
    routes,
    route: '/convoys',
    roles: ['Dispatcher'],
  });
  await expect.element(asDispatcher.getByRole('link', { name: 'New convoy' })).toBeInTheDocument();
});
