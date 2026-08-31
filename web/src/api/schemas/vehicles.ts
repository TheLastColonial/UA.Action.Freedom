import { z } from 'zod';

import { fuelTypeSchema, transmissionSchema } from './common';

// Response shape — src/UA.Action.Freedom.Application/Vehicles/VehicleReadModel.cs. Optional
// scalars come back as JSON null (System.Text.Json does not omit them).
export const vehicleReadModelSchema = z.object({
  vin: z.string(),
  plate: z.string(),
  brand: z.string().nullable(),
  model: z.string().nullable(),
  colour: z.string().nullable(),
  transmission: transmissionSchema,
  notes: z.string().nullable(),
  mileage: z.number().int().nullable(),
  servicing: z.boolean(),
  year: z.number().int(),
  fuel: fuelTypeSchema,
  convoyId: z.number().int().nullable(),
  purchaserName: z.string().nullable(),
  purchaseDate: z.string().nullable(),
  weightKg: z.number().int(),
});

export type VehicleReadModel = z.infer<typeof vehicleReadModelSchema>;

// Request shape — src/UA.Action.Freedom.Api/Vehicles/VehicleRequests.cs. Optional fields are
// omitted entirely rather than sent as null.
export interface CreateVehicleRequest {
  vin: string;
  plate: string;
  brand?: string;
  model?: string;
  colour?: string;
  transmission: z.infer<typeof transmissionSchema>;
  notes?: string;
  mileage?: number;
  servicing: boolean;
  year: number;
  fuel: z.infer<typeof fuelTypeSchema>;
  convoyId?: number;
  purchaserName?: string;
  purchaseDate?: string;
  weightKg: number;
}

export type UpdateVehicleRequest = Omit<CreateVehicleRequest, 'vin'>;
