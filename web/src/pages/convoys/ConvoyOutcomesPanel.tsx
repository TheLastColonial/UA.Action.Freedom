import type { JSX } from 'react';
import { useState } from 'react';

import { useConvoyVehicles, useVehicleBoxes } from '../../api/convoys';
import { useLocations } from '../../api/locations';
import { useBoxOutcomes, useDeliverVehicle, useRecordBoxOutcome } from '../../api/outcomes';
import { ApiDomainProblem } from '../../api/problem';
import type { LocationReadModel } from '../../api/schemas/locations';
import { undeliverableReasons } from '../../api/schemas/outcomes';
import type {
  BoxOutcome,
  BoxOutcomeReadModel,
  UndeliverableReason,
} from '../../api/schemas/outcomes';
import { Button } from '../../components/Button';
import { DataTable } from '../../components/DataTable';
import type { Column } from '../../components/DataTable';
import { Gate } from '../../components/Gate';
import { PageSkeleton } from '../../components/PageSkeleton';
import { Spinner } from '../../components/Spinner';
import { nextOutcomes } from './outcomeModel';

interface ConvoyOutcomesPanelProps {
  convoyId: number;
  departed: boolean;
}

const LABEL: Record<BoxOutcome, string> = {
  Delivered: 'Delivered',
  Accepted: 'Accepted',
  Seized: 'Seized',
  ReturnedToHub: 'Returned to a hub',
  Undeliverable: 'Undeliverable',
};

const problemMessage = (error: unknown) =>
  error instanceof ApiDomainProblem ? (error.detail ?? error.message) : undefined;

/**
 * How each box ended on the convoy (ADR 0015): delivered, accepted, seized, returned to a registered hub or undeliverable.
 * The Dispatcher records these for the Convoy Leader. A seized or undeliverable box leaves the vehicle's load, which makes
 * that vehicle's filed declarations stale.
 */
export function ConvoyOutcomesPanel({ convoyId, departed }: ConvoyOutcomesPanelProps): JSX.Element {
  const vehicles = useConvoyVehicles(convoyId);
  const outcomes = useBoxOutcomes(convoyId);
  const locations = useLocations({ page: 1, pageSize: 200 });

  if (vehicles.isPending || outcomes.isPending) {
    return <PageSkeleton />;
  }
  if (vehicles.isError || outcomes.isError) {
    return <p role="alert">The outcomes could not be loaded.</p>;
  }
  if (!departed) {
    return <p role="status">Outcomes are recorded once the convoy has departed.</p>;
  }

  const rows = 'parentMissing' in vehicles.data ? [] : vehicles.data;
  const hubs = (locations.data ?? []).filter((location) => location.isRegisteredHub);

  return (
    <div>
      {rows.map((vehicle) => (
        <VehicleOutcomes
          key={vehicle.vin}
          convoyId={convoyId}
          vin={vehicle.vin}
          plate={vehicle.plate}
          withdrawn={vehicle.withdrawn}
          delivered={vehicle.delivered}
          outcomes={outcomes.data.filter((outcome) => outcome.vin === vehicle.vin)}
          hubs={hubs}
        />
      ))}
    </div>
  );
}

interface VehicleOutcomesProps {
  convoyId: number;
  vin: string;
  plate: string;
  withdrawn: boolean;
  delivered: boolean;
  outcomes: readonly BoxOutcomeReadModel[];
  hubs: readonly LocationReadModel[];
}

function VehicleOutcomes({
  convoyId,
  vin,
  plate,
  withdrawn,
  delivered,
  outcomes,
  hubs,
}: VehicleOutcomesProps): JSX.Element {
  const boxes = useVehicleBoxes(convoyId, vin);
  const deliver = useDeliverVehicle(convoyId);
  const record = useRecordBoxOutcome(convoyId);

  if (boxes.isPending) {
    return <Spinner label={`Loading ${plate}…`} />;
  }

  const onLoad =
    boxes.isError || 'parentMissing' in boxes.data ? [] : boxes.data.map((box) => box.boxId);
  const boxIds = [...new Set([...onLoad, ...outcomes.map((outcome) => outcome.boxId)])].sort(
    (a, b) => a - b,
  );
  const outcomeOf = (boxId: number) => outcomes.find((outcome) => outcome.boxId === boxId);
  const message = problemMessage(deliver.error) ?? problemMessage(record.error);

  const columns: readonly Column<number>[] = [
    { header: 'Box', cell: (boxId) => `#${String(boxId)}` },
    {
      header: 'Outcome',
      cell: (boxId) => {
        const current = outcomeOf(boxId);
        if (!current) {
          return 'No outcome yet';
        }
        return current.reason
          ? `${LABEL[current.outcome]} (${current.reason})`
          : LABEL[current.outcome];
      },
    },
    {
      header: 'Record',
      cell: (boxId) => (
        <Gate policy="convoys:write">
          <BoxOutcomeForm
            boxId={boxId}
            current={outcomeOf(boxId)?.outcome}
            hubs={hubs}
            pending={record.isPending}
            onRecord={(body) => {
              record.mutate({ boxId, body });
            }}
          />
        </Gate>
      ),
    },
  ];

  return (
    <section aria-label={`Outcomes for ${plate}`}>
      <h3>{plate}</h3>
      {delivered ? <p>Delivered.</p> : null}
      {!delivered && !withdrawn ? (
        <Gate policy="convoys:write">
          <Button
            type="button"
            disabled={deliver.isPending}
            onClick={() => {
              deliver.mutate(vin);
            }}
          >
            Mark {plate} delivered
          </Button>
        </Gate>
      ) : null}
      {message ? (
        <p role="alert" className="field__error">
          {message}
        </p>
      ) : null}
      <DataTable
        caption={`Box outcomes on ${plate}`}
        columns={columns}
        rows={boxIds}
        rowKey={(boxId) => String(boxId)}
        emptyMessage="No boxes on this vehicle."
      />
    </section>
  );
}

interface BoxOutcomeFormProps {
  boxId: number;
  current: BoxOutcome | undefined;
  hubs: readonly LocationReadModel[];
  pending: boolean;
  onRecord: (body: {
    outcome: BoxOutcome;
    reason?: UndeliverableReason;
    hubLocationId?: number;
  }) => void;
}

function BoxOutcomeForm({
  boxId,
  current,
  hubs,
  pending,
  onRecord,
}: BoxOutcomeFormProps): JSX.Element | null {
  const choices = nextOutcomes(current);
  const [outcome, setOutcome] = useState<BoxOutcome | ''>('');
  const [reason, setReason] = useState<UndeliverableReason>('damaged');
  const [hub, setHub] = useState('');

  if (choices.length === 0) {
    return null;
  }

  const ready = outcome !== '' && (outcome !== 'ReturnedToHub' || hub !== '');

  return (
    <form
      aria-label={`Record an outcome for box ${String(boxId)}`}
      onSubmit={(event) => {
        event.preventDefault();
        if (outcome === '' || !ready) {
          return;
        }
        onRecord({
          outcome,
          ...(outcome === 'Undeliverable' ? { reason } : {}),
          ...(outcome === 'ReturnedToHub' ? { hubLocationId: Number(hub) } : {}),
        });
      }}
      style={{ display: 'flex', gap: 'var(--space-2)', alignItems: 'end', flexWrap: 'wrap' }}
    >
      <select
        aria-label={`Outcome for box ${String(boxId)}`}
        value={outcome}
        onChange={(event) => {
          setOutcome(event.target.value as BoxOutcome | '');
        }}
      >
        <option value="">Choose an outcome…</option>
        {choices.map((choice) => (
          <option key={choice} value={choice}>
            {LABEL[choice]}
          </option>
        ))}
      </select>
      {outcome === 'Undeliverable' ? (
        <select
          aria-label={`Reason for box ${String(boxId)}`}
          value={reason}
          onChange={(event) => {
            setReason(event.target.value as UndeliverableReason);
          }}
        >
          {undeliverableReasons.map((value) => (
            <option key={value} value={value}>
              {value}
            </option>
          ))}
        </select>
      ) : null}
      {outcome === 'ReturnedToHub' ? (
        <select
          aria-label={`Hub for box ${String(boxId)}`}
          value={hub}
          onChange={(event) => {
            setHub(event.target.value);
          }}
        >
          <option value="">Choose a registered hub…</option>
          {hubs.map((location) => (
            <option key={location.id} value={String(location.id)}>
              {location.name}
            </option>
          ))}
        </select>
      ) : null}
      <Button type="submit" disabled={pending || !ready}>
        Record
      </Button>
    </form>
  );
}
