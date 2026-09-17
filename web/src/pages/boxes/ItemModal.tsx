import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useEffect, useId } from 'react';
import { useFieldArray, useForm } from 'react-hook-form';

import { Button } from '../../components/Button';
import { TextField } from '../../components/form/fields';
import { addItemFormSchema } from './boxModels';
import type { AddItemFormValues } from './boxModels';
import './ItemModal.css';

interface ItemModalProps {
  mode: 'add' | 'edit';
  initialValues: AddItemFormValues;
  submitting: boolean;
  errorMessage?: string | undefined;
  onClose: () => void;
  onSubmit: (values: AddItemFormValues) => void;
}

// The parent mounts this only while a modal should be showing (rather than keeping it mounted
// and toggling an `isOpen` prop), so `useForm`'s `defaultValues` are correct from the very first
// render — no reset effect is needed, and none can race the values a caller types in right after
// opening it.
export function ItemModal({
  mode,
  initialValues,
  submitting,
  errorMessage,
  onClose,
  onSubmit,
}: ItemModalProps): JSX.Element {
  const titleId = useId();

  const {
    register,
    control,
    handleSubmit,
    formState: { errors },
  } = useForm<AddItemFormValues>({
    resolver: zodResolver(addItemFormSchema),
    defaultValues: initialValues,
  });
  const properties = useFieldArray({ control, name: 'properties' });

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
      }
    };
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('keydown', onKey);
    };
  }, [onClose]);

  return (
    <div
      className="item-modal__overlay"
      role="presentation"
      onClick={(event) => {
        if (event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      <div className="item-modal" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <h2 id={titleId}>{mode === 'edit' ? 'Edit item' : 'Add item'}</h2>
        <form
          noValidate
          onSubmit={(event) => {
            void handleSubmit(onSubmit)(event);
          }}
        >
          {errorMessage ? (
            <p role="alert" className="field__error">
              {errorMessage}
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
                  label="Name"
                  error={errors.properties?.[index]?.key?.message}
                  {...register(`properties.${index}.key`)}
                />
                <TextField label="Value" {...register(`properties.${index}.value`)} />
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

          <div style={{ display: 'flex', gap: 'var(--space-3)' }}>
            <Button type="button" variant="secondary" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" disabled={submitting}>
              Save item
            </Button>
          </div>
        </form>
      </div>
    </div>
  );
}
