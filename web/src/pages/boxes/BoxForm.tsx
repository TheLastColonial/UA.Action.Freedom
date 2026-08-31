import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';

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

      <TextField
        label="Receiver reference"
        hint="The receiver's opaque reference, if known."
        error={errors.receiverRef?.message}
        {...register('receiverRef')}
      />
      <TextField label="House" error={errors.house?.message} {...register('house')} />
      <TextField label="Street" error={errors.street?.message} {...register('street')} />
      <TextField label="City" error={errors.city?.message} {...register('city')} />
      <TextField label="Country" error={errors.country?.message} {...register('country')} />
      <TextField label="Postcode" error={errors.postcode?.message} {...register('postcode')} />

      <button type="submit" disabled={submitting}>
        {submitting ? 'Saving…' : submitLabel}
      </button>
    </form>
  );
}
