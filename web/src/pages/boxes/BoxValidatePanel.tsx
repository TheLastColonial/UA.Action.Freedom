import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';

import { useValidateBox } from '../../api/boxes';
import { usePeople } from '../../api/people';
import { ApiDomainProblem, ApiNotFound } from '../../api/problem';
import { Button } from '../../components/Button';
import { DetailCard } from '../../components/DetailCard';
import { SelectField, TextField } from '../../components/form/fields';
import { emptyValidateForm, validateFormSchema, validateFormToRequest } from './boxModels';
import type { ValidateFormValues } from './boxModels';

interface BoxValidatePanelProps {
  boxId: number;
}

export function BoxValidatePanel({ boxId }: BoxValidatePanelProps): JSX.Element {
  const volunteers = usePeople({ page: 1, pageSize: 200 });
  const validate = useValidateBox(boxId);

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ValidateFormValues>({
    resolver: zodResolver(validateFormSchema),
    defaultValues: emptyValidateForm(),
  });

  const message =
    validate.error instanceof ApiNotFound
      ? 'The volunteer named as having checked this box is not on file.'
      : validate.error instanceof ApiDomainProblem
        ? (validate.error.detail ?? validate.error.message)
        : undefined;

  const options = [
    { value: '', label: 'Select a volunteer…' },
    ...(volunteers.data ?? []).map((person) => ({
      value: person.id,
      label: `${person.firstName} ${person.lastName}`,
    })),
  ];

  return (
    <DetailCard title="Validate this box">
      <p>Confirming the contents and weight freezes the box.</p>
      <form
        noValidate
        onSubmit={(event) => {
          void handleSubmit((values) => {
            validate.mutate(validateFormToRequest(values));
          })(event);
        }}
      >
        {message ? (
          <p role="alert" className="field__error">
            {message}
          </p>
        ) : null}

        <SelectField
          label="Checked by"
          options={options}
          error={errors.validatedByPersonId?.message}
          {...register('validatedByPersonId')}
        />
        <TextField
          label="Confirmed weight (kg)"
          type="number"
          inputMode="numeric"
          error={errors.weightKg?.message}
          {...register('weightKg')}
        />
        <TextField
          label="Width (cm)"
          type="number"
          inputMode="decimal"
          step="0.01"
          error={errors.widthCm?.message}
          {...register('widthCm')}
        />
        <TextField
          label="Depth (cm)"
          type="number"
          inputMode="decimal"
          step="0.01"
          error={errors.depthCm?.message}
          {...register('depthCm')}
        />
        <TextField
          label="Height (cm)"
          type="number"
          inputMode="decimal"
          step="0.01"
          error={errors.heightCm?.message}
          {...register('heightCm')}
        />

        <Button type="submit" disabled={validate.isPending}>
          Validate box
        </Button>
      </form>
    </DetailCard>
  );
}
