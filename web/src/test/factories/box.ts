import type {
  BoxBayAssignmentReadModel,
  BoxItemReadModel,
  BoxQrCodeReadModel,
  BoxReadModel,
} from '../../api/schemas/boxes';

let boxSeq = 0;
let itemSeq = 0;
let tokenSeq = 0;
let bayAssignmentSeq = 0;

export function makeBox(overrides: Partial<BoxReadModel> = {}): BoxReadModel {
  boxSeq += 1;
  return {
    id: boxSeq,
    weightKg: 0,
    widthCm: null,
    depthCm: null,
    heightCm: null,
    receiverRef: null,
    locationId: null,
    validatedByPersonId: null,
    validatedAt: null,
    validated: false,
    ...overrides,
  };
}

export function makeBoxBayAssignment(
  overrides: Partial<BoxBayAssignmentReadModel> = {},
): BoxBayAssignmentReadModel {
  bayAssignmentSeq += 1;
  return {
    id: bayAssignmentSeq,
    boxId: 1,
    bayId: 1,
    assignedByPersonId: 'aaaaaaaa-1111-0000-0000-000000000001',
    assignedAt: '2026-05-01T09:00:00',
    vacatedAt: null,
    active: true,
    ...overrides,
  };
}

export function makeBoxItem(overrides: Partial<BoxItemReadModel> = {}): BoxItemReadModel {
  itemSeq += 1;
  return {
    id: `aaaaaaaa-0000-0000-0000-${String(itemSeq).padStart(12, '0')}`,
    description: `Item ${String(itemSeq)}`,
    properties: {},
    ...overrides,
  };
}

export function makeBoxQrCode(overrides: Partial<BoxQrCodeReadModel> = {}): BoxQrCodeReadModel {
  tokenSeq += 1;
  return {
    token: `cccccccc-0000-0000-0000-${String(tokenSeq).padStart(12, '0')}`,
    boxId: 1,
    issuedAt: '2026-05-01T09:00:00',
    revokedAt: null,
    active: true,
    ...overrides,
  };
}
