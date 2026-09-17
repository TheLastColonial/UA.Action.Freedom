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

test('adds an item with a property through the add-item modal, then removes it', async () => {
  worker.use(...boxApi([makeBox({ id: 8 })]).handlers);

  const screen = await renderWithProviders(<BoxItemsPanel boxId={8} frozen={false} />, {
    roles: ['Loader'],
  });

  await expect.element(screen.getByText('Nothing packed yet.')).toBeInTheDocument();

  await screen.getByRole('button', { name: 'Add item' }).click();
  await expect.element(screen.getByRole('dialog', { name: 'Add item' })).toBeInTheDocument();

  await screen.getByLabelText('Description').fill('Winter coats');
  await screen.getByRole('button', { name: 'Add property' }).click();
  await screen.getByLabelText('Name').fill('size');
  await screen.getByLabelText('Value').fill('L');
  await screen.getByRole('button', { name: 'Save item' }).click();

  await expect.element(screen.getByRole('dialog')).not.toBeInTheDocument();
  await expect.element(screen.getByText('Winter coats')).toBeInTheDocument();
  await expect.element(screen.getByText('size')).toBeInTheDocument();
  await expect.element(screen.getByText('L')).toBeInTheDocument();

  await screen.getByRole('button', { name: 'Remove' }).click();
  await expect.element(screen.getByText('Nothing packed yet.')).toBeInTheDocument();
});

test('requires a description', async () => {
  worker.use(...boxApi([makeBox({ id: 8 })]).handlers);

  const screen = await renderWithProviders(<BoxItemsPanel boxId={8} frozen={false} />, {
    roles: ['Loader'],
  });

  await screen.getByRole('button', { name: 'Add item' }).click();
  await screen.getByRole('button', { name: 'Save item' }).click();
  await expect.element(screen.getByText('Describe the item')).toBeInTheDocument();
});

test('edits an existing item through the edit-item modal', async () => {
  const api = boxApi([makeBox({ id: 8 })]);
  const item = makeBoxItem({ description: 'Blankets', properties: { size: 'double' } });
  api.items.set(8, [item]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(<BoxItemsPanel boxId={8} frozen={false} />, {
    roles: ['Loader'],
  });

  await screen.getByRole('button', { name: 'Edit' }).click();
  await expect.element(screen.getByRole('dialog', { name: 'Edit item' })).toBeInTheDocument();
  await expect.element(screen.getByLabelText('Description')).toHaveValue('Blankets');
  await expect.element(screen.getByLabelText('Name')).toHaveValue('size');
  await expect.element(screen.getByLabelText('Value')).toHaveValue('double');

  await screen.getByLabelText('Description').fill('Blankets (large)');
  await screen.getByLabelText('Value').fill('XL');
  await screen.getByRole('button', { name: 'Save item' }).click();

  await expect.element(screen.getByRole('dialog')).not.toBeInTheDocument();
  await expect.element(screen.getByText('Blankets (large)')).toBeInTheDocument();
  await expect.element(screen.getByText('XL')).toBeInTheDocument();
});

test('is read-only when the box is frozen', async () => {
  const api = boxApi([makeBox({ id: 8, validated: true })]);
  api.items.set(8, [makeBoxItem({ description: 'Sealed contents' })]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(<BoxItemsPanel boxId={8} frozen />, {
    roles: ['Loader'],
  });

  await expect.element(screen.getByText('Sealed contents')).toBeInTheDocument();
  await expect
    .element(screen.getByText('This box has been validated — its contents are now fixed.'))
    .toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Add item' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Edit' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Remove' })).not.toBeInTheDocument();
});
