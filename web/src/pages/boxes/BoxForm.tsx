import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';

import { useLocations } from '../../api/locations';
import { Button } from '../../components/Button';
import { FormCard } from '../../components/form/FormCard';
import { SelectField, TextField } from '../../components/form/fields';
import { boxFormSchema } from './boxModels';
import type { BoxFormValues } from './boxModels';

interface BoxFormProps {
  initialValues: BoxFormValues;
  submitLabel: string;
  submitting: boolean;
  errorMessage?: string | undefined;
  onSubmit: (values: BoxFormValues) => void;
}

export function BoxForm({
  initialValues,
  submitLabel,
  submitting,
  errorMessage,
  onSubmit,
}: BoxFormProps): JSX.Element {
  const locations = useLocations({ page: 1, pageSize: 200 });

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<BoxFormValues>({
    resolver: zodResolver(boxFormSchema),
    defaultValues: initialValues,
  });

  const locationOptions = [
    { value: '', label: 'Not yet at a depot' },
    ...(locations.data ?? []).map((location) => ({
      value: String(location.id),
      label: location.name,
    })),
  ];

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

      <FormCard title="Receiver">
        <TextField
          label="Receiver reference"
          hint="The receiver's opaque reference, if known."
          error={errors.receiverRef?.message}
          {...register('receiverRef')}
        />
      </FormCard>

      <FormCard title="Location">
        <SelectField
          label="Distribution hub"
          hint="Which depot the box is currently at, if it has arrived at one. A Loader places it in a specific bay separately."
          options={locationOptions}
          error={errors.locationId?.message}
          {...register('locationId')}
        />
      </FormCard>

      <Button type="submit" disabled={submitting}>
        {submitting ? 'Saving…' : submitLabel}
      </Button>
    </form>
  );
}
