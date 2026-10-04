import type { JSX } from 'react';

import { useRedeclareTasks } from '../../api/declarations';
import type { DeclarationKind, RedeclareResolution } from '../../api/schemas/declarations';
import { DetailCard } from '../../components/DetailCard';

const KIND_LABEL: Record<DeclarationKind, string> = {
  Gmr: 'GMR',
  Ens: 'ENS',
  Elo: 'ELO',
  GoodsList: 'Goods list',
};

const RESOLUTION_TEXT: Record<RedeclareResolution, string> = {
  UpdateOrRecreate: 'Update it, or delete and recreate it.',
  InvalidateAndRefile: 'Invalidate and refile. The old MRN is kept as history.',
  NewEnvelopeAgainstNewMrn: 'Create a new envelope against the new MRN.',
  PrepareNewListAndHoldAtHub:
    'Prepare a new list for the Receiver to file. The goods wait at a registered hub until it is accepted.',
};

/**
 * The Dispatcher's re-declare tasks (D13): one for every declaration whose load has changed since it
 * was written. They are derived, shown on screen only and never emailed; withdrawing the stale
 * declaration on the vehicle's manifest page is what clears one.
 */
export function ConvoyRedeclareTasks({ convoyId }: { convoyId: number }): JSX.Element {
  const query = useRedeclareTasks(convoyId);

  if (query.isPending) {
    return <p>Checking declarations…</p>;
  }
  if (query.isError) {
    return <p role="alert">Re-declare tasks could not be loaded.</p>;
  }

  return (
    <DetailCard title="Re-declare tasks">
      {query.data.length === 0 ? (
        <p role="status">Every declaration matches its load.</p>
      ) : (
        <ul>
          {query.data.map((task) => (
            <li key={task.declarationId}>
              <strong>{`${KIND_LABEL[task.kind]} for ${task.vin} is stale.`}</strong>
              {` ${RESOLUTION_TEXT[task.resolution]}`}
            </li>
          ))}
        </ul>
      )}
    </DetailCard>
  );
}
