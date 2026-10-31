import { z } from 'zod';

import { budgetSummaryReadModelSchema } from './convoys';

// src/UA.Action.Freedom.Domain/BoxOutcome.cs. "Lost" is not an outcome: a lost box is Undeliverable.
export const boxOutcomeSchema = z.enum([
  'Delivered',
  'Accepted',
  'Seized',
  'ReturnedToHub',
  'Undeliverable',
]);
export type BoxOutcome = z.infer<typeof boxOutcomeSchema>;

// A bounded set, never free text (BoxOutcomes.UndeliverableReasons).
export const undeliverableReasons = ['damaged', 'stolen', 'other'] as const;
export type UndeliverableReason = (typeof undeliverableReasons)[number];

// src/UA.Action.Freedom.Application/Boxes/IBoxOutcomeRepository.cs. No receiver, address or contact.
export const boxOutcomeReadModelSchema = z.object({
  boxId: z.number().int(),
  convoyId: z.number().int(),
  vin: z.string(),
  outcome: boxOutcomeSchema,
  reason: z.string().nullable(),
  hubLocationId: z.number().int().nullable(),
  recordedAt: z.string(),
  recordedByName: z.string().nullable(),
});
export type BoxOutcomeReadModel = z.infer<typeof boxOutcomeReadModelSchema>;

export interface RecordBoxOutcomeRequest {
  outcome: BoxOutcome;
  reason?: UndeliverableReason;
  hubLocationId?: number;
}

export const recordedBoxOutcomeSchema = z.object({
  boxId: z.number().int(),
  outcome: boxOutcomeSchema,
});
export const deliveredVehicleSchema = z.object({ boxesDelivered: z.number().int() });
export const acceptedGoodsListSchema = z.object({ boxesAccepted: z.number().int() });

// src/UA.Action.Freedom.Application/Convoys/ConvoyReportUseCases.cs. Receivers at organisation and region only.
export const reportOutcomeCountSchema = z.object({
  outcome: z.string(),
  boxes: z.number().int(),
  valueGbp: z.number(),
});

export const reportReceiverSchema = z.object({
  organisation: z.string(),
  region: z.string(),
  boxes: z.number().int(),
  boxesDelivered: z.number().int(),
});

export const reportVehicleSchema = z.object({
  vin: z.string(),
  plate: z.string(),
  valueGbp: z.number().nullable(),
  valueSource: z.string().nullable(),
  withdrawn: z.boolean(),
  delivered: z.boolean(),
  boxes: z.number().int(),
  byOutcome: z.array(reportOutcomeCountSchema),
  deliveredValueGbp: z.number(),
  receivers: z.array(reportReceiverSchema),
});

export const convoyReportSchema = z.object({
  convoyId: z.number().int(),
  arrivedAt: z.string().nullable(),
  closedAt: z.string().nullable(),
  budget: budgetSummaryReadModelSchema.nullable(),
  vehicles: z.array(reportVehicleSchema),
  totals: z.object({
    boxes: z.number().int(),
    boxesDelivered: z.number().int(),
    boxesNotDelivered: z.number().int(),
    boxesUndecided: z.number().int(),
    deliveredValueGbp: z.number(),
    vehicleValueGbp: z.number(),
  }),
});
export type ConvoyReport = z.infer<typeof convoyReportSchema>;
