import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';

import { useBays, useLocations } from '../../api/locations';
import { usePeople } from '../../api/people';
import { Button } from '../../components/Button';
import { Gate } from '../../components/Gate';
import { FormCard } from '../../components/form/FormCard';
import { SelectField, TextField } from '../../components/form/fields';
import { boxFormSchema } from './boxModels';
import type { BoxFormValues } from './boxModels';

interface BoxFormProps {
  initialValues: BoxFormValues;
  submitLabel: string;
  submitting: boolean;
  errorMessage?: string | undefined;
  // Only offered on the create page — a Loader who already knows the bay can place the box in
  // it there and then, rather than making a second trip to the detail page. Bay changes for an
  // existing box stay on BoxBayPanel, which already handles a box moving between locations.
  enableBayAssignment?: boolean;
  onSubmit: (values: BoxFormValues) => void;
}

export function BoxForm({
  initialValues,
  submitLabel,
  submitting,
  errorMessage,
  enableBayAssignment = false,
  onSubmit,
}: BoxFormProps): JSX.Element {
  const locations = useLocations({ page: 1, pageSize: 200 });

  const {
    register,
    handleSubmit,
    watch,
    formState: { errors },
  } = useForm<BoxFormValues>({
    resolver: zodResolver(boxFormSchema),
    defaultValues: initialValues,
  });

  const watchedLocationId = watch('locationId');
  const locationId = watchedLocationId.trim().length > 0 ? Number(watchedLocationId) : null;
  const bays = useBays(locationId ?? -1, { enabled: enableBayAssignment && locationId !== null });
  const volunteers = usePeople({ page: 1, pageSize: 200 });

  const locationOptions = [
    { value: '', label: 'Not yet at a depot' },
    ...(locations.data ?? []).map((location) => ({
      value: String(location.id),
      label: location.name,
    })),
  ];

  const availableBays = bays.data && !('parentMissing' in bays.data) ? bays.data : [];
  const bayOptions = [
    { value: '', label: 'Select a bay…' },
    ...availableBays.map((bay) => ({ value: String(bay.id), label: bay.code })),
  ];
  const volunteerOptions = [
    { value: '', label: 'Select a volunteer…' },
    ...(volunteers.data ?? []).map((person) => ({
      value: person.id,
      label: `${person.firstName} ${person.lastName}`,
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

      {enableBayAssignment && locationId !== null ? (
        <Gate policy="boxes:allocate-bay">
          <FormCard title="Bay (optional)">
            <SelectField
              label="Bay"
              hint="Only if you already know where this box is going — it can always be placed later."
              options={bayOptions}
              error={errors.bayId?.message}
              {...register('bayId')}
            />
            <SelectField
              label="Placed by"
              options={volunteerOptions}
              error={errors.assignedByPersonId?.message}
              {...register('assignedByPersonId')}
            />
          </FormCard>
        </Gate>
      ) : null}

      <Button type="submit" disabled={submitting}>
        {submitting ? 'Saving…' : submitLabel}
      </Button>
    </form>
  );
}
