import { z } from 'zod';

import { lastChangedShape } from './common';

// Response shape — src/UA.Action.Freedom.Application/Donations/DonationReadModels.cs.
// A donor is personal data: never log a value from it.
export const donorReadModelSchema = z.object({
  ...lastChangedShape,
  id: z.string(),
  name: z.string(),
  email: z.string().nullable(),
  phone: z.string().nullable(),
});
export type DonorReadModel = z.infer<typeof donorReadModelSchema>;

// An erased donor reads "Former donor" wherever a donation names them.
export const donationReadModelSchema = z.object({
  ...lastChangedShape,
  id: z.number().int(),
  donorId: z.string(),
  donorName: z.string(),
  receivedOn: z.string(),
  notes: z.string().nullable(),
});
export type DonationReadModel = z.infer<typeof donationReadModelSchema>;

// src/UA.Action.Freedom.Application/Donations/DonorReport.cs — DonorItemStatus, sent by name. The report is
// built only from what a donor may be told: there is no receiver, region, route or address on it.
export const donorItemStatusSchema = z.enum(['BeingPacked', 'PackedAndChecked']);
export type DonorItemStatus = z.infer<typeof donorItemStatusSchema>;

export const donorReportSchema = z.object({
  donorId: z.string(),
  donorName: z.string(),
  itemCount: z.number().int(),
  totalValueGbp: z.number(),
  byCategory: z.array(
    z.object({
      categoryNameEn: z.string(),
      items: z.number().int(),
      valueGbp: z.number(),
    }),
  ),
  donations: z.array(
    z.object({
      donationId: z.number().int(),
      receivedOn: z.string(),
      items: z.array(
        z.object({
          categoryNameEn: z.string(),
          quantity: z.number().int(),
          valueGbp: z.number().nullable(),
          status: donorItemStatusSchema,
        }),
      ),
    }),
  ),
});
export type DonorReport = z.infer<typeof donorReportSchema>;

// Request shapes — src/UA.Action.Freedom.Api/Donations/DonationRequests.cs.
export interface DonorRequest {
  name: string;
  email?: string;
  phone?: string;
}

export interface CreateDonationRequest {
  donorId: string;
  receivedOn: string;
  notes?: string;
}
