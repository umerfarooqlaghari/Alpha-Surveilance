import { apiFetch } from '@/lib/utils/auth';
import type {
    CreateCameraRequest,
    UpdateCameraRequest,
    CameraResponse
} from '@/types/admin';

const API_BASE = '/api/admin/cameras';

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

export async function getCameras(tenantId: string): Promise<CameraResponse[]> {
    const response = await apiFetch(`${API_BASE}?tenantId=${tenantId}`);

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

/**
 * SuperAdmin-only helper to retrieve the decrypted RTSP URL for an existing
 * camera. The BFF/Violation API enforces the SuperAdmin policy — this throws
 * for any non-SA caller. Used by the edit modal to pre-populate the field.
 */
export async function getCameraRtspUrl(id: string): Promise<string | null> {
    const response = await apiFetch(`${API_BASE}/${id}/rtsp-url`);
    if (response.status === 403 || response.status === 401) return null;
    if (!response.ok) return null;
    const data = await response.json().catch(() => null);
    return (data && typeof data.rtspUrl === 'string') ? data.rtspUrl : null;
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

export async function updateCameraStatus(id: string, status: number): Promise<CameraResponse> {
    const response = await apiFetch(`${API_BASE}/${id}/status`, {
        method: 'PATCH',
        body: JSON.stringify({ status }),
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to update camera status');
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
