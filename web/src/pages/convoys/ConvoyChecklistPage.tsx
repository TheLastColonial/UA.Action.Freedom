import type { JSX } from 'react';
import { useState } from 'react';
import { useParams } from 'react-router-dom';

import { useAddCost } from '../../api/budget';
import { useConvoyProgress, useMarkPoint } from '../../api/progress';
import { ApiDomainProblem } from '../../api/problem';
import type { ProgressPoint, RoutePointMark } from '../../api/schemas/progress';
import { Button } from '../../components/Button';
import { PageSkeleton } from '../../components/PageSkeleton';
import { ConvoyOutcomesList } from './ConvoyOutcomesPanel';
import './ConvoyChecklistPage.css';

const problemMessage = (error: unknown) =>
  error instanceof ApiDomainProblem ? (error.detail ?? error.message) : undefined;

const timeOf = (iso: string) => iso.slice(11, 16);

const KIND_LABEL: Record<ProgressPoint['kind'], string> = {
  Stop: 'Stop',
  Overnight: 'Overnight',
  Border: 'Border',
  Hub: 'Hub',
};

function describeMark(mark: RoutePointMark): string {
  const who = mark.recordedByName ?? 'Someone';
  const verb = mark.kind === 'Reached' ? 'Reached' : 'Crossed';
  const behalf = mark.onBehalfOfLeader ? `, entered by ${who} for the leader` : `, ${who}`;
  return `${verb} at ${timeOf(mark.recordedAt)}${behalf}`;
}

/**
 * The Convoy Leader's checklist, made for a phone (ADR 0016, X2, X10). Progress is reported by the leader, point by point:
 * nothing is tracked and nothing is kept on the device, and the page shows each point's name and kind and no address.
 * A Dispatcher may use the same page to mark on the leader's behalf; the API records who did.
 *
 * It keeps nothing on the device, by any means (pinned by ConvoyChecklistPage.test.tsx), and the
 * API sends every answer it fetches with Cache-Control: no-store.
 */
export function ConvoyChecklistPage(): JSX.Element {
  const { id } = useParams();
  const convoyId = Number(id);
  const progress = useConvoyProgress(convoyId);
  const mark = useMarkPoint(convoyId);

  if (progress.isPending) {
    return <PageSkeleton />;
  }
  if (progress.isError) {
    return (
      <section className="checklist">
        <p role="alert">The checklist could not be loaded.</p>
        <Fallback hqPhone={null} />
      </section>
    );
  }

  const { points, hubs, hqPhone, departedAt } = progress.data;
  const departed = departedAt !== null;
  const message = problemMessage(mark.error);
  const reportable = departed && progress.data.arrivedAt === null;

  return (
    <section className="checklist" aria-label="Convoy checklist">
      <h1>Checklist</h1>
      {!departed ? <p role="status">This convoy has not departed yet.</p> : null}
      {message ? (
        <p role="alert" className="field__error">
          {message}
        </p>
      ) : null}

      <ol className="checklist__points" aria-label="Route">
        {points.map((point) => (
          <PointRow
            key={point.routePointId}
            point={point}
            canMark={reportable}
            pending={mark.isPending}
            onMark={(kind) => {
              mark.mutate({ pointId: point.routePointId, kind });
            }}
          />
        ))}
      </ol>

      {reportable ? (
        <>
          <section aria-label="Fuel" className="checklist__noprint">
            <h2>Fuel</h2>
            <FuelForm convoyId={convoyId} />
          </section>

          <section aria-label="Boxes" className="checklist__noprint">
            <h2>Boxes</h2>
            <ConvoyOutcomesList
              convoyId={convoyId}
              departed={departed}
              canRecord
              hubs={hubs.map((hub) => ({ id: hub.locationId, name: hub.name }))}
            />
          </section>
        </>
      ) : null}

      <Fallback hqPhone={hqPhone} />
    </section>
  );
}

interface PointRowProps {
  point: ProgressPoint;
  canMark: boolean;
  pending: boolean;
  onMark: (kind: 'Reached' | 'Crossed') => void;
}

function PointRow({ point, canMark, pending, onMark }: PointRowProps): JSX.Element {
  const convoyWide = point.marks.filter((mark) => mark.vin === null);
  const reached = convoyWide.some((mark) => mark.kind === 'Reached');
  const crossed = convoyWide.some((mark) => mark.kind === 'Crossed');
  const border = point.kind === 'Border';

  return (
    <li className="checklist__point">
      <div>
        <strong>{point.name}</strong>
        <span className="checklist__kind">{KIND_LABEL[point.kind]}</span>
      </div>
      {point.accommodation.map((booking) => (
        <p key={`${booking.provider}-${booking.checkIn}`} className="checklist__stay">
          Staying: {booking.provider}, {booking.checkIn.slice(0, 10)} to{' '}
          {booking.checkOut.slice(0, 10)}
        </p>
      ))}
      <ul className="checklist__marks">
        {point.marks.map((mark) => (
          <li key={`${mark.kind}-${mark.vin ?? 'all'}-${mark.recordedAt}`}>{describeMark(mark)}</li>
        ))}
      </ul>
      {canMark ? (
        <div className="checklist__actions checklist__noprint">
          {!reached ? (
            <Button
              type="button"
              disabled={pending}
              aria-label={`Reached ${point.name}`}
              onClick={() => {
                onMark('Reached');
              }}
            >
              Reached
            </Button>
          ) : null}
          {border && !crossed ? (
            <Button
              type="button"
              disabled={pending}
              aria-label={`Crossed ${point.name}`}
              onClick={() => {
                onMark('Crossed');
              }}
            >
              Crossed
            </Button>
          ) : null}
        </div>
      ) : null}
    </li>
  );
}

function FuelForm({ convoyId }: { convoyId: number }): JSX.Element {
  const addCost = useAddCost(convoyId);
  const [amount, setAmount] = useState('');
  const [saved, setSaved] = useState(false);
  const message = problemMessage(addCost.error);
  const value = Number(amount);

  return (
    <form
      aria-label="Enter fuel"
      className="checklist__fuel"
      onSubmit={(event) => {
        event.preventDefault();
        setSaved(false);
        addCost.mutate(
          { type: 'Fuel', amountGbp: value },
          {
            onSuccess: () => {
              setAmount('');
              setSaved(true);
            },
          },
        );
      }}
    >
      <label>
        Fuel cost (£)
        <input
          type="number"
          inputMode="decimal"
          min="0"
          step="0.01"
          value={amount}
          onChange={(event) => {
            setAmount(event.target.value);
          }}
        />
      </label>
      <Button type="submit" disabled={addCost.isPending || amount === '' || !(value >= 0)}>
        Add fuel
      </Button>
      {saved ? <p role="status">Fuel added.</p> : null}
      {message ? (
        <p role="alert" className="field__error">
          {message}
        </p>
      ) : null}
    </form>
  );
}

/** What to do when this page is out of reach: ring HQ and use the printed documents (O18). */
function Fallback({ hqPhone }: { hqPhone: string | null }): JSX.Element {
  return (
    <section aria-label="If this page is not working" className="checklist__fallback">
      <h2>If this page is not working</h2>
      <p>
        {hqPhone ? (
          <>
            Call HQ on <a href={`tel:${hqPhone.replace(/\s+/g, '')}`}>{hqPhone}</a> and use the
            printed documents.
          </>
        ) : (
          'Call HQ and use the printed documents.'
        )}
      </p>
      <Button
        type="button"
        variant="secondary"
        onClick={() => {
          window.print();
        }}
      >
        Print the route sheet
      </Button>
    </section>
  );
}
