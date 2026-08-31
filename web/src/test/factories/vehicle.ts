import type { VehicleReadModel } from '../../api/schemas/vehicles';

let seq = 0;

export function makeVehicle(overrides: Partial<VehicleReadModel> = {}): VehicleReadModel {
  seq += 1;
  return {
    vin: `WBA000000000${String(seq).padStart(5, '0')}`,
    plate: `AB${String(10 + (seq % 89))} CDE`,
    brand: 'Ford',
    model: 'Transit',
    colour: 'white',
    transmission: 'Manual',
    notes: null,
    mileage: 90_000,
    servicing: false,
    year: 2015,
    fuel: 'Diesel',
    convoyId: null,
    purchaserName: null,
    purchaseDate: null,
    weightKg: 2000,
    ...overrides,
  };
}
