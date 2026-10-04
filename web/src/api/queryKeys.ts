export interface PageParams {
  page?: number;
  pageSize?: number;
}

export interface PeopleListParams extends PageParams {
  driversOnly?: boolean;
}

// One factory per slice. Mutations invalidate the narrowest safe prefix — see each slice's
// hooks module. A published truck list is a precondition for proposing a manifest, so
// publishing invalidates ['manifests'] as well as the convoy.
export const qk = {
  me: ['me'] as const,
  vehicles: {
    all: ['vehicles'] as const,
    list: (params: PageParams) => ['vehicles', 'list', params] as const,
    detail: (vin: string) => ['vehicles', 'detail', vin] as const,
  },
  people: {
    all: ['people'] as const,
    list: (params: PeopleListParams) => ['people', 'list', params] as const,
    detail: (id: string) => ['people', 'detail', id] as const,
  },
  donors: {
    all: ['donors'] as const,
    list: (params: PageParams) => ['donors', 'list', params] as const,
    detail: (id: string) => ['donors', 'detail', id] as const,
    donations: (id: string) => ['donors', id, 'donations'] as const,
    report: (id: string) => ['donors', id, 'report'] as const,
  },
  donations: {
    all: ['donations'] as const,
    list: (params: PageParams) => ['donations', 'list', params] as const,
  },
  convoys: {
    all: ['convoys'] as const,
    list: (params: PageParams) => ['convoys', 'list', params] as const,
    detail: (id: number) => ['convoys', 'detail', id] as const,
    route: (id: number) => ['convoys', id, 'route'] as const,
    vehicles: (id: number) => ['convoys', id, 'vehicles'] as const,
    vehicleCrew: (id: number, vin: string) => ['convoys', id, 'vehicles', vin, 'crew'] as const,
    insurance: (id: number, vin: string) => ['convoys', id, 'vehicles', vin, 'insurance'] as const,
    vehicleBoxes: (id: number, vin: string) => ['convoys', id, 'vehicles', vin, 'boxes'] as const,
    ferry: (id: number, vin: string) => ['convoys', id, 'vehicles', vin, 'ferry'] as const,
    readiness: (id: number) => ['convoys', id, 'readiness'] as const,
  },
  receivers: {
    all: ['receivers'] as const,
    list: (params: PageParams) => ['receivers', 'list', params] as const,
    detail: (ref: string) => ['receivers', 'detail', ref] as const,
    usage: (ref: string) => ['receivers', ref, 'usage'] as const,
    sensitive: (ref: string) => ['receivers', ref, 'sensitive-detail'] as const,
  },
  boxes: {
    all: ['boxes'] as const,
    list: (params: PageParams) => ['boxes', 'list', params] as const,
    detail: (id: number) => ['boxes', 'detail', id] as const,
    items: (id: number) => ['boxes', id, 'items'] as const,
    qrCode: (id: number) => ['boxes', id, 'qr-code'] as const,
    label: (id: number) => ['boxes', id, 'label'] as const,
    bay: (id: number) => ['boxes', id, 'bay'] as const,
    bayHistory: (id: number) => ['boxes', id, 'bay', 'history'] as const,
  },
  categories: {
    all: ['categories'] as const,
    list: ['categories', 'list'] as const,
    detail: (id: number) => ['categories', 'detail', id] as const,
  },
  locations: {
    all: ['locations'] as const,
    list: (params: PageParams) => ['locations', 'list', params] as const,
    detail: (id: number) => ['locations', 'detail', id] as const,
    bays: (id: number) => ['locations', id, 'bays'] as const,
  },
  declarations: {
    convoy: (convoyId: number) => ['declarations', convoyId] as const,
    vehicle: (convoyId: number, vin: string) => ['declarations', convoyId, vin] as const,
    list: (convoyId: number, vin: string) => ['declarations', convoyId, vin, 'list'] as const,
    ens: (convoyId: number, vin: string) => ['declarations', convoyId, vin, 'ens'] as const,
    tasks: (convoyId: number) => ['declaration-tasks', convoyId] as const,
  },
  manifests: {
    all: ['manifests'] as const,
    list: (params: PageParams) => ['manifests', 'list', params] as const,
    detail: (id: string) => ['manifests', 'detail', id] as const,
    crew: (id: string) => ['manifests', id, 'crew'] as const,
    boxes: (id: string) => ['manifests', id, 'boxes'] as const,
    weight: (id: string) => ['manifests', id, 'weight'] as const,
  },
} as const;
