import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import {
  useAddCost,
  useBudget,
  useBudgetSummary,
  useCosts,
  useDeleteCost,
  useSetBudget,
} from '../../api/budget';
import { useConvoyVehicles } from '../../api/convoys';
import { ApiDomainProblem } from '../../api/problem';
import { COST_TYPES, ENTERED_COST_TYPES } from '../../api/schemas/convoys';
import type { BudgetLineReadModel, CostType } from '../../api/schemas/convoys';
import { Button } from '../../components/Button';
import { DetailCard } from '../../components/DetailCard';
import { Gate } from '../../components/Gate';
import { SelectField, TextField } from '../../components/form/fields';
import { PageSkeleton } from '../../components/PageSkeleton';

const gbp = (amount: number) => `£${amount.toFixed(2)}`;

const amountText = z
  .string()
  .refine((value) => value.trim() === '' || Number(value) >= 0, 'Amount cannot be negative');

const budgetFormSchema = z.object({
  Fuel: amountText,
  Ferry: amountText,
  Hotel: amountText,
  Insurance: amountText,
  Other: amountText,
});
type BudgetFormValues = z.infer<typeof budgetFormSchema>;

const budgetToForm = (lines: readonly BudgetLineReadModel[]): BudgetFormValues => {
  const planned = (type: CostType) => {
    const line = lines.find((candidate) => candidate.type === type);
    return line ? String(line.plannedGbp) : '';
  };
  return {
    Fuel: planned('Fuel'),
    Ferry: planned('Ferry'),
    Hotel: planned('Hotel'),
    Insurance: planned('Insurance'),
    Other: planned('Other'),
  };
};

const costFormSchema = z.object({
  type: z.enum(['Fuel', 'Other']),
  amountGbp: z
    .string()
    .refine((value) => value.trim() !== '' && Number(value) > 0, 'Amount must be more than zero'),
  vin: z.string(),
  note: z.string().max(500, 'Note must be 500 characters or fewer'),
});
type CostFormValues = z.infer<typeof costFormSchema>;

interface ConvoyBudgetPanelProps {
  convoyId: number;
}

/**
 * A convoy's budget (O12): a planned amount per cost type, the costs entered against it and the
 * summary beside the costs already held on bookings. Allocating it is a step in creating a convoy,
 * and it can be skipped: a budget is not required to depart (O37), so this only ever advises.
 */
export function ConvoyBudgetPanel({ convoyId }: ConvoyBudgetPanelProps): JSX.Element {
  const budget = useBudget(convoyId);
  const summary = useBudgetSummary(convoyId);
  const costs = useCosts(convoyId);
  const deleteCost = useDeleteCost(convoyId);

  if (budget.isPending || summary.isPending || costs.isPending) {
    return <PageSkeleton />;
  }
  if (budget.isError || summary.isError || costs.isError) {
    return <p role="alert">The budget could not be loaded.</p>;
  }

  return (
    <div>
      <DetailCard title="Budget against actual costs">
        {summary.data.budgetSet ? null : (
          <p role="status">No budget set. You can allocate one now or come back to it.</p>
        )}
        <table>
          <thead>
            <tr>
              <th scope="col">Cost</th>
              <th scope="col">Budget</th>
              <th scope="col">Actual</th>
              <th scope="col">Status</th>
            </tr>
          </thead>
          <tbody>
            {summary.data.lines.map((line) => (
              <tr key={line.type}>
                <th scope="row">{line.type}</th>
                <td>{line.plannedGbp === null ? '–' : gbp(line.plannedGbp)}</td>
                <td>{gbp(line.actualGbp)}</td>
                <td>{line.overBudget ? 'Over budget' : ''}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <p className="field__hint">
          Ferry, hotel and insurance are read from their bookings and policies. Equipment bought for
          vehicles ({gbp(summary.data.equipmentGbp)}) counts under Other.
        </p>
      </DetailCard>

      <Gate policy="convoys:write">
        <BudgetForm
          convoyId={convoyId}
          key={budget.dataUpdatedAt}
          initialValues={budgetToForm(budget.data)}
        />
      </Gate>

      <DetailCard title="Costs entered">
        {costs.data.length === 0 ? (
          <p>No costs entered yet.</p>
        ) : (
          <ul>
            {costs.data.map((cost) => (
              <li key={cost.id}>
                {cost.type} {gbp(cost.amountGbp)}
                {cost.vin ? ` (${cost.vin})` : ''}
                {cost.note ? `: ${cost.note}` : ''}
                {cost.lastChangedByName ? `, entered by ${cost.lastChangedByName}` : ''}
                <Gate policy="convoys:write">
                  <Button
                    type="button"
                    variant="danger"
                    disabled={deleteCost.isPending}
                    onClick={() => {
                      deleteCost.mutate(cost.id);
                    }}
                  >
                    {`Delete ${cost.type} cost ${gbp(cost.amountGbp)}`}
                  </Button>
                </Gate>
              </li>
            ))}
          </ul>
        )}
        <Gate policy="convoys:write">
          <CostForm convoyId={convoyId} />
        </Gate>
      </DetailCard>
    </div>
  );
}

function BudgetForm({
  convoyId,
  initialValues,
}: {
  convoyId: number;
  initialValues: BudgetFormValues;
}): JSX.Element {
  const save = useSetBudget(convoyId);
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<BudgetFormValues>({
    resolver: zodResolver(budgetFormSchema),
    defaultValues: initialValues,
  });

  return (
    <form
      noValidate
      aria-label="Budget"
      onSubmit={(event) => {
        void handleSubmit((values) => {
          save.mutate({
            lines: COST_TYPES.filter((type) => values[type].trim() !== '').map((type) => ({
              type,
              plannedGbp: Number(values[type]),
            })),
          });
        })(event);
      }}
    >
      {COST_TYPES.map((type) => (
        <TextField
          key={type}
          label={`${type} budget (£)`}
          inputMode="decimal"
          error={errors[type]?.message}
          {...register(type)}
        />
      ))}
      {save.isError ? (
        <p role="alert" className="field__error">
          {save.error.message}
        </p>
      ) : null}
      <Button type="submit" disabled={save.isPending}>
        {save.isPending ? 'Saving…' : 'Save budget'}
      </Button>
    </form>
  );
}

function CostForm({ convoyId }: { convoyId: number }): JSX.Element {
  const add = useAddCost(convoyId);
  const vehicles = useConvoyVehicles(convoyId);
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<CostFormValues>({
    resolver: zodResolver(costFormSchema),
    defaultValues: { type: 'Fuel', amountGbp: '', vin: '', note: '' },
  });

  const fleet = vehicles.data && !('parentMissing' in vehicles.data) ? vehicles.data : [];
  const problem =
    add.error instanceof ApiDomainProblem ? (add.error.detail ?? add.error.message) : undefined;

  return (
    <form
      noValidate
      aria-label="Enter a cost"
      onSubmit={(event) => {
        void handleSubmit((values) => {
          add.mutate(
            {
              type: values.type,
              amountGbp: Number(values.amountGbp),
              ...(values.vin === '' ? {} : { vin: values.vin }),
              ...(values.note.trim() === '' ? {} : { note: values.note.trim() }),
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
      <SelectField
        label="Cost type"
        options={ENTERED_COST_TYPES.map((type) => ({ value: type, label: type }))}
        {...register('type')}
      />
      <TextField
        label="Amount (£)"
        inputMode="decimal"
        error={errors.amountGbp?.message}
        {...register('amountGbp')}
      />
      <SelectField
        label="Vehicle"
        options={[
          { value: '', label: 'No specific vehicle' },
          ...fleet.map((vehicle) => ({ value: vehicle.vin, label: vehicle.plate })),
        ]}
        {...register('vin')}
      />
      <TextField label="Note" error={errors.note?.message} {...register('note')} />
      {problem ? (
        <p role="alert" className="field__error">
          {problem}
        </p>
      ) : null}
      <Button type="submit" disabled={add.isPending}>
        {add.isPending ? 'Saving…' : 'Add cost'}
      </Button>
    </form>
  );
}
