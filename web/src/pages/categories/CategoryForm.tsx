import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';

import { Button } from '../../components/Button';
import { FormCard } from '../../components/form/FormCard';
import { CheckboxField, TextField } from '../../components/form/fields';
import { categoryFormSchema } from './categoryModels';
import type { CategoryFormValues } from './categoryModels';

interface CategoryFormProps {
  initialValues: CategoryFormValues;
  submitLabel: string;
  submitting: boolean;
  errorMessage?: string | undefined;
  onSubmit: (values: CategoryFormValues) => void;
}

export function CategoryForm({
  initialValues,
  submitLabel,
  submitting,
  errorMessage,
  onSubmit,
}: CategoryFormProps): JSX.Element {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<CategoryFormValues>({
    resolver: zodResolver(categoryFormSchema),
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

      <FormCard title="Category">
        <TextField label="Name" error={errors.nameEn?.message} {...register('nameEn')} />
        <TextField
          label="Name in Ukrainian"
          hint="Printed on the bilingual label. May stay empty until it is translated."
          error={errors.nameUk?.message}
          {...register('nameUk')}
        />
      </FormCard>

      <FormCard title="Handling">
        <TextField
          label="Hazard class"
          hint="The ADR class from 1 to 9. Leave blank for goods that are not dangerous."
          inputMode="numeric"
          error={errors.hazardClass?.message}
          {...register('hazardClass')}
        />
        <CheckboxField
          label="Sensitive goods"
          error={errors.isSensitive?.message}
          {...register('isSensitive')}
        />
        <CheckboxField
          label="Not carried on a convoy"
          error={errors.isNotCarried?.message}
          {...register('isNotCarried')}
        />
        <TextField
          label="Short shelf life within (days)"
          hint="An item expiring within this many days is flagged. Leave blank for no warning. The thresholds are unverified."
          inputMode="numeric"
          error={errors.warnWithinDays?.message}
          {...register('warnWithinDays')}
        />
      </FormCard>

      <Button type="submit" disabled={submitting}>
        {submitting ? 'Saving…' : submitLabel}
      </Button>
    </form>
  );
}
