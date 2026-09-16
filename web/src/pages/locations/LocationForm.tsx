import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';

import { Button } from '../../components/Button';
import { FormCard } from '../../components/form/FormCard';
import { TextField } from '../../components/form/fields';
import { locationFormSchema } from './locationModels';
import type { LocationFormValues } from './locationModels';

interface LocationFormProps {
  initialValues: LocationFormValues;
  submitLabel: string;
  submitting: boolean;
  errorMessage?: string | undefined;
  onSubmit: (values: LocationFormValues) => void;
}

export function LocationForm({
  initialValues,
  submitLabel,
  submitting,
  errorMessage,
  onSubmit,
}: LocationFormProps): JSX.Element {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<LocationFormValues>({
    resolver: zodResolver(locationFormSchema),
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

      <FormCard title="Distribution hub">
        <TextField label="Name" error={errors.name?.message} {...register('name')} />
      </FormCard>

      <FormCard title="Address">
        <TextField label="House" error={errors.house?.message} {...register('house')} />
        <TextField label="Street" error={errors.street?.message} {...register('street')} />
        <TextField label="City" error={errors.city?.message} {...register('city')} />
        <TextField label="Country" error={errors.country?.message} {...register('country')} />
        <TextField label="Postcode" error={errors.postcode?.message} {...register('postcode')} />
      </FormCard>

      <Button type="submit" disabled={submitting}>
        {submitting ? 'Saving…' : submitLabel}
      </Button>
    </form>
  );
}
