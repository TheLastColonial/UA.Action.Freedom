import { render } from 'vitest-browser-react';
import { expect, test, vi } from 'vitest';

import { Pagination } from './Pagination';

test('cannot go back from the first page', async () => {
  const screen = await render(<Pagination page={1} hasNext onPageChange={vi.fn()} />);

  await expect.element(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();
  await expect.element(screen.getByText('Page 1')).toHaveAttribute('aria-current', 'page');
});

test('cannot go forward past the last page', async () => {
  const screen = await render(<Pagination page={3} hasNext={false} onPageChange={vi.fn()} />);

  await expect.element(screen.getByRole('button', { name: 'Next' })).toBeDisabled();
});

test('asks for the neighbouring page in each direction', async () => {
  const onPageChange = vi.fn<(page: number) => void>();
  const screen = await render(<Pagination page={3} hasNext onPageChange={onPageChange} />);

  await screen.getByRole('button', { name: 'Next' }).click();
  await screen.getByRole('button', { name: 'Previous' }).click();

  expect(onPageChange.mock.calls).toEqual([[4], [2]]);
});
