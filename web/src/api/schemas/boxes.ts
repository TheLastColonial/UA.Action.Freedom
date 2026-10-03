import { z } from 'zod';

import { lastChangedShape } from './common';

// Response shapes — src/UA.Action.Freedom.Application/Boxes/BoxReadModel.cs.
export const boxReadModelSchema = z.object({
  ...lastChangedShape,
  id: z.number().int(),
  weightKg: z.number().int(),
  widthCm: z.number().nullable(),
  depthCm: z.number().nullable(),
  heightCm: z.number().nullable(),
  receiverRef: z.string().nullable(),
  locationId: z.number().int().nullable(),
  validatedByPersonId: z.string().nullable(),
  validatedAt: z.string().nullable(),
  validated: z.boolean(),
});
export type BoxReadModel = z.infer<typeof boxReadModelSchema>;

// The box's current (or historical) bay assignment.
export const boxBayAssignmentReadModelSchema = z.object({
  id: z.number().int(),
  boxId: z.number().int(),
  bayId: z.number().int(),
  assignedByPersonId: z.string(),
  assignedAt: z.string(),
  vacatedAt: z.string().nullable(),
  active: z.boolean(),
});
export type BoxBayAssignmentReadModel = z.infer<typeof boxBayAssignmentReadModelSchema>;

// src/UA.Action.Freedom.Domain/ItemCategory.cs — ValueSource and ShelfLifeStatus, sent by name. An item is
// valued by its donor or by an estimate; only a vehicle is bought.
export const itemValueSourceSchema = z.enum(['Donor', 'Estimate']);
export type ItemValueSource = z.infer<typeof itemValueSourceSchema>;

export const shelfLifeStatusSchema = z.enum(['Fine', 'Short', 'Expired']);
export type ShelfLifeStatus = z.infer<typeof shelfLifeStatusSchema>;

// Response shape — BoxItemReadModel in BoxReadModel.cs. The last three are worked out at read time from
// the item's category and the date, so they cannot go stale in storage.
export const boxItemReadModelSchema = z.object({
  id: z.string(),
  description: z.string(),
  properties: z.record(z.string(), z.string()),
  categoryId: z.number().int(),
  commodityCode: z.string().nullable(),
  quantity: z.number().int().nullable(),
  valueGbp: z.number().nullable(),
  valueSource: itemValueSourceSchema.nullable(),
  expiresOn: z.string().nullable(),
  categoryNameEn: z.string().nullable(),
  isNotCarried: z.boolean(),
  shelfLife: shelfLifeStatusSchema,
  donationId: z.number().int().nullable(),
});
export type BoxItemReadModel = z.infer<typeof boxItemReadModelSchema>;

// Something worth telling the packer about an item that was nonetheless accepted.
export const itemWarningSchema = z.enum(['NotCarried', 'ShortShelfLife', 'Expired']);
export type ItemWarning = z.infer<typeof itemWarningSchema>;

// POST /boxes/{id}/items answers 200 with the new item's identifier and any warnings about it.
export const addBoxItemResultSchema = z.object({
  itemId: z.string(),
  warnings: z.array(itemWarningSchema),
});
export type AddBoxItemResult = z.infer<typeof addBoxItemResultSchema>;

// The box's active QR label — src/UA.Action.Freedom.Application/Boxes/BoxReadModel.cs.
export const boxQrCodeReadModelSchema = z.object({
  token: z.string(),
  boxId: z.number().int(),
  issuedAt: z.string(),
  revokedAt: z.string().nullable(),
  active: z.boolean(),
});
export type BoxQrCodeReadModel = z.infer<typeof boxQrCodeReadModelSchema>;

// Request shapes — src/UA.Action.Freedom.Api/Boxes/BoxRequests.cs.
export interface CreateBoxRequest {
  receiverRef?: string;
  locationId?: number;
}
export type UpdateBoxRequest = CreateBoxRequest;

export interface AssignBoxBayRequest {
  bayId: number;
}

export interface ValidateBoxRequest {
  weightKg: number;
  widthCm?: number;
  depthCm?: number;
  heightCm?: number;
}

export interface AddBoxItemRequest {
  description: string;
  properties: Record<string, string>;
  categoryId: number;
  commodityCode?: string;
  quantity?: number;
  valueGbp?: number;
  valueSource?: ItemValueSource;
  expiresOn?: string;
  donationId?: number;
}
