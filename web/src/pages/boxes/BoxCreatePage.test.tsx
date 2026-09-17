import type { RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeBay, makeLocation } from '../../test/factories/location';
import { makePerson } from '../../test/factories/person';
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

test('a Loader can place the new box in a bay from the same form', async () => {
  const location = makeLocation({ id: 3, name: 'Coventry Depot' });
  const bay = makeBay({ id: 9, locationId: 3, code: 'A1' });
  const volunteer = makePerson({ id: 'v1', firstName: 'Val', lastName: 'Checker' });
  worker.use(
    ...boxApi([], [volunteer.id]).handlers,
    ...personApi([volunteer]).handlers,
    ...locationApi([location], [bay]).handlers,
  );

  const screen = await renderWithProviders(null, {
    routes,
    route: '/boxes/new',
    roles: ['Loader'],
  });

  await screen.getByLabelText('Distribution hub').selectOptions('3');
  await screen.getByRole('combobox', { name: 'Bay' }).selectOptions('9');
  await screen.getByLabelText('Placed by').selectOptions('v1');
  await screen.getByRole('button', { name: 'Create box' }).click();

  await expect.element(screen.getByRole('heading', { name: /Box #/ })).toBeInTheDocument();
  await expect.element(screen.getByText(/Currently in bay.*A1/)).toBeInTheDocument();
});

test('a Dispatcher does not see the bay section at all', async () => {
  const location = makeLocation({ id: 3, name: 'Coventry Depot' });
  worker.use(
    ...boxApi([]).handlers,
    ...personApi([]).handlers,
    ...locationApi([location]).handlers,
  );

  const screen = await renderWithProviders(null, {
    routes,
    route: '/boxes/new',
    roles: ['Dispatcher'],
  });

  await screen.getByLabelText('Distribution hub').selectOptions('3');

  await expect.element(screen.getByLabelText('Bay')).not.toBeInTheDocument();
});
