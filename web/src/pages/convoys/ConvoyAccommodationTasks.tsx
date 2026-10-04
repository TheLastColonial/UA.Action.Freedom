import type { JSX } from 'react';
import { useState } from 'react';

import {
  useAccommodationCoverage,
  useCancelBooking,
  useMigrateBooking,
} from '../../api/accommodation';
import { ApiDomainProblem } from '../../api/problem';
import type { AccommodationCoverage, LeftoverBookingTask } from '../../api/schemas/accommodation';
import { useLeftoverBookingTasks } from '../../api/tasks';
import { Button } from '../../components/Button';
import { DetailCard } from '../../components/DetailCard';
import { Gate } from '../../components/Gate';

/**
 * The Dispatcher's accommodation tasks (P13, P16): a booking that outlived its guest on the crew. It is a warning,
 * never a block, and it is derived, so cancelling the booking or migrating its place to a replacement is what clears it.
 */
export function ConvoyAccommodationTasks({ convoyId }: { convoyId: number }): JSX.Element {
  const tasks = useLeftoverBookingTasks(convoyId);
  const coverage = useAccommodationCoverage(convoyId);

  if (tasks.isPending || coverage.isPending) {
    return <p>Checking accommodation…</p>;
  }
  if (tasks.isError || coverage.isError) {
    return <p role="alert">Accommodation tasks could not be loaded.</p>;
  }

  const stopName = (routePointId: number) =>
    coverage.data.stops.find((stop) => stop.routePointId === routePointId)?.name ??
    'a removed stop';

  return (
    <DetailCard title="Accommodation tasks">
      {tasks.data.length === 0 ? (
        <p role="status">No accommodation needs dealing with.</p>
      ) : (
        <ul>
          {tasks.data.map((task) => (
            <li key={task.bookingId}>
              <strong>
                {`Booking ${String(task.bookingId)} at ${stopName(task.routePointId)} covers nobody still crewed.`}
              </strong>
              {' Cancel it, or migrate its place to a replacement.'}
              <Gate policy="convoys:write">
                <LeftoverActions convoyId={convoyId} task={task} coverage={coverage.data} />
              </Gate>
            </li>
          ))}
        </ul>
      )}
    </DetailCard>
  );
}

function LeftoverActions({
  convoyId,
  task,
  coverage,
}: {
  convoyId: number;
  task: LeftoverBookingTask;
  coverage: AccommodationCoverage;
}): JSX.Element {
  const cancel = useCancelBooking(convoyId);
  const migrate = useMigrateBooking(convoyId);
  const [replacements, setReplacements] = useState<Record<string, string>>({});

  const failure = [cancel.error, migrate.error].find((error) => error !== null);
  const label = (guestNumber: number) =>
    `guest ${String(guestNumber)} of booking ${String(task.bookingId)}`;

  return (
    <div>
      <Button
        type="button"
        variant="danger"
        disabled={cancel.isPending}
        onClick={() => {
          cancel.mutate(task.bookingId);
        }}
      >
        {`Cancel booking ${String(task.bookingId)}`}
      </Button>
      {task.guestIds.map((guestId, index) => {
        const replacement = replacements[guestId] ?? '';
        const selectId = `replacement-${String(task.bookingId)}-${guestId}`;
        return (
          <div key={guestId}>
            <label htmlFor={selectId}>{`Replacement for ${label(index + 1)}`}</label>
            <span>
              <select
                id={selectId}
                value={replacement}
                onChange={(event) => {
                  setReplacements({ ...replacements, [guestId]: event.target.value });
                }}
              >
                <option value="">Choose a crew member</option>
                {coverage.crew.map((member) => (
                  <option key={member.personId} value={member.personId}>
                    {member.name}
                  </option>
                ))}
              </select>
            </span>
            <Button
              type="button"
              variant="secondary"
              disabled={replacement === '' || migrate.isPending}
              onClick={() => {
                migrate.mutate({
                  bookingId: task.bookingId,
                  fromPersonId: guestId,
                  toPersonId: replacement,
                });
              }}
            >
              {`Migrate ${label(index + 1)}`}
            </Button>
          </div>
        );
      })}
      {failure ? (
        <p role="alert" className="field__error">
          {failure instanceof ApiDomainProblem
            ? (failure.detail ?? failure.message)
            : failure.message}
        </p>
      ) : null}
    </div>
  );
}
