import type { RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makePerson } from '../../test/factories/person';
import { personApi } from '../../test/msw/people';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { peopleRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'people', children: peopleRoutes },
];

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('lists volunteers by name', async () => {
  worker.use(
    ...personApi([
      makePerson({ firstName: 'Olena', lastName: 'K' }),
      makePerson({ firstName: 'Ihor', lastName: 'M' }),
    ]).handlers,
  );

  const screen = renderWithProviders(null, { routes, route: '/people', roles: ['Dispatcher'] });

  await expect.element(screen.getByRole('link', { name: 'Olena K' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Ihor M' })).toBeInTheDocument();
});

test('the drivers-only toggle filters the list', async () => {
  worker.use(
    ...personApi([
      makePerson({ firstName: 'Driver', lastName: 'One', isDriver: true }),
      makePerson({ firstName: 'Packer', lastName: 'Two', isDriver: false }),
    ]).handlers,
  );

  const screen = renderWithProviders(null, { routes, route: '/people', roles: ['Dispatcher'] });

  await expect.element(screen.getByRole('link', { name: 'Packer Two' })).toBeInTheDocument();

  await screen.getByLabelText('Drivers only').click();

  await expect.element(screen.getByRole('link', { name: 'Driver One' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Packer Two' })).not.toBeInTheDocument();
});

test('only an administrator sees "New volunteer"', async () => {
  worker.use(...personApi([]).handlers);

  const asDispatcher = renderWithProviders(null, {
    routes,
    route: '/people',
    roles: ['Dispatcher'],
  });
  await expect.element(asDispatcher.getByText('No volunteers recorded yet.')).toBeInTheDocument();
  await expect
    .element(asDispatcher.getByRole('link', { name: 'New volunteer' }))
    .not.toBeInTheDocument();

  const asAdmin = renderWithProviders(null, {
    routes,
    route: '/people',
    roles: ['Administrator'],
  });
  await expect.element(asAdmin.getByRole('link', { name: 'New volunteer' })).toBeInTheDocument();
});
