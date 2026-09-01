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
  convoys: {
    all: ['convoys'] as const,
    list: (params: PageParams) => ['convoys', 'list', params] as const,
    detail: (id: number) => ['convoys', 'detail', id] as const,
    route: (id: number) => ['convoys', id, 'route'] as const,
    vehicles: (id: number) => ['convoys', id, 'vehicles'] as const,
  },
  receivers: {
    all: ['receivers'] as const,
    list: (params: PageParams) => ['receivers', 'list', params] as const,
    detail: (ref: string) => ['receivers', 'detail', ref] as const,
    sensitive: (ref: string) => ['receivers', ref, 'sensitive-detail'] as const,
  },
  boxes: {
    all: ['boxes'] as const,
    list: (params: PageParams) => ['boxes', 'list', params] as const,
    detail: (id: number) => ['boxes', 'detail', id] as const,
    items: (id: number) => ['boxes', id, 'items'] as const,
    qrCode: (id: number) => ['boxes', id, 'qr-code'] as const,
    label: (id: number) => ['boxes', id, 'label'] as const,
  },
  manifests: {
    all: ['manifests'] as const,
    list: (params: PageParams) => ['manifests', 'list', params] as const,
    detail: (id: string) => ['manifests', 'detail', id] as const,
    teams: (id: string) => ['manifests', id, 'teams'] as const,
    boxes: (id: string) => ['manifests', id, 'boxes'] as const,
    weight: (id: string) => ['manifests', id, 'weight'] as const,
  },
} as const;
