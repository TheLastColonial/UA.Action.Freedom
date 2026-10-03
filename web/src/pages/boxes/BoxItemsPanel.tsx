import { zodResolver } from '@hookform/resolvers/zod';
import { useState } from 'react';
import type { JSX } from 'react';
import { useFieldArray, useForm } from 'react-hook-form';

import { useAddBoxItem, useBoxItems, useRemoveBoxItem } from '../../api/boxes';
import { useCategories } from '../../api/categories';
import { ApiDomainProblem } from '../../api/problem';
import type { BoxItemReadModel, ItemWarning } from '../../api/schemas/boxes';
import { Button } from '../../components/Button';
import { PageSkeleton } from '../../components/PageSkeleton';
import { SelectField, TextField } from '../../components/form/fields';
import { addItemFormSchema, addItemFormToRequest, emptyAddItemForm } from './boxModels';
import type { AddItemFormValues } from './boxModels';

interface BoxItemsPanelProps {
  boxId: number;
  frozen: boolean;
}

const WARNING_TEXT: Record<ItemWarning, string> = {
  NotCarried:
    'The convoy does not carry this kind of goods. Take it out unless it has been cleared.',
  ShortShelfLife: 'This item has a short shelf life. Check it will still be in date on arrival.',
  Expired: 'This item has already expired. The box cannot be validated while it is in it.',
};

const VALUE_SOURCE_OPTIONS = [
  { value: '', label: 'Choose…' },
  { value: 'Donor', label: 'Stated by the donor' },
  { value: 'Estimate', label: 'Estimate' },
] as const;

const poundsFormat = new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP' });

function describeItem(item: BoxItemReadModel): string {
  const parts: string[] = [];
  if (item.categoryNameEn) parts.push(item.categoryNameEn);
  if (item.quantity !== null) parts.push(`quantity ${String(item.quantity)}`);
  if (item.valueGbp !== null && item.valueSource !== null) {
    parts.push(`${poundsFormat.format(item.valueGbp)} (${item.valueSource.toLowerCase()})`);
  }
  if (item.expiresOn) parts.push(`expires ${item.expiresOn}`);
  if (item.commodityCode) parts.push(`code ${item.commodityCode}`);
  for (const [key, value] of Object.entries(item.properties)) parts.push(`${key}: ${value}`);
  return parts.join(', ');
}

function ItemBadge({ children }: { children: string }): JSX.Element {
  return (
    <span
      style={{
        marginLeft: 'var(--space-2)',
        padding: '0 var(--space-2)',
        border: '1px solid var(--color-border)',
        borderRadius: 'var(--radius-md)',
        fontWeight: 600,
      }}
    >
      {children}
    </span>
  );
}

export function BoxItemsPanel({ boxId, frozen }: BoxItemsPanelProps): JSX.Element {
  const query = useBoxItems(boxId);
  const categories = useCategories();
  const add = useAddBoxItem(boxId);
  const removeItem = useRemoveBoxItem(boxId);
  const [warnings, setWarnings] = useState<readonly ItemWarning[]>([]);

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
  const categoryOptions = [
    { value: '', label: 'Choose a category…' },
    ...(categories.data ?? []).map((category) => ({
      value: String(category.id),
      label: category.nameEn,
    })),
  ];

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
              {describeItem(item).length > 0 ? <span> ({describeItem(item)})</span> : null}
              {item.shelfLife === 'Expired' ? <ItemBadge>Expired</ItemBadge> : null}
              {item.shelfLife === 'Short' ? <ItemBadge>Short shelf life</ItemBadge> : null}
              {item.isNotCarried ? <ItemBadge>Not carried</ItemBadge> : null}
              {!frozen ? (
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
                onSuccess: (result) => {
                  setWarnings(result.warnings);
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
          {warnings.length > 0 ? (
            <ul role="status">
              {warnings.map((warning) => (
                <li key={warning}>{WARNING_TEXT[warning]}</li>
              ))}
            </ul>
          ) : null}

          <TextField
            label="Description"
            error={errors.description?.message}
            {...register('description')}
          />

          <SelectField
            label="Category"
            options={categoryOptions}
            error={errors.categoryId?.message}
            {...register('categoryId')}
          />

          <TextField
            label="Quantity"
            inputMode="numeric"
            error={errors.quantity?.message}
            {...register('quantity')}
          />

          <TextField
            label="Value (£)"
            inputMode="decimal"
            error={errors.valueGbp?.message}
            {...register('valueGbp')}
          />
          <SelectField
            label="Value source"
            options={VALUE_SOURCE_OPTIONS}
            error={errors.valueSource?.message}
            {...register('valueSource')}
          />

          <TextField
            label="Expires on"
            type="date"
            error={errors.expiresOn?.message}
            {...register('expiresOn')}
          />

          <TextField
            label="Commodity code"
            hint="Leave blank to use the code of the category."
            inputMode="numeric"
            error={errors.commodityCode?.message}
            {...register('commodityCode')}
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
                <Button
                  type="button"
                  variant="secondary"
                  onClick={() => {
                    properties.remove(index);
                  }}
                >
                  Remove property
                </Button>
              </div>
            ))}
            <Button
              type="button"
              variant="secondary"
              onClick={() => {
                properties.append({ key: '', value: '' });
              }}
            >
              Add property
            </Button>
          </fieldset>

          <Button type="submit" disabled={add.isPending}>
            Add item
          </Button>
        </form>
      )}
    </div>
  );
}
