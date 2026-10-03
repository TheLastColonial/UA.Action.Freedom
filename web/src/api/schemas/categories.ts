import { z } from 'zod';

import { lastChangedShape } from './common';

// src/UA.Action.Freedom.Domain/ItemCategory.cs — CustomsAuthority. Sent and accepted by name.
export const customsAuthoritySchema = z.enum(['UK', 'EU', 'UA']);
export type CustomsAuthority = z.infer<typeof customsAuthoritySchema>;

export const CUSTOMS_AUTHORITIES: readonly CustomsAuthority[] = ['UK', 'EU', 'UA'];

// Response shape — src/UA.Action.Freedom.Application/Categories/ItemCategoryReadModel.cs. A category
// carries what a border needs to know about a kind of item and the customs code it maps to for each
// authority (ADR 0014). `warnWithinDays` is how close to expiry an item counts as short-dated; the
// thresholds are unverified (D25) and held as data, not constants.
export const itemCategoryReadModelSchema = z.object({
  ...lastChangedShape,
  id: z.number().int(),
  nameEn: z.string(),
  nameUk: z.string(),
  isFixed: z.boolean(),
  hazardClass: z.number().int().nullable(),
  isSensitive: z.boolean(),
  isNotCarried: z.boolean(),
  warnWithinDays: z.number().int().nullable(),
  ukCode: z.string().nullable(),
  euCode: z.string().nullable(),
  uaCode: z.string().nullable(),
});
export type ItemCategoryReadModel = z.infer<typeof itemCategoryReadModelSchema>;

// Request shapes — src/UA.Action.Freedom.Api/Categories/CategoryRequests.cs. Whether a category is
// fixed is not a field: only the seeded list is.
export interface CreateCategoryRequest {
  nameEn: string;
  nameUk?: string;
  hazardClass?: number;
  isSensitive: boolean;
  isNotCarried: boolean;
  warnWithinDays?: number;
}
export type UpdateCategoryRequest = CreateCategoryRequest;

// Body of PUT /categories/{id}/codes/{authority}. `null` clears the mapping.
export interface SetCategoryCodeRequest {
  code: string | null;
}
