import { apiFetch } from '@/lib/utils/auth';
import type { AiModelResponse, RegisterAiModelRequest } from '@/types/admin';

const API_BASE = '/api/ai-models';

export async function getAiModels(): Promise<AiModelResponse[]> {
    const response = await apiFetch(API_BASE);
    if (!response.ok) throw new Error('Failed to fetch AI models');
    return response.json();
}

export async function getAiModel(id: string): Promise<AiModelResponse> {
    const response = await apiFetch(`${API_BASE}/${id}`);
    if (!response.ok) throw new Error('Failed to fetch AI model');
    return response.json();
}

export async function registerAiModel(data: RegisterAiModelRequest): Promise<AiModelResponse> {
    const response = await apiFetch(API_BASE, {
        method: 'POST',
        body: JSON.stringify(data),
    });
    if (!response.ok) {
        const err = await response.json().catch(() => ({}));
        throw new Error((err as { error?: string }).error || 'Failed to register model');
    }
    return response.json();
}

export async function updateAiModel(id: string, data: RegisterAiModelRequest): Promise<AiModelResponse> {
    const response = await apiFetch(`${API_BASE}/${id}`, {
        method: 'PUT',
        body: JSON.stringify(data),
    });
    if (!response.ok) {
        const err = await response.json().catch(() => ({}));
        throw new Error((err as { error?: string }).error || 'Failed to update model');
    }
    return response.json();
}

export async function enableAiModel(id: string): Promise<void> {
    const response = await apiFetch(`${API_BASE}/${id}/enable`, { method: 'POST' });
    if (!response.ok) throw new Error('Failed to enable model');
}

export async function disableAiModel(id: string): Promise<void> {
    const response = await apiFetch(`${API_BASE}/${id}/disable`, { method: 'POST' });
    if (!response.ok) throw new Error('Failed to disable model');
}

export async function deleteAiModel(id: string): Promise<void> {
    const response = await apiFetch(`${API_BASE}/${id}`, { method: 'DELETE' });
    if (!response.ok) {
        const err = await response.json().catch(() => ({}));
        throw new Error((err as { error?: string }).error || 'Failed to delete model');
    }
}
