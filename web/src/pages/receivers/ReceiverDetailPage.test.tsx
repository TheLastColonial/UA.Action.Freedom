import type { RouteObject } from 'react-router-dom';
import { expect, test } from 'vitest';

import { makeReceiver, makeReceiverDetail } from '../../test/factories/receiver';
import { receiverApi } from '../../test/msw/receivers';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { receiverRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'receivers', children: receiverRoutes },
];

test('renders organisation and region, and Not found for an unknown ref', async () => {
  worker.use(
    ...receiverApi([makeReceiver({ ref: 'r1', organisation: 'Kyiv Aid', region: 'Kyiv Oblast' })])
      .handlers,
  );

  const found = await renderWithProviders(null, {
    routes,
    route: '/receivers/r1',
    roles: ['Dispatcher'],
  });
  await expect.element(found.getByRole('heading', { name: 'Kyiv Aid' })).toBeInTheDocument();
  await expect.element(found.getByText('Kyiv Oblast')).toBeInTheDocument();

  const missing = await renderWithProviders(null, {
    routes,
    route: '/receivers/nope',
    roles: ['Dispatcher'],
  });
  await expect.element(missing.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});

test('a non–Ground Officer never sees the delivery-detail panel', async () => {
  worker.use(
    ...receiverApi(
      [makeReceiver({ ref: 'r1', organisation: 'Kyiv Aid' })],
      [makeReceiverDetail({ ref: 'r1', addressLine1: '17 Khreshchatyk' })],
    ).handlers,
  );

  const screen = await renderWithProviders(null, {
    routes,
    route: '/receivers/r1',
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Kyiv Aid' })).toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Reveal delivery detail' }))
    .not.toBeInTheDocument();
  await expect.element(screen.getByText('17 Khreshchatyk')).not.toBeInTheDocument();
  await expect
    .element(
      screen.getByText(
        'Delivery address and contact are held separately, visible to a Ground Officer only.',
      ),
    )
    .toBeInTheDocument();
});

test('groups fields into a named card', async () => {
  worker.use(...receiverApi([makeReceiver({ ref: 'r1', organisation: 'Kyiv Aid' })]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/receivers/r1',
    roles: ['Dispatcher'],
  });

  await expect
    .element(screen.getByRole('region', { name: 'Receiver details' }))
    .toBeInTheDocument();
});

test('a Ground Officer sees the reveal control', async () => {
  worker.use(...receiverApi([makeReceiver({ ref: 'r1', organisation: 'Kyiv Aid' })]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/receivers/r1',
    roles: ['GroundOfficer'],
  });

  await expect
    .element(screen.getByRole('button', { name: 'Reveal delivery detail' }))
    .toBeInTheDocument();
});

test('a receiver says who last changed it and when', async () => {
  worker.use(
    ...receiverApi([
      makeReceiver({
        ref: 'r1',
        lastChangedByName: 'Olena Shevchenko',
        lastChangedAt: '2026-10-03T18:04:11',
      }),
    ]).handlers,
  );

  const screen = await renderWithProviders(null, {
    routes,
    route: '/receivers/r1',
    roles: ['Dispatcher'],
  });

  await expect
    .element(screen.getByText('Last changed by Olena Shevchenko on 2026-10-03 18:04 UTC'))
    .toBeInTheDocument();
});
