import type { RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeBox } from '../../test/factories/box';
import { makePerson } from '../../test/factories/person';
import { boxApi } from '../../test/msw/boxes';
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

test('renders the box and Not found for an unknown id', async () => {
  worker.use(...boxApi([makeBox({ id: 4 })]).handlers);

  const found = renderWithProviders(null, { routes, route: '/boxes/4', roles: ['Loader'] });
  await expect.element(found.getByRole('heading', { name: 'Box #4' })).toBeInTheDocument();

  const missing = renderWithProviders(null, { routes, route: '/boxes/99', roles: ['Loader'] });
  await expect.element(missing.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});

test('a Dispatcher can pack a box but sees no validate panel', async () => {
  worker.use(...boxApi([makeBox({ id: 4 })]).handlers, ...personApi([]).handlers);

  const screen = renderWithProviders(null, { routes, route: '/boxes/4', roles: ['Dispatcher'] });

  await expect.element(screen.getByRole('heading', { name: 'Contents' })).toBeInTheDocument();
  await expect
    .element(screen.getByRole('heading', { name: 'Validate this box' }))
    .not.toBeInTheDocument();
});

test('validating the box freezes it: panel gone, contents fixed, no Edit', async () => {
  worker.use(
    ...boxApi([makeBox({ id: 4 })]).handlers,
    ...personApi([makePerson({ id: 'v1', firstName: 'Val', lastName: 'Checker' })]).handlers,
  );

  const screen = renderWithProviders(null, { routes, route: '/boxes/4', roles: ['Loader'] });

  await screen.getByLabelText('Checked by').selectOptions('v1');
  await screen.getByLabelText('Confirmed weight (kg)').fill('18');
  await screen.getByRole('button', { name: 'Validate box' }).click();

  await expect.element(screen.getByText('Validated', { exact: true })).toBeInTheDocument();
  await expect
    .element(screen.getByRole('heading', { name: 'Validate this box' }))
    .not.toBeInTheDocument();
  await expect
    .element(screen.getByText('This box has been validated — its contents are now fixed.'))
    .toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Edit' })).not.toBeInTheDocument();
});

test('rejects a confirmed weight outside 1..500', async () => {
  worker.use(
    ...boxApi([makeBox({ id: 4 })]).handlers,
    ...personApi([makePerson({ id: 'v1' })]).handlers,
  );

  const screen = renderWithProviders(null, { routes, route: '/boxes/4', roles: ['Loader'] });

  await screen.getByLabelText('Checked by').selectOptions('v1');
  await screen.getByLabelText('Confirmed weight (kg)').fill('750');
  await screen.getByRole('button', { name: 'Validate box' }).click();

  await expect
    .element(screen.getByText("'Weight' must be a whole number between 1 and 500"))
    .toBeInTheDocument();
});
