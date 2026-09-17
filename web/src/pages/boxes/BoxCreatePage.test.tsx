import type { RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeLocation } from '../../test/factories/location';
import { boxApi } from '../../test/msw/boxes';
import { locationApi } from '../../test/msw/locations';
import { personApi } from '../../test/msw/people';
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

test('creates a box and opens it', async () => {
  const location = makeLocation({ id: 3, name: 'Coventry Depot' });
  worker.use(
    ...boxApi([]).handlers,
    ...personApi([]).handlers,
    ...locationApi([location]).handlers,
  );

  const screen = await renderWithProviders(null, {
    routes,
    route: '/boxes/new',
    roles: ['Loader'],
  });

  await screen.getByLabelText('Distribution hub').selectOptions('3');
  await screen.getByRole('button', { name: 'Create box' }).click();

  await expect.element(screen.getByRole('heading', { name: /Box #/ })).toBeInTheDocument();
});
