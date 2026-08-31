import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';

import { CheckboxField, TextField } from '../../components/form/fields';
import { manifestFormSchema } from './manifestModels';
import type { ManifestFormValues } from './manifestModels';

interface ManifestFormProps {
  mode: 'create' | 'edit';
  initialValues: ManifestFormValues;
  submitLabel: string;
  submitting: boolean;
  errorMessage?: string | undefined;
  onSubmit: (values: ManifestFormValues) => void;
}

export function ManifestForm({
  mode,
  initialValues,
  submitLabel,
  submitting,
  errorMessage,
  onSubmit,
}: ManifestFormProps): JSX.Element {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ManifestFormValues>({
    resolver: zodResolver(manifestFormSchema),
    defaultValues: initialValues,
  });

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

      {mode === 'create' ? (
        <TextField label="Reference" error={errors.id?.message} {...register('id')} />
      ) : (
        <TextField label="Reference" value={initialValues.id} readOnly disabled />
      )}

      <TextField label="Vehicle VIN" error={errors.vin?.message} {...register('vin')} />
      <TextField
        label="Convoy id"
        type="number"
        inputMode="numeric"
        error={errors.convoyId?.message}
        {...register('convoyId')}
      />
      <TextField
        label="Delivery notes"
        error={errors.deliveryNotes?.message}
        {...register('deliveryNotes')}
      />
      <CheckboxField label="Ferry booking complete" {...register('ferryBookingComplete')} />

      <button type="submit" disabled={submitting}>
        {submitting ? 'Saving…' : submitLabel}
      </button>
    </form>
  );
}
