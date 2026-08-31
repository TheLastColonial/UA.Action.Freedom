import { z } from 'zod';

// The non-sensitive receiver. This type has NO address or contact fields — code holding one
// has nothing sensitive to leak. Do not add fields here; the delivery detail lives behind
// `receivers:detail` and its own module (api/receiverDetail.ts).
export const receiverReadModelSchema = z.object({
  ref: z.string(),
  organisation: z.string(),
  region: z.string(),
});
export type ReceiverReadModel = z.infer<typeof receiverReadModelSchema>;

// Request shape — src/UA.Action.Freedom.Api/Receivers/ReceiverRequests.cs.
export interface CreateReceiverRequest {
  organisation: string;
  region: string;
}
export type UpdateReceiverRequest = CreateReceiverRequest;
