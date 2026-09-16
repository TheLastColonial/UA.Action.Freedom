import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import type { FieldPath } from 'react-hook-form';

import { problemFieldToFormPath } from '../../api/problem';
import { Button } from '../../components/Button';
import { FormCard } from '../../components/form/FormCard';
import { CheckboxField, TextField } from '../../components/form/fields';
import { personFormSchema } from './personFormModel';
import type { PersonFormValues } from './personFormModel';

const FORM_FIELDS = new Set<string>([
  'firstName',
  'lastName',
  'dateOfBirth',
  'joined',
  'phone',
  'isDriver',
  'committed',
]);

interface PersonFormProps {
  initialValues: PersonFormValues;
  submitLabel: string;
  submitting: boolean;
  errorMessage?: string | undefined;
  fieldErrors?: Readonly<Record<string, readonly string[]>> | undefined;
  onSubmit: (values: PersonFormValues) => void;
}

export function PersonForm({
  initialValues,
  submitLabel,
  submitting,
  errorMessage,
  fieldErrors,
  onSubmit,
}: PersonFormProps): JSX.Element {
  const {
    register,
    handleSubmit,
    watch,
    setValue,
    setError,
    formState: { errors },
  } = useForm<PersonFormValues>({
    resolver: zodResolver(personFormSchema),
    defaultValues: initialValues,
  });

  const isDriver = watch('isDriver');

  useEffect(() => {
    if (!isDriver) {
      setValue('committed', false);
    }
  }, [isDriver, setValue]);

  useEffect(() => {
    if (!fieldErrors) {
      return;
    }
    for (const [field, messages] of Object.entries(fieldErrors)) {
      const path = problemFieldToFormPath(field);
      if (FORM_FIELDS.has(path) && messages[0] !== undefined) {
        setError(path as FieldPath<PersonFormValues>, { type: 'server', message: messages[0] });
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

      <FormCard title="Personal details">
        <TextField label="First name" error={errors.firstName?.message} {...register('firstName')} />
        <TextField label="Last name" error={errors.lastName?.message} {...register('lastName')} />
        <TextField
          label="Date of birth"
          type="date"
          error={errors.dateOfBirth?.message}
          {...register('dateOfBirth')}
        />
        <TextField label="Phone" error={errors.phone?.message} {...register('phone')} />
      </FormCard>

      <FormCard title="Volunteering">
        <TextField
          label="Joined"
          type="date"
          error={errors.joined?.message}
          {...register('joined')}
        />
        <CheckboxField label="Volunteers to drive" {...register('isDriver')} />
        <CheckboxField
          label="Committed to a convoy"
          disabled={!isDriver}
          error={errors.committed?.message}
          {...register('committed')}
        />
      </FormCard>

      <Button type="submit" disabled={submitting}>
        {submitting ? 'Saving…' : submitLabel}
      </Button>
    </form>
  );
}
