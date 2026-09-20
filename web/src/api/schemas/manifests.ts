import { z } from 'zod';

import { manifestStatusSchema } from './common';

// Response shapes — src/UA.Action.Freedom.Application/Manifests/ManifestReadModel.cs.
// The document pack for one vehicle on one convoy. convoyId and vin are the truck-list entry it
// is the paperwork for — its identity, not editable attributes — so PUT /manifests/{id} has no
// field for either.
export const manifestReadModelSchema = z.object({
  id: z.string(),
  convoyId: z.number().int(),
  vin: z.string(),
  status: manifestStatusSchema,
  deliveryNotes: z.string().nullable(),
  ferryBookingComplete: z.boolean(),
  gmrSubmittedAt: z.string().nullable(),
  frozen: z.boolean(),
});
export type ManifestReadModel = z.infer<typeof manifestReadModelSchema>;

export const manifestBoxReadModelSchema = z.object({
  boxId: z.number().int(),
  weightKg: z.number().int(),
  validated: z.boolean(),
  widthCm: z.number().nullable(),
  depthCm: z.number().nullable(),
  heightCm: z.number().nullable(),
});
export type ManifestBoxReadModel = z.infer<typeof manifestBoxReadModelSchema>;

// MaxCargoWeightKg, CargoOverweight and OversizedBoxIds are advisory only — this endpoint never
// rejects anything, they just surface whether the cargo would exceed the vehicle's stated
// capacity (docs/domain/key-concepts.md § Manifest weight).
export const manifestWeightReadModelSchema = z.object({
  vehicleKg: z.number().int(),
  cargoKg: z.number().int(),
  crewAndBagsKg: z.number().int(),
  fuelKg: z.number().int(),
  totalKg: z.number().int(),
  unvalidatedBoxCount: z.number().int(),
  maxCargoWeightKg: z.number().nullable(),
  cargoOverweight: z.boolean(),
  oversizedBoxIds: z.array(z.number().int()),
});
export type ManifestWeightReadModel = z.infer<typeof manifestWeightReadModelSchema>;

// Request shapes — src/UA.Action.Freedom.Api/Manifests/ManifestRequests.cs.
//
// A manifest is opened on its truck-list entry, POST /convoys/{id}/vehicles/{vin}/manifest, so
// the convoy and the vehicle come from the route rather than the body.
export interface CreateConvoyVehicleManifestRequest {
  id: string;
  deliveryNotes?: string;
  ferryBookingComplete: boolean;
}

export interface UpdateManifestRequest {
  deliveryNotes?: string;
  ferryBookingComplete: boolean;
}
