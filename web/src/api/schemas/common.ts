import { z } from 'zod';

// Enums are sent and accepted BY NAME by the API (System.Text.Json JsonStringEnumConverter).
// These lists must match the C# enum members exactly.

// src/UA.Action.Freedom.Domain/Vehicle.cs — TransmissionType
export const transmissionSchema = z.enum(['Unknown', 'Manual', 'Automatic']);
export type Transmission = z.infer<typeof transmissionSchema>;

// src/UA.Action.Freedom.Domain/Vehicle.cs — FuelType
export const fuelTypeSchema = z.enum(['Unknown', 'Petrol', 'Diesel', 'Electric', 'Hybrid']);
export type FuelType = z.infer<typeof fuelTypeSchema>;

// src/UA.Action.Freedom.Domain/Manifest.cs — ManifestStatus
export const manifestStatusSchema = z.enum([
  'Created',
  'Proposed',
  'Rejected',
  'Confirmed',
  'Preparing',
  'Ready',
  'InTransit',
  'Delivered',
  'Lost',
  'Returned',
]);
export type ManifestStatus = z.infer<typeof manifestStatusSchema>;

// src/UA.Action.Freedom.Application/Manifests/ManifestReadModel.cs — ManifestLeg
export const manifestLegSchema = z.enum(['Uk', 'Border']);
export type ManifestLeg = z.infer<typeof manifestLegSchema>;
