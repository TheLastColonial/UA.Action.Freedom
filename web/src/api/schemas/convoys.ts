import { z } from 'zod';

import { lastChangedShape } from './common';

// Response shapes — src/UA.Action.Freedom.Application/Convoys/ConvoyReadModel.cs.
export const convoyReadModelSchema = z.object({
  ...lastChangedShape,
  id: z.number().int(),
  start: z.string(),
  expectedEnd: z.string(),
  truckListPublishedAt: z.string().nullable(),
  truckListPublished: z.boolean(),
  arrivedAt: z.string().nullable(),
  arrived: z.boolean(),
});
export type ConvoyReadModel = z.infer<typeof convoyReadModelSchema>;

// src/UA.Action.Freedom.Domain/RoutePoint.cs. Only a Border point has an authority.
export const routePointKindSchema = z.enum(['Stop', 'Overnight', 'Border', 'Hub']);
export type RoutePointKind = z.infer<typeof routePointKindSchema>;
export const customsAuthoritySchema = z.enum(['UK', 'EU', 'UA']);
export type CustomsAuthority = z.infer<typeof customsAuthoritySchema>;

// `routePointId` is stable across edits of the route; send it back to keep the point.
export const routeStopReadModelSchema = z.object({
  sequence: z.number().int(),
  house: z.string().nullable(),
  street: z.string().nullable(),
  city: z.string().nullable(),
  country: z.string().nullable(),
  postcode: z.string(),
  countryCode: z.string().nullable(),
  routePointId: z.number().int(),
  name: z.string(),
  kind: routePointKindSchema,
  authority: customsAuthoritySchema.nullable(),
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
  // The registered receiver this vehicle is handed over to in Ukraine, once chosen.
  handoverReceiverRef: z.string().nullable(),
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
  routePointId?: number;
  name: string;
  kind: RoutePointKind;
  authority?: CustomsAuthority;
  countryCode?: string;
  house?: string;
  street?: string;
  city?: string;
  country?: string;
  postcode: string;
}

// src/UA.Action.Freedom.Application/Convoys/IConvoyLeaderRepository.cs. History is newest first.
export const convoyLeaderAssignmentSchema = z.object({
  id: z.number().int(),
  convoyId: z.number().int(),
  personId: z.string(),
  personName: z.string(),
  from: z.string(),
  until: z.string().nullable(),
  ...lastChangedShape,
});
export type ConvoyLeaderAssignment = z.infer<typeof convoyLeaderAssignmentSchema>;

export const convoyLeaderSchema = z.object({
  current: convoyLeaderAssignmentSchema.nullable(),
  history: z.array(convoyLeaderAssignmentSchema),
});
export type ConvoyLeader = z.infer<typeof convoyLeaderSchema>;

export interface NominateLeaderRequest {
  personId: string;
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
  // Budget advice (O37): no budget set, or a line over its plan. Never a reason.
  advisories: z.array(z.string()).default([]),
});
export type ConvoyReadinessReadModel = z.infer<typeof convoyReadinessReadModelSchema>;

// src/UA.Action.Freedom.Application/Convoys/FerryBookingUseCases.cs — FerryBookingReadModel. A
// vehicle's outbound crossing (P1); there is no return leg, because vehicles are handed over.
export const ferryBookingReadModelSchema = z.object({
  convoyId: z.number().int(),
  vin: z.string(),
  operator: z.string(),
  reference: z.string(),
  sailingAt: z.string(),
  ticketDetails: z.string().nullable(),
  costGbp: z.number().nullable(),
  lastChangedByName: z.string().nullable(),
  lastChangedAt: z.string().nullable(),
});
export type FerryBookingReadModel = z.infer<typeof ferryBookingReadModelSchema>;

// Body of PUT /convoys/{id}/vehicles/{vin}/ferry — RecordFerryBookingRequest.
export interface RecordFerryBookingRequest {
  operator: string;
  reference: string;
  sailingAt: string;
  ticketDetails?: string;
  costGbp?: number;
}

// src/UA.Action.Freedom.Domain/Budget.cs — CostType. Fuel and Other are entered; the rest are
// read from their booking or policy, so they are never entered twice.
export const costTypeSchema = z.enum(['Fuel', 'Ferry', 'Hotel', 'Insurance', 'Other']);
export type CostType = z.infer<typeof costTypeSchema>;
export const COST_TYPES: readonly CostType[] = costTypeSchema.options;
export const ENTERED_COST_TYPES: readonly CostType[] = ['Fuel', 'Other'];

// src/UA.Action.Freedom.Application/Convoys/IConvoyBudgetRepository.cs
export const budgetLineReadModelSchema = z.object({
  type: costTypeSchema,
  plannedGbp: z.number(),
  lastChangedByName: z.string().nullable(),
  lastChangedAt: z.string().nullable(),
});
export type BudgetLineReadModel = z.infer<typeof budgetLineReadModelSchema>;

// Body of PUT /convoys/{id}/budget — the whole budget; a cost type left out has no line.
export interface SetBudgetRequest {
  lines: { type: CostType; plannedGbp: number }[];
}

export const convoyCostReadModelSchema = z.object({
  id: z.number().int(),
  convoyId: z.number().int(),
  type: costTypeSchema,
  amountGbp: z.number(),
  vin: z.string().nullable(),
  note: z.string().nullable(),
  lastChangedByName: z.string().nullable(),
  lastChangedAt: z.string().nullable(),
});
export type ConvoyCostReadModel = z.infer<typeof convoyCostReadModelSchema>;

// Body of POST /convoys/{id}/costs. Who entered it comes from the caller's login.
export interface AddCostRequest {
  type: CostType;
  amountGbp: number;
  vin?: string;
  note?: string;
}

// src/UA.Action.Freedom.Application/Convoys/BudgetUseCases.cs — BudgetSummaryReadModel.
export const budgetSummaryReadModelSchema = z.object({
  budgetSet: z.boolean(),
  lines: z.array(
    z.object({
      type: costTypeSchema,
      plannedGbp: z.number().nullable(),
      actualGbp: z.number(),
      overBudget: z.boolean(),
    }),
  ),
  plannedTotalGbp: z.number(),
  actualTotalGbp: z.number(),
  equipmentGbp: z.number(),
  anyOverBudget: z.boolean(),
});
export type BudgetSummaryReadModel = z.infer<typeof budgetSummaryReadModelSchema>;

// src/UA.Action.Freedom.Application/Convoys/IVehicleEquipmentRepository.cs (O13): bought for the
// vehicle, no donor, never part of the value delivered.
export const equipmentItemReadModelSchema = z.object({
  id: z.number().int(),
  name: z.string(),
  unitCostGbp: z.number().nullable(),
});
export type EquipmentItemReadModel = z.infer<typeof equipmentItemReadModelSchema>;

export const vehicleEquipmentReadModelSchema = z.object({
  vin: z.string(),
  equipmentItemId: z.number().int(),
  name: z.string(),
  quantity: z.number().int(),
  unitCostGbp: z.number().nullable(),
  costGbp: z.number().nullable(),
  countedCostGbp: z.number(),
});
export type VehicleEquipmentReadModel = z.infer<typeof vehicleEquipmentReadModelSchema>;

// Body of PUT /convoys/{id}/vehicles/{vin}/equipment — everything on the vehicle.
export interface SetVehicleEquipmentRequest {
  lines: { equipmentItemId: number; quantity: number; costGbp?: number }[];
}
