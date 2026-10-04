import { zodResolver } from '@hookform/resolvers/zod';
import { useState } from 'react';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import {
  useDeclarations,
  useEns,
  useFileDeclaration,
  useRecordDeclaration,
  useRecordEns,
  useRefuseDeclaration,
  useWithdrawDeclaration,
  useWithdrawEns,
} from '../../api/declarations';
import { ApiDomainProblem } from '../../api/problem';
import { REFUSAL_REASONS } from '../../api/schemas/declarations';
import type {
  DeclarationKind,
  DeclarationReadModel,
  RecordEnsRequest,
  RefusalReason,
} from '../../api/schemas/declarations';
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

// AcceptedAt is a DateTimeOffset on the wire; the form only asks for a date and time, so the
// entered value is treated as UTC — the same convention the ICS2 filing sheet composes it under.
function toEnsRequest(values: EnsFormValues): RecordEnsRequest {
  const request: RecordEnsRequest = {
    mrn: values.mrn,
    acceptedAt: `${values.acceptedAt}:00+00:00`,
    filedBy: values.filedBy,
  };
  return values.filingReference === ''
    ? request
    : { ...request, filingReference: values.filingReference };
}

function messageOf(error: Error | null): string | undefined {
  if (error === null) {
    return undefined;
  }
  return error instanceof ApiDomainProblem ? (error.detail ?? error.message) : error.message;
}

function ErrorText({ error }: { error: Error | null }): JSX.Element | null {
  const message = messageOf(error);
  return message ? (
    <p role="alert" className="field__error">
      {message}
    </p>
  ) : null;
}

const KIND_LABEL: Record<DeclarationKind, string> = {
  Gmr: 'GMR',
  Ens: 'ENS',
  Elo: 'ELO',
  GoodsList: 'Goods list',
};

function describe(declaration: DeclarationReadModel | undefined, label: string): string {
  if (declaration === undefined) {
    return `${label}: not filed.`;
  }
  const reference = declaration.reference === null ? '' : ` — reference ${declaration.reference}`;
  const reason = declaration.reasonCode === null ? '' : ` (${declaration.reasonCode})`;
  return `${label}: ${declaration.status}${reference}${reason}.`;
}

function isOpen(declaration: DeclarationReadModel | undefined): boolean {
  return (
    declaration === undefined ||
    declaration.status === 'Draft' ||
    declaration.status === 'Refused' ||
    (declaration.status === 'Filed' && declaration.reference === null)
  );
}

interface PanelProps {
  convoyId: number;
  vin: string;
}

/**
 * A vehicle's customs declarations (ADR 0005, ADR 0006): one row per instrument, and a row per receiver
 * for the Ukrainian goods lists. Filing is manual by default — a Dispatcher records the reference they
 * obtained in the authority's portal — and the file button only works where an authority's submission
 * mode is automatic. Approval signs off the load and files none of this.
 */
export function DeclarationsPanel({ convoyId, vin }: PanelProps): JSX.Element {
  const declarations = useDeclarations(convoyId, vin);

  if (declarations.isPending) {
    return <Spinner label="Loading declarations…" />;
  }
  if (declarations.isError) {
    return <p role="alert">The declarations could not be loaded.</p>;
  }

  const current = declarations.data.filter((declaration) => declaration.status !== 'Withdrawn');
  const goodsLists = current.filter((declaration) => declaration.kind === 'GoodsList');

  return (
    <section aria-label="Declarations">
      <h2>Declarations</h2>
      <EnsSection convoyId={convoyId} vin={vin} />
      <ReferenceSection
        convoyId={convoyId}
        vin={vin}
        kind="Gmr"
        declaration={current.find((declaration) => declaration.kind === 'Gmr')}
      />
      <ReferenceSection
        convoyId={convoyId}
        vin={vin}
        kind="Elo"
        declaration={current.find((declaration) => declaration.kind === 'Elo')}
      />
      <section aria-label="Goods lists">
        <h3>Ukrainian goods lists</h3>
        {goodsLists.length === 0 ? <p role="status">No goods list recorded.</p> : null}
        {goodsLists.map((declaration) => (
          <ReferenceSection
            key={declaration.id}
            convoyId={convoyId}
            vin={vin}
            kind="GoodsList"
            declaration={declaration}
          />
        ))}
        <GoodsListForm convoyId={convoyId} vin={vin} />
      </section>
    </section>
  );
}

function ReferenceSection({
  convoyId,
  vin,
  kind,
  declaration,
}: PanelProps & {
  kind: DeclarationKind;
  declaration: DeclarationReadModel | undefined;
}): JSX.Element {
  const label = KIND_LABEL[kind];
  const receiverRef = declaration?.receiverRef ?? undefined;
  const heading = receiverRef === undefined ? label : `${label} for ${receiverRef}`;

  return (
    <section aria-label={heading}>
      <h3>{heading}</h3>
      <p role="status">{describe(declaration, heading)}</p>
      <Gate policy="manifests:declare">
        {isOpen(declaration) ? (
          <RecordForm
            convoyId={convoyId}
            vin={vin}
            kind={kind}
            label={heading}
            receiverRef={receiverRef}
          />
        ) : null}
        {declaration?.status === 'Stale' ? (
          <StaleNotice
            convoyId={convoyId}
            vin={vin}
            declarationId={declaration.id}
            label={heading}
          />
        ) : null}
        {declaration?.status === 'Filed' ? (
          <RefuseForm
            convoyId={convoyId}
            vin={vin}
            kind={kind}
            label={heading}
            receiverRef={receiverRef}
          />
        ) : null}
        {kind === 'Gmr' || kind === 'Elo' ? (
          <FileButton convoyId={convoyId} vin={vin} kind={kind} label={heading} />
        ) : null}
      </Gate>
    </section>
  );
}

/**
 * The load changed after this was written (ADR 0005). Nobody flags it: the API says so by reading
 * Stale. Withdrawing keeps the record and its reference as history and starts a fresh draft.
 */
function StaleNotice({
  convoyId,
  vin,
  declarationId,
  label,
}: PanelProps & { declarationId: number; label: string }): JSX.Element {
  const withdraw = useWithdrawDeclaration(convoyId, vin);

  return (
    <div role="alert" className="field__error">
      <strong>Stale</strong>
      <span>{` — the load has changed since ${label} was filed.`}</span>
      <ErrorText error={withdraw.error} />
      <Button
        type="button"
        variant="secondary"
        disabled={withdraw.isPending}
        onClick={() => {
          withdraw.mutate(declarationId);
        }}
      >
        {`Withdraw ${label} and re-declare`}
      </Button>
    </div>
  );
}

function RecordForm({
  convoyId,
  vin,
  kind,
  label,
  receiverRef,
}: PanelProps & {
  kind: DeclarationKind;
  label: string;
  receiverRef: string | undefined;
}): JSX.Element {
  const record = useRecordDeclaration(convoyId, vin);
  const [reference, setReference] = useState('');

  return (
    <form
      noValidate
      aria-label={`Record ${label}`}
      onSubmit={(event) => {
        event.preventDefault();
        record.mutate({
          kind,
          body: { reference: reference.trim(), ...(receiverRef ? { receiverRef } : {}) },
        });
      }}
    >
      <TextField
        label={`${label} reference`}
        value={reference}
        onChange={(event) => {
          setReference(event.target.value);
        }}
      />
      <ErrorText error={record.error} />
      <Button type="submit" disabled={record.isPending || reference.trim() === ''}>
        {`Record ${label} reference`}
      </Button>
    </form>
  );
}

function RefuseForm({
  convoyId,
  vin,
  kind,
  label,
  receiverRef,
}: PanelProps & {
  kind: DeclarationKind;
  label: string;
  receiverRef: string | undefined;
}): JSX.Element {
  const refuse = useRefuseDeclaration(convoyId, vin);
  const [reasonCode, setReasonCode] = useState<RefusalReason>('data-error');

  return (
    <form
      aria-label={`Refuse ${label}`}
      onSubmit={(event) => {
        event.preventDefault();
        refuse.mutate({ kind, body: { reasonCode, ...(receiverRef ? { receiverRef } : {}) } });
      }}
    >
      <label>
        {`${label} refusal reason`}
        <select
          value={reasonCode}
          onChange={(event) => {
            setReasonCode(event.target.value as RefusalReason);
          }}
        >
          {REFUSAL_REASONS.map((reason) => (
            <option key={reason} value={reason}>
              {reason}
            </option>
          ))}
        </select>
      </label>
      <ErrorText error={refuse.error} />
      <Button type="submit" variant="danger" disabled={refuse.isPending}>
        {`Mark ${label} refused`}
      </Button>
    </form>
  );
}

function FileButton({
  convoyId,
  vin,
  kind,
  label,
}: PanelProps & { kind: DeclarationKind; label: string }): JSX.Element {
  const file = useFileDeclaration(convoyId, vin);

  return (
    <div>
      <ErrorText error={file.error} />
      <Button
        variant="secondary"
        disabled={file.isPending}
        onClick={() => {
          file.mutate(kind);
        }}
      >
        {`File ${label} automatically`}
      </Button>
    </div>
  );
}

function GoodsListForm({ convoyId, vin }: PanelProps): JSX.Element {
  const record = useRecordDeclaration(convoyId, vin);
  const [receiverRef, setReceiverRef] = useState('');
  const [reference, setReference] = useState('');
  const incomplete = receiverRef.trim() === '' || reference.trim() === '';

  return (
    <Gate policy="manifests:declare">
      <form
        noValidate
        aria-label="Record goods list"
        onSubmit={(event) => {
          event.preventDefault();
          record.mutate({
            kind: 'GoodsList',
            body: { reference: reference.trim(), receiverRef: receiverRef.trim() },
          });
        }}
      >
        <TextField
          label="Receiver"
          value={receiverRef}
          onChange={(event) => {
            setReceiverRef(event.target.value);
          }}
        />
        <TextField
          label="Goods list reference"
          value={reference}
          onChange={(event) => {
            setReference(event.target.value);
          }}
        />
        <ErrorText error={record.error} />
        <Button type="submit" disabled={record.isPending || incomplete}>
          Record goods list
        </Button>
      </form>
    </Gate>
  );
}

function EnsSection({ convoyId, vin }: PanelProps): JSX.Element {
  const query = useEns(convoyId, vin);

  if (query.isPending) {
    return <Spinner label="Loading ENS…" />;
  }
  if (query.isError) {
    return <p role="alert">The ICS2 declaration could not be loaded.</p>;
  }

  return (
    <section aria-label="ICS2 declaration">
      <h3>ICS2 declaration (ENS)</h3>
      {query.data === null ? (
        <p role="status">
          No ICS2 Entry Summary Declaration recorded. The ELO is created from it, so it comes first.
        </p>
      ) : (
        <p role="status">
          Declared under MRN {query.data.mrn}, filed by {query.data.filedBy}.
        </p>
      )}
      <Gate policy="manifests:declare">
        {query.data === null ? (
          <EnsForm convoyId={convoyId} vin={vin} />
        ) : (
          <WithdrawEnsButton convoyId={convoyId} vin={vin} />
        )}
      </Gate>
    </section>
  );
}

function WithdrawEnsButton({ convoyId, vin }: PanelProps): JSX.Element {
  const withdraw = useWithdrawEns(convoyId, vin);

  return (
    <div>
      <ErrorText error={withdraw.error} />
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

function EnsForm({ convoyId, vin }: PanelProps): JSX.Element {
  const record = useRecordEns(convoyId, vin);
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<EnsFormValues>({
    resolver: zodResolver(ensFormSchema),
    defaultValues: { mrn: '', acceptedAt: '', filedBy: '', filingReference: '' },
  });

  return (
    <form
      noValidate
      aria-label="Record ICS2 declaration"
      onSubmit={(event) => {
        void handleSubmit((values) => {
          record.mutate(toEnsRequest(values));
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
      <ErrorText error={record.error} />
      <Button type="submit" disabled={record.isPending}>
        {record.isPending ? 'Saving…' : 'Record declaration'}
      </Button>
    </form>
  );
}
