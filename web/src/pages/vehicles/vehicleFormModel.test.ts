import { describe, expect, it } from 'vitest';

import type { VehicleReadModel } from '../../api/schemas/vehicles';
import { makeVehicle } from '../../test/factories/vehicle';
import {
  emptyVehicleForm,
  vehicleFormSchema,
  vehicleFormToRequest,
  vehicleFormToUpdateRequest,
  vehicleToFormValues,
} from './vehicleFormModel';

const filledForm = {
  ...emptyVehicleForm(),
  vin: '  WVWZZZ1KZAW000001 ',
  plate: 'AB12 CDE',
  brand: ' Ford ',
  model: '',
  colour: 'white',
  transmission: 'Manual' as const,
  notes: '',
  mileage: '120000',
  servicing: true,
  year: '2012',
  fuel: 'Diesel' as const,
  purchaserName: 'A. Buyer',
  purchaseDate: '2026-01-15',
  weightKg: '1800',
  maxCargoWeightKg: '900.50',
  cargoWidthCm: '150.25',
  cargoDepthCm: '300',
  cargoHeightCm: '180.75',
};

describe('vehicleFormToRequest', () => {
  it('trims text and coerces the numeric fields', () => {
    const request = vehicleFormToRequest(filledForm);

    expect(request.vin).toBe('WVWZZZ1KZAW000001');
    expect(request.brand).toBe('Ford');
    expect(request.year).toBe(2012);
    expect(request.weightKg).toBe(1800);
    expect(request.mileage).toBe(120000);
    expect(request.servicing).toBe(true);
  });

  it('omits empty optional fields rather than sending null or empty strings', () => {
    const request = vehicleFormToRequest(filledForm);

    expect('model' in request).toBe(false);
    expect('convoyId' in request).toBe(false);
    expect('notes' in request).toBe(false);
  });

  it('keeps enum values as their names', () => {
    const request = vehicleFormToRequest(filledForm);

    expect(request.transmission).toBe('Manual');
    expect(request.fuel).toBe('Diesel');
  });

  it('coerces the cargo capacity fields, decimal places included', () => {
    const request = vehicleFormToRequest(filledForm);

    expect(request.maxCargoWeightKg).toBe(900.5);
    expect(request.cargoWidthCm).toBe(150.25);
    expect(request.cargoDepthCm).toBe(300);
    expect(request.cargoHeightCm).toBe(180.75);
  });

  it('omits cargo capacity fields left blank', () => {
    const request = vehicleFormToRequest({
      ...filledForm,
      maxCargoWeightKg: '',
      cargoWidthCm: '',
      cargoDepthCm: '',
      cargoHeightCm: '',
    });

    expect('maxCargoWeightKg' in request).toBe(false);
    expect('cargoWidthCm' in request).toBe(false);
    expect('cargoDepthCm' in request).toBe(false);
    expect('cargoHeightCm' in request).toBe(false);
  });
});

describe('vehicleFormToUpdateRequest', () => {
  it('drops the VIN — it is the route key, not editable', () => {
    const request = vehicleFormToUpdateRequest(filledForm);

    expect('vin' in request).toBe(false);
    expect(request.plate).toBe('AB12 CDE');
  });
});

describe('vehicleToFormValues', () => {
  it('renders nullable fields as empty strings and dates as yyyy-mm-dd', () => {
    const vehicle: VehicleReadModel = {
      vin: 'V1',
      plate: 'P1',
      brand: null,
      model: null,
      colour: null,
      transmission: 'Automatic',
      notes: null,
      mileage: null,
      servicing: false,
      year: 2020,
      fuel: 'Hybrid',
      convoyId: null,
      purchaserName: null,
      purchaseDate: '2025-11-02T00:00:00Z',
      weightKg: 1500,
      maxCargoWeightKg: null,
      cargoWidthCm: null,
      cargoDepthCm: null,
      cargoHeightCm: null,
      inspectionStatus: 'Pending',
      inspectionNotes: null,
      handedOverAt: null,
      valueGbp: null,
      valueSource: null,
      lastChangedByName: null,
      lastChangedAt: null,
    };

    const values = vehicleToFormValues(vehicle);

    expect(values.brand).toBe('');
    expect(values.mileage).toBe('');
    expect(values.purchaseDate).toBe('2025-11-02');
    expect(values.year).toBe('2020');
    expect(values.maxCargoWeightKg).toBe('');
    expect(values.cargoWidthCm).toBe('');
  });
});

describe('vehicleFormSchema', () => {
  it('accepts a well-formed form', () => {
    expect(vehicleFormSchema.safeParse(filledForm).success).toBe(true);
  });

  it('rejects a missing VIN and plate', () => {
    const result = vehicleFormSchema.safeParse({ ...emptyVehicleForm(), vin: '', plate: '' });
    expect(result.success).toBe(false);
    const messages = result.error?.issues.map((issue) => issue.message) ?? [];
    expect(messages).toContain('VIN is required');
    expect(messages).toContain('Number plate is required');
  });

  it('rejects a year outside 1950–2100', () => {
    const result = vehicleFormSchema.safeParse({ ...filledForm, year: '1930' });
    expect(result.success).toBe(false);
    expect(result.error?.issues.map((i) => i.message)).toContain(
      'Year must be a whole number between 1950 and 2100',
    );
  });

  it('rejects a negative weight', () => {
    const result = vehicleFormSchema.safeParse({ ...filledForm, weightKg: '-5' });
    expect(result.success).toBe(false);
  });

  it('accepts blank cargo capacity fields — they are optional', () => {
    const result = vehicleFormSchema.safeParse({
      ...filledForm,
      maxCargoWeightKg: '',
      cargoWidthCm: '',
      cargoDepthCm: '',
      cargoHeightCm: '',
    });
    expect(result.success).toBe(true);
  });

  it('accepts cargo capacity values with up to 2 decimal places', () => {
    const result = vehicleFormSchema.safeParse({ ...filledForm, maxCargoWeightKg: '900.5' });
    expect(result.success).toBe(true);
  });

  it('rejects a cargo capacity value with more than 2 decimal places', () => {
    const result = vehicleFormSchema.safeParse({ ...filledForm, cargoWidthCm: '150.256' });
    expect(result.success).toBe(false);
  });

  it('rejects a negative cargo capacity value', () => {
    const result = vehicleFormSchema.safeParse({ ...filledForm, cargoHeightCm: '-10' });
    expect(result.success).toBe(false);
  });
});

describe('vehicle value', () => {
  it('sends the value with its source, and neither when blank', () => {
    expect(
      vehicleFormToRequest({ ...filledForm, valueGbp: '4250.50', valueSource: 'Purchased' }),
    ).toMatchObject({ valueGbp: 4250.5, valueSource: 'Purchased' });

    const blank = vehicleFormToRequest(filledForm);
    expect('valueGbp' in blank).toBe(false);
    expect('valueSource' in blank).toBe(false);
  });

  it('reads a stored value back into the form', () => {
    const values = vehicleToFormValues({
      ...makeVehicle(),
      valueGbp: 1800,
      valueSource: 'Estimate',
    });

    expect(values.valueGbp).toBe('1800');
    expect(values.valueSource).toBe('Estimate');
  });

  it('asks for the source of a value, and the value of a source', () => {
    const noSource = vehicleFormSchema.safeParse({ ...filledForm, valueGbp: '100' });
    expect(noSource.error?.issues.map((i) => i.path.join('.'))).toContain('valueSource');

    const noValue = vehicleFormSchema.safeParse({ ...filledForm, valueSource: 'Estimate' });
    expect(noValue.error?.issues.map((i) => i.path.join('.'))).toContain('valueGbp');
  });

  it('rejects a negative value or more than two decimal places', () => {
    for (const valueGbp of ['-5', '10.555']) {
      expect(
        vehicleFormSchema.safeParse({ ...filledForm, valueGbp, valueSource: 'Purchased' }).success,
      ).toBe(false);
    }
  });
});
