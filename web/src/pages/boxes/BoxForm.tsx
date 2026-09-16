import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';

import { Button } from '../../components/Button';
import { FormCard } from '../../components/form/FormCard';
import { TextField } from '../../components/form/fields';
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
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<BoxFormValues>({
    resolver: zodResolver(boxFormSchema),
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

      <FormCard title="Receiver">
        <TextField
          label="Receiver reference"
          hint="The receiver's opaque reference, if known."
          error={errors.receiverRef?.message}
          {...register('receiverRef')}
        />
      </FormCard>

      <FormCard title="Destination">
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
