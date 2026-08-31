import type { BoxItemReadModel, BoxReadModel } from '../../api/schemas/boxes';

let boxSeq = 0;
let itemSeq = 0;

export function makeBox(overrides: Partial<BoxReadModel> = {}): BoxReadModel {
  boxSeq += 1;
  return {
    id: boxSeq,
    weightKg: 0,
    receiverRef: null,
    house: null,
    street: null,
    city: null,
    country: null,
    postcode: null,
    validatedByPersonId: null,
    validatedAt: null,
    validated: false,
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
