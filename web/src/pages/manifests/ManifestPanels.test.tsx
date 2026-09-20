import { expect, test } from 'vitest';

import { makeVehicleCrew } from '../../test/factories/convoy';
import { makeManifest, makeManifestBox } from '../../test/factories/manifest';
import { manifestApi } from '../../test/msw/manifests';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ManifestBoxesPanel } from './ManifestBoxesPanel';
import { ManifestCrewPanel } from './ManifestCrewPanel';
import { ManifestWeightPanel } from './ManifestWeightPanel';

test('the crew panel reports who is travelling, split by leg', async () => {
  const api = manifestApi([makeManifest({ id: 'T1', convoyId: 7, vin: 'VIN-1' })], {
    crewByManifest: new Map([
      [
        'T1',
        [
          makeVehicleCrew({ personId: 'd1', firstName: 'Dana', lastName: 'Road', leg: 'Uk' }),
          makeVehicleCrew({
            personId: 'd2',
            firstName: 'Taras',
            lastName: 'Shevchuk',
            leg: 'Border',
          }),
        ],
      ],
    ]),
  });
  worker.use(...api.handlers);

  const screen = await renderWithProviders(
    <ManifestCrewPanel manifestId="T1" convoyId={7} vin="VIN-1" />,
    { roles: ['Dispatcher'] },
  );

  await expect.element(screen.getByRole('cell', { name: 'Dana Road' })).toBeInTheDocument();
  await expect.element(screen.getByRole('cell', { name: 'Taras Shevchuk' })).toBeInTheDocument();
  await expect.element(screen.getByText('UK to Europe leg')).toBeInTheDocument();
  await expect.element(screen.getByText('Europe to Ukraine leg')).toBeInTheDocument();
});

test('the crew panel offers no way to crew from here and points at the convoy', async () => {
  // Crewing happens once, on the truck-list entry. A second crew record written here is what let
  // a printed manifest name people the insurance had never heard of.
  const api = manifestApi([makeManifest({ id: 'T2', convoyId: 7, vin: 'VIN-1' })]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(
    <ManifestCrewPanel manifestId="T2" convoyId={7} vin="VIN-1" />,
    { roles: ['Dispatcher'] },
  );

  await expect.element(screen.getByText('Nobody crewing this leg yet').first()).toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: /Save/ })).not.toBeInTheDocument();
  await expect
    .element(screen.getByRole('link', { name: /manage the crew of VIN-1 on its convoy/ }))
    .toBeInTheDocument();
});

test('cargo panel adds and removes a box', async () => {
  worker.use(...manifestApi([makeManifest({ id: 'C1' })]).handlers);

  const screen = await renderWithProviders(<ManifestBoxesPanel manifestId="C1" frozen={false} />, {
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByText('No boxes on this manifest yet.')).toBeInTheDocument();

  await screen.getByLabelText('Box id to add').fill('9');
  await screen.getByRole('button', { name: 'Add box' }).click();
  await expect.element(screen.getByText('#9')).toBeInTheDocument();

  await screen.getByRole('button', { name: 'Remove' }).click();
  await expect.element(screen.getByText('No boxes on this manifest yet.')).toBeInTheDocument();
});

test('cargo panel is read-only when the manifest is frozen', async () => {
  const api = manifestApi([makeManifest({ id: 'C2', frozen: true })]);
  api.boxes.set('C2', [makeManifestBox({ boxId: 3 })]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(<ManifestBoxesPanel manifestId="C2" frozen />, {
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByText('#3')).toBeInTheDocument();
  await expect
    .element(screen.getByText('Frozen — cargo can no longer be changed.'))
    .toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Add box' })).not.toBeInTheDocument();
});

test('weight panel shows the border-check total and a provisional warning', async () => {
  const api = manifestApi([makeManifest({ id: 'W1' })]);
  api.boxes.set('W1', [
    makeManifestBox({ boxId: 1, weightKg: 20, validated: true }),
    makeManifestBox({ boxId: 2, weightKg: 10, validated: false }),
  ]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(<ManifestWeightPanel manifestId="W1" />, {
    roles: ['Loader'],
  });

  await expect.element(screen.getByText('2275 kg')).toBeInTheDocument();
  await expect
    .element(screen.getByText('1 box(es) are not yet validated — this total is provisional.'))
    .toBeInTheDocument();
});

test("weight panel warns when cargo exceeds the vehicle's stated capacity, without blocking anything", async () => {
  const api = manifestApi([makeManifest({ id: 'W2' })], {
    vehicleCargoCapacity: { maxCargoWeightKg: 15 },
  });
  api.boxes.set('W2', [makeManifestBox({ boxId: 1, weightKg: 20, validated: true })]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(<ManifestWeightPanel manifestId="W2" />, {
    roles: ['Loader'],
  });

  await expect
    .element(
      screen.getByText(
        "Cargo is over the vehicle's stated capacity of 15 kg. This is advisory only — nothing is blocked.",
      ),
    )
    .toBeInTheDocument();
});

test('weight panel warns about an oversized box without blocking anything', async () => {
  const api = manifestApi([makeManifest({ id: 'W3' })], {
    vehicleCargoCapacity: { cargoWidthCm: 100, cargoDepthCm: 100, cargoHeightCm: 30 },
  });
  api.boxes.set('W3', [
    makeManifestBox({
      boxId: 1,
      weightKg: 20,
      validated: true,
      widthCm: 200,
      depthCm: 50,
      heightCm: 50,
    }),
  ]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(<ManifestWeightPanel manifestId="W3" />, {
    roles: ['Loader'],
  });

  await expect
    .element(
      screen.getByText(
        "Box(es) #1 may not fit the vehicle's cargo space. This is advisory only — nothing is blocked.",
      ),
    )
    .toBeInTheDocument();
});

test('weight panel shows no capacity warning when nobody has measured anything', async () => {
  const api = manifestApi([makeManifest({ id: 'W4' })]);
  api.boxes.set('W4', [makeManifestBox({ boxId: 1, weightKg: 20, validated: true })]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(<ManifestWeightPanel manifestId="W4" />, {
    roles: ['Loader'],
  });

  await expect.element(screen.getByText('2265 kg')).toBeInTheDocument();
  await expect.element(screen.getByText(/stated capacity/)).not.toBeInTheDocument();
  await expect.element(screen.getByText(/cargo space/)).not.toBeInTheDocument();
});
