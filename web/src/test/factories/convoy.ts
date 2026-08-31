import type {
  ConvoyReadModel,
  ConvoyVehicleReadModel,
  RouteStopReadModel,
} from '../../api/schemas/convoys';

let seq = 0;

export function makeConvoy(overrides: Partial<ConvoyReadModel> = {}): ConvoyReadModel {
  seq += 1;
  return {
    id: seq,
    start: '2026-03-01T08:00:00',
    expectedEnd: '2026-03-06T20:00:00',
    truckListPublishedAt: null,
    truckListPublished: false,
    ...overrides,
  };
}

export function makeRouteStop(overrides: Partial<RouteStopReadModel> = {}): RouteStopReadModel {
  return {
    sequence: 1,
    house: null,
    street: null,
    city: null,
    country: null,
    postcode: 'M1 1AA',
    ...overrides,
  };
}

export function makeConvoyVehicle(
  overrides: Partial<ConvoyVehicleReadModel> = {},
): ConvoyVehicleReadModel {
  return { vin: 'VIN-CONVOY-1', plate: 'AB12 CDE', weightKg: 2000, ...overrides };
}
