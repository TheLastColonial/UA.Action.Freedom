import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import type { FieldPath } from 'react-hook-form';

import { fuelTypeSchema, transmissionSchema } from '../../api/schemas/common';
import { problemFieldToFormPath } from '../../api/problem';
import { Button } from '../../components/Button';
import { FormCard } from '../../components/form/FormCard';
import { SelectField, TextField } from '../../components/form/fields';
import { vehicleFormSchema } from './vehicleFormModel';
import type { VehicleFormValues } from './vehicleFormModel';

const toOptions = (values: readonly string[]) => values.map((value) => ({ value, label: value }));

const FORM_FIELDS = new Set<string>([
  'vin',
  'plate',
  'brand',
  'model',
  'colour',
  'transmission',
  'notes',
  'mileage',
  'servicing',
  'year',
  'fuel',
  'purchaserName',
  'purchaseDate',
  'weightKg',
  'maxCargoWeightKg',
  'cargoWidthCm',
  'cargoDepthCm',
  'cargoHeightCm',
]);

interface VehicleFormProps {
  mode: 'create' | 'edit';
  initialValues: VehicleFormValues;
  submitLabel: string;
  submitting: boolean;
  errorMessage?: string | undefined;
  /** Field-keyed messages from a 400 problem+json, PascalCase as the API sends them. */
  fieldErrors?: Readonly<Record<string, readonly string[]>> | undefined;
  onSubmit: (values: VehicleFormValues) => void;
}

export function VehicleForm({
  mode,
  initialValues,
  submitLabel,
  submitting,
  errorMessage,
  fieldErrors,
  onSubmit,
}: VehicleFormProps): JSX.Element {
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<VehicleFormValues>({
    // The same schema in both modes: in edit mode the VIN field is not rendered, and the
    // initial (already valid) VIN carries through, so its rule is a no-op.
    resolver: zodResolver(vehicleFormSchema),
    defaultValues: initialValues,
  });

  useEffect(() => {
    if (!fieldErrors) {
      return;
    }
    for (const [field, messages] of Object.entries(fieldErrors)) {
      const path = problemFieldToFormPath(field);
      if (FORM_FIELDS.has(path) && messages[0] !== undefined) {
        setError(path as FieldPath<VehicleFormValues>, { type: 'server', message: messages[0] });
      }
    }
  }, [fieldErrors, setError]);

  return (
    <form
      noValidate
      onSubmit={(event) => {
        void handleSubmit(onSubmit)(event);
      }}
    >
      {errorMessage ? (
        <p role="alert" className="field__error">
          {errorMessage}
        </p>
      ) : null}

      <FormCard title="Vehicle details">
        {mode === 'create' ? (
          <TextField label="VIN" error={errors.vin?.message} {...register('vin')} />
        ) : (
          <TextField label="VIN" value={initialValues.vin} readOnly disabled />
        )}

        <TextField label="Number plate" error={errors.plate?.message} {...register('plate')} />
        <TextField label="Make" error={errors.brand?.message} {...register('brand')} />
        <TextField label="Model" error={errors.model?.message} {...register('model')} />
        <TextField label="Colour" error={errors.colour?.message} {...register('colour')} />
        <TextField
          label="Year"
          type="number"
          inputMode="numeric"
          error={errors.year?.message}
          {...register('year')}
        />
      </FormCard>

      <FormCard title="Engine details">
        <SelectField
          label="Transmission"
          options={toOptions(transmissionSchema.options)}
          error={errors.transmission?.message}
          {...register('transmission')}
        />
        <SelectField
          label="Fuel"
          options={toOptions(fuelTypeSchema.options)}
          error={errors.fuel?.message}
          {...register('fuel')}
        />
        <TextField
          label="Kerb weight (kg)"
          type="number"
          inputMode="numeric"
          error={errors.weightKg?.message}
          {...register('weightKg')}
        />
        <TextField
          label="Mileage"
          type="number"
          inputMode="numeric"
          error={errors.mileage?.message}
          {...register('mileage')}
        />
      </FormCard>

      <FormCard title="Cargo capacity">
        <TextField
          label="Maximum weight (kg)"
          type="number"
          inputMode="decimal"
          step="0.01"
          error={errors.maxCargoWeightKg?.message}
          {...register('maxCargoWeightKg')}
        />
        <TextField
          label="Width (cm)"
          type="number"
          inputMode="decimal"
          step="0.01"
          error={errors.cargoWidthCm?.message}
          {...register('cargoWidthCm')}
        />
        <TextField
          label="Depth (cm)"
          type="number"
          inputMode="decimal"
          step="0.01"
          error={errors.cargoDepthCm?.message}
          {...register('cargoDepthCm')}
        />
        <TextField
          label="Height (cm)"
          type="number"
          inputMode="decimal"
          step="0.01"
          error={errors.cargoHeightCm?.message}
          {...register('cargoHeightCm')}
        />
      </FormCard>

      <FormCard title="Purchase information">
        <TextField
          label="Purchaser"
          error={errors.purchaserName?.message}
          {...register('purchaserName')}
        />
        <TextField
          label="Purchase date"
          type="date"
          error={errors.purchaseDate?.message}
          {...register('purchaseDate')}
        />
      </FormCard>

      <FormCard title="Notes">
        <TextField label="Notes" error={errors.notes?.message} {...register('notes')} />
      </FormCard>

      <Button type="submit" disabled={submitting}>
        {submitting ? 'Saving…' : submitLabel}
      </Button>
    </form>
  );
}
