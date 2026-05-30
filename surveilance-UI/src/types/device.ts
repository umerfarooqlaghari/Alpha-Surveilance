export interface EdgeDeviceResponse {
    id: string;
    tenantId: string;
    tenantName?: string;
    locationId?: string;
    locationName?: string;
    deviceIdentifier: string;
    hostname: string;
    displayName: string;
    status: 'Active' | 'Disabled';
    lastSeenAt?: string;
    cameraCount: number;
    distinctLocationIds: string[];
    registeredAt: string;
    updatedAt?: string;
}

export interface CreateEdgeDeviceRequest {
    tenantId: string;
    locationId?: string;
    deviceIdentifier: string;
    displayName: string;
    hostname?: string;
}

export interface UpdateEdgeDeviceRequest {
    displayName?: string;
    hostname?: string;
    locationId?: string;
    /** 0 = Active, 1 = Disabled */
    status?: number;
}

export type DeviceOnlineStatus = 'online' | 'idle' | 'offline' | 'unknown';

export function getOnlineStatus(lastSeenAt?: string): DeviceOnlineStatus {
    if (!lastSeenAt) return 'unknown';
    const diffMs = Date.now() - new Date(lastSeenAt).getTime();
    if (diffMs < 2 * 60 * 1000) return 'online';
    if (diffMs < 10 * 60 * 1000) return 'idle';
    return 'offline';
}
