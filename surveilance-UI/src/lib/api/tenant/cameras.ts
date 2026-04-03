import { getAuthHeaders } from '@/lib/utils/auth';

import type {
    CreateCameraRequest,
    UpdateCameraRequest,
    CameraResponse
} from '@/types/admin';

const API_BASE = '/api/tenant/cameras';

export async function getCameras(): Promise<CameraResponse[]> {
    const response = await fetch(API_BASE, {
        headers: getAuthHeaders()
    });

    if (!response.ok) {
        throw new Error('Failed to fetch cameras');
    }

    return response.json();
}

export async function getCamera(id: string): Promise<CameraResponse> {
    const response = await fetch(`${API_BASE}/${id}`, {
        headers: getAuthHeaders()
    });

    if (!response.ok) {
        throw new Error('Failed to fetch camera');
    }

    return response.json();
}

export async function createCamera(data: CreateCameraRequest): Promise<CameraResponse> {
    const response = await fetch(API_BASE, {
        method: 'POST',
        headers: {
            ...getAuthHeaders() as Record<string, string>,
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(data),
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to create camera');
    }

    return response.json();
}

export async function updateCamera(id: string, data: UpdateCameraRequest): Promise<CameraResponse> {
    const response = await fetch(`${API_BASE}/${id}`, {
        method: 'PUT',
        headers: {
            ...getAuthHeaders() as Record<string, string>,
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(data),
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to update camera');
    }

    return response.json();
}

export async function deleteCamera(id: string): Promise<void> {
    const response = await fetch(`${API_BASE}/${id}`, {
        method: 'DELETE',
        headers: getAuthHeaders()
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to delete camera');
    }
}
