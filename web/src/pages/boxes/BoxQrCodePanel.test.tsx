import { afterEach, beforeEach, expect, test, vi } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeBox } from '../../test/factories/box';
import { boxApi } from '../../test/msw/boxes';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { BoxQrCodePanel } from './BoxQrCodePanel';

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
  vi.restoreAllMocks();
});

test('a loader issues a label and then sees it, ready to print', async () => {
  worker.use(...boxApi([makeBox({ id: 8 })]).handlers);

  const screen = renderWithProviders(<BoxQrCodePanel boxId={8} />, { roles: ['Loader'] });

  await expect.element(screen.getByText('This box has no QR label.')).toBeInTheDocument();

  await screen.getByRole('button', { name: 'Issue label' }).click();

  await expect.element(screen.getByText('Label issued', { exact: false })).toBeInTheDocument();
  await expect.element(screen.getByRole('img', { name: 'QR label for box 8' })).toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Print label' })).toBeInTheDocument();
});

test('Print label triggers the browser print dialog', async () => {
  const api = boxApi([makeBox({ id: 8 })]);
  api.qr.set(8, { token: 'cccccccc-0000-0000-0000-000000000009', issuedAt: '2026-05-01T09:00:00' });
  worker.use(...api.handlers);

  const printSpy = vi.spyOn(window, 'print').mockImplementation(() => undefined);

  const screen = renderWithProviders(<BoxQrCodePanel boxId={8} />, { roles: ['Loader'] });

  const print = screen.getByRole('button', { name: 'Print label' });
  await expect.element(print).toBeEnabled();
  await print.click();

  expect(printSpy).toHaveBeenCalledOnce();
});

test('revoking a label returns the box to having none', async () => {
  const api = boxApi([makeBox({ id: 8 })]);
  api.qr.set(8, { token: 'cccccccc-0000-0000-0000-000000000009', issuedAt: '2026-05-01T09:00:00' });
  worker.use(...api.handlers);

  const screen = renderWithProviders(<BoxQrCodePanel boxId={8} />, { roles: ['Loader'] });

  await screen.getByRole('button', { name: 'Revoke label' }).click();

  await expect.element(screen.getByText('This box has no QR label.')).toBeInTheDocument();
});

test('a purchaser may print an existing label but not issue, reissue or revoke', async () => {
  const api = boxApi([makeBox({ id: 8 })]);
  api.qr.set(8, { token: 'cccccccc-0000-0000-0000-000000000009', issuedAt: '2026-05-01T09:00:00' });
  worker.use(...api.handlers);

  const screen = renderWithProviders(<BoxQrCodePanel boxId={8} />, { roles: ['Purchaser'] });

  await expect.element(screen.getByRole('button', { name: 'Print label' })).toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Reissue label' }))
    .not.toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Revoke label' }))
    .not.toBeInTheDocument();
});

test('a purchaser with no label is pointed at someone who can issue one', async () => {
  worker.use(...boxApi([makeBox({ id: 8 })]).handlers);

  const screen = renderWithProviders(<BoxQrCodePanel boxId={8} />, { roles: ['Purchaser'] });

  await expect
    .element(screen.getByText('Ask a dispatcher or loader to issue one.'))
    .toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Issue label' })).not.toBeInTheDocument();
});

test('the label never carries the box destination', async () => {
  const api = boxApi([makeBox({ id: 8, city: 'Coventry' })]);
  api.qr.set(8, { token: 'cccccccc-0000-0000-0000-000000000009', issuedAt: '2026-05-01T09:00:00' });
  worker.use(...api.handlers);

  const screen = renderWithProviders(<BoxQrCodePanel boxId={8} />, { roles: ['Loader'] });

  const image = screen.getByRole('img', { name: 'QR label for box 8' });
  await expect.element(image).toBeInTheDocument();

  const src = (image.element() as HTMLImageElement).getAttribute('src') ?? '';
  const svg = decodeURIComponent(src.replace('data:image/svg+xml,', ''));
  expect(svg).toContain('BOX #8');
  expect(svg).not.toContain('Coventry');
});
