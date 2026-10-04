import { z } from 'zod';

import { customsAuthoritySchema, routePointKindSchema } from '../../api/schemas/convoys';
import type {
  CustomsAuthority,
  ReplaceConvoyRouteRequest,
  RouteStopReadModel,
  RoutePointKind,
} from '../../api/schemas/convoys';

export interface RouteStopFormValues {
  // 0 for a point not saved yet. The id is what keeps a point through an edit of the route.
  routePointId: number;
  name: string;
  kind: RoutePointKind;
  authority: CustomsAuthority | '';
  countryCode: string;
  house: string;
  street: string;
  city: string;
  country: string;
  postcode: string;
}

export function emptyRouteStop(): RouteStopFormValues {
  return {
    routePointId: 0,
    name: '',
    kind: 'Stop',
    authority: '',
    countryCode: '',
    house: '',
    street: '',
    city: '',
    country: '',
    postcode: '',
  };
}

export function routeStopsToFormValues(
  stops: readonly RouteStopReadModel[],
): RouteStopFormValues[] {
  return stops.map((stop) => ({
    routePointId: stop.routePointId,
    name: stop.name,
    kind: stop.kind,
    authority: stop.authority ?? '',
    countryCode: stop.countryCode ?? '',
    house: stop.house ?? '',
    street: stop.street ?? '',
    city: stop.city ?? '',
    country: stop.country ?? '',
    postcode: stop.postcode,
  }));
}

function trimmed(value: string): string | undefined {
  const t = value.trim();
  return t.length > 0 ? t : undefined;
}

// The whole route is replaced at once; list order is the sequence and the server renumbers.
export function routeStopsFormToRequest(
  rows: readonly RouteStopFormValues[],
): ReplaceConvoyRouteRequest {
  return {
    stops: rows.map((row) => {
      const stop: ReplaceConvoyRouteRequest['stops'][number] = {
        postcode: row.postcode.trim(),
        name: row.name.trim(),
        kind: row.kind,
      };
      if (row.routePointId !== 0) stop.routePointId = row.routePointId;
      if (row.kind === 'Border' && row.authority !== '') stop.authority = row.authority;
      const countryCode = trimmed(row.countryCode);
      if (countryCode !== undefined) stop.countryCode = countryCode.toUpperCase();
      const house = trimmed(row.house);
      if (house !== undefined) stop.house = house;
      const street = trimmed(row.street);
      if (street !== undefined) stop.street = street;
      const city = trimmed(row.city);
      if (city !== undefined) stop.city = city;
      const country = trimmed(row.country);
      if (country !== undefined) stop.country = country;
      return stop;
    }),
  };
}

export const routeStopSchema = z
  .object({
    routePointId: z.number().int(),
    name: z
      .string()
      .trim()
      .min(1, 'Name is required')
      .max(100, 'Name must be 100 characters or fewer'),
    kind: routePointKindSchema,
    authority: customsAuthoritySchema.or(z.literal('')),
    countryCode: z.string().trim().max(2, 'Use the two-letter country code'),
    house: z.string().max(100, 'House must be 100 characters or fewer'),
    street: z.string().max(200, 'Street must be 200 characters or fewer'),
    city: z.string().max(100, 'City must be 100 characters or fewer'),
    country: z.string().max(100, 'Country must be 100 characters or fewer'),
    postcode: z
      .string()
      .trim()
      .min(1, 'Postcode is required')
      .max(20, 'Postcode must be 20 characters or fewer'),
  })
  .refine((stop) => stop.kind !== 'Border' || stop.authority !== '', {
    path: ['authority'],
    message: 'A border point needs the authority it is a crossing for',
  });

export const routeFormSchema = z.object({
  stops: z.array(routeStopSchema).max(100, 'A route may have at most 100 stops.'),
});

export type RouteFormValues = z.infer<typeof routeFormSchema>;
