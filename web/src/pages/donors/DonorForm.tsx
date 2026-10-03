import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import type { FieldPath } from 'react-hook-form';

import { problemFieldToFormPath } from '../../api/problem';
import { Button } from '../../components/Button';
import { FormCard } from '../../components/form/FormCard';
import { TextField } from '../../components/form/fields';
import { donorFormSchema } from './donorFormModel';
import type { DonorFormValues } from './donorFormModel';

const FORM_FIELDS = new Set<string>(['name', 'email', 'phone']);

interface DonorFormProps {
  initialValues: DonorFormValues;
  submitLabel: string;
  submitting: boolean;
  errorMessage?: string | undefined;
  fieldErrors?: Readonly<Record<string, readonly string[]>> | undefined;
  onSubmit: (values: DonorFormValues) => void;
}

export function DonorForm({
  initialValues,
  submitLabel,
  submitting,
  errorMessage,
  fieldErrors,
  onSubmit,
}: DonorFormProps): JSX.Element {
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<DonorFormValues>({
    resolver: zodResolver(donorFormSchema),
    defaultValues: initialValues,
  });

  useEffect(() => {
    if (!fieldErrors) {
      return;
    }
    for (const [field, messages] of Object.entries(fieldErrors)) {
      const path = problemFieldToFormPath(field);
      if (FORM_FIELDS.has(path) && messages[0] !== undefined) {
        setError(path as FieldPath<DonorFormValues>, { type: 'server', message: messages[0] });
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

      <FormCard title="Donor details">
        <TextField label="Name" error={errors.name?.message} {...register('name')} />
        <TextField
          label="Email"
          type="email"
          error={errors.email?.message}
          {...register('email')}
        />
        <TextField label="Phone" error={errors.phone?.message} {...register('phone')} />
      </FormCard>

      <Button type="submit" disabled={submitting}>
        {submitting ? 'Saving…' : submitLabel}
      </Button>
    </form>
  );
}
