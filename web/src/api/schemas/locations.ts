import { z } from 'zod';

import { lastChangedShape } from './common';

// Response shapes — src/UA.Action.Freedom.Application/Locations/LocationReadModel.cs.
export const locationReadModelSchema = z.object({
  ...lastChangedShape,
  id: z.number().int(),
  name: z.string(),
  house: z.string().nullable(),
  street: z.string().nullable(),
  city: z.string().nullable(),
  country: z.string().nullable(),
  postcode: z.string().nullable(),
  // A distribution hub is a location an Administrator has registered (decision D36).
  isRegisteredHub: z.boolean(),
});
export type LocationReadModel = z.infer<typeof locationReadModelSchema>;

export const bayReadModelSchema = z.object({
  id: z.number().int(),
  locationId: z.number().int(),
  code: z.string(),
});
export type BayReadModel = z.infer<typeof bayReadModelSchema>;

// One stretch of a Loader managing a location; `until` is null while it is open (O14, O31).
export const loaderAssignmentSchema = z.object({
  ...lastChangedShape,
  id: z.number().int(),
  locationId: z.number().int(),
  personId: z.string(),
  personName: z.string(),
  from: z.string(),
  until: z.string().nullable(),
});
export type LoaderAssignment = z.infer<typeof loaderAssignmentSchema>;

// Request shapes — src/UA.Action.Freedom.Api/Locations/LocationRequests.cs.
export interface CreateLocationRequest {
  name: string;
  house?: string;
  street?: string;
  city?: string;
  country?: string;
  postcode?: string;
  isRegisteredHub?: boolean;
}
export type UpdateLocationRequest = CreateLocationRequest;

export interface CreateBayRequest {
  code: string;
}
export type UpdateBayRequest = CreateBayRequest;
