import { expect, test, vi } from 'vitest';

import { renderWithProviders } from '../test/render';
import { Button, LinkButton } from './Button';

test('renders as a button and fires onClick', async () => {
  const onClick = vi.fn();
  const screen = await renderWithProviders(<Button onClick={onClick}>Save</Button>);

  await screen.getByRole('button', { name: 'Save' }).click();

  expect(onClick).toHaveBeenCalledOnce();
});

test('respects the disabled prop', async () => {
  const screen = await renderWithProviders(<Button disabled>Save</Button>);

  await expect.element(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
});

test('defaults to type="button" so it never submits a form by accident', async () => {
  const screen = await renderWithProviders(<Button>Save</Button>);

  await expect
    .element(screen.getByRole('button', { name: 'Save' }))
    .toHaveAttribute('type', 'button');
});

test('renders as a link to the given destination', async () => {
  const screen = await renderWithProviders(<LinkButton to="/vehicles/new">New vehicle</LinkButton>);

  await expect
    .element(screen.getByRole('link', { name: 'New vehicle' }))
    .toHaveAttribute('href', '/vehicles/new');
});
