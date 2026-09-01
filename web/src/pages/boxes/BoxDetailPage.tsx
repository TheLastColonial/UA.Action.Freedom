import type { JSX } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';

import { useBox, useDeleteBox } from '../../api/boxes';
import { ApiNotFound } from '../../api/problem';
import { Gate } from '../../components/Gate';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { BoxItemsPanel } from './BoxItemsPanel';
import { BoxQrCodePanel } from './BoxQrCodePanel';
import { BoxValidatePanel } from './BoxValidatePanel';

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

      <dl>
        <dt>Weight</dt>
        <dd>{box.validated ? `${box.weightKg} kg` : 'Not yet confirmed'}</dd>
        <dt>Receiver</dt>
        <dd>{box.receiverRef ?? '—'}</dd>
        <dt>Destination</dt>
        <dd>
          {[box.house, box.street, box.city, box.country, box.postcode]
            .filter(Boolean)
            .join(', ') || '—'}
        </dd>
      </dl>

      <Gate policy="boxes:write">
        <span style={{ display: 'flex', gap: 'var(--space-3)' }}>
          {!box.validated ? <Link to={`/boxes/${String(box.id)}/edit`}>Edit</Link> : null}
          <button
            type="button"
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
          </button>
        </span>
      </Gate>
      {remove.isError ? <p role="alert">The box could not be removed.</p> : null}

      <BoxItemsPanel boxId={box.id} frozen={box.validated} />

      <BoxQrCodePanel boxId={box.id} />

      {!box.validated ? (
        <Gate policy="boxes:validate">
          <BoxValidatePanel boxId={box.id} />
        </Gate>
      ) : null}
    </section>
  );
}
