import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import { useManifestEns, useRecordManifestEns, useWithdrawManifestEns } from '../../api/manifests';
import { ApiDomainProblem } from '../../api/problem';
import type { EnsDeclarationReadModel, RecordEnsRequest } from '../../api/schemas/manifests';
import { Button } from '../../components/Button';
import { Gate } from '../../components/Gate';
import { TextField } from '../../components/form/fields';
import { Spinner } from '../../components/Spinner';

// Mirrors EnsMrn.IsWellFormed — the shape only, not the check character. Verifying that wrongly
// would refuse an MRN ICS2 has already issued, stranding a convoy over a bug of ours.
const MRN_SHAPE = /^[0-9]{2}[A-Z]{2}[0-9A-Z]{14}$/;

const ensFormSchema = z.object({
  mrn: z
    .string()
    .trim()
    .toUpperCase()
    .refine((value) => MRN_SHAPE.test(value), {
      message:
        "An ICS2 MRN is eighteen characters: two digits of year, the country's ISO code, then " +
        'thirteen characters of reference and a check character. For example 25FR17551780961AT5.',
    }),
  acceptedAt: z.string().min(1, 'Accepted at is required'),
  filedBy: z.string().trim().min(1, 'Filed by is required').max(200),
  filingReference: z.string().trim().max(100),
});

type EnsFormValues = z.infer<typeof ensFormSchema>;

function emptyFormValues(): EnsFormValues {
  return { mrn: '', acceptedAt: '', filedBy: '', filingReference: '' };
}

// AcceptedAt is a DateTimeOffset on the wire; the form only asks for a date and time, so the
// entered value is treated as UTC — the same convention the ICS2 filing sheet composes it under.
function toRequest(values: EnsFormValues): RecordEnsRequest {
  const request: RecordEnsRequest = {
    mrn: values.mrn,
    acceptedAt: `${values.acceptedAt}:00+00:00`,
    filedBy: values.filedBy,
  };
  return values.filingReference === ''
    ? request
    : { ...request, filingReference: values.filingReference };
}

function EnsStatus({ declaration }: { declaration: EnsDeclarationReadModel | null }): JSX.Element {
  if (declaration === null) {
    return (
      <p role="status">
        No ICS2 Entry Summary Declaration recorded. Approval will not proceed without one.
      </p>
    );
  }
  return (
    <p role="status">
      Declared under MRN {declaration.mrn}, filed by {declaration.filedBy}.
    </p>
  );
}

interface ManifestEnsPanelProps {
  manifestId: string;
}

/**
 * The ICS2 Entry Summary Declaration a crossing was accepted under. Freedom does not submit an
 * ENS — a Ground Officer files it in the EU Customs Trader Portal — so this only records the MRN
 * that comes back (docs/adr/0003). Approval is refused without one.
 */
export function ManifestEnsPanel({ manifestId }: ManifestEnsPanelProps): JSX.Element {
  const query = useManifestEns(manifestId);

  if (query.isPending) {
    return <Spinner label="Loading declaration…" />;
  }
  if (query.isError) {
    return <p role="alert">The ICS2 declaration could not be loaded.</p>;
  }

  return (
    <section aria-label="ICS2 declaration">
      <h2>ICS2 declaration</h2>
      <EnsStatus declaration={query.data} />
      <Gate policy="manifests:declare">
        {query.data === null ? (
          <EnsForm manifestId={manifestId} />
        ) : (
          <WithdrawButton manifestId={manifestId} />
        )}
      </Gate>
    </section>
  );
}

function WithdrawButton({ manifestId }: { manifestId: string }): JSX.Element {
  const withdraw = useWithdrawManifestEns(manifestId);
  const errorMessage =
    withdraw.error instanceof ApiDomainProblem
      ? (withdraw.error.detail ?? withdraw.error.message)
      : undefined;

  return (
    <div>
      {errorMessage ? (
        <p role="alert" className="field__error">
          {errorMessage}
        </p>
      ) : null}
      <Button
        variant="danger"
        disabled={withdraw.isPending}
        onClick={() => {
          withdraw.mutate();
        }}
      >
        Withdraw declaration
      </Button>
    </div>
  );
}

function EnsForm({ manifestId }: { manifestId: string }): JSX.Element {
  const record = useRecordManifestEns(manifestId);
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<EnsFormValues>({
    resolver: zodResolver(ensFormSchema),
    defaultValues: emptyFormValues(),
  });
  const errorMessage =
    record.error instanceof ApiDomainProblem
      ? (record.error.detail ?? record.error.message)
      : record.error instanceof Error
        ? record.error.message
        : undefined;

  return (
    <form
      noValidate
      aria-label="Record ICS2 declaration"
      onSubmit={(event) => {
        void handleSubmit((values) => {
          record.mutate(toRequest(values));
        })(event);
      }}
    >
      <TextField label="MRN" error={errors.mrn?.message} {...register('mrn')} />
      <TextField
        label="Accepted at"
        type="datetime-local"
        error={errors.acceptedAt?.message}
        {...register('acceptedAt')}
      />
      <TextField label="Filed by" error={errors.filedBy?.message} {...register('filedBy')} />
      <TextField
        label="Filing reference"
        error={errors.filingReference?.message}
        {...register('filingReference')}
      />
      {errorMessage ? (
        <p role="alert" className="field__error">
          {errorMessage}
        </p>
      ) : null}
      <Button type="submit" disabled={record.isPending}>
        {record.isPending ? 'Saving…' : 'Record declaration'}
      </Button>
    </form>
  );
}
