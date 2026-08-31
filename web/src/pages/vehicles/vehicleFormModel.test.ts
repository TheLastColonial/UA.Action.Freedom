import { describe, expect, it } from 'vitest';

import type { VehicleReadModel } from '../../api/schemas/vehicles';
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
  convoyId: '',
  purchaserName: 'A. Buyer',
  purchaseDate: '2026-01-15',
  weightKg: '1800',
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
    };

    const values = vehicleToFormValues(vehicle);

    expect(values.brand).toBe('');
    expect(values.mileage).toBe('');
    expect(values.convoyId).toBe('');
    expect(values.purchaseDate).toBe('2025-11-02');
    expect(values.year).toBe('2020');
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
});
