import { getAuthHeaders } from '@/lib/utils/auth';
import type {
    CreateTenantRequest,
    UpdateTenantRequest,
    TenantResponse,
    TenantListResponse
} from '@/types/admin';

const API_BASE = '/api/admin/tenants';

export async function createTenant(data: CreateTenantRequest): Promise<TenantResponse> {
    const response = await fetch(API_BASE, {
        method: 'POST',
        headers: getAuthHeaders(),
        body: JSON.stringify(data),
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to create tenant');
    }

    return response.json();
}

export async function getTenants(pageNumber = 1, pageSize = 10): Promise<TenantListResponse> {
    const response = await fetch(`${API_BASE}?pageNumber=${pageNumber}&pageSize=${pageSize}`, {
        headers: getAuthHeaders(),
    });

    if (!response.ok) {
        throw new Error('Failed to fetch tenants');
    }

    return response.json();
}

export async function getTenant(id: string): Promise<TenantResponse> {
    const response = await fetch(`${API_BASE}/${id}`, {
        headers: getAuthHeaders(),
    });

    if (!response.ok) {
        throw new Error('Failed to fetch tenant');
    }

    return response.json();
}

export async function updateTenant(id: string, data: UpdateTenantRequest): Promise<TenantResponse> {
    const response = await fetch(`${API_BASE}/${id}`, {
        method: 'PUT',
        headers: getAuthHeaders(),
        body: JSON.stringify(data),
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to update tenant');
    }

    return response.json();
}

export async function uploadTenantLogo(id: string, file: File): Promise<{ logoUrl: string; publicId: string }> {
    const formData = new FormData();
    formData.append('file', file);

    const headers = getAuthHeaders() as Record<string, string>;
    delete headers['Content-Type'];

    const response = await fetch(`${API_BASE}/${id}/logo`, {
        method: 'POST',
        headers,
        body: formData,
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to upload logo');
    }

    return response.json();
}

export async function updateTenantStatus(id: string, status: number): Promise<TenantResponse> {
    const response = await fetch(`${API_BASE}/${id}/status`, {
        method: 'PATCH',
        headers: getAuthHeaders(),
        body: JSON.stringify({ status }),
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to update tenant status');
    }

    return response.json();
}

export async function deleteTenant(id: string): Promise<void> {
    const response = await fetch(`${API_BASE}/${id}`, {
        method: 'DELETE',
        headers: getAuthHeaders(),
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to delete tenant');
    }
}
