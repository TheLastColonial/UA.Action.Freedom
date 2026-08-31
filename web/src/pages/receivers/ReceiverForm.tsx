import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';

import { TextField } from '../../components/form/fields';
import { receiverFormSchema } from './receiverModels';
import type { ReceiverFormValues } from './receiverModels';

interface ReceiverFormProps {
  initialValues: ReceiverFormValues;
  submitLabel: string;
  submitting: boolean;
  errorMessage?: string | undefined;
  onSubmit: (values: ReceiverFormValues) => void;
}

export function ReceiverForm({
  initialValues,
  submitLabel,
  submitting,
  errorMessage,
  onSubmit,
}: ReceiverFormProps): JSX.Element {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ReceiverFormValues>({
    resolver: zodResolver(receiverFormSchema),
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
        label="Organisation"
        error={errors.organisation?.message}
        {...register('organisation')}
      />
      <TextField
        label="Region"
        hint="Region-level only — as precise as anything that crosses a border gets."
        error={errors.region?.message}
        {...register('region')}
      />

      <button type="submit" disabled={submitting}>
        {submitting ? 'Saving…' : submitLabel}
      </button>
    </form>
  );
}
