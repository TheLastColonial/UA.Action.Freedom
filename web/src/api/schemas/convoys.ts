import { z } from 'zod';

// Response shapes — src/UA.Action.Freedom.Application/Convoys/ConvoyReadModel.cs.
export const convoyReadModelSchema = z.object({
  id: z.number().int(),
  start: z.string(),
  expectedEnd: z.string(),
  truckListPublishedAt: z.string().nullable(),
  truckListPublished: z.boolean(),
});
export type ConvoyReadModel = z.infer<typeof convoyReadModelSchema>;

export const routeStopReadModelSchema = z.object({
  sequence: z.number().int(),
  house: z.string().nullable(),
  street: z.string().nullable(),
  city: z.string().nullable(),
  country: z.string().nullable(),
  postcode: z.string(),
});
export type RouteStopReadModel = z.infer<typeof routeStopReadModelSchema>;

// src/UA.Action.Freedom.Domain/CrewRole.cs. A driver must be registered to drive; a passenger
// can be any volunteer. Only drivers count towards a vehicle's two.
export const crewRoleSchema = z.enum(['Driver', 'Passenger']);
export type CrewRole = z.infer<typeof crewRoleSchema>;

export const convoyVehicleReadModelSchema = z.object({
  vin: z.string(),
  plate: z.string(),
  weightKg: z.number().int(),
  driverCount: z.number().int(),
  passengerCount: z.number().int(),
});
export type ConvoyVehicleReadModel = z.infer<typeof convoyVehicleReadModelSchema>;

export const vehicleDriverReadModelSchema = z.object({
  personId: z.string(),
  firstName: z.string(),
  lastName: z.string(),
  role: crewRoleSchema,
});
export type VehicleDriverReadModel = z.infer<typeof vehicleDriverReadModelSchema>;

// Request shapes — src/UA.Action.Freedom.Api/Convoys/ConvoyRequests.cs.
export interface CreateConvoyRequest {
  start: string;
  expectedEnd: string;
}
export type UpdateConvoyRequest = CreateConvoyRequest;

export interface RouteStopRequest {
  house?: string;
  street?: string;
  city?: string;
  country?: string;
  postcode: string;
}

export interface ReplaceConvoyRouteRequest {
  stops: RouteStopRequest[];
}

// src/UA.Action.Freedom.Application/Convoys/InsuranceUseCases.cs — VehicleInsuranceReadModel.
// `voided` is set when the crew changed after it was recorded: the policy names the crew.
export const vehicleInsuranceReadModelSchema = z.object({
  convoyId: z.number().int(),
  vin: z.string(),
  insurer: z.string(),
  policyNumber: z.string(),
  coverStart: z.string(),
  coverEnd: z.string(),
  costGbp: z.number().nullable(),
  recordedBy: z.string(),
  recordedAt: z.string(),
  voidedAt: z.string().nullable(),
  voided: z.boolean(),
});
export type VehicleInsuranceReadModel = z.infer<typeof vehicleInsuranceReadModelSchema>;

// Body of PUT /convoys/{id}/vehicles/{vin}/insurance — RecordInsuranceRequest. Who recorded it
// comes from the token, not from here.
export interface RecordInsuranceRequest {
  insurer: string;
  policyNumber: string;
  coverStart: string;
  coverEnd: string;
  costGbp?: number;
}
