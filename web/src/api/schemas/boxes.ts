import { z } from 'zod';

// Response shapes — src/UA.Action.Freedom.Application/Boxes/BoxReadModel.cs.
export const boxReadModelSchema = z.object({
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

export const boxItemReadModelSchema = z.object({
  id: z.string(),
  description: z.string(),
  properties: z.record(z.string(), z.string()),
});
export type BoxItemReadModel = z.infer<typeof boxItemReadModelSchema>;

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
  assignedByPersonId: string;
}

export interface ValidateBoxRequest {
  validatedByPersonId: string;
  weightKg: number;
  widthCm?: number;
  depthCm?: number;
  heightCm?: number;
}

export interface AddBoxItemRequest {
  description: string;
  properties: Record<string, string>;
}
export type UpdateBoxItemRequest = AddBoxItemRequest;
