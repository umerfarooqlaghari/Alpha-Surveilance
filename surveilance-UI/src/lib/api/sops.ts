import { apiFetch } from '@/lib/utils/auth';
import type {
    SopResponse,
    SopViolationTypeResponse,
    CreateSopRequest,
    UpdateSopRequest,
    CreateSopViolationTypeRequest,
    UpdateSopViolationTypeRequest
} from '@/types/admin';

const API_URL = '/api/admin/sops';

export async function getSops(): Promise<SopResponse[]> {
    const response = await apiFetch(API_URL);
    if (!response.ok) throw new Error('Failed to fetch SOPs');
    return response.json();
}

export async function getSop(id: string): Promise<SopResponse> {
    const response = await apiFetch(`${API_URL}/${id}`);
    if (!response.ok) throw new Error('Failed to fetch SOP');
    return response.json();
}

export async function createSop(data: CreateSopRequest): Promise<SopResponse> {
    const response = await apiFetch(API_URL, {
        method: 'POST',
        body: JSON.stringify(data),
    });
    if (!response.ok) throw new Error('Failed to create SOP');
    return response.json();
}

export async function updateSop(id: string, data: UpdateSopRequest): Promise<SopResponse> {
    const response = await apiFetch(`${API_URL}/${id}`, {
        method: 'PUT',
        body: JSON.stringify(data),
    });
    if (!response.ok) throw new Error('Failed to update SOP');
    return response.json();
}

export async function deleteSop(id: string): Promise<void> {
    const response = await apiFetch(`${API_URL}/${id}`, { method: 'DELETE' });
    if (!response.ok) throw new Error('Failed to delete SOP');
}

// VIOLATION TYPES

export async function createViolationType(sopId: string, data: CreateSopViolationTypeRequest): Promise<SopViolationTypeResponse> {
    const response = await apiFetch(`${API_URL}/${sopId}/violations`, {
        method: 'POST',
        body: JSON.stringify(data),
    });
    const errorData = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(errorData.error || 'Failed to create violation type');
    return errorData;
}

export async function updateViolationType(id: string, data: UpdateSopViolationTypeRequest): Promise<SopViolationTypeResponse> {
    const response = await apiFetch(`${API_URL}/violations/${id}`, {
        method: 'PUT',
        body: JSON.stringify(data),
    });
    const errorData = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(errorData.error || 'Failed to update violation type');
    return errorData;
}

export async function deleteViolationType(id: string): Promise<void> {
    const response = await apiFetch(`${API_URL}/violations/${id}`, { method: 'DELETE' });
    if (!response.ok) throw new Error('Failed to delete violation type');
}
