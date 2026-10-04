import { http, HttpResponse } from 'msw';
import type { RequestHandler } from 'msw';
import { z } from 'zod';

import { costTypeSchema } from '../../api/schemas/convoys';
import type {
  BudgetLineReadModel,
  BudgetSummaryReadModel,
  ConvoyCostReadModel,
  CostType,
  EquipmentItemReadModel,
  VehicleEquipmentReadModel,
} from '../../api/schemas/convoys';
import { problem } from './problem';

// Mirrors SetBudgetRequest and its validator: one line per cost type, no negative amounts.
const budgetBodySchema = z.object({
  lines: z
    .array(z.object({ type: costTypeSchema, plannedGbp: z.number().min(0) }))
    .refine((lines) => new Set(lines.map((line) => line.type)).size === lines.length),
});

// Mirrors AddCostRequest and its validator.
const costBodySchema = z.object({
  type: costTypeSchema,
  amountGbp: z.number().gt(0),
  vin: z.string().max(32).optional(),
  note: z.string().max(500).optional(),
});

const equipmentBodySchema = z.object({
  lines: z.array(
    z.object({
      equipmentItemId: z.number().int(),
      quantity: z.number().int().min(1),
      costGbp: z.number().min(0).optional(),
    }),
  ),
});

const COST_ORDER: readonly CostType[] = ['Fuel', 'Ferry', 'Hotel', 'Insurance', 'Other'];

export interface BudgetApi {
  /** Planned amounts per convoy. */
  lines: Map<number, BudgetLineReadModel[]>;
  costs: Map<number, ConvoyCostReadModel[]>;
  catalogue: EquipmentItemReadModel[];
  /** Equipment per `convoyId:vin`. */
  equipment: Map<string, VehicleEquipmentReadModel[]>;
  /** Costs held on bookings and policies per convoy: the API reads them, they are never entered. */
  booked: Map<number, { type: CostType; amountGbp: number }[]>;
  handlers: RequestHandler[];
}

/**
 * The budget, cost and equipment routes, as the API enforces them: only fuel and other are entered
 * (422 otherwise), equipment names a catalogued item, and the summary derives booked costs and counts
 * equipment under Other, which is where the real summary puts it.
 */
export function budgetApi(): BudgetApi {
  const lines = new Map<number, BudgetLineReadModel[]>();
  const costs = new Map<number, ConvoyCostReadModel[]>();
  const catalogue: EquipmentItemReadModel[] = [];
  const equipment = new Map<string, VehicleEquipmentReadModel[]>();
  const booked = new Map<number, { type: CostType; amountGbp: number }[]>();
  let nextCost = 1;
  let nextItem = 1;

  const idFrom = (raw: string | readonly string[] | undefined) => Number(String(raw));
  const key = (id: number, vin: string) => `${String(id)}:${vin}`;

  const summaryFor = (id: number): BudgetSummaryReadModel => {
    const planned = lines.get(id) ?? [];
    const equipmentGbp = [...equipment.entries()]
      .filter(([k]) => k.startsWith(`${String(id)}:`))
      .flatMap(([, list]) => list)
      .reduce((sum, line) => sum + line.countedCostGbp, 0);
    const actuals = [
      ...(costs.get(id) ?? []).map((cost) => ({ type: cost.type, amountGbp: cost.amountGbp })),
      ...(booked.get(id) ?? []),
      { type: 'Other' as const, amountGbp: equipmentGbp },
    ];
    const summaryLines = COST_ORDER.map((type) => {
      const line = planned.find((candidate) => candidate.type === type);
      const actualGbp = actuals
        .filter((actual) => actual.type === type)
        .reduce((sum, actual) => sum + actual.amountGbp, 0);
      return {
        type,
        plannedGbp: line?.plannedGbp ?? null,
        actualGbp,
        overBudget: line !== undefined && actualGbp > line.plannedGbp,
      };
    });
    return {
      budgetSet: planned.length > 0,
      lines: summaryLines,
      plannedTotalGbp: planned.reduce((sum, line) => sum + line.plannedGbp, 0),
      actualTotalGbp: summaryLines.reduce((sum, line) => sum + line.actualGbp, 0),
      equipmentGbp,
      anyOverBudget: summaryLines.some((line) => line.overBudget),
    };
  };

  const handlers: RequestHandler[] = [
    http.get('/convoys/:id/budget', ({ params }) =>
      HttpResponse.json(lines.get(idFrom(params['id'])) ?? []),
    ),

    http.put('/convoys/:id/budget', async ({ params, request }) => {
      const parsed = budgetBodySchema.safeParse(await request.json());
      if (!parsed.success) {
        return problem(400, 'The budget is not valid.');
      }
      lines.set(
        idFrom(params['id']),
        parsed.data.lines.map((line) => ({
          ...line,
          lastChangedByName: null,
          lastChangedAt: null,
        })),
      );
      return new HttpResponse(null, { status: 204 });
    }),

    http.get('/convoys/:id/budget/summary', ({ params }) =>
      HttpResponse.json(summaryFor(idFrom(params['id']))),
    ),

    http.get('/convoys/:id/costs', ({ params }) =>
      HttpResponse.json(costs.get(idFrom(params['id'])) ?? []),
    ),

    http.post('/convoys/:id/costs', async ({ params, request }) => {
      const id = idFrom(params['id']);
      const parsed = costBodySchema.safeParse(await request.json());
      if (!parsed.success) {
        return problem(400, 'The cost is not valid.');
      }
      if (parsed.data.type !== 'Fuel' && parsed.data.type !== 'Other') {
        return problem(
          422,
          'Ferry, hotel and insurance costs are held on their booking or policy and shown from it. Only fuel and other costs are entered.',
        );
      }
      const costId = nextCost++;
      costs.set(id, [
        ...(costs.get(id) ?? []),
        {
          id: costId,
          convoyId: id,
          type: parsed.data.type,
          amountGbp: parsed.data.amountGbp,
          vin: parsed.data.vin ?? null,
          note: parsed.data.note ?? null,
          lastChangedByName: null,
          lastChangedAt: null,
        },
      ]);
      return new HttpResponse(null, {
        status: 201,
        headers: { Location: `/convoys/${String(id)}/costs/${String(costId)}` },
      });
    }),

    http.delete('/convoys/:id/costs/:costId', ({ params }) => {
      const id = idFrom(params['id']);
      const costId = idFrom(params['costId']);
      const existing = costs.get(id) ?? [];
      if (!existing.some((cost) => cost.id === costId)) {
        return new HttpResponse(null, { status: 404 });
      }
      costs.set(
        id,
        existing.filter((cost) => cost.id !== costId),
      );
      return new HttpResponse(null, { status: 204 });
    }),

    http.get('/equipment-items', () => HttpResponse.json(catalogue)),

    http.post('/equipment-items', async ({ request }) => {
      const body = z
        .object({ name: z.string().min(1), unitCostGbp: z.number().min(0).optional() })
        .parse(await request.json());
      if (catalogue.some((item) => item.name.toLowerCase() === body.name.toLowerCase())) {
        return problem(409, 'That item is already in the equipment catalogue.');
      }
      const id = nextItem++;
      catalogue.push({ id, name: body.name, unitCostGbp: body.unitCostGbp ?? null });
      return new HttpResponse(null, {
        status: 201,
        headers: { Location: `/equipment-items/${String(id)}` },
      });
    }),

    http.get('/convoys/:id/vehicles/:vin/equipment', ({ params }) =>
      HttpResponse.json(equipment.get(key(idFrom(params['id']), String(params['vin']))) ?? []),
    ),

    http.put('/convoys/:id/vehicles/:vin/equipment', async ({ params, request }) => {
      const parsed = equipmentBodySchema.safeParse(await request.json());
      if (!parsed.success) {
        return problem(400, 'The equipment is not valid.');
      }
      const vin = String(params['vin']);
      const resolved: VehicleEquipmentReadModel[] = [];
      for (const line of parsed.data.lines) {
        const item = catalogue.find((candidate) => candidate.id === line.equipmentItemId);
        if (!item) {
          return problem(422, 'An equipment item is not in the catalogue.');
        }
        const costGbp = line.costGbp ?? null;
        resolved.push({
          vin,
          equipmentItemId: item.id,
          name: item.name,
          quantity: line.quantity,
          unitCostGbp: item.unitCostGbp,
          costGbp,
          countedCostGbp: costGbp ?? line.quantity * (item.unitCostGbp ?? 0),
        });
      }
      equipment.set(key(idFrom(params['id']), vin), resolved);
      return new HttpResponse(null, { status: 204 });
    }),
  ];

  return { lines, costs, catalogue, equipment, booked, handlers };
}
