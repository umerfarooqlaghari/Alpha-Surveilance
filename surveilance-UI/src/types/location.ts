export type LocationStatus = 'Active' | 'Inactive';

export interface Location {
    id: string;
    tenantId: string;
    tenantName?: string;
    name: string;
    code: string;
    address?: string | null;
    city?: string | null;
    country?: string | null;
    timezone?: string | null;
    status: LocationStatus | string;
    cameraCount: number;
    createdAt: string;
    updatedAt?: string | null;
}

export interface CreateLocationRequest {
    /** Only honoured for SuperAdmin callers; tenant BFF overrides from JWT. */
    tenantId?: string;
    name: string;
    code: string;
    address?: string;
    city?: string;
    country?: string;
    timezone?: string;
}

export interface UpdateLocationRequest {
    name?: string;
    code?: string;
    address?: string;
    city?: string;
    country?: string;
    timezone?: string;
    /** 0 = Active, 1 = Inactive */
    status?: number;
}
