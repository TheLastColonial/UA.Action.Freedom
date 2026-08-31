import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';

import type { ReceiverDetailReadModel } from '../../api/receiverDetail';
import { TextField } from '../../components/form/fields';
import { detailFormSchema, emptyDetailForm } from './receiverModels';
import type { DetailFormValues } from './receiverModels';

interface ReceiverDetailFormProps {
  current: ReceiverDetailReadModel | null;
  submitting: boolean;
  errorMessage?: string | undefined;
  onSubmit: (values: DetailFormValues) => void;
  onCancel: () => void;
}

function toValues(detail: ReceiverDetailReadModel | null): DetailFormValues {
  if (!detail) {
    return emptyDetailForm();
  }
  return {
    contactName: detail.contactName,
    contactPhone: detail.contactPhone,
    addressLine1: detail.addressLine1,
    addressLine2: detail.addressLine2 ?? '',
    city: detail.city,
    postCode: detail.postCode ?? '',
  };
}

export function ReceiverDetailForm({
  current,
  submitting,
  errorMessage,
  onSubmit,
  onCancel,
}: ReceiverDetailFormProps): JSX.Element {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<DetailFormValues>({
    resolver: zodResolver(detailFormSchema),
    defaultValues: toValues(current),
  });

  return (
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
        label="Contact name"
        error={errors.contactName?.message}
        {...register('contactName')}
      />
      <TextField
        label="Contact phone"
        error={errors.contactPhone?.message}
        {...register('contactPhone')}
      />
      <TextField
        label="Address line 1"
        error={errors.addressLine1?.message}
        {...register('addressLine1')}
      />
      <TextField
        label="Address line 2"
        error={errors.addressLine2?.message}
        {...register('addressLine2')}
      />
      <TextField label="City" error={errors.city?.message} {...register('city')} />
      <TextField label="Postcode" error={errors.postCode?.message} {...register('postCode')} />

      <div style={{ display: 'flex', gap: 'var(--space-3)' }}>
        <button type="button" onClick={onCancel}>
          Cancel
        </button>
        <button type="submit" disabled={submitting}>
          {submitting ? 'Saving…' : 'Save delivery detail'}
        </button>
      </div>
    </form>
  );
}
