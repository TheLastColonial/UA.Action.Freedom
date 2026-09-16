import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';

import { useBays, useCreateBay, useDeleteBay } from '../../api/locations';
import { ApiDomainProblem } from '../../api/problem';
import { Button } from '../../components/Button';
import { Gate } from '../../components/Gate';
import { PageSkeleton } from '../../components/PageSkeleton';
import { TextField } from '../../components/form/fields';
import { bayFormSchema, bayFormToRequest, emptyBayForm } from './locationModels';
import type { BayFormValues } from './locationModels';

interface BaysPanelProps {
  locationId: number;
}

export function BaysPanel({ locationId }: BaysPanelProps): JSX.Element {
  const query = useBays(locationId);
  const create = useCreateBay(locationId);
  const remove = useDeleteBay(locationId);

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<BayFormValues>({
    resolver: zodResolver(bayFormSchema),
    defaultValues: emptyBayForm(),
  });

  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The bays could not be loaded.</p>;
  }

  const bays = 'parentMissing' in query.data ? [] : query.data;
  const message =
    create.error instanceof ApiDomainProblem
      ? (create.error.detail ?? create.error.message)
      : undefined;

  return (
    <div>
      <h2>Bays</h2>

      {bays.length === 0 ? (
        <p>No bays at this location yet.</p>
      ) : (
        <ul>
          {bays.map((bay) => (
            <li
              key={bay.id}
              style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-3)' }}
            >
              {bay.code}
              <Gate policy="locations:write">
                <Button
                  type="button"
                  variant="danger"
                  disabled={remove.isPending}
                  onClick={() => {
                    remove.mutate(bay.id);
                  }}
                >
                  Remove
                </Button>
              </Gate>
            </li>
          ))}
        </ul>
      )}

      <Gate policy="locations:write">
        <form
          noValidate
          onSubmit={(event) => {
            void handleSubmit((values) => {
              create.mutate(bayFormToRequest(values), {
                onSuccess: () => {
                  reset();
                },
              });
            })(event);
          }}
        >
          {message ? (
            <p role="alert" className="field__error">
              {message}
            </p>
          ) : null}
          <TextField label="Bay code" error={errors.code?.message} {...register('code')} />
          <Button type="submit" disabled={create.isPending}>
            Add bay
          </Button>
        </form>
      </Gate>
    </div>
  );
}
