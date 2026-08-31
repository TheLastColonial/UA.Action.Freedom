import type { JSX } from 'react';
import { useState } from 'react';

import { revealReceiverDetail, setReceiverDetail } from '../../api/receiverDetail';
import type { ReceiverDetailReadModel } from '../../api/receiverDetail';
import { ApiNotFound } from '../../api/problem';
import { useDeleteReceiver } from '../../api/receivers';
import { ReasonModal } from './ReasonModal';
import { ReceiverDetailForm } from './ReceiverDetailForm';
import { detailFormToRequest } from './receiverModels';
import type { DetailFormValues } from './receiverModels';

interface ReceiverSensitivePanelProps {
  receiverRef: string;
  onDeleted: () => void;
}

type Status = 'hidden' | 'revealed' | 'no-detail';

export function ReceiverSensitivePanel({
  receiverRef,
  onDeleted,
}: ReceiverSensitivePanelProps): JSX.Element {
  const [status, setStatus] = useState<Status>('hidden');
  const [detail, setDetail] = useState<ReceiverDetailReadModel | null>(null);
  const [reason, setReason] = useState<string | null>(null);
  const [modalOpen, setModalOpen] = useState(false);
  const [revealing, setRevealing] = useState(false);
  const [revealError, setRevealError] = useState<string | undefined>(undefined);
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | undefined>(undefined);

  const remove = useDeleteReceiver();

  const reveal = (statedReason: string) => {
    setRevealing(true);
    setRevealError(undefined);
    revealReceiverDetail(receiverRef, statedReason)
      .then((loaded) => {
        setDetail(loaded);
        setReason(statedReason);
        setStatus('revealed');
        setModalOpen(false);
      })
      .catch((error: unknown) => {
        if (error instanceof ApiNotFound) {
          setReason(statedReason);
          setStatus('no-detail');
          setModalOpen(false);
        } else {
          setRevealError('The delivery detail could not be loaded.');
        }
      })
      .finally(() => {
        setRevealing(false);
      });
  };

  const save = (values: DetailFormValues) => {
    setSaving(true);
    setSaveError(undefined);
    setReceiverDetail(receiverRef, detailFormToRequest(values))
      .then(() => revealReceiverDetail(receiverRef, reason ?? 'confirming updated delivery detail'))
      .then((loaded) => {
        setDetail(loaded);
        setStatus('revealed');
        setEditing(false);
      })
      .catch(() => {
        setSaveError('The delivery detail could not be saved.');
      })
      .finally(() => {
        setSaving(false);
      });
  };

  const forget = () => {
    setDetail(null);
    setStatus('hidden');
    setEditing(false);
  };

  return (
    <section aria-label="Delivery detail">
      <h2>Delivery detail</h2>
      <p>Ground Officer access only. Every view is written to the receiver access log.</p>

      {status === 'hidden' && !editing ? (
        <button
          type="button"
          onClick={() => {
            setModalOpen(true);
          }}
        >
          Reveal delivery detail
        </button>
      ) : null}

      <ReasonModal
        isOpen={modalOpen}
        onOpenChange={setModalOpen}
        submitting={revealing}
        errorMessage={revealError}
        onSubmit={reveal}
      />

      {status === 'no-detail' && !editing ? (
        <div>
          <p>No delivery detail has been recorded for this receiver.</p>
          <button
            type="button"
            onClick={() => {
              setEditing(true);
            }}
          >
            Add delivery detail
          </button>
        </div>
      ) : null}

      {status === 'revealed' && !editing ? (
        <div>
          <dl>
            <dt>Contact</dt>
            <dd>
              {detail?.contactName} · {detail?.contactPhone}
            </dd>
            <dt>Address</dt>
            <dd>
              {[detail?.addressLine1, detail?.addressLine2, detail?.city, detail?.postCode]
                .filter(Boolean)
                .join(', ')}
            </dd>
          </dl>
          <div style={{ display: 'flex', gap: 'var(--space-3)' }}>
            <button
              type="button"
              onClick={() => {
                setEditing(true);
              }}
            >
              Edit delivery detail
            </button>
            <button type="button" onClick={forget}>
              Hide
            </button>
          </div>
        </div>
      ) : null}

      {editing ? (
        <ReceiverDetailForm
          current={detail}
          submitting={saving}
          errorMessage={saveError}
          onSubmit={save}
          onCancel={() => {
            setEditing(false);
          }}
        />
      ) : null}

      <hr />
      <button
        type="button"
        disabled={remove.isPending}
        onClick={() => {
          remove.mutate(receiverRef, { onSuccess: onDeleted });
        }}
      >
        Delete receiver
      </button>
      {remove.isError ? <p role="alert">The receiver could not be removed.</p> : null}
    </section>
  );
}
