import { z } from 'zod';

import type {
  CreateCategoryRequest,
  CustomsAuthority,
  ItemCategoryReadModel,
  UpdateCategoryRequest,
} from '../../api/schemas/categories';

export interface CategoryFormValues {
  nameEn: string;
  nameUk: string;
  hazardClass: string;
  isSensitive: boolean;
  isNotCarried: boolean;
  warnWithinDays: string;
}

export function emptyCategoryForm(): CategoryFormValues {
  return {
    nameEn: '',
    nameUk: '',
    hazardClass: '',
    isSensitive: false,
    isNotCarried: false,
    warnWithinDays: '',
  };
}

export function categoryToFormValues(category: ItemCategoryReadModel): CategoryFormValues {
  return {
    nameEn: category.nameEn,
    nameUk: category.nameUk,
    hazardClass: category.hazardClass === null ? '' : String(category.hazardClass),
    isSensitive: category.isSensitive,
    isNotCarried: category.isNotCarried,
    warnWithinDays: category.warnWithinDays === null ? '' : String(category.warnWithinDays),
  };
}

function trimmed(value: string): string | undefined {
  const t = value.trim();
  return t.length > 0 ? t : undefined;
}

export function categoryFormToRequest(values: CategoryFormValues): CreateCategoryRequest {
  const request: CreateCategoryRequest = {
    nameEn: values.nameEn.trim(),
    isSensitive: values.isSensitive,
    isNotCarried: values.isNotCarried,
  };

  const nameUk = trimmed(values.nameUk);
  if (nameUk !== undefined) request.nameUk = nameUk;
  const hazardClass = trimmed(values.hazardClass);
  if (hazardClass !== undefined) request.hazardClass = Number(hazardClass);
  const warnWithinDays = trimmed(values.warnWithinDays);
  if (warnWithinDays !== undefined) request.warnWithinDays = Number(warnWithinDays);

  return request;
}

export function categoryFormToUpdateRequest(values: CategoryFormValues): UpdateCategoryRequest {
  return categoryFormToRequest(values);
}

export const categoryFormSchema = z.object({
  nameEn: z
    .string()
    .trim()
    .min(1, 'Name is required')
    .max(100, 'Name must be 100 characters or fewer'),
  nameUk: z.string().max(100, 'Ukrainian name must be 100 characters or fewer'),
  hazardClass: z
    .string()
    .refine(
      (raw) => raw.trim().length === 0 || /^[1-9]$/.test(raw.trim()),
      'Hazard class must be a whole number from 1 to 9',
    ),
  isSensitive: z.boolean(),
  isNotCarried: z.boolean(),
  warnWithinDays: z
    .string()
    .refine(
      (raw) => raw.trim().length === 0 || /^\d+$/.test(raw.trim()),
      'Days must be a whole number of 0 or more',
    ),
});

// ---- Customs code for one authority ------------------------------------------------------

export const codeFormSchema = z.object({
  code: z
    .string()
    .refine(
      (raw) => raw.trim().length === 0 || /^\d{6,10}$/.test(raw.trim()),
      'A customs code is 6 to 10 digits',
    ),
});

export function codeToRequestValue(raw: string): string | null {
  const t = raw.trim();
  return t.length > 0 ? t : null;
}

export function codeOf(category: ItemCategoryReadModel, authority: CustomsAuthority): string {
  const code = { UK: category.ukCode, EU: category.euCode, UA: category.uaCode }[authority];
  return code ?? '';
}

export const AUTHORITY_LABELS: Record<CustomsAuthority, string> = {
  UK: 'United Kingdom (UK)',
  EU: 'European Union (EU)',
  UA: 'Ukraine (UA)',
};
