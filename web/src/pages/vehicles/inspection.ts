import type { InspectionStatus } from '../../api/schemas/vehicles';

export const INSPECTION_STATUS_LABELS: Readonly<Record<InspectionStatus, string>> = {
  Pending: 'Not yet inspected',
  Inspecting: 'Currently being serviced',
  Passed: 'Ready for convoy',
  Failed: 'Issues found',
};
