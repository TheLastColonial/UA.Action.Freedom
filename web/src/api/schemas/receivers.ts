import { z } from 'zod';

import { lastChangedShape } from './common';

// src/UA.Action.Freedom.Domain/Receiver.cs — ReceiverStatus. Only Registered lets a box or a vehicle
// name the receiver. The status says that an Administrator has authorised sending to it, and nothing
// about what kind of body it is (decision D33).
export const receiverStatusSchema = z.enum(['Pending', 'Registered', 'Suspended', 'Expired']);
export type ReceiverStatus = z.infer<typeof receiverStatusSchema>;

// The non-sensitive receiver. This type has NO address or contact fields — code holding one
// has nothing sensitive to leak. Do not add fields here; the delivery detail lives behind
// `receivers:detail` and its own module (api/receiverDetail.ts). `status` is allowed because it is
// not sensitive: it records only that the receiver may be sent to. Do not add a field that says what
// kind of body a receiver is.
export const receiverReadModelSchema = z.object({
  ...lastChangedShape,
  ref: z.string(),
  organisation: z.string(),
  region: z.string(),
  status: receiverStatusSchema,
});
export type ReceiverReadModel = z.infer<typeof receiverReadModelSchema>;

// Request shape — src/UA.Action.Freedom.Api/Receivers/ReceiverRequests.cs.
export interface CreateReceiverRequest {
  organisation: string;
  region: string;
}
export type UpdateReceiverRequest = CreateReceiverRequest;

// PUT /receivers/{ref}/status — Administrator only (`receivers:register`).
export interface SetReceiverStatusRequest {
  status: ReceiverStatus;
}

// GET /receivers/{ref}/usage — what a status change touches. Identifiers and counts only.
export const receiverUsageSchema = z.object({
  boxIds: z.array(z.number().int()),
  convoyIds: z.array(z.number().int()),
  boxCount: z.number().int(),
  convoyCount: z.number().int(),
});
export type ReceiverUsage = z.infer<typeof receiverUsageSchema>;
