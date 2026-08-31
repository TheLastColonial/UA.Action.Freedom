import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useEffect } from 'react';
import { useFieldArray, useForm } from 'react-hook-form';

import { useConvoyRoute, useReplaceConvoyRoute } from '../../api/convoys';
import { ApiDomainProblem } from '../../api/problem';
import { PageSkeleton } from '../../components/PageSkeleton';
import { TextField } from '../../components/form/fields';
import {
  emptyRouteStop,
  routeFormSchema,
  routeStopsFormToRequest,
  routeStopsToFormValues,
} from './routeModel';
import type { RouteFormValues } from './routeModel';

interface RouteEditorProps {
  convoyId: number;
  disabled: boolean;
}

export function RouteEditor({ convoyId, disabled }: RouteEditorProps): JSX.Element {
  const query = useConvoyRoute(convoyId);
  const save = useReplaceConvoyRoute(convoyId);

  const {
    register,
    control,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<RouteFormValues>({
    resolver: zodResolver(routeFormSchema),
    defaultValues: { stops: [] },
  });

  const { fields, append, remove, move } = useFieldArray({ control, name: 'stops' });

  useEffect(() => {
    if (query.isSuccess && !('parentMissing' in query.data)) {
      reset({ stops: routeStopsToFormValues(query.data) });
    }
  }, [query.isSuccess, query.data, reset]);

  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The route could not be loaded.</p>;
  }

  const errorMessage =
    save.error instanceof ApiDomainProblem ? (save.error.detail ?? save.error.message) : undefined;

  return (
    <form
      noValidate
      onSubmit={(event) => {
        void handleSubmit((values) => {
          save.mutate(routeStopsFormToRequest(values.stops));
        })(event);
      }}
    >
      {disabled ? <p role="status">The truck list is published — the route is now fixed.</p> : null}
      {errorMessage ? (
        <p role="alert" className="field__error">
          {errorMessage}
        </p>
      ) : null}
      {errors.stops?.root?.message ? (
        <p role="alert" className="field__error">
          {errors.stops.root.message}
        </p>
      ) : null}

      <ol>
        {fields.map((field, index) => (
          <li key={field.id}>
            <fieldset disabled={disabled}>
              <legend>Stop {index + 1}</legend>
              <TextField label="House" {...register(`stops.${index}.house`)} />
              <TextField label="Street" {...register(`stops.${index}.street`)} />
              <TextField label="City" {...register(`stops.${index}.city`)} />
              <TextField label="Country" {...register(`stops.${index}.country`)} />
              <TextField
                label="Postcode"
                error={errors.stops?.[index]?.postcode?.message}
                {...register(`stops.${index}.postcode`)}
              />
              <div style={{ display: 'flex', gap: 'var(--space-2)' }}>
                <button
                  type="button"
                  disabled={index === 0}
                  onClick={() => {
                    move(index, index - 1);
                  }}
                >
                  Move up
                </button>
                <button
                  type="button"
                  disabled={index === fields.length - 1}
                  onClick={() => {
                    move(index, index + 1);
                  }}
                >
                  Move down
                </button>
                <button
                  type="button"
                  onClick={() => {
                    remove(index);
                  }}
                >
                  Remove stop
                </button>
              </div>
            </fieldset>
          </li>
        ))}
      </ol>

      {!disabled ? (
        <div style={{ display: 'flex', gap: 'var(--space-3)' }}>
          <button
            type="button"
            onClick={() => {
              append(emptyRouteStop());
            }}
          >
            Add stop
          </button>
          <button type="submit" disabled={save.isPending}>
            {save.isPending ? 'Saving…' : 'Save route'}
          </button>
        </div>
      ) : null}
    </form>
  );
}
