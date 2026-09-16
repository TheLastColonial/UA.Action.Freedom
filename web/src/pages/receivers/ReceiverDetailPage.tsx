import type { JSX } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { ApiNotFound } from '../../api/problem';
import { useReceiver } from '../../api/receivers';
import { LinkButton } from '../../components/Button';
import { DetailCard } from '../../components/DetailCard';
import { Gate } from '../../components/Gate';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { ReceiverSensitivePanel } from './ReceiverSensitivePanel';

export function ReceiverDetailPage(): JSX.Element {
  const { ref = '' } = useParams();
  const navigate = useNavigate();
  const query = useReceiver(ref);

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The receiver could not be loaded.</p>;
  }

  const receiver = query.data;

  return (
    <section>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <h1>{receiver.organisation}</h1>
        <Gate policy="receivers:write">
          <LinkButton
            to={`/receivers/${encodeURIComponent(receiver.ref)}/edit`}
            variant="secondary"
          >
            Edit
          </LinkButton>
        </Gate>
      </header>

      <DetailCard title="Receiver details">
        <dl>
          <dt>Region</dt>
          <dd>{receiver.region}</dd>
        </dl>
      </DetailCard>

      <Gate
        policy="receivers:detail"
        fallback={
          <p>Delivery address and contact are held separately, visible to a Ground Officer only.</p>
        }
      >
        <ReceiverSensitivePanel
          receiverRef={receiver.ref}
          onDeleted={() => {
            void navigate('/receivers');
          }}
        />
      </Gate>
    </section>
  );
}
