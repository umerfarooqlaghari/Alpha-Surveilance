import { apiFetch } from '@/lib/utils/auth';
import type { Location, CreateLocationRequest, UpdateLocationRequest } from '@/types/location';

const API_BASE = '/api/tenant/locations';

export async function getLocations(params?: { search?: string }): Promise<Location[]> {
    const query = new URLSearchParams();
    if (params?.search) query.append('search', params.search);
    const url = query.toString() ? `${API_BASE}?${query}` : API_BASE;
    const response = await apiFetch(url);
    if (!response.ok) throw new Error('Failed to fetch locations');
    return response.json();
}

export async function getLocation(id: string): Promise<Location> {
    const response = await apiFetch(`${API_BASE}/${id}`);
    if (!response.ok) throw new Error('Failed to fetch location');
    return response.json();
}

export async function createLocation(data: CreateLocationRequest): Promise<Location> {
    const response = await apiFetch(API_BASE, {
        method: 'POST',
        body: JSON.stringify(data),
    });
    if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        throw new Error(error.error || 'Failed to create location');
    }
    return response.json();
}

export async function updateLocation(id: string, data: UpdateLocationRequest): Promise<Location> {
    const response = await apiFetch(`${API_BASE}/${id}`, {
        method: 'PUT',
        body: JSON.stringify(data),
    });
    if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        throw new Error(error.error || 'Failed to update location');
    }
    return response.json();
}

export async function deleteLocation(id: string): Promise<void> {
    const response = await apiFetch(`${API_BASE}/${id}`, { method: 'DELETE' });
    if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        throw new Error(error.error || 'Failed to delete location');
    }
}
