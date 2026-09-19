import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import { useRecordInsurance, useVehicleInsurance } from '../../api/convoys';
import type { RecordInsuranceRequest, VehicleInsuranceReadModel } from '../../api/schemas/convoys';
import { Button } from '../../components/Button';
import { Gate } from '../../components/Gate';
import { TextField } from '../../components/form/fields';

const insuranceFormSchema = z
  .object({
    insurer: z.string().trim().min(1, 'Insurer is required').max(200),
    policyNumber: z.string().trim().min(1, 'Policy number is required').max(100),
    coverStart: z.string().min(1, 'Cover start is required'),
    coverEnd: z.string().min(1, 'Cover end is required'),
    costGbp: z
      .string()
      .refine((value) => value.trim() === '' || Number(value) >= 0, 'Cost cannot be negative'),
  })
  .refine((values) => values.coverEnd >= values.coverStart, {
    message: 'Cover cannot end before it starts',
    path: ['coverEnd'],
  });

type InsuranceFormValues = z.infer<typeof insuranceFormSchema>;

const day = (iso: string) => iso.slice(0, 10);

function toRequest(values: InsuranceFormValues): RecordInsuranceRequest {
  const request: RecordInsuranceRequest = {
    insurer: values.insurer.trim(),
    policyNumber: values.policyNumber.trim(),
    coverStart: values.coverStart,
    coverEnd: values.coverEnd,
  };
  return values.costGbp.trim() === '' ? request : { ...request, costGbp: Number(values.costGbp) };
}

function toFormValues(policy: VehicleInsuranceReadModel | null): InsuranceFormValues {
  return {
    insurer: policy?.insurer ?? '',
    policyNumber: policy?.policyNumber ?? '',
    coverStart: policy ? day(policy.coverStart) : '',
    coverEnd: policy ? day(policy.coverEnd) : '',
    costGbp: policy?.costGbp === null || policy === null ? '' : String(policy.costGbp),
  };
}

function InsuranceStatus({ policy }: { policy: VehicleInsuranceReadModel | null }): JSX.Element {
  if (policy === null) {
    return <p role="status">Insurance not recorded</p>;
  }
  if (policy.voided) {
    return (
      <p role="status">
        Policy {policy.policyNumber} was voided by a crew change — record it again before the
        vehicle departs.
      </p>
    );
  }
  return (
    <p role="status">
      Insured with {policy.insurer}, policy {policy.policyNumber}, cover {day(policy.coverStart)} to{' '}
      {day(policy.coverEnd)}.
    </p>
  );
}

interface VehicleInsurancePanelProps {
  convoyId: number;
  vin: string;
  plate: string;
}

/**
 * A vehicle's insurance for this convoy. It names the crew, so a crew change voids it; a
 * manifest cannot depart without it recorded, not voided, and in cover.
 */
export function VehicleInsurancePanel({
  convoyId,
  vin,
  plate,
}: VehicleInsurancePanelProps): JSX.Element {
  const query = useVehicleInsurance(convoyId, vin);

  if (query.isPending) {
    return <p>Loading insurance…</p>;
  }
  if (query.isError) {
    return <p role="alert">The insurance for {plate} could not be loaded.</p>;
  }

  return (
    <div>
      <InsuranceStatus policy={query.data} />
      <Gate policy="convoys:write">
        <InsuranceForm
          convoyId={convoyId}
          vin={vin}
          plate={plate}
          initialValues={toFormValues(query.data)}
        />
      </Gate>
    </div>
  );
}

function InsuranceForm({
  convoyId,
  vin,
  plate,
  initialValues,
}: VehicleInsurancePanelProps & { initialValues: InsuranceFormValues }): JSX.Element {
  const record = useRecordInsurance(convoyId, vin);
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<InsuranceFormValues>({
    resolver: zodResolver(insuranceFormSchema),
    defaultValues: initialValues,
  });

  return (
    <form
      noValidate
      aria-label={`Insurance for ${plate}`}
      onSubmit={(event) => {
        void handleSubmit((values) => {
          record.mutate(toRequest(values));
        })(event);
      }}
    >
      <TextField label="Insurer" error={errors.insurer?.message} {...register('insurer')} />
      <TextField
        label="Policy number"
        error={errors.policyNumber?.message}
        {...register('policyNumber')}
      />
      <TextField
        label="Cover starts"
        type="date"
        error={errors.coverStart?.message}
        {...register('coverStart')}
      />
      <TextField
        label="Cover ends"
        type="date"
        error={errors.coverEnd?.message}
        {...register('coverEnd')}
      />
      <TextField
        label="Cost (£)"
        inputMode="decimal"
        error={errors.costGbp?.message}
        {...register('costGbp')}
      />
      {record.isError ? (
        <p role="alert" className="field__error">
          {record.error.message}
        </p>
      ) : null}
      <Button type="submit" disabled={record.isPending}>
        {record.isPending ? 'Saving…' : 'Record insurance'}
      </Button>
    </form>
  );
}
