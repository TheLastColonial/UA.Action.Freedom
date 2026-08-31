import { z } from 'zod';

import { getJson, put204 } from './http';

// -------------------------------------------------------------------------
// SENSITIVE. The delivery address and contact for a Ukrainian receiver — the
// highest-risk data in the system. This module is deliberately isolated:
//   * it is imported only by the Ground Officer's receiver-detail panel;
//   * nothing here is cached (no React Query) — every read is a fresh, audited
//     server round trip with a stated reason;
//   * validation messages are static (max-length / required) and never echo a
//     submitted value.
// Do not import this module into list, print, or verification code.
// -------------------------------------------------------------------------

export const receiverDetailReadModelSchema = z.object({
  ref: z.string(),
  contactName: z.string(),
  contactPhone: z.string(),
  addressLine1: z.string(),
  addressLine2: z.string().nullable(),
  city: z.string(),
  postCode: z.string().nullable(),
  deleteAfter: z.string().nullable(),
});
export type ReceiverDetailReadModel = z.infer<typeof receiverDetailReadModelSchema>;

export interface SetReceiverDetailRequest {
  contactName: string;
  contactPhone: string;
  addressLine1: string;
  addressLine2?: string;
  city: string;
  postCode?: string;
  deleteAfter?: string;
}

const detailPath = (ref: string) => `/receivers/${encodeURIComponent(ref)}/detail`;

/** Every call is audited server-side, in the same transaction as the read. */
export function revealReceiverDetail(
  ref: string,
  reason: string,
): Promise<ReceiverDetailReadModel> {
  return getJson(detailPath(ref), receiverDetailReadModelSchema, { reason });
}

export function setReceiverDetail(ref: string, body: SetReceiverDetailRequest): Promise<void> {
  return put204(detailPath(ref), body);
}
