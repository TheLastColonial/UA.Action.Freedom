import { useState } from 'react';
import type { JSX } from 'react';
import { Link } from 'react-router-dom';

import { ApiDomainProblem } from '../../api/problem';
import { useReceiverUsage, useSetReceiverStatus } from '../../api/receivers';
import type { ReceiverStatus } from '../../api/schemas/receivers';
import { Button } from '../../components/Button';
import { DetailCard } from '../../components/DetailCard';
import { RECEIVER_STATUSES, RECEIVER_STATUS_LABELS } from './receiverStatus';

interface ReceiverRegistrationPanelProps {
  receiverRef: string;
  status: ReceiverStatus;
}

// Administrator only (`receivers:register`). Suspending or expiring a receiver does not unpick what
// already names it, so the panel says which boxes and convoys it touches — identifiers and counts,
// never an address.
export function ReceiverRegistrationPanel({
  receiverRef,
  status,
}: ReceiverRegistrationPanelProps): JSX.Element {
  const [chosen, setChosen] = useState<ReceiverStatus>(status);
  const usage = useReceiverUsage(receiverRef);
  const setStatus = useSetReceiverStatus(receiverRef);

  const error =
    setStatus.error instanceof ApiDomainProblem
      ? (setStatus.error.detail ?? setStatus.error.message)
      : setStatus.isError
        ? 'The status could not be changed.'
        : undefined;

  return (
    <DetailCard title="Registration">
      <p>
        Only a registered receiver can be a box destination or a vehicle handover receiver. This
        records that it may be sent to, and nothing about what kind of body it is.
      </p>

      {error ? (
        <p role="alert" className="field__error">
          {error}
        </p>
      ) : null}

      <div style={{ display: 'flex', gap: 'var(--space-3)', alignItems: 'end' }}>
        <label>
          Status
          <select
            aria-label="Registration status"
            value={chosen}
            onChange={(event) => {
              setChosen(event.target.value as ReceiverStatus);
            }}
          >
            {RECEIVER_STATUSES.map((option) => (
              <option key={option} value={option}>
                {RECEIVER_STATUS_LABELS[option]}
              </option>
            ))}
          </select>
        </label>
        <Button
          type="button"
          disabled={setStatus.isPending || chosen === status}
          onClick={() => {
            setStatus.mutate({ status: chosen });
          }}
        >
          Save status
        </Button>
      </div>

      {usage.isSuccess ? (
        <p>
          Named by {usage.data.boxCount} {usage.data.boxCount === 1 ? 'box' : 'boxes'} and{' '}
          {usage.data.convoyCount} live {usage.data.convoyCount === 1 ? 'convoy' : 'convoys'}.
          {usage.data.convoyIds.length > 0 ? (
            <>
              {' '}
              Convoys:{' '}
              {usage.data.convoyIds.map((id, index) => (
                <span key={id}>
                  {index > 0 ? ', ' : ''}
                  <Link to={`/convoys/${String(id)}`}>#{id}</Link>
                </span>
              ))}
              .
            </>
          ) : null}
        </p>
      ) : null}
    </DetailCard>
  );
}
