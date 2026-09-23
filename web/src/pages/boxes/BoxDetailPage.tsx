import type { JSX } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useBox, useDeleteBox } from '../../api/boxes';
import { useLocation } from '../../api/locations';
import { ApiNotFound } from '../../api/problem';
import { Button, LinkButton } from '../../components/Button';
import { DetailCard } from '../../components/DetailCard';
import { Gate } from '../../components/Gate';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { BoxBayPanel } from './BoxBayPanel';
import { BoxItemsPanel } from './BoxItemsPanel';
import { BoxQrCodePanel } from './BoxQrCodePanel';
import { BoxValidatePanel } from './BoxValidatePanel';
import { Spinner } from '../../components/Spinner';

function BoxLocationName({ locationId }: { locationId: number }): JSX.Element {
  const query = useLocation(locationId);
  if (query.isPending) return <Spinner size="sm" label="Loading location…" />;
  if (query.isError) return <>—</>;
  return <>{query.data.name}</>;
}

export function BoxDetailPage(): JSX.Element {
  const { id = '' } = useParams();
  const boxId = Number(id);
  const navigate = useNavigate();
  const query = useBox(boxId);
  const remove = useDeleteBox();

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The box could not be loaded.</p>;
  }

  const box = query.data;

  return (
    <section>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <h1>Box #{box.id}</h1>
        <span>{box.validated ? 'Validated' : 'Open'}</span>
      </header>

      <DetailCard title="Box details">
        <dl>
          <dt>Weight</dt>
          <dd>{box.validated ? `${box.weightKg} kg` : 'Not yet confirmed'}</dd>
          <dt>Receiver</dt>
          <dd>{box.receiverRef ?? '—'}</dd>
          <dt>Location</dt>
          <dd>{box.locationId === null ? '—' : <BoxLocationName locationId={box.locationId} />}</dd>
        </dl>
      </DetailCard>

      <Gate policy="boxes:write">
        <span style={{ display: 'flex', gap: 'var(--space-3)' }}>
          {!box.validated ? (
            <LinkButton to={`/boxes/${String(box.id)}/edit`} variant="secondary">
              Edit
            </LinkButton>
          ) : null}
          <Button
            variant="danger"
            disabled={remove.isPending}
            onClick={() => {
              remove.mutate(box.id, {
                onSuccess: () => {
                  void navigate('/boxes');
                },
              });
            }}
          >
            Delete
          </Button>
        </span>
      </Gate>
      {remove.isError ? <p role="alert">The box could not be removed.</p> : null}

      <BoxItemsPanel boxId={box.id} frozen={box.validated} />

      <BoxQrCodePanel boxId={box.id} />

      <BoxBayPanel boxId={box.id} locationId={box.locationId} />

      {!box.validated ? (
        <Gate policy="boxes:validate">
          <BoxValidatePanel boxId={box.id} />
        </Gate>
      ) : null}
    </section>
  );
}
