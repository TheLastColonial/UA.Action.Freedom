import { http } from 'msw';
import type { RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { validationProblem } from '../../test/msw/problem';
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

test('shows client validation for the required names', async () => {
  worker.use(...personApi([]).handlers);
  const screen = renderWithProviders(null, {
    routes,
    route: '/people/new',
    roles: ['Administrator'],
  });

  await screen.getByRole('button', { name: 'Create volunteer' }).click();

  await expect.element(screen.getByText('First name is required')).toBeInTheDocument();
  await expect.element(screen.getByText('Last name is required')).toBeInTheDocument();
});

test('keeps "Committed" disabled until the volunteer is a driver', async () => {
  worker.use(...personApi([]).handlers);
  const screen = renderWithProviders(null, {
    routes,
    route: '/people/new',
    roles: ['Administrator'],
  });

  await expect.element(screen.getByLabelText('Committed to a convoy')).toBeDisabled();

  await screen.getByLabelText('Volunteers to drive').click();

  await expect.element(screen.getByLabelText('Committed to a convoy')).toBeEnabled();
});

test('maps a 400 problem+json error onto the named field', async () => {
  worker.use(
    http.post('/people', () => validationProblem({ Phone: ['That number is not valid.'] })),
  );
  const screen = renderWithProviders(null, {
    routes,
    route: '/people/new',
    roles: ['Administrator'],
  });

  await screen.getByLabelText('First name').fill('Olena');
  await screen.getByLabelText('Last name').fill('Kovalenko');
  await screen.getByLabelText('Date of birth').fill('1991-03-04');
  await screen.getByRole('button', { name: 'Create volunteer' }).click();

  await expect.element(screen.getByText('That number is not valid.')).toBeInTheDocument();
});

test('creates the volunteer and navigates to it', async () => {
  worker.use(...personApi([]).handlers);
  const screen = renderWithProviders(null, {
    routes,
    route: '/people/new',
    roles: ['Administrator'],
  });

  await screen.getByLabelText('First name').fill('Olena');
  await screen.getByLabelText('Last name').fill('Kovalenko');
  await screen.getByLabelText('Date of birth').fill('1991-03-04');
  await screen.getByRole('button', { name: 'Create volunteer' }).click();

  await expect
    .element(screen.getByRole('heading', { name: 'Olena Kovalenko' }))
    .toBeInTheDocument();
});
