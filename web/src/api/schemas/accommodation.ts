import { z } from 'zod';

// src/UA.Action.Freedom.Domain/Accommodation.cs — CoverageStatus.
export const coverageStatusSchema = z.enum(['Booked', 'SelfArranged', 'Missing']);
export type CoverageStatus = z.infer<typeof coverageStatusSchema>;

// src/UA.Action.Freedom.Application/Convoys/IAccommodationRepository.cs — a booking names its guests by
// person id only; the grid supplies the names.
export const accommodationBookingSchema = z.object({
  id: z.number().int(),
  convoyId: z.number().int(),
  routePointId: z.number().int(),
  provider: z.string(),
  reference: z.string().nullable(),
  checkIn: z.string(),
  checkOut: z.string(),
  details: z.string().nullable(),
  costGbp: z.number().nullable(),
  cancelled: z.boolean(),
  guests: z.array(z.string()),
  lastChangedByName: z.string().nullable(),
  lastChangedAt: z.string().nullable(),
});
export type AccommodationBooking = z.infer<typeof accommodationBookingSchema>;

export const selfAccommodationSchema = z.object({
  convoyId: z.number().int(),
  routePointId: z.number().int(),
  personId: z.string(),
  lastChangedByName: z.string().nullable(),
  lastChangedAt: z.string().nullable(),
});
export type SelfAccommodation = z.infer<typeof selfAccommodationSchema>;

// GET /convoys/{id}/accommodation
export const accommodationSchema = z.object({
  bookings: z.array(accommodationBookingSchema),
  selfArranged: z.array(selfAccommodationSchema),
});
export type Accommodation = z.infer<typeof accommodationSchema>;

// src/UA.Action.Freedom.Application/Convoys/AccommodationUseCases.cs — AccommodationCoverageReadModel.
export const accommodationCoverageSchema = z.object({
  stops: z.array(
    z.object({ routePointId: z.number().int(), sequence: z.number().int(), name: z.string() }),
  ),
  crew: z.array(z.object({ personId: z.string(), name: z.string() })),
  cells: z.array(
    z.object({
      routePointId: z.number().int(),
      personId: z.string(),
      status: coverageStatusSchema,
    }),
  ),
  missingCount: z.number().int(),
  allCovered: z.boolean(),
  leftoverBookings: z.array(
    z.object({
      bookingId: z.number().int(),
      routePointId: z.number().int(),
      guestIds: z.array(z.string()),
    }),
  ),
  warnings: z.array(z.string()),
});
export type AccommodationCoverage = z.infer<typeof accommodationCoverageSchema>;

// Body of POST /convoys/{id}/accommodation and PUT /convoys/{id}/accommodation/{bookingId}.
export interface AccommodationBookingRequest {
  routePointId: number;
  provider: string;
  checkIn: string;
  checkOut: string;
  guests: string[];
  reference?: string;
  details?: string;
  costGbp?: number;
}

// A leftover booking as a Dispatcher task (P13, P16): derived, identifiers only.
export const leftoverBookingTaskSchema = z.object({
  type: z.literal('accommodation-leftover'),
  bookingId: z.number().int(),
  routePointId: z.number().int(),
  guestIds: z.array(z.string()),
  resolution: z.literal('CancelOrMigrate'),
});
export type LeftoverBookingTask = z.infer<typeof leftoverBookingTaskSchema>;
