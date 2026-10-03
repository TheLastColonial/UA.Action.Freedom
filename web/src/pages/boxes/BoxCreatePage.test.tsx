import type { RouteObject } from 'react-router-dom';
import { expect, test } from 'vitest';

import { makeLocation } from '../../test/factories/location';
import { makeReceiver } from '../../test/factories/receiver';
import { boxApi } from '../../test/msw/boxes';
import { locationApi } from '../../test/msw/locations';
import { personApi } from '../../test/msw/people';
import { receiverApi } from '../../test/msw/receivers';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { boxRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'boxes', children: boxRoutes },
];

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

test('offers only registered receivers and sends the one chosen', async () => {
  const receivers = receiverApi([
    makeReceiver({ ref: 'r-reg', organisation: 'Kyiv Aid', region: 'Kyiv Oblast' }),
    makeReceiver({ ref: 'r-pending', organisation: 'Lviv Clinic', status: 'Pending' }),
    makeReceiver({ ref: 'r-suspended', organisation: 'Odesa Shelter', status: 'Suspended' }),
  ]);
  const boxes = boxApi(
    [],
    'caller-person-id',
    new Map([
      ['r-reg', 'Registered'],
      ['r-pending', 'Pending'],
      ['r-suspended', 'Suspended'],
    ]),
  );
  worker.use(...boxes.handlers, ...receivers.handlers, ...locationApi([]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/boxes/new',
    roles: ['Loader'],
  });

  await expect
    .element(screen.getByRole('option', { name: 'Kyiv Aid — Kyiv Oblast' }))
    .toBeInTheDocument();
  await expect.element(screen.getByRole('option', { name: /Lviv Clinic/ })).not.toBeInTheDocument();
  await expect
    .element(screen.getByRole('option', { name: /Odesa Shelter/ }))
    .not.toBeInTheDocument();

  await screen.getByLabelText('Receiver').selectOptions('r-reg');
  await screen.getByRole('button', { name: 'Create box' }).click();

  await expect.element(screen.getByRole('heading', { name: /Box #/ })).toBeInTheDocument();
  expect([...boxes.db.values()].map((box) => box.receiverRef)).toEqual(['r-reg']);
});

test('shows the reason when the API refuses a receiver', async () => {
  // The picker never offers an unregistered receiver, so this is the race: it was suspended after
  // the page loaded. The API answer is shown rather than swallowed.
  const receivers = receiverApi([makeReceiver({ ref: 'r-reg', organisation: 'Kyiv Aid' })]);
  const boxes = boxApi([], 'caller-person-id', new Map([['r-reg', 'Suspended']]));
  worker.use(...boxes.handlers, ...receivers.handlers, ...locationApi([]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/boxes/new',
    roles: ['Loader'],
  });

  await screen.getByLabelText('Receiver').selectOptions('r-reg');
  await screen.getByRole('button', { name: 'Create box' }).click();

  await expect
    .element(screen.getByText('Only a registered receiver can be a destination.'))
    .toBeInTheDocument();
});
