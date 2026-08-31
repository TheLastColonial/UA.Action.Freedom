import { z } from 'zod';

import type {
  CreateVehicleRequest,
  UpdateVehicleRequest,
  VehicleReadModel,
} from '../../api/schemas/vehicles';
import { fuelTypeSchema, transmissionSchema } from '../../api/schemas/common';

// Form values are all strings/booleans/enum literals — what native inputs produce. The
// conversion to the request DTO (trimming, empty -> omitted, numeric coercion) is a pure
// function, kept separate from validation so both are easy to test.
export interface VehicleFormValues {
  vin: string;
  plate: string;
  brand: string;
  model: string;
  colour: string;
  transmission: z.infer<typeof transmissionSchema>;
  notes: string;
  mileage: string;
  servicing: boolean;
  year: string;
  fuel: z.infer<typeof fuelTypeSchema>;
  convoyId: string;
  purchaserName: string;
  purchaseDate: string;
  weightKg: string;
}

export function emptyVehicleForm(): VehicleFormValues {
  return {
    vin: '',
    plate: '',
    brand: '',
    model: '',
    colour: '',
    transmission: 'Unknown',
    notes: '',
    mileage: '',
    servicing: false,
    year: String(new Date().getUTCFullYear()),
    fuel: 'Unknown',
    convoyId: '',
    purchaserName: '',
    purchaseDate: '',
    weightKg: '',
  };
}

export function vehicleToFormValues(vehicle: VehicleReadModel): VehicleFormValues {
  return {
    vin: vehicle.vin,
    plate: vehicle.plate,
    brand: vehicle.brand ?? '',
    model: vehicle.model ?? '',
    colour: vehicle.colour ?? '',
    transmission: vehicle.transmission,
    notes: vehicle.notes ?? '',
    mileage: vehicle.mileage === null ? '' : String(vehicle.mileage),
    servicing: vehicle.servicing,
    year: String(vehicle.year),
    fuel: vehicle.fuel,
    convoyId: vehicle.convoyId === null ? '' : String(vehicle.convoyId),
    purchaserName: vehicle.purchaserName ?? '',
    purchaseDate: vehicle.purchaseDate ? vehicle.purchaseDate.slice(0, 10) : '',
    weightKg: String(vehicle.weightKg),
  };
}

function trimmed(value: string): string | undefined {
  const t = value.trim();
  return t.length > 0 ? t : undefined;
}

function wholeNumber(value: string): number | undefined {
  const t = value.trim();
  return t.length > 0 ? Number(t) : undefined;
}

export function vehicleFormToRequest(values: VehicleFormValues): CreateVehicleRequest {
  const request: CreateVehicleRequest = {
    vin: values.vin.trim(),
    plate: values.plate.trim(),
    transmission: values.transmission,
    fuel: values.fuel,
    servicing: values.servicing,
    year: Number(values.year),
    weightKg: Number(values.weightKg),
  };

  const brand = trimmed(values.brand);
  if (brand !== undefined) request.brand = brand;
  const model = trimmed(values.model);
  if (model !== undefined) request.model = model;
  const colour = trimmed(values.colour);
  if (colour !== undefined) request.colour = colour;
  const notes = trimmed(values.notes);
  if (notes !== undefined) request.notes = notes;
  const purchaserName = trimmed(values.purchaserName);
  if (purchaserName !== undefined) request.purchaserName = purchaserName;
  const purchaseDate = trimmed(values.purchaseDate);
  if (purchaseDate !== undefined) request.purchaseDate = purchaseDate;

  const mileage = wholeNumber(values.mileage);
  if (mileage !== undefined) request.mileage = mileage;
  const convoyId = wholeNumber(values.convoyId);
  if (convoyId !== undefined) request.convoyId = convoyId;

  return request;
}

export function vehicleFormToUpdateRequest(values: VehicleFormValues): UpdateVehicleRequest {
  const { vin, ...rest } = vehicleFormToRequest(values);
  return rest;
}

const integerInRange = (min: number, max: number, message: string) =>
  z.string().refine((raw) => {
    const n = Number(raw.trim());
    return raw.trim().length > 0 && Number.isInteger(n) && n >= min && n <= max;
  }, message);

const optionalNonNegativeInteger = (message: string) =>
  z.string().refine((raw) => {
    if (raw.trim().length === 0) return true;
    const n = Number(raw.trim());
    return Number.isInteger(n) && n >= 0;
  }, message);

// Validation only — the resolver output type equals its input type (no transform), so
// react-hook-form keeps working with VehicleFormValues.
export const vehicleFormSchema = z.object({
  vin: z.string().trim().min(1, 'VIN is required').max(32, 'VIN must be 32 characters or fewer'),
  plate: z
    .string()
    .trim()
    .min(1, 'Number plate is required')
    .max(16, 'Number plate must be 16 characters or fewer'),
  brand: z.string().max(64, 'Make must be 64 characters or fewer'),
  model: z.string().max(64, 'Model must be 64 characters or fewer'),
  colour: z.string().max(32, 'Colour must be 32 characters or fewer'),
  transmission: transmissionSchema,
  notes: z.string().max(1000, 'Notes must be 1000 characters or fewer'),
  mileage: optionalNonNegativeInteger('Mileage must be a whole number of 0 or more'),
  servicing: z.boolean(),
  year: integerInRange(1950, 2100, 'Year must be a whole number between 1950 and 2100'),
  fuel: fuelTypeSchema,
  convoyId: optionalNonNegativeInteger('Convoy must be a whole number'),
  purchaserName: z.string().max(200, 'Purchaser must be 200 characters or fewer'),
  purchaseDate: z.string(),
  weightKg: integerInRange(0, 1_000_000, 'Weight must be a whole number of 0 or more'),
});
