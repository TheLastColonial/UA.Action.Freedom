import type { JSX } from 'react';

import { useConvoyProgress } from '../../api/progress';
import type { ProgressPoint } from '../../api/schemas/progress';
import { DetailCard } from '../../components/DetailCard';

const timeOf = (iso: string) => iso.replace('T', ' ').slice(0, 16);

function status(point: ProgressPoint): string {
  const crossed = point.marks.find((mark) => mark.kind === 'Crossed');
  const reached = point.marks.find((mark) => mark.kind === 'Reached');
  const latest = crossed ?? reached;

  if (!latest) {
    return 'Not yet';
  }

  const verb = crossed ? 'Crossed' : 'Reached';
  const who = latest.onBehalfOfLeader
    ? `${latest.recordedByName ?? 'HQ'}, for the leader`
    : (latest.recordedByName ?? '');
  return `${verb} ${timeOf(latest.recordedAt)}${who ? ` (${who})` : ''}`;
}

/**
 * HQ's view of how the convoy is getting on (ADR 0016). It is exactly what the Convoy Leader has reported, point by point:
 * nothing here is a position, and a point nobody has reported reads "Not yet" rather than a guess.
 */
export function ConvoyProgressCard({
  convoyId,
  departed,
}: {
  convoyId: number;
  departed: boolean;
}): JSX.Element | null {
  const progress = useConvoyProgress(convoyId);

  if (!departed || progress.isPending || progress.isError || progress.data.points.length === 0) {
    return null;
  }

  return (
    <DetailCard title="Progress">
      <dl>
        {progress.data.points.map((point) => (
          <div key={point.routePointId}>
            <dt>
              {point.name} ({point.kind})
            </dt>
            <dd>{status(point)}</dd>
          </div>
        ))}
      </dl>
    </DetailCard>
  );
}
