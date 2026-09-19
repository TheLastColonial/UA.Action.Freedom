import type {
  ConvoyReadModel,
  ConvoyVehicleReadModel,
  RouteStopReadModel,
  VehicleInsuranceReadModel,
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
  return {
    vin: 'VIN-CONVOY-1',
    plate: 'AB12 CDE',
    weightKg: 2000,
    driverCount: 0,
    passengerCount: 0,
    ...overrides,
  };
}

export function makeInsurance(
  overrides: Partial<VehicleInsuranceReadModel> = {},
): VehicleInsuranceReadModel {
  return {
    convoyId: 1,
    vin: 'VIN-CONVOY-1',
    insurer: 'Ukraine Aid Mutual',
    policyNumber: 'POL-1',
    coverStart: '2026-08-25T00:00:00',
    coverEnd: '2026-09-30T00:00:00',
    costGbp: 412.5,
    recordedBy: 'test-user',
    recordedAt: '2026-08-24T12:00:00',
    voidedAt: null,
    voided: false,
    ...overrides,
  };
}
