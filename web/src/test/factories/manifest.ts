import type {
  ManifestBoxReadModel,
  ManifestDriverTeamReadModel,
  ManifestReadModel,
} from '../../api/schemas/manifests';

let seq = 0;

export function makeManifest(overrides: Partial<ManifestReadModel> = {}): ManifestReadModel {
  seq += 1;
  return {
    id: `UA-2026-${String(seq).padStart(3, '0')}`,
    vin: null,
    convoyId: null,
    status: 'Created',
    deliveryNotes: null,
    ferryBookingComplete: false,
    gmrSubmittedAt: null,
    frozen: false,
    ...overrides,
  };
}

export function makeManifestTeam(
  overrides: Partial<ManifestDriverTeamReadModel> = {},
): ManifestDriverTeamReadModel {
  return {
    leg: 'Uk',
    primaryPersonId: 'driver-1',
    secondaryPersonId: null,
    ...overrides,
  };
}

export function makeManifestBox(
  overrides: Partial<ManifestBoxReadModel> = {},
): ManifestBoxReadModel {
  return { boxId: 1, weightKg: 12, validated: true, ...overrides };
}
