import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import { inspectionStatusSchema } from '../../api/schemas/vehicles';
import type { InspectionStatus, RecordInspectionRequest } from '../../api/schemas/vehicles';
import { Button } from '../../components/Button';
import { FormCard } from '../../components/form/FormCard';
import { SelectField, TextareaField } from '../../components/form/fields';
import { INSPECTION_STATUS_LABELS } from './inspection';

const servicingFormSchema = z.object({
  inspectionStatus: inspectionStatusSchema,
  inspectionNotes: z.string().max(2000, 'Notes must not exceed 2000 characters'),
});

type ServicingFormValues = z.infer<typeof servicingFormSchema>;

interface VehicleServicingFormProps {
  vin: string;
  initialStatus: InspectionStatus;
  initialNotes: string | null;
  submitting: boolean;
  errorMessage?: string | undefined;
  onSubmit: (body: RecordInspectionRequest) => void;
}

function toRequest(values: ServicingFormValues): RecordInspectionRequest {
  const notes = values.inspectionNotes.trim();
  return notes ? { status: values.inspectionStatus, notes } : { status: values.inspectionStatus };
}

export function VehicleServicingForm({
  vin,
  initialStatus,
  initialNotes,
  submitting,
  errorMessage,
  onSubmit,
}: VehicleServicingFormProps): JSX.Element {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ServicingFormValues>({
    resolver: zodResolver(servicingFormSchema),
    defaultValues: {
      inspectionStatus: initialStatus,
      inspectionNotes: initialNotes ?? '',
    },
  });

  return (
    <form
      noValidate
      onSubmit={(event) => {
        void handleSubmit((values) => {
          onSubmit(toRequest(values));
        })(event);
      }}
    >
      {errorMessage ? (
        <p role="alert" className="field__error">
          {errorMessage}
        </p>
      ) : null}

      <FormCard title={`Vehicle Inspection — ${vin}`}>
        <SelectField
          label="Inspection status"
          error={errors.inspectionStatus?.message}
          options={inspectionStatusSchema.options.map((status) => ({
            value: status,
            label: `${status} — ${INSPECTION_STATUS_LABELS[status]}`,
          }))}
          {...register('inspectionStatus')}
        />
        <TextareaField
          label="Inspection notes (defects/issues)"
          placeholder="Record any defects, damage, or issues found during inspection…"
          error={errors.inspectionNotes?.message}
          rows={5}
          {...register('inspectionNotes')}
        />
      </FormCard>

      <Button type="submit" disabled={submitting}>
        {submitting ? 'Saving…' : 'Save inspection'}
      </Button>
    </form>
  );
}
