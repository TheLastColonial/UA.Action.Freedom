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

test('renders the volunteer', async () => {
  worker.use(
    ...personApi([makePerson({ id: 'p1', firstName: 'Olena', lastName: 'K', isDriver: true })])
      .handlers,
  );

  const screen = renderWithProviders(null, { routes, route: '/people/p1', roles: ['Loader'] });

  await expect.element(screen.getByRole('heading', { name: 'Olena K' })).toBeInTheDocument();
});

test('renders Not found for an unknown id', async () => {
  worker.use(...personApi([]).handlers);

  const screen = renderWithProviders(null, { routes, route: '/people/gone', roles: ['Loader'] });

  await expect.element(screen.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});

test('hides Edit and Delete from a non-administrator', async () => {
  worker.use(...personApi([makePerson({ id: 'p1', firstName: 'Olena', lastName: 'K' })]).handlers);

  const screen = renderWithProviders(null, { routes, route: '/people/p1', roles: ['Dispatcher'] });

  await expect.element(screen.getByRole('heading', { name: 'Olena K' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Edit' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Delete' })).not.toBeInTheDocument();
});

test('an administrator can delete and return to the list', async () => {
  worker.use(...personApi([makePerson({ id: 'p1', firstName: 'Olena', lastName: 'K' })]).handlers);

  const screen = renderWithProviders(null, {
    routes,
    route: '/people/p1',
    roles: ['Administrator'],
  });

  await screen.getByRole('button', { name: 'Delete' }).click();

  await expect.element(screen.getByRole('heading', { name: 'Volunteers' })).toBeInTheDocument();
  await expect.element(screen.getByText('No volunteers recorded yet.')).toBeInTheDocument();
});
