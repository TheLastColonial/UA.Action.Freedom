import type { ManifestBoxReadModel, ManifestReadModel } from '../../api/schemas/manifests';

let seq = 0;

export function makeManifest(overrides: Partial<ManifestReadModel> = {}): ManifestReadModel {
  seq += 1;
  return {
    id: `UA-2026-${String(seq).padStart(3, '0')}`,
    // Never null: a manifest is the paperwork for one vehicle on one convoy, and the pair is a
    // composite foreign key to that truck-list entry.
    convoyId: 1,
    vin: 'VIN-CONVOY-1',
    status: 'Created',
    deliveryNotes: null,
    ferryBookingComplete: false,
    gmrSubmittedAt: null,
    frozen: false,
    ...overrides,
  };
}

export function makeManifestBox(
  overrides: Partial<ManifestBoxReadModel> = {},
): ManifestBoxReadModel {
  return {
    boxId: 1,
    weightKg: 12,
    validated: true,
    widthCm: null,
    depthCm: null,
    heightCm: null,
    ...overrides,
  };
}
