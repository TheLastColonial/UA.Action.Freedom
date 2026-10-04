import { http, HttpResponse } from 'msw';
import type { RequestHandler } from 'msw';
import { z } from 'zod';

import type {
  AccommodationBooking,
  AccommodationCoverage,
  CoverageStatus,
  SelfAccommodation,
} from '../../api/schemas/accommodation';
import { problem } from './problem';

// Mirrors AccommodationBookingRequest and its validator.
const bookingBodySchema = z.object({
  routePointId: z.number().int().positive(),
  provider: z.string().min(1).max(200),
  checkIn: z.string(),
  checkOut: z.string(),
  guests: z.array(z.string()).min(1),
  reference: z.string().max(100).optional(),
  details: z.string().max(1000).optional(),
  costGbp: z.number().min(0).optional(),
});

const migrateBodySchema = z.object({ fromPersonId: z.string(), toPersonId: z.string() });

export interface Stop {
  routePointId: number;
  sequence: number;
  name: string;
}

export interface CrewMember {
  personId: string;
  name: string;
}

export interface AccommodationApi {
  /** The overnight stops of the convoy, in route order. */
  stops: Stop[];
  /** Who is crewed on the convoy now. Take one out to leave their booking behind. */
  crew: CrewMember[];
  bookings: AccommodationBooking[];
  selfArranged: SelfAccommodation[];
  handlers: RequestHandler[];
}

/**
 * The accommodation routes of one convoy, as the API enforces them: guests must be crewed and the stop one of the
 * route's, a flag needs a crew member, coverage and the leftover-booking tasks are derived on read, and cancelling
 * keeps the booking. Pass the convoy's crew and overnight stops; the lists are live, so a test can change the crew.
 */
export function accommodationApi(initial: { crew: CrewMember[]; stops: Stop[] }): AccommodationApi {
  const stops = [...initial.stops];
  const crew = [...initial.crew];
  const bookings: AccommodationBooking[] = [];
  const selfArranged: SelfAccommodation[] = [];
  let nextId = 1;

  const idFrom = (raw: string | readonly string[] | undefined) => Number(String(raw));
  const crewed = (personId: string) => crew.some((member) => member.personId === personId);

  const statusOf = (routePointId: number, personId: string): CoverageStatus => {
    if (
      bookings.some(
        (booking) =>
          !booking.cancelled &&
          booking.routePointId === routePointId &&
          booking.guests.includes(personId),
      )
    ) {
      return 'Booked';
    }
    return selfArranged.some(
      (flag) => flag.routePointId === routePointId && flag.personId === personId,
    )
      ? 'SelfArranged'
      : 'Missing';
  };

  const leftover = () =>
    bookings.filter(
      (booking) => !booking.cancelled && !booking.guests.some((guest) => crewed(guest)),
    );

  const coverage = (): AccommodationCoverage => {
    const cells = stops.flatMap((stop) =>
      crew.map((member) => ({
        routePointId: stop.routePointId,
        personId: member.personId,
        status: statusOf(stop.routePointId, member.personId),
      })),
    );
    const missingCount = cells.filter((cell) => cell.status === 'Missing').length;
    const left = leftover();
    return {
      stops,
      crew,
      cells,
      missingCount,
      allCovered: missingCount === 0,
      leftoverBookings: left.map((booking) => ({
        bookingId: booking.id,
        routePointId: booking.routePointId,
        guestIds: booking.guests,
      })),
      warnings: left.map(
        (booking) =>
          `Booking ${String(booking.id)} at ${stops.find((stop) => stop.routePointId === booking.routePointId)?.name ?? 'a stop'} covers nobody still crewed: cancel or migrate it`,
      ),
    };
  };

  const handlers: RequestHandler[] = [
    http.get('/convoys/:id/accommodation', () => HttpResponse.json({ bookings, selfArranged })),

    http.get('/convoys/:id/accommodation/coverage', () => HttpResponse.json(coverage())),

    http.get('/convoys/:id/tasks', () =>
      HttpResponse.json(
        leftover().map((booking) => ({
          type: 'accommodation-leftover',
          bookingId: booking.id,
          routePointId: booking.routePointId,
          guestIds: booking.guests,
          resolution: 'CancelOrMigrate',
        })),
      ),
    ),

    http.post('/convoys/:id/accommodation', async ({ params, request }) => {
      const parsed = bookingBodySchema.safeParse(await request.json());
      if (!parsed.success) {
        return problem(400, 'The booking is not valid.');
      }
      const body = parsed.data;
      if (!stops.some((stop) => stop.routePointId === body.routePointId)) {
        return problem(422, "That route point is not on this convoy's route.");
      }
      if (!body.guests.every(crewed)) {
        return problem(422, 'Everyone named must be crewed on this convoy.');
      }
      const id = nextId++;
      bookings.push({
        id,
        convoyId: idFrom(params['id']),
        routePointId: body.routePointId,
        provider: body.provider,
        reference: body.reference ?? null,
        checkIn: body.checkIn,
        checkOut: body.checkOut,
        details: body.details ?? null,
        costGbp: body.costGbp ?? null,
        cancelled: false,
        guests: body.guests,
        lastChangedByName: null,
        lastChangedAt: null,
      });
      return new HttpResponse(null, {
        status: 201,
        headers: { Location: `/convoys/${String(params['id'])}/accommodation/${String(id)}` },
      });
    }),

    http.delete('/convoys/:id/accommodation/self/:routePointId/:personId', ({ params }) => {
      const routePointId = idFrom(params['routePointId']);
      const personId = String(params['personId']);
      const index = selfArranged.findIndex(
        (flag) => flag.routePointId === routePointId && flag.personId === personId,
      );
      if (index < 0) {
        return new HttpResponse(null, { status: 404 });
      }
      selfArranged.splice(index, 1);
      return new HttpResponse(null, { status: 204 });
    }),

    http.put('/convoys/:id/accommodation/self/:routePointId/:personId', ({ params }) => {
      const routePointId = idFrom(params['routePointId']);
      const personId = String(params['personId']);
      if (!stops.some((stop) => stop.routePointId === routePointId)) {
        return problem(
          422,
          'Only an overnight stop needs accommodation, so only one can be flagged.',
        );
      }
      if (!crewed(personId)) {
        return problem(422, 'Everyone named must be crewed on this convoy.');
      }
      if (
        !selfArranged.some(
          (flag) => flag.routePointId === routePointId && flag.personId === personId,
        )
      ) {
        selfArranged.push({
          convoyId: idFrom(params['id']),
          routePointId,
          personId,
          lastChangedByName: null,
          lastChangedAt: null,
        });
      }
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/convoys/:id/accommodation/:bookingId', ({ params }) => {
      const index = bookings.findIndex((booking) => booking.id === idFrom(params['bookingId']));
      if (index < 0) {
        return new HttpResponse(null, { status: 404 });
      }
      const existing = bookings[index];
      if (existing) {
        bookings[index] = { ...existing, cancelled: true };
      }
      return new HttpResponse(null, { status: 204 });
    }),

    http.post('/convoys/:id/accommodation/:bookingId/migrate', async ({ params, request }) => {
      const index = bookings.findIndex((booking) => booking.id === idFrom(params['bookingId']));
      const existing = bookings[index];
      if (!existing) {
        return new HttpResponse(null, { status: 404 });
      }
      const body = migrateBodySchema.parse(await request.json());
      if (!crewed(body.toPersonId)) {
        return problem(422, 'The replacement must be crewed on this convoy.');
      }
      if (!existing.guests.includes(body.fromPersonId)) {
        return problem(422, 'That person is not a guest on this booking.');
      }
      bookings[index] = {
        ...existing,
        guests: [
          ...new Set([
            ...existing.guests.filter((guest) => guest !== body.fromPersonId),
            body.toPersonId,
          ]),
        ],
      };
      return new HttpResponse(null, { status: 204 });
    }),
  ];

  return { stops, crew, bookings, selfArranged, handlers };
}
