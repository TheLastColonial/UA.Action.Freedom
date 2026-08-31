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

test('pre-populates the form from the volunteer', async () => {
  worker.use(
    ...personApi([
      makePerson({ id: 'p1', firstName: 'Olena', lastName: 'K', phone: '07700 900123' }),
    ]).handlers,
  );

  const screen = renderWithProviders(null, {
    routes,
    route: '/people/p1/edit',
    roles: ['Administrator'],
  });

  await expect.element(screen.getByLabelText('Phone')).toHaveValue('07700 900123');
});

test('saves changes and returns to the detail page', async () => {
  worker.use(...personApi([makePerson({ id: 'p1', firstName: 'Olena', lastName: 'K' })]).handlers);

  const screen = renderWithProviders(null, {
    routes,
    route: '/people/p1/edit',
    roles: ['Administrator'],
  });

  await screen.getByLabelText('Last name').fill('Kovalenko');
  await screen.getByRole('button', { name: 'Save changes' }).click();

  await expect
    .element(screen.getByRole('heading', { name: 'Olena Kovalenko' }))
    .toBeInTheDocument();
});
