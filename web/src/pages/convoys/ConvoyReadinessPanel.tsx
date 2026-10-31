import type { JSX } from 'react';
import { Link } from 'react-router-dom';

import { useConvoyReadiness, useDepartConvoy } from '../../api/convoys';
import { ApiDomainProblem } from '../../api/problem';
import type { Requirement } from '../../api/schemas/convoys';
import { Button } from '../../components/Button';
import { DetailCard } from '../../components/DetailCard';
import { Gate } from '../../components/Gate';

// Where each requirement is fixed, as a tab of the convoy page. The code is a stable name (ADR 0008), so this is
// the one place that knows the layout. Declarations are filed from the vehicle's manifest, and the manifest
// is not addressed by the requirement, so they point at the cargo tab for now.
const TAB_FOR: Readonly<Record<string, string>> = {
  route: 'route',
  vehicles: 'vehicles',
  leader: 'drivers',
  accommodation: 'accommodation',
  driver: 'drivers',
  insurance: 'drivers',
  ferry: 'cargo',
  'handover-receiver': 'vehicles',
  boxes: 'cargo',
};

function tabFor(code: string): string {
  if (code.startsWith('declaration.')) {
    return 'cargo';
  }
  if (code === 'advisory.budget') {
    return 'budget';
  }
  if (code.startsWith('advisory.')) {
    return code === 'advisory.leftover-booking' ? 'accommodation' : 'cargo';
  }
  return TAB_FOR[code] ?? 'overview';
}

const outstanding = (requirement: Requirement) =>
  requirement.severity === 'Blocking' && requirement.state !== 'Done';

function RequirementItem({
  convoyId,
  requirement,
  plate,
}: {
  convoyId: number;
  requirement: Requirement;
  plate?: string;
}): JSX.Element {
  return (
    <li>
      {plate ? `${plate}: ` : ''}
      {requirement.message}
      {requirement.state === 'Blocked' ? ' (waiting on another item)' : ''}
      {' — '}
      <Link to={`/convoys/${String(convoyId)}?tab=${tabFor(requirement.code)}`}>
        {requirement.ownerRole}: fix
      </Link>
    </li>
  );
}

/**
 * Whether the convoy is ready to travel, as the requirements it is made of (ADR 0008): what blocks departure, what is
 * only advised, grouped by what they are about, each linking to where it is fixed. Departure is the one action
 * below it, and it refuses with exactly this list; nothing overrides it.
 */
export function ConvoyReadinessPanel({
  convoyId,
  departed = false,
  arrived = false,
}: {
  convoyId: number;
  departed?: boolean;
  arrived?: boolean;
}): JSX.Element {
  const query = useConvoyReadiness(convoyId);
  const depart = useDepartConvoy(convoyId);

  if (query.isPending) {
    return <p>Checking readiness…</p>;
  }
  if (query.isError) {
    return <p role="alert">Readiness could not be checked.</p>;
  }

  const readiness = query.data;
  const blockers = readiness.requirements.filter(outstanding);
  const convoyBlockers = blockers.filter((requirement) => requirement.scope === 'Convoy');
  const vehicles = readiness.vehicles
    .map((vehicle) => ({
      vehicle,
      blockers: blockers.filter((requirement) => requirement.vin === vehicle.vin),
    }))
    .filter((entry) => entry.blockers.length > 0);
  const advice = readiness.requirements.filter(
    (requirement) => requirement.severity === 'Advisory' && requirement.state === 'Warning',
  );
  const refusal =
    depart.error instanceof ApiDomainProblem
      ? (depart.error.detail ?? depart.error.message)
      : undefined;

  return (
    <DetailCard title="Readiness">
      <h3>{readiness.ready ? 'Ready to travel' : 'Not ready yet'}</h3>
      {convoyBlockers.length > 0 ? (
        <ul aria-label="Convoy requirements">
          {convoyBlockers.map((requirement) => (
            <RequirementItem key={requirement.code} convoyId={convoyId} requirement={requirement} />
          ))}
        </ul>
      ) : null}
      {vehicles.map(({ vehicle, blockers: vehicleBlockers }) => (
        <ul key={vehicle.vin} aria-label={`${vehicle.plate} requirements`}>
          {vehicleBlockers.map((requirement) => (
            <RequirementItem
              key={requirement.code}
              convoyId={convoyId}
              requirement={requirement}
              plate={vehicle.plate}
            />
          ))}
        </ul>
      ))}
      {advice.length > 0 ? (
        <ul aria-label="Advice">
          {advice.map((requirement) => (
            <li key={`${requirement.code}:${requirement.vin ?? ''}`}>
              {requirement.vin
                ? `${readiness.vehicles.find((vehicle) => vehicle.vin === requirement.vin)?.plate ?? requirement.vin}: `
                : ''}
              {requirement.message}
            </li>
          ))}
        </ul>
      ) : null}
      <p className="field__hint">
        Everything marked to do must be done before the convoy can depart. Advice does not stop it.
      </p>
      {departed ? <p>This convoy has departed.</p> : null}
      <Gate policy="convoys:write">
        {!departed && !arrived ? (
          <Button
            type="button"
            disabled={depart.isPending}
            onClick={() => {
              depart.mutate();
            }}
          >
            Depart
          </Button>
        ) : null}
      </Gate>
      {depart.isError ? (
        <div role="alert" className="field__error">
          <p>{refusal ?? 'The convoy could not depart.'}</p>
          {blockers.length > 0 ? (
            <ul aria-label="Why it cannot depart">
              {blockers.map((requirement) => (
                <li key={`${requirement.code}:${requirement.vin ?? ''}`}>{requirement.message}</li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}
    </DetailCard>
  );
}
