import type { RouteObject } from 'react-router-dom';
import { expect, test } from 'vitest';

import type { DonorReport } from '../../api/schemas/donations';
import { makeDonation, makeDonor } from '../../test/factories/donation';
import { donationApi } from '../../test/msw/donations';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { donorRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'donors', children: donorRoutes },
];

const margaret = makeDonor({ id: 'd1', name: 'Margaret Hollis', email: 'margaret@example.org' });

function aReport(donorName: string): DonorReport {
  return {
    donorId: 'd1',
    donorName,
    itemCount: 16,
    totalValueGbp: 30,
    byCategory: [
      { categoryNameEn: 'Tinned food', items: 12, valueGbp: 30 },
      { categoryNameEn: 'Blankets', items: 4, valueGbp: 0 },
    ],
    donations: [
      {
        donationId: 7,
        receivedOn: '2026-09-20T00:00:00',
        items: [
          { categoryNameEn: 'Tinned food', quantity: 12, valueGbp: 30, status: 'PackedAndChecked' },
          { categoryNameEn: 'Blankets', quantity: 4, valueGbp: null, status: 'BeingPacked' },
        ],
      },
    ],
  };
}

test('lists the donors and offers a new one to a loader', async () => {
  worker.use(...donationApi([margaret]).handlers);

  const screen = await renderWithProviders(null, { routes, route: '/donors', roles: ['Loader'] });

  await expect.element(screen.getByRole('link', { name: 'Margaret Hollis' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'New donor' })).toBeInTheDocument();
});

test('does not offer a purchaser a way to enter a donor', async () => {
  worker.use(...donationApi([margaret]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/donors',
    roles: ['Purchaser'],
  });

  await expect.element(screen.getByRole('link', { name: 'Margaret Hollis' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'New donor' })).not.toBeInTheDocument();
});

test('requires a name to create a donor', async () => {
  worker.use(...donationApi([]).handlers);
  const screen = await renderWithProviders(null, {
    routes,
    route: '/donors/new',
    roles: ['Dispatcher'],
  });

  await screen.getByRole('button', { name: 'Create donor' }).click();

  await expect.element(screen.getByText('Name is required')).toBeInTheDocument();
});

test('creates the donor and navigates to it', async () => {
  worker.use(...donationApi([]).handlers);
  const screen = await renderWithProviders(null, {
    routes,
    route: '/donors/new',
    roles: ['Dispatcher'],
  });

  await screen.getByLabelText('Name').fill('Riverside Church');
  await screen.getByLabelText('Email').fill('office@example.org');
  await screen.getByRole('button', { name: 'Create donor' }).click();

  await expect
    .element(screen.getByRole('heading', { name: 'Riverside Church' }))
    .toBeInTheDocument();
});

test('shows the donor and their donations', async () => {
  const donation = makeDonation('d1', { id: 7, notes: 'Two boxes of tins' });
  worker.use(...donationApi([margaret], [donation]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/donors/d1',
    roles: ['Loader'],
  });

  await expect
    .element(screen.getByRole('heading', { name: 'Margaret Hollis' }))
    .toBeInTheDocument();
  await expect.element(screen.getByText('Two boxes of tins')).toBeInTheDocument();
  await expect.element(screen.getByText('margaret@example.org')).toBeInTheDocument();
});

test('offers erasure to an administrator only', async () => {
  worker.use(...donationApi([margaret]).handlers);

  const asLoader = await renderWithProviders(null, {
    routes,
    route: '/donors/d1',
    roles: ['Loader'],
  });
  await expect
    .element(asLoader.getByRole('heading', { name: 'Margaret Hollis' }))
    .toBeInTheDocument();
  await expect
    .element(asLoader.getByRole('button', { name: 'Erase donor' }))
    .not.toBeInTheDocument();
});

test('erases the donor after confirmation and returns to the list', async () => {
  worker.use(...donationApi([margaret]).handlers);
  const screen = await renderWithProviders(null, {
    routes,
    route: '/donors/d1',
    roles: ['Administrator'],
  });

  await screen.getByRole('button', { name: 'Erase donor' }).click();
  await expect.element(screen.getByRole('alertdialog')).toBeInTheDocument();
  await screen.getByRole('button', { name: 'Erase permanently' }).click();

  await expect.element(screen.getByRole('heading', { name: 'Donors' })).toBeInTheDocument();
});

test('records a donation and returns to the donor', async () => {
  const api = donationApi([margaret]);
  worker.use(...api.handlers);
  const screen = await renderWithProviders(null, {
    routes,
    route: '/donors/d1/donations/new',
    roles: ['Dispatcher'],
  });

  await screen.getByLabelText('Notes').fill('Dropped off at the depot');
  await screen.getByRole('button', { name: 'Record donation' }).click();

  await expect.element(screen.getByText('Dropped off at the depot')).toBeInTheDocument();
  expect([...api.donations.values()].map((donation) => donation.notes)).toEqual([
    'Dropped off at the depot',
  ]);
});

test('the report says what was given and how far it has got, and names no destination', async () => {
  worker.use(...donationApi([margaret], [], aReport).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/donors/d1/report',
    roles: ['Dispatcher'],
  });

  await expect
    .element(screen.getByRole('heading', { name: 'Donation report for Margaret Hollis' }))
    .toBeInTheDocument();
  await expect.element(screen.getByText('Packed and checked')).toBeInTheDocument();
  await expect.element(screen.getByText('Being packed')).toBeInTheDocument();

  const text = document.body.textContent.toLowerCase();
  for (const word of ['receiver', 'region', 'route', 'address']) {
    expect(text).not.toContain(word);
  }
});

test('the report of an erased donor reads Former donor with the same totals', async () => {
  const api = donationApi([margaret], [makeDonation('d1', { id: 7 })], aReport);
  worker.use(...api.handlers);
  api.donors.delete('d1');

  const screen = await renderWithProviders(null, {
    routes,
    route: '/donors/d1/report',
    roles: ['Dispatcher'],
  });

  await expect
    .element(screen.getByRole('heading', { name: 'Donation report for Former donor' }))
    .toBeInTheDocument();
  await expect.element(screen.getByText(/16 items given/)).toBeInTheDocument();
});

test('renders Not found for a donor who never existed', async () => {
  worker.use(...donationApi([]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/donors/gone',
    roles: ['Loader'],
  });

  await expect.element(screen.getByText(/not found/i)).toBeInTheDocument();
});
