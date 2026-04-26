import { apiFetch } from '@/lib/utils/auth';

import type {
    CreateCameraRequest,
    UpdateCameraRequest,
    CameraResponse
} from '@/types/admin';

const API_BASE = '/api/tenant/cameras';

export async function getCameras(): Promise<CameraResponse[]> {
    const response = await apiFetch(API_BASE);

    if (!response.ok) {
        throw new Error('Failed to fetch cameras');
    }

    return response.json();
}

export async function getCamera(id: string): Promise<CameraResponse> {
    const response = await apiFetch(`${API_BASE}/${id}`);

    if (!response.ok) {
        throw new Error('Failed to fetch camera');
    }

    return response.json();
}

export async function createCamera(data: CreateCameraRequest): Promise<CameraResponse> {
    const response = await apiFetch(API_BASE, {
        method: 'POST',
        body: JSON.stringify(data),
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to create camera');
    }

    return response.json();
}

export async function updateCamera(id: string, data: UpdateCameraRequest): Promise<CameraResponse> {
    const response = await apiFetch(`${API_BASE}/${id}`, {
        method: 'PUT',
        body: JSON.stringify(data),
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to update camera');
    }

    return response.json();
}

export async function deleteCamera(id: string): Promise<void> {
    const response = await apiFetch(`${API_BASE}/${id}`, { method: 'DELETE' });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to delete camera');
    }
}
