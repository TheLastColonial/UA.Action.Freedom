import type { RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeReceiver } from '../../test/factories/receiver';
import { receiverApi } from '../../test/msw/receivers';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { receiverRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'receivers', children: receiverRoutes },
];

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('lists receivers by organisation and region only', async () => {
  worker.use(
    ...receiverApi([
      makeReceiver({ organisation: 'Kyiv Aid', region: 'Kyiv Oblast' }),
      makeReceiver({ organisation: 'Lviv Relief', region: 'Lviv Oblast' }),
    ]).handlers,
  );

  const screen = renderWithProviders(null, {
    routes,
    route: '/receivers',
    roles: ['GroundOfficer'],
  });

  await expect.element(screen.getByRole('link', { name: 'Kyiv Aid' })).toBeInTheDocument();
  await expect.element(screen.getByText('Lviv Oblast')).toBeInTheDocument();
});

test('only a receivers:write holder sees "New receiver"', async () => {
  worker.use(...receiverApi([]).handlers);

  const asLoader = renderWithProviders(null, {
    routes,
    route: '/receivers',
    roles: ['Loader'],
  });
  await expect.element(asLoader.getByText('No receivers recorded yet.')).toBeInTheDocument();
  await expect
    .element(asLoader.getByRole('link', { name: 'New receiver' }))
    .not.toBeInTheDocument();

  const asGroundOfficer = renderWithProviders(null, {
    routes,
    route: '/receivers',
    roles: ['GroundOfficer'],
  });
  await expect
    .element(asGroundOfficer.getByRole('link', { name: 'New receiver' }))
    .toBeInTheDocument();
});
