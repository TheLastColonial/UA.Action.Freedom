import type { RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeReceiver, makeReceiverDetail } from '../../test/factories/receiver';
import { receiverApi } from '../../test/msw/receivers';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { receiverRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'receivers', children: receiverRoutes },
];

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('renders organisation and region, and Not found for an unknown ref', async () => {
  worker.use(
    ...receiverApi([makeReceiver({ ref: 'r1', organisation: 'Kyiv Aid', region: 'Kyiv Oblast' })])
      .handlers,
  );

  const found = renderWithProviders(null, {
    routes,
    route: '/receivers/r1',
    roles: ['Dispatcher'],
  });
  await expect.element(found.getByRole('heading', { name: 'Kyiv Aid' })).toBeInTheDocument();
  await expect.element(found.getByText('Kyiv Oblast')).toBeInTheDocument();

  const missing = renderWithProviders(null, {
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

  const screen = renderWithProviders(null, {
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

test('a Ground Officer sees the reveal control', async () => {
  worker.use(...receiverApi([makeReceiver({ ref: 'r1', organisation: 'Kyiv Aid' })]).handlers);

  const screen = renderWithProviders(null, {
    routes,
    route: '/receivers/r1',
    roles: ['GroundOfficer'],
  });

  await expect
    .element(screen.getByRole('button', { name: 'Reveal delivery detail' }))
    .toBeInTheDocument();
});
