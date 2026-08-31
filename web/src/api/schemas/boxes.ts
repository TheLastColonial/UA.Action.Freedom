import { z } from 'zod';

// Response shapes — src/UA.Action.Freedom.Application/Boxes/BoxReadModel.cs.
export const boxReadModelSchema = z.object({
  id: z.number().int(),
  weightKg: z.number().int(),
  receiverRef: z.string().nullable(),
  house: z.string().nullable(),
  street: z.string().nullable(),
  city: z.string().nullable(),
  country: z.string().nullable(),
  postcode: z.string().nullable(),
  validatedByPersonId: z.string().nullable(),
  validatedAt: z.string().nullable(),
  validated: z.boolean(),
});
export type BoxReadModel = z.infer<typeof boxReadModelSchema>;

export const boxItemReadModelSchema = z.object({
  id: z.string(),
  description: z.string(),
  properties: z.record(z.string(), z.string()),
});
export type BoxItemReadModel = z.infer<typeof boxItemReadModelSchema>;

// Request shapes — src/UA.Action.Freedom.Api/Boxes/BoxRequests.cs.
export interface CreateBoxRequest {
  receiverRef?: string;
  house?: string;
  street?: string;
  city?: string;
  country?: string;
  postcode?: string;
}
export type UpdateBoxRequest = CreateBoxRequest;

export interface ValidateBoxRequest {
  validatedByPersonId: string;
  weightKg: number;
}

export interface AddBoxItemRequest {
  description: string;
  properties: Record<string, string>;
}
