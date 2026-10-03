import { expect, test } from 'vitest';

import { renderWithProviders } from '../test/render';
import { LastChanged } from './LastChanged';

test('says who last changed the record and when', async () => {
  const screen = await renderWithProviders(
    <LastChanged by="Olena Shevchenko" at="2026-10-03T18:04:11" />,
  );

  await expect
    .element(screen.getByText('Last changed by Olena Shevchenko on 2026-10-03 18:04 UTC'))
    .toBeInTheDocument();
});

test('a volunteer who has been erased reads as the API sends it, a former volunteer', async () => {
  const screen = await renderWithProviders(
    <LastChanged by="Former volunteer" at="2026-10-03T18:04:11" />,
  );

  await expect
    .element(screen.getByText('Last changed by Former volunteer on 2026-10-03 18:04 UTC'))
    .toBeInTheDocument();
});

test('a change made by a login with no linked volunteer has a time and no name', async () => {
  const screen = await renderWithProviders(<LastChanged by={null} at="2026-10-03T18:04:11" />);

  await expect
    .element(screen.getByText('Last changed on 2026-10-03 18:04 UTC'))
    .toBeInTheDocument();
});

test('a record nobody has changed since it was seeded says nothing', async () => {
  const screen = await renderWithProviders(<LastChanged by={null} at={null} />);

  await expect.element(screen.getByText(/Last changed/)).not.toBeInTheDocument();
});
