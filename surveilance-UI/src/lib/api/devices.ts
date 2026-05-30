import { apiFetch } from '@/lib/utils/auth';
import type { EdgeDeviceResponse, CreateEdgeDeviceRequest, UpdateEdgeDeviceRequest } from '@/types/device';

const API_BASE = '/api/admin/devices';

export const getDevices = async (tenantId?: string): Promise<EdgeDeviceResponse[]> => {
    const qs = tenantId
        ? `?${new URLSearchParams({ tenantId }).toString()}`
        : '';
    const response = await apiFetch(`${API_BASE}${qs}`);
    if (!response.ok) throw new Error('Failed to fetch devices');
    return response.json();
};

export async function getDevice(id: string): Promise<EdgeDeviceResponse> {
    const response = await apiFetch(`${API_BASE}/${id}`);
    if (!response.ok) throw new Error('Failed to fetch device');
    return response.json();
}

export async function createDevice(data: CreateEdgeDeviceRequest): Promise<EdgeDeviceResponse> {
    const response = await apiFetch(API_BASE, {
        method: 'POST',
        body: JSON.stringify(data),
    });
    if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        throw new Error(error.error || 'Failed to create device');
    }
    return response.json();
}

export async function updateDevice(id: string, data: UpdateEdgeDeviceRequest): Promise<EdgeDeviceResponse> {
    const response = await apiFetch(`${API_BASE}/${id}`, {
        method: 'PATCH',
        body: JSON.stringify(data),
    });
    if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        throw new Error(error.error || 'Failed to update device');
    }
    return response.json();
}

export async function deleteDevice(id: string): Promise<void> {
    const response = await apiFetch(`${API_BASE}/${id}`, { method: 'DELETE' });
    if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        throw new Error(error.error || 'Failed to delete device');
    }
}

export async function assignCameraToDevice(deviceId: string, cameraId: string): Promise<void> {
    const response = await apiFetch(`${API_BASE}/${deviceId}/cameras/${cameraId}`, {
        method: 'POST',
    });
    if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        throw new Error(error.error || 'Failed to assign camera');
    }
}

export async function unassignCameraFromDevice(deviceId: string, cameraId: string): Promise<void> {
    const response = await apiFetch(`${API_BASE}/${deviceId}/cameras/${cameraId}`, {
        method: 'DELETE',
    });
    if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        throw new Error(error.error || 'Failed to unassign camera');
    }
}
