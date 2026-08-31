import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useFieldArray, useForm } from 'react-hook-form';

import { useAddBoxItem, useBoxItems, useRemoveBoxItem } from '../../api/boxes';
import { ApiDomainProblem } from '../../api/problem';
import { PageSkeleton } from '../../components/PageSkeleton';
import { TextField } from '../../components/form/fields';
import { addItemFormSchema, addItemFormToRequest, emptyAddItemForm } from './boxModels';
import type { AddItemFormValues } from './boxModels';

interface BoxItemsPanelProps {
  boxId: number;
  frozen: boolean;
}

export function BoxItemsPanel({ boxId, frozen }: BoxItemsPanelProps): JSX.Element {
  const query = useBoxItems(boxId);
  const add = useAddBoxItem(boxId);
  const removeItem = useRemoveBoxItem(boxId);

  const {
    register,
    control,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<AddItemFormValues>({
    resolver: zodResolver(addItemFormSchema),
    defaultValues: emptyAddItemForm(),
  });
  const properties = useFieldArray({ control, name: 'properties' });

  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The box contents could not be loaded.</p>;
  }

  const items = 'parentMissing' in query.data ? [] : query.data;
  const addError =
    add.error instanceof ApiDomainProblem ? (add.error.detail ?? add.error.message) : undefined;

  return (
    <div>
      <h2>Contents</h2>
      {items.length === 0 ? (
        <p>Nothing packed yet.</p>
      ) : (
        <ul>
          {items.map((item) => (
            <li key={item.id}>
              {item.description}
              {Object.keys(item.properties).length > 0 ? (
                <span>
                  {' '}
                  (
                  {Object.entries(item.properties)
                    .map(([key, value]) => `${key}: ${value}`)
                    .join(', ')}
                  )
                </span>
              ) : null}
              {!frozen ? (
                <button
                  type="button"
                  disabled={removeItem.isPending}
                  onClick={() => {
                    removeItem.mutate(item.id);
                  }}
                >
                  Remove
                </button>
              ) : null}
            </li>
          ))}
        </ul>
      )}

      {frozen ? (
        <p role="status">This box has been validated — its contents are now fixed.</p>
      ) : (
        <form
          noValidate
          onSubmit={(event) => {
            void handleSubmit((values) => {
              add.mutate(addItemFormToRequest(values), {
                onSuccess: () => {
                  reset(emptyAddItemForm());
                },
              });
            })(event);
          }}
        >
          {addError ? (
            <p role="alert" className="field__error">
              {addError}
            </p>
          ) : null}

          <TextField
            label="Description"
            error={errors.description?.message}
            {...register('description')}
          />

          <fieldset>
            <legend>Properties</legend>
            {properties.fields.map((field, index) => (
              <div key={field.id} style={{ display: 'flex', gap: 'var(--space-2)' }}>
                <TextField
                  label={`Property ${String(index + 1)} name`}
                  error={errors.properties?.[index]?.key?.message}
                  {...register(`properties.${index}.key`)}
                />
                <TextField
                  label={`Property ${String(index + 1)} value`}
                  {...register(`properties.${index}.value`)}
                />
                <button
                  type="button"
                  onClick={() => {
                    properties.remove(index);
                  }}
                >
                  Remove property
                </button>
              </div>
            ))}
            <button
              type="button"
              onClick={() => {
                properties.append({ key: '', value: '' });
              }}
            >
              Add property
            </button>
          </fieldset>

          <button type="submit" disabled={add.isPending}>
            Add item
          </button>
        </form>
      )}
    </div>
  );
}
