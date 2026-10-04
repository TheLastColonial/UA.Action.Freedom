import { z } from 'zod';

// Mirrors Domain.DeclarationKind / DeclarationStatus, which the API serialises as names.
export const declarationKindSchema = z.enum(['Gmr', 'Ens', 'Elo', 'GoodsList']);
export type DeclarationKind = z.infer<typeof declarationKindSchema>;

export const declarationStatusSchema = z.enum([
  'Draft',
  'ReadyToFile',
  'Filed',
  'Accepted',
  'Refused',
  'Stale',
  'Withdrawn',
  'Closed',
]);
export type DeclarationStatus = z.infer<typeof declarationStatusSchema>;

// One declaration about a vehicle's load (ADR 0005). A refusal is a bounded reason code only: the
// authority's own text can quote the declaration it objected to, so it is never stored.
export const declarationReadModelSchema = z.object({
  id: z.number().int(),
  convoyId: z.number().int(),
  vin: z.string(),
  kind: declarationKindSchema,
  status: declarationStatusSchema,
  receiverRef: z.string().nullable(),
  reference: z.string().nullable(),
  reasonCode: z.string().nullable(),
  recordedByName: z.string().nullable(),
  recordedAt: z.string().nullable(),
  lastChangedByName: z.string().nullable(),
  lastChangedAt: z.string().nullable(),
});
export type DeclarationReadModel = z.infer<typeof declarationReadModelSchema>;

// The ICS2 Entry Summary Declaration's detail — recorded, not submitted (docs/adr/0003).
export const ensDeclarationReadModelSchema = z.object({
  declarationId: z.number().int(),
  mrn: z.string(),
  acceptedAt: z.string(),
  filedBy: z.string(),
  filingReference: z.string().nullable(),
});
export type EnsDeclarationReadModel = z.infer<typeof ensDeclarationReadModelSchema>;

// Body of PUT .../declarations/ens — AcceptedAt is ICS2's own timestamp, supplied rather than
// stamped, because Freedom did not submit the declaration.
export interface RecordEnsRequest {
  mrn: string;
  acceptedAt: string;
  filedBy: string;
  filingReference?: string;
}

// Body of POST .../declarations/{kind}/record. The receiver is for a goods list only.
export interface RecordDeclarationRequest {
  reference: string;
  receiverRef?: string;
}

// The bounded reasons a refusal may carry — DeclarationRefusalReasons.Codes.
export const REFUSAL_REASONS = [
  'data-error',
  'goods-mismatch',
  'missing-document',
  'technical',
  'other',
] as const;
export type RefusalReason = (typeof REFUSAL_REASONS)[number];

export interface RefuseDeclarationRequest {
  reasonCode: RefusalReason;
  receiverRef?: string;
}

/** The route segment for each kind, as `DeclarationEndpoints.ParseKind` reads it. */
export const KIND_SEGMENT: Record<DeclarationKind, string> = {
  Gmr: 'gmr',
  Ens: 'ens',
  Elo: 'elo',
  GoodsList: 'goods-list',
};

// What the Dispatcher does about a stale declaration; mirrors Application RedeclareResolution.
export const redeclareResolutionSchema = z.enum([
  'UpdateOrRecreate',
  'InvalidateAndRefile',
  'NewEnvelopeAgainstNewMrn',
  'PrepareNewListAndHoldAtHub',
]);
export type RedeclareResolution = z.infer<typeof redeclareResolutionSchema>;

// A re-declare task (D13): derived from the stale declarations, identifiers only.
export const redeclareTaskSchema = z.object({
  declarationId: z.number().int(),
  vin: z.string(),
  kind: declarationKindSchema,
  receiverRef: z.string().nullable(),
  reference: z.string().nullable(),
  resolution: redeclareResolutionSchema,
});
export type RedeclareTask = z.infer<typeof redeclareTaskSchema>;
