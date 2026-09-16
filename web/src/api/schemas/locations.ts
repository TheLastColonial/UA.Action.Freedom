import { z } from 'zod';

// Response shapes — src/UA.Action.Freedom.Application/Locations/LocationReadModel.cs.
export const locationReadModelSchema = z.object({
  id: z.number().int(),
  name: z.string(),
  house: z.string().nullable(),
  street: z.string().nullable(),
  city: z.string().nullable(),
  country: z.string().nullable(),
  postcode: z.string().nullable(),
});
export type LocationReadModel = z.infer<typeof locationReadModelSchema>;

export const bayReadModelSchema = z.object({
  id: z.number().int(),
  locationId: z.number().int(),
  code: z.string(),
});
export type BayReadModel = z.infer<typeof bayReadModelSchema>;

// Request shapes — src/UA.Action.Freedom.Api/Locations/LocationRequests.cs.
export interface CreateLocationRequest {
  name: string;
  house?: string;
  street?: string;
  city?: string;
  country?: string;
  postcode?: string;
}
export type UpdateLocationRequest = CreateLocationRequest;

export interface CreateBayRequest {
  code: string;
}
export type UpdateBayRequest = CreateBayRequest;
