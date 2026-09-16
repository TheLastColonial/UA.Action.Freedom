import type { BayReadModel, LocationReadModel } from '../../api/schemas/locations';

let locationSeq = 0;
let baySeq = 0;

export function makeLocation(overrides: Partial<LocationReadModel> = {}): LocationReadModel {
  locationSeq += 1;
  return {
    id: locationSeq,
    name: `Depot ${String(locationSeq)}`,
    house: null,
    street: null,
    city: null,
    country: null,
    postcode: null,
    ...overrides,
  };
}

export function makeBay(overrides: Partial<BayReadModel> = {}): BayReadModel {
  baySeq += 1;
  return {
    id: baySeq,
    locationId: 1,
    code: `A${String(baySeq)}`,
    ...overrides,
  };
}
