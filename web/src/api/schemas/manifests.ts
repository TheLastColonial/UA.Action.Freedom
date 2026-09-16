import { z } from 'zod';

import { manifestLegSchema, manifestStatusSchema } from './common';

// Response shapes — src/UA.Action.Freedom.Application/Manifests/ManifestReadModel.cs.
export const manifestReadModelSchema = z.object({
  id: z.string(),
  vin: z.string().nullable(),
  convoyId: z.number().int().nullable(),
  status: manifestStatusSchema,
  deliveryNotes: z.string().nullable(),
  ferryBookingComplete: z.boolean(),
  gmrSubmittedAt: z.string().nullable(),
  frozen: z.boolean(),
});
export type ManifestReadModel = z.infer<typeof manifestReadModelSchema>;

export const manifestDriverTeamReadModelSchema = z.object({
  leg: manifestLegSchema,
  primaryPersonId: z.string(),
  secondaryPersonId: z.string().nullable(),
});
export type ManifestDriverTeamReadModel = z.infer<typeof manifestDriverTeamReadModelSchema>;

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
export interface CreateManifestRequest {
  id: string;
  vin?: string;
  convoyId?: number;
  deliveryNotes?: string;
  ferryBookingComplete: boolean;
}

export interface UpdateManifestRequest {
  vin?: string;
  convoyId?: number;
  deliveryNotes?: string;
  ferryBookingComplete: boolean;
}

export interface SetManifestTeamRequest {
  primaryPersonId: string;
  secondaryPersonId?: string;
}
