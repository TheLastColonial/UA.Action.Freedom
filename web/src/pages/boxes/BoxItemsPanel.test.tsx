import { expect, test } from 'vitest';

import { makeBox, makeBoxItem } from '../../test/factories/box';
import { makeCategory } from '../../test/factories/category';
import { makeDonation, makeDonor } from '../../test/factories/donation';
import { boxApi } from '../../test/msw/boxes';
import { categoryApi } from '../../test/msw/categories';
import { donationApi } from '../../test/msw/donations';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { BoxItemsPanel } from './BoxItemsPanel';

const blankets = makeCategory({ id: 11, nameEn: 'Bedding' });
const gas = makeCategory({ id: 12, nameEn: 'Gas', isNotCarried: true });
const medicine = makeCategory({ id: 13, nameEn: 'Medicine', warnWithinDays: 180 });
const categories = [blankets, gas, medicine];

function daysFromNow(days: number): string {
  return new Date(Date.now() + days * 86_400_000).toISOString().slice(0, 10);
}

function serve(box = makeBox({ id: 8 })) {
  const api = boxApi([box], 'caller-person-id', undefined, categories);
  worker.use(...api.handlers, ...categoryApi(categories).handlers);
  return api;
}

test('adds an item with a property, then removes it', async () => {
  serve();

  const screen = await renderWithProviders(<BoxItemsPanel boxId={8} frozen={false} />, {
    roles: ['Loader'],
  });

  await expect.element(screen.getByText('Nothing packed yet.')).toBeInTheDocument();

  await screen.getByLabelText('Description').fill('Winter coats');
  await screen.getByLabelText('Category').selectOptions('Bedding');
  await screen.getByRole('button', { name: 'Add property' }).click();
  await screen.getByLabelText('Property 1 name').fill('size');
  await screen.getByLabelText('Property 1 value').fill('L');
  await screen.getByRole('button', { name: 'Add item' }).click();

  await expect.element(screen.getByText('Winter coats', { exact: false })).toBeInTheDocument();
  await expect.element(screen.getByText('size: L', { exact: false })).toBeInTheDocument();

  await screen.getByRole('button', { name: 'Remove' }).click();
  await expect.element(screen.getByText('Nothing packed yet.')).toBeInTheDocument();
});

test('requires a description and a category', async () => {
  serve();

  const screen = await renderWithProviders(<BoxItemsPanel boxId={8} frozen={false} />, {
    roles: ['Loader'],
  });

  await screen.getByRole('button', { name: 'Add item' }).click();
  await expect.element(screen.getByText('Describe the item')).toBeInTheDocument();
  await expect.element(screen.getByText('Choose a category')).toBeInTheDocument();
});

test('shows the category, quantity, value and expiry the item was packed with', async () => {
  serve();

  const screen = await renderWithProviders(<BoxItemsPanel boxId={8} frozen={false} />, {
    roles: ['Loader'],
  });

  await screen.getByLabelText('Description').fill('Paracetamol');
  await screen.getByLabelText('Category').selectOptions('Medicine');
  await screen.getByLabelText('Quantity').fill('40');
  await screen.getByLabelText('Value (£)').fill('62.50');
  await screen.getByLabelText('Value source').selectOptions('Estimate');
  await screen.getByLabelText('Expires on').fill(daysFromNow(400));
  await screen.getByLabelText('Commodity code').fill('30049000');
  await screen.getByRole('button', { name: 'Add item' }).click();

  await expect.element(screen.getByText(/Medicine, quantity 40/)).toBeInTheDocument();
  await expect.element(screen.getByText(/£62\.50 \(estimate\)/)).toBeInTheDocument();
  await expect.element(screen.getByText(/code 30049000/)).toBeInTheDocument();
});

test('asks for the source of a value before it is sent', async () => {
  serve();

  const screen = await renderWithProviders(<BoxItemsPanel boxId={8} frozen={false} />, {
    roles: ['Loader'],
  });

  await screen.getByLabelText('Description').fill('Paracetamol');
  await screen.getByLabelText('Category').selectOptions('Medicine');
  await screen.getByLabelText('Value (£)').fill('10');
  await screen.getByRole('button', { name: 'Add item' }).click();

  await expect
    .element(screen.getByText('Say whether the donor gave the value or it is an estimate'))
    .toBeInTheDocument();
});

test('warns, and badges the item, when it is a kind the convoy will not carry', async () => {
  serve();

  const screen = await renderWithProviders(<BoxItemsPanel boxId={8} frozen={false} />, {
    roles: ['Loader'],
  });

  await screen.getByLabelText('Description').fill('Camping gas');
  await screen.getByLabelText('Category').selectOptions('Gas');
  await screen.getByRole('button', { name: 'Add item' }).click();

  await expect
    .element(screen.getByText(/The convoy does not carry this kind of goods/))
    .toBeInTheDocument();
  await expect.element(screen.getByText('Not carried')).toBeInTheDocument();
});

test('warns, and badges the item, when it has already expired', async () => {
  serve();

  const screen = await renderWithProviders(<BoxItemsPanel boxId={8} frozen={false} />, {
    roles: ['Loader'],
  });

  await screen.getByLabelText('Description').fill('Paracetamol');
  await screen.getByLabelText('Category').selectOptions('Medicine');
  await screen.getByLabelText('Expires on').fill(daysFromNow(-3));
  await screen.getByRole('button', { name: 'Add item' }).click();

  await expect.element(screen.getByText(/This item has already expired/)).toBeInTheDocument();
  await expect.element(screen.getByText('Expired', { exact: true })).toBeInTheDocument();
});

test('badges a short shelf life by the thresholds of the category', async () => {
  serve();

  const screen = await renderWithProviders(<BoxItemsPanel boxId={8} frozen={false} />, {
    roles: ['Loader'],
  });

  await screen.getByLabelText('Description').fill('Paracetamol');
  await screen.getByLabelText('Category').selectOptions('Medicine');
  await screen.getByLabelText('Expires on').fill(daysFromNow(30));
  await screen.getByRole('button', { name: 'Add item' }).click();

  await expect.element(screen.getByText('Short shelf life')).toBeInTheDocument();
});

test('is read-only when the box is frozen', async () => {
  const api = serve(makeBox({ id: 8, validated: true }));
  api.items.set(8, [makeBoxItem({ description: 'Sealed contents' })]);

  const screen = await renderWithProviders(<BoxItemsPanel boxId={8} frozen />, {
    roles: ['Loader'],
  });

  await expect.element(screen.getByText('Sealed contents')).toBeInTheDocument();
  await expect
    .element(screen.getByText('This box has been validated — its contents are now fixed.'))
    .toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Add item' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Remove' })).not.toBeInTheDocument();
});

test('names the donation an item came in', async () => {
  serve();
  const donor = makeDonor({ id: 'd1', name: 'Margaret Hollis' });
  worker.use(...donationApi([donor], [makeDonation('d1', { id: 41 })]).handlers);

  const screen = await renderWithProviders(<BoxItemsPanel boxId={8} frozen={false} />, {
    roles: ['Loader'],
  });

  await screen.getByLabelText('Description').fill('Tinned soup');
  await screen.getByLabelText('Category').selectOptions('Bedding');
  await screen
    .getByLabelText('Donation')
    .selectOptions('#41 from Margaret Hollis, received 2026-09-20');
  await screen.getByRole('button', { name: 'Add item' }).click();

  await expect.element(screen.getByText(/donation #41 from Margaret Hollis/)).toBeInTheDocument();
});
