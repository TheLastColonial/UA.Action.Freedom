import type { RouteObject } from 'react-router-dom';
import { expect, test } from 'vitest';

import { makeManifest } from '../../test/factories/manifest';
import { manifestApi } from '../../test/msw/manifests';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { manifestRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'manifests', children: manifestRoutes },
];

test('lists manifests with status and freeze state', async () => {
  worker.use(
    ...manifestApi([
      makeManifest({ id: 'UA-1', status: 'Created' }),
      makeManifest({ id: 'UA-2', status: 'Confirmed', frozen: true }),
    ]).handlers,
  );

  const screen = await renderWithProviders(null, {
    routes,
    route: '/manifests',
    roles: ['Loader'],
  });

  await expect.element(screen.getByRole('link', { name: 'UA-1' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'UA-2' })).toBeInTheDocument();
  await expect.element(screen.getByText('Confirmed')).toBeInTheDocument();
});

test('the list points at the convoys, because a manifest is opened from a truck list', async () => {
  // There is no "New manifest" action any more: a manifest is the paperwork for one vehicle on
  // one convoy, so it is opened from that convoy's truck list. The pointer is a link to the
  // convoys, shown to anyone who may read them rather than gated on a write policy.
  worker.use(...manifestApi([]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/manifests',
    roles: ['Loader'],
  });

  await expect.element(screen.getByText('No manifests yet.')).toBeInTheDocument();
  await expect
    .element(screen.getByRole('link', { name: 'Open one from a convoy' }))
    .toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'New manifest' })).not.toBeInTheDocument();
});
