import type { ConvoyReport } from '../../api/schemas/outcomes';

export function makeConvoyReport(overrides: Partial<ConvoyReport> = {}): ConvoyReport {
  return {
    convoyId: 7,
    arrivedAt: '2026-03-06T20:00:00',
    closedAt: null,
    budget: {
      budgetSet: true,
      lines: [
        { type: 'Fuel', plannedGbp: 800, actualGbp: 950, overBudget: true },
        { type: 'Ferry', plannedGbp: 400, actualGbp: 400, overBudget: false },
      ],
      plannedTotalGbp: 1200,
      actualTotalGbp: 1350,
      equipmentGbp: 0,
      anyOverBudget: true,
    },
    vehicles: [
      {
        vin: 'VIN-A',
        plate: 'PL-001',
        valueGbp: 4000,
        valueSource: 'Estimate',
        withdrawn: false,
        delivered: true,
        boxes: 3,
        byOutcome: [
          { outcome: 'Delivered', boxes: 1, valueGbp: 100 },
          { outcome: 'Seized', boxes: 1, valueGbp: 400 },
          { outcome: 'ReturnedToHub', boxes: 1, valueGbp: 30 },
        ],
        deliveredValueGbp: 100,
        receivers: [
          { organisation: 'Lviv Hospital 4', region: 'Lviv', boxes: 2, boxesDelivered: 1 },
          { organisation: 'Kyiv Clinic', region: 'Kyiv', boxes: 1, boxesDelivered: 0 },
        ],
      },
    ],
    totals: {
      boxes: 3,
      boxesDelivered: 1,
      boxesNotDelivered: 2,
      boxesUndecided: 0,
      deliveredValueGbp: 100,
      vehicleValueGbp: 4000,
    },
    ...overrides,
  };
}
