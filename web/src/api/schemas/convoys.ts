import { z } from 'zod';

// Response shapes — src/UA.Action.Freedom.Application/Convoys/ConvoyReadModel.cs.
export const convoyReadModelSchema = z.object({
  id: z.number().int(),
  start: z.string(),
  expectedEnd: z.string(),
  truckListPublishedAt: z.string().nullable(),
  truckListPublished: z.boolean(),
  arrivedAt: z.string().nullable(),
  arrived: z.boolean(),
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
// can be any volunteer. Only drivers count towards a vehicle's crew requirement.
export const crewRoleSchema = z.enum(['Driver', 'Passenger']);
export type CrewRole = z.infer<typeof crewRoleSchema>;

// An entry on the truck list — dbo.ConvoyVehicle. A vehicle needs one driver and is advised two.
//
// `withdrawn` marks a vehicle that left the convoy mid-journey, usually a breakdown. The entry
// stays on the list: its manifest still describes a real load, and which convoy it set off with
// is part of what happened.
export const convoyVehicleReadModelSchema = z.object({
  vin: z.string(),
  plate: z.string(),
  weightKg: z.number().int(),
  driverCount: z.number().int(),
  passengerCount: z.number().int(),
  withdrawnAt: z.string().nullable(),
  withdrawnReason: z.string().nullable(),
  travelling: z.boolean(),
  withdrawn: z.boolean(),
});
export type ConvoyVehicleReadModel = z.infer<typeof convoyVehicleReadModelSchema>;

// The one crew record in the system. The manifest used to keep its own driver teams alongside
// it, connected by nothing, so a printed document could name a crew the insurance had not heard
// of — see src/UA.Action.Freedom.Application/Convoys/ConvoyReadModel.cs.
export const vehicleCrewReadModelSchema = z.object({
  personId: z.string(),
  firstName: z.string(),
  lastName: z.string(),
  role: crewRoleSchema,
});
export type VehicleCrewReadModel = z.infer<typeof vehicleCrewReadModelSchema>;

// Body of PUT /convoys/{id}/vehicles/{vin}/crew/{personId}. A seat is one person on one vehicle
// on one convoy, so there is no leg.
export interface AssignCrewRequest {
  role?: CrewRole;
}

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
// The policy names the drivers it covers. `uncoveredDrivers` are crew drivers added after it was
// recorded; `voided` is an explicit void, not set by crew changes.
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
  uncoveredDrivers: z.array(z.string()),
  coversAllDrivers: z.boolean(),
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

// src/UA.Action.Freedom.Application/Convoys/ReadinessUseCases.cs. Advisory: it says what is
// missing and blocks nothing.
export const vehicleReadinessReadModelSchema = z.object({
  vin: z.string(),
  plate: z.string(),
  insured: z.boolean(),
  ready: z.boolean(),
  reasons: z.array(z.string()),
  advisories: z.array(z.string()),
});
export type VehicleReadinessReadModel = z.infer<typeof vehicleReadinessReadModelSchema>;

export const convoyReadinessReadModelSchema = z.object({
  ready: z.boolean(),
  routePlanned: z.boolean(),
  reasons: z.array(z.string()),
  vehicles: z.array(vehicleReadinessReadModelSchema),
});
export type ConvoyReadinessReadModel = z.infer<typeof convoyReadinessReadModelSchema>;
