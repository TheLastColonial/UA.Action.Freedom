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

test('requires a reference', async () => {
  worker.use(...manifestApi([]).handlers);
  const screen = renderWithProviders(null, {
    routes,
    route: '/manifests/new',
    roles: ['Dispatcher'],
  });

  await screen.getByRole('button', { name: 'Create manifest' }).click();
  await expect.element(screen.getByText('A manifest reference is required')).toBeInTheDocument();
});

test('surfaces a duplicate-reference 409 and stays on the form', async () => {
  worker.use(...manifestApi([makeManifest({ id: 'UA-DUP' })]).handlers);
  const screen = renderWithProviders(null, {
    routes,
    route: '/manifests/new',
    roles: ['Dispatcher'],
  });

  await screen.getByLabelText('Reference').fill('UA-DUP');
  await screen.getByRole('button', { name: 'Create manifest' }).click();

  await expect
    .element(screen.getByText("A manifest with reference 'UA-DUP' already exists."))
    .toBeInTheDocument();
  await expect.element(screen.getByRole('heading', { name: 'New manifest' })).toBeInTheDocument();
});

test('creates the manifest and opens it', async () => {
  worker.use(...manifestApi([]).handlers);
  const screen = renderWithProviders(null, {
    routes,
    route: '/manifests/new',
    roles: ['Dispatcher'],
  });

  await screen.getByLabelText('Reference').fill('UA-NEW-1');
  await screen.getByRole('button', { name: 'Create manifest' }).click();

  await expect.element(screen.getByRole('heading', { name: 'UA-NEW-1' })).toBeInTheDocument();
});
