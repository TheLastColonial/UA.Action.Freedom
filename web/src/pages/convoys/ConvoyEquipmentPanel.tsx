import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import {
  useAddEquipmentItem,
  useEquipmentItems,
  useSetVehicleEquipment,
  useVehicleEquipment,
} from '../../api/budget';
import { useConvoyVehicles } from '../../api/convoys';
import { ApiDomainProblem } from '../../api/problem';
import type { EquipmentItemReadModel, VehicleEquipmentReadModel } from '../../api/schemas/convoys';
import { Button } from '../../components/Button';
import { Gate } from '../../components/Gate';
import { SelectField, TextField } from '../../components/form/fields';
import { PageSkeleton } from '../../components/PageSkeleton';
import { Spinner } from '../../components/Spinner';

const gbp = (amount: number) => `£${amount.toFixed(2)}`;

interface ConvoyEquipmentPanelProps {
  convoyId: number;
}

/**
 * Equipment the charity buys for each vehicle, such as warning triangles (O13). A step in creating a
 * convoy that can be skipped and returned to. It has no donor, is accounted for apart from
 * donations, and is not part of the value delivered.
 */
export function ConvoyEquipmentPanel({ convoyId }: ConvoyEquipmentPanelProps): JSX.Element {
  const vehicles = useConvoyVehicles(convoyId);
  const items = useEquipmentItems();

  if (vehicles.isPending || items.isPending) {
    return <PageSkeleton />;
  }
  if (vehicles.isError || items.isError) {
    return <p role="alert">The equipment could not be loaded.</p>;
  }

  const fleet = 'parentMissing' in vehicles.data ? [] : vehicles.data;
  if (fleet.length === 0) {
    return <p>Put vehicles on the truck list before adding equipment.</p>;
  }

  return (
    <div>
      <Gate policy="convoys:write">
        <CatalogueForm />
      </Gate>
      {fleet.map((vehicle) => (
        <section key={vehicle.vin} aria-label={`Equipment for ${vehicle.plate}`}>
          <h3>{vehicle.plate}</h3>
          <VehicleEquipment
            convoyId={convoyId}
            vin={vehicle.vin}
            plate={vehicle.plate}
            catalogue={items.data}
          />
        </section>
      ))}
    </div>
  );
}

function VehicleEquipment({
  convoyId,
  vin,
  plate,
  catalogue,
}: {
  convoyId: number;
  vin: string;
  plate: string;
  catalogue: readonly EquipmentItemReadModel[];
}): JSX.Element {
  const query = useVehicleEquipment(convoyId, vin);
  const save = useSetVehicleEquipment(convoyId, vin);

  if (query.isPending) {
    return <Spinner label="Loading equipment…" />;
  }
  if (query.isError) {
    return <p role="alert">The equipment for {plate} could not be loaded.</p>;
  }

  const lines = query.data;
  const request = (next: readonly VehicleEquipmentReadModel[]) => ({
    lines: next.map((line) => ({
      equipmentItemId: line.equipmentItemId,
      quantity: line.quantity,
      ...(line.costGbp === null ? {} : { costGbp: line.costGbp }),
    })),
  });

  return (
    <div>
      {lines.length === 0 ? (
        <p>No equipment added.</p>
      ) : (
        <ul>
          {lines.map((line) => (
            <li key={line.equipmentItemId}>
              {line.quantity} × {line.name} ({gbp(line.countedCostGbp)})
              <Gate policy="convoys:write">
                <Button
                  type="button"
                  variant="danger"
                  disabled={save.isPending}
                  onClick={() => {
                    save.mutate(
                      request(
                        lines.filter((other) => other.equipmentItemId !== line.equipmentItemId),
                      ),
                    );
                  }}
                >
                  {`Remove ${line.name} from ${plate}`}
                </Button>
              </Gate>
            </li>
          ))}
        </ul>
      )}
      <Gate policy="convoys:write">
        <AddLineForm
          plate={plate}
          catalogue={catalogue}
          pending={save.isPending}
          error={
            save.error instanceof ApiDomainProblem
              ? (save.error.detail ?? save.error.message)
              : undefined
          }
          onAdd={(equipmentItemId, quantity) => {
            const kept = lines.filter((line) => line.equipmentItemId !== equipmentItemId);
            const previous = lines.find((line) => line.equipmentItemId === equipmentItemId);
            save.mutate({
              lines: [
                ...request(kept).lines,
                { equipmentItemId, quantity: quantity + (previous?.quantity ?? 0) },
              ],
            });
          }}
        />
      </Gate>
    </div>
  );
}

const addLineSchema = z.object({
  itemId: z.string().min(1, 'Choose an item'),
  quantity: z
    .string()
    .refine(
      (value) => Number.isInteger(Number(value)) && Number(value) >= 1,
      'Quantity must be 1 or more',
    ),
});
type AddLineValues = z.infer<typeof addLineSchema>;

function AddLineForm({
  plate,
  catalogue,
  pending,
  error,
  onAdd,
}: {
  plate: string;
  catalogue: readonly EquipmentItemReadModel[];
  pending: boolean;
  error: string | undefined;
  onAdd: (equipmentItemId: number, quantity: number) => void;
}): JSX.Element {
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<AddLineValues>({
    resolver: zodResolver(addLineSchema),
    defaultValues: { itemId: '', quantity: '1' },
  });

  return (
    <form
      noValidate
      aria-label={`Add equipment to ${plate}`}
      onSubmit={(event) => {
        void handleSubmit((values) => {
          onAdd(Number(values.itemId), Number(values.quantity));
          reset();
        })(event);
      }}
    >
      <SelectField
        label={`Equipment item for ${plate}`}
        error={errors.itemId?.message}
        options={[
          { value: '', label: 'Choose equipment' },
          ...catalogue.map((item) => ({ value: String(item.id), label: item.name })),
        ]}
        {...register('itemId')}
      />
      <TextField
        label={`Quantity for ${plate}`}
        inputMode="numeric"
        error={errors.quantity?.message}
        {...register('quantity')}
      />
      {error ? (
        <p role="alert" className="field__error">
          {error}
        </p>
      ) : null}
      <Button type="submit" disabled={pending}>
        Add equipment
      </Button>
    </form>
  );
}

const catalogueSchema = z.object({
  name: z.string().trim().min(1, 'Name is required').max(200),
  unitCostGbp: z
    .string()
    .refine((value) => value.trim() === '' || Number(value) >= 0, 'Cost cannot be negative'),
});
type CatalogueValues = z.infer<typeof catalogueSchema>;

function CatalogueForm(): JSX.Element {
  const add = useAddEquipmentItem();
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<CatalogueValues>({
    resolver: zodResolver(catalogueSchema),
    defaultValues: { name: '', unitCostGbp: '' },
  });
  const problem =
    add.error instanceof ApiDomainProblem ? (add.error.detail ?? add.error.message) : undefined;

  return (
    <form
      noValidate
      aria-label="New equipment item"
      onSubmit={(event) => {
        void handleSubmit((values) => {
          add.mutate(
            {
              name: values.name.trim(),
              ...(values.unitCostGbp.trim() === ''
                ? {}
                : { unitCostGbp: Number(values.unitCostGbp) }),
            },
            {
              onSuccess: () => {
                reset();
              },
            },
          );
        })(event);
      }}
    >
      <TextField label="Equipment name" error={errors.name?.message} {...register('name')} />
      <TextField
        label="Unit cost (£)"
        inputMode="decimal"
        error={errors.unitCostGbp?.message}
        {...register('unitCostGbp')}
      />
      {problem ? (
        <p role="alert" className="field__error">
          {problem}
        </p>
      ) : null}
      <Button type="submit" disabled={add.isPending}>
        Add to catalogue
      </Button>
    </form>
  );
}
