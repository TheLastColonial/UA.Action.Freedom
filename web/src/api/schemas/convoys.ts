import { z } from 'zod';

import { journeyLegSchema, type JourneyLeg } from './common';

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
// can be any volunteer. Only drivers count towards a vehicle's two.
export const crewRoleSchema = z.enum(['Driver', 'Passenger']);
export type CrewRole = z.infer<typeof crewRoleSchema>;

// An entry on the truck list — dbo.ConvoyVehicle. Crew is counted per leg because a vehicle is
// crewed twice, out of the UK and into Ukraine; a single count could not tell a fully crewed
// vehicle from one with nobody booked for the second half.
//
// `withdrawn` marks a vehicle that left the convoy mid-journey, usually a breakdown. The entry
// stays on the list: its manifest still describes a real load, and which convoy it set off with
// is part of what happened.
export const convoyVehicleReadModelSchema = z.object({
  vin: z.string(),
  plate: z.string(),
  weightKg: z.number().int(),
  ukDriverCount: z.number().int(),
  ukPassengerCount: z.number().int(),
  borderDriverCount: z.number().int(),
  borderPassengerCount: z.number().int(),
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
  leg: journeyLegSchema,
  role: crewRoleSchema,
});
export type VehicleCrewReadModel = z.infer<typeof vehicleCrewReadModelSchema>;

// Body of PUT /convoys/{id}/vehicles/{vin}/crew/{personId}. The leg is required: there is no
// sensible default for which half of the journey somebody is driving.
export interface AssignCrewRequest {
  leg: JourneyLeg;
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

// src/UA.Action.Freedom.Application/Convoys/ReadinessUseCases.cs. Advisory: it says what is
// missing and blocks nothing.
export const vehicleLegReadinessReadModelSchema = z.object({
  leg: journeyLegSchema,
  drivers: z.number().int(),
  ready: z.boolean(),
  reasons: z.array(z.string()),
});
export type VehicleLegReadinessReadModel = z.infer<typeof vehicleLegReadinessReadModelSchema>;

export const vehicleReadinessReadModelSchema = z.object({
  vin: z.string(),
  plate: z.string(),
  insured: z.boolean(),
  ready: z.boolean(),
  legs: z.array(vehicleLegReadinessReadModelSchema),
  reasons: z.array(z.string()),
});
export type VehicleReadinessReadModel = z.infer<typeof vehicleReadinessReadModelSchema>;

export const convoyReadinessReadModelSchema = z.object({
  ready: z.boolean(),
  routePlanned: z.boolean(),
  reasons: z.array(z.string()),
  vehicles: z.array(vehicleReadinessReadModelSchema),
});
export type ConvoyReadinessReadModel = z.infer<typeof convoyReadinessReadModelSchema>;
