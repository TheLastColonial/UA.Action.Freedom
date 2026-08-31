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
});
export type ManifestBoxReadModel = z.infer<typeof manifestBoxReadModelSchema>;

export const manifestWeightReadModelSchema = z.object({
  vehicleKg: z.number().int(),
  cargoKg: z.number().int(),
  crewAndBagsKg: z.number().int(),
  fuelKg: z.number().int(),
  totalKg: z.number().int(),
  unvalidatedBoxCount: z.number().int(),
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
