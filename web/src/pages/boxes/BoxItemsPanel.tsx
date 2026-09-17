import { Fragment, useState } from 'react';
import type { JSX } from 'react';

import type { BoxItemReadModel } from '../../api/schemas/boxes';
import { useAddBoxItem, useBoxItems, useRemoveBoxItem, useUpdateBoxItem } from '../../api/boxes';
import { ApiDomainProblem } from '../../api/problem';
import { Button } from '../../components/Button';
import { DetailCard } from '../../components/DetailCard';
import { PageSkeleton } from '../../components/PageSkeleton';
import { ItemModal } from './ItemModal';
import { addItemFormToRequest, emptyAddItemForm, itemToFormValues } from './boxModels';

interface BoxItemsPanelProps {
  boxId: number;
  frozen: boolean;
}

type ModalState = { mode: 'add' } | { mode: 'edit'; item: BoxItemReadModel } | null;

export function BoxItemsPanel({ boxId, frozen }: BoxItemsPanelProps): JSX.Element {
  const query = useBoxItems(boxId);
  const add = useAddBoxItem(boxId);
  const update = useUpdateBoxItem(boxId);
  const removeItem = useRemoveBoxItem(boxId);
  const [modal, setModal] = useState<ModalState>(null);

  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The box contents could not be loaded.</p>;
  }

  const items = 'parentMissing' in query.data ? [] : query.data;
  const editing = modal?.mode === 'edit' ? modal.item : null;
  const mutation = editing ? update : add;
  const mutationError =
    mutation.error instanceof ApiDomainProblem
      ? (mutation.error.detail ?? mutation.error.message)
      : undefined;

  return (
    <DetailCard title="Contents">
      {items.length === 0 ? (
        <p>Nothing packed yet.</p>
      ) : (
        <ul>
          {items.map((item) => (
            <li key={item.id}>
              <p>{item.description}</p>
              {Object.keys(item.properties).length > 0 ? (
                <dl>
                  {Object.entries(item.properties).map(([key, value]) => (
                    <Fragment key={key}>
                      <dt>{key}</dt>
                      <dd>{value}</dd>
                    </Fragment>
                  ))}
                </dl>
              ) : null}
              {!frozen ? (
                <span style={{ display: 'flex', gap: 'var(--space-2)' }}>
                  <Button
                    type="button"
                    variant="secondary"
                    onClick={() => {
                      setModal({ mode: 'edit', item });
                    }}
                  >
                    Edit
                  </Button>
                  <Button
                    type="button"
                    variant="danger"
                    disabled={removeItem.isPending}
                    onClick={() => {
                      removeItem.mutate(item.id);
                    }}
                  >
                    Remove
                  </Button>
                </span>
              ) : null}
            </li>
          ))}
        </ul>
      )}

      {frozen ? (
        <p role="status">This box has been validated — its contents are now fixed.</p>
      ) : (
        <Button
          type="button"
          onClick={() => {
            setModal({ mode: 'add' });
          }}
        >
          Add item
        </Button>
      )}

      {modal !== null ? (
        <ItemModal
          mode={modal.mode}
          initialValues={editing ? itemToFormValues(editing) : emptyAddItemForm()}
          submitting={mutation.isPending}
          errorMessage={mutationError}
          onClose={() => {
            setModal(null);
          }}
          onSubmit={(values) => {
            const body = addItemFormToRequest(values);
            if (editing) {
              update.mutate(
                { itemId: editing.id, body },
                {
                  onSuccess: () => {
                    setModal(null);
                  },
                },
              );
            } else {
              add.mutate(body, {
                onSuccess: () => {
                  setModal(null);
                },
              });
            }
          }}
        />
      ) : null}
    </DetailCard>
  );
}
