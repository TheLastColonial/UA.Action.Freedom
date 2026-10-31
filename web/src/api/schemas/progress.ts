import { z } from 'zod';

import { customsAuthoritySchema, routePointKindSchema } from './convoys';

// src/UA.Action.Freedom.Domain/RoutePointMark.cs. Reported, never tracked: a mark has a time of entry and a name, no position.
export const routePointMarkKindSchema = z.enum(['Reached', 'Crossed']);
export type RoutePointMarkKind = z.infer<typeof routePointMarkKindSchema>;

// src/UA.Action.Freedom.Application/Convoys/ProgressUseCases.cs.
export const routePointMarkSchema = z.object({
  routePointId: z.number().int(),
  kind: routePointMarkKindSchema,
  vin: z.string().nullable(),
  recordedByName: z.string().nullable(),
  recordedAt: z.string(),
  onBehalfOfLeader: z.boolean(),
});
export type RoutePointMark = z.infer<typeof routePointMarkSchema>;

export const progressBookingSchema = z.object({
  provider: z.string(),
  checkIn: z.string(),
  checkOut: z.string(),
});

// A point's name and kind only: the header. No address of any kind (ADR 0016, X13).
export const progressPointSchema = z.object({
  routePointId: z.number().int(),
  sequence: z.number().int(),
  name: z.string(),
  kind: routePointKindSchema,
  authority: customsAuthoritySchema.nullable(),
  marks: z.array(routePointMarkSchema),
  accommodation: z.array(progressBookingSchema),
});
export type ProgressPoint = z.infer<typeof progressPointSchema>;

export const progressVehicleSchema = z.object({
  vin: z.string(),
  plate: z.string(),
  delivered: z.boolean(),
});

export const progressHubSchema = z.object({ locationId: z.number().int(), name: z.string() });

export const convoyProgressSchema = z.object({
  convoyId: z.number().int(),
  departedAt: z.string().nullable(),
  arrivedAt: z.string().nullable(),
  points: z.array(progressPointSchema),
  vehicles: z.array(progressVehicleSchema),
  hubs: z.array(progressHubSchema),
  hqPhone: z.string().nullable(),
});
export type ConvoyProgress = z.infer<typeof convoyProgressSchema>;
