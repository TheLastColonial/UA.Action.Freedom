import type { ItemCategoryReadModel } from '../../api/schemas/categories';

let seq = 0;

export function makeCategory(
  overrides: Partial<ItemCategoryReadModel> = {},
): ItemCategoryReadModel {
  seq += 1;
  return {
    id: seq,
    nameEn: `Category ${String(seq)}`,
    nameUk: '',
    isFixed: false,
    hazardClass: null,
    isSensitive: false,
    isNotCarried: false,
    warnWithinDays: null,
    ukCode: null,
    euCode: null,
    uaCode: null,
    lastChangedByName: null,
    lastChangedAt: null,
    ...overrides,
  };
}
