import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeBox, makeBoxItem } from '../../test/factories/box';
import { boxApi } from '../../test/msw/boxes';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { BoxItemsPanel } from './BoxItemsPanel';

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('adds an item with a property, then removes it', async () => {
  worker.use(...boxApi([makeBox({ id: 8 })]).handlers);

  const screen = renderWithProviders(<BoxItemsPanel boxId={8} frozen={false} />, {
    roles: ['Loader'],
  });

  await expect.element(screen.getByText('Nothing packed yet.')).toBeInTheDocument();

  await screen.getByLabelText('Description').fill('Winter coats');
  await screen.getByRole('button', { name: 'Add property' }).click();
  await screen.getByLabelText('Property 1 name').fill('size');
  await screen.getByLabelText('Property 1 value').fill('L');
  await screen.getByRole('button', { name: 'Add item' }).click();

  await expect.element(screen.getByText('Winter coats', { exact: false })).toBeInTheDocument();
  await expect.element(screen.getByText('size: L', { exact: false })).toBeInTheDocument();

  await screen.getByRole('button', { name: 'Remove' }).click();
  await expect.element(screen.getByText('Nothing packed yet.')).toBeInTheDocument();
});

test('requires a description', async () => {
  worker.use(...boxApi([makeBox({ id: 8 })]).handlers);

  const screen = renderWithProviders(<BoxItemsPanel boxId={8} frozen={false} />, {
    roles: ['Loader'],
  });

  await screen.getByRole('button', { name: 'Add item' }).click();
  await expect.element(screen.getByText('Describe the item')).toBeInTheDocument();
});

test('is read-only when the box is frozen', async () => {
  const api = boxApi([makeBox({ id: 8, validated: true })]);
  api.items.set(8, [makeBoxItem({ description: 'Sealed contents' })]);
  worker.use(...api.handlers);

  const screen = renderWithProviders(<BoxItemsPanel boxId={8} frozen />, { roles: ['Loader'] });

  await expect.element(screen.getByText('Sealed contents')).toBeInTheDocument();
  await expect
    .element(screen.getByText('This box has been validated — its contents are now fixed.'))
    .toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Add item' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Remove' })).not.toBeInTheDocument();
});
